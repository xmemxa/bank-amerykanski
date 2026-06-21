using Microsoft.AspNetCore.Mvc;
using System.Xml.Linq;
using bank.Data;
using bank.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace bank.Controllers
{
    [ApiController]
    [Route("api/swift")]
    public class SwiftWebhookController : ControllerBase
    {
        private readonly BankDbContext _dbContext;
        private readonly ILogger<SwiftWebhookController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public SwiftWebhookController(BankDbContext dbContext, ILogger<SwiftWebhookController> logger, IHttpClientFactory httpClientFactory)
        {
            _dbContext = dbContext;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        [HttpPost("receive")]
        [Consumes("application/xml")]
        public async Task<IActionResult> ReceiveSwiftMessage()
        {
            try
            {
                using var reader = new StreamReader(Request.Body);
                var xmlString = await reader.ReadToEndAsync();
                _logger.LogInformation("Received SWIFT message: {Xml}", xmlString);

                var uetr = Request.Headers["X-SWIFT-UETR"].ToString();
                var messageType = Request.Headers["X-SWIFT-Message-Type"].ToString();

                var doc = XDocument.Parse(xmlString);
                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

                var txInf = doc.Descendants(ns + "CdtTrfTxInf").FirstOrDefault();
                if (txInf == null)
                {
                    _logger.LogWarning("Invalid SWIFT XML, missing CdtTrfTxInf");
                    return BadRequest("Invalid XML");
                }

                if (messageType == "RETURN" || doc.Descendants(ns + "RmtInf").Elements(ns + "Ustrd").Any(e => e.Value == "Zwrot"))
                {
                    _logger.LogInformation("Received a SWIFT RETURN message. Processing return...");
                    return await HandleReturnAsync(doc, ns, uetr);
                }


                var amountElement = txInf.Element(ns + "IntrBkSttlmAmt");
                var amountStr = amountElement?.Value;
                var currency = amountElement?.Attribute("Ccy")?.Value ?? "USD";
                
                if (!decimal.TryParse(amountStr, System.Globalization.CultureInfo.InvariantCulture, out var amount))
                {
                    return BadRequest("Invalid amount");
                }

                var senderBic = txInf.Element(ns + "DbtrAgt")?.Element(ns + "FinInstnId")?.Element(ns + "BICFI")?.Value;
                var receiverBic = txInf.Element(ns + "CdtrAgt")?.Element(ns + "FinInstnId")?.Element(ns + "BICFI")?.Value;
                var receiverAccountStr = txInf.Element(ns + "CdtrAcct")?.Element(ns + "Id")?.Element(ns + "Othr")?.Element(ns + "Id")?.Value;

                if (string.IsNullOrEmpty(receiverAccountStr) && txInf.Element(ns + "CdtrAcct")?.Element(ns + "Id")?.Element(ns + "IBAN") != null)
                {
                    receiverAccountStr = txInf.Element(ns + "CdtrAcct")?.Element(ns + "Id")?.Element(ns + "IBAN")?.Value;
                }

                if (!string.IsNullOrEmpty(receiverAccountStr) && receiverAccountStr.StartsWith("US", StringComparison.OrdinalIgnoreCase))
                {
                    receiverAccountStr = receiverAccountStr.Substring(2);
                }

                _logger.LogInformation("SWIFT payment to account {Account} from BIC {Bic} for {Amount} {Currency}", receiverAccountStr, senderBic, amount, currency);

                var customerAccount = await _dbContext.Accounts
                    .Include(a => a.User)
                    .FirstOrDefaultAsync(a => a.AccountNumber == receiverAccountStr);


                if (customerAccount == null || customerAccount.AccountType == "Correspondent")
                {
                    _logger.LogWarning("Account {Account} not found or invalid. Initiating SWIFT RETURN.", receiverAccountStr);
                    await InitiateReturnAsync(doc, ns, "receiver_account_closed");
                    return Accepted(new { status = "accepted_for_return" });
                }


                var corrAccountNumber = $"CORR-{senderBic}";
                var corrAccount = await _dbContext.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == corrAccountNumber);
                if (corrAccount == null)
                {
                    _logger.LogWarning("Correspondent account {Corr} not found. Creating it on the fly.", corrAccountNumber);
                    var swiftUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == "swift_system");
                    if (swiftUser != null)
                    {
                        corrAccount = new Account
                        {
                            Id = Guid.NewGuid(),
                            UserId = swiftUser.Id,
                            AccountNumber = corrAccountNumber,
                            RoutingNumber = "SWIFT",
                            Balance = 1000000.00m,
                            Currency = "USD",
                            AccountType = "Correspondent"
                        };
                        _dbContext.Accounts.Add(corrAccount);
                    }
                    else
                    {
                        return StatusCode(500, "SWIFT system user not found.");
                    }
                }


                decimal rate = GetExchangeRate(currency);
                decimal usdAmount = Math.Round(amount * rate, 2);





                corrAccount.Balance -= usdAmount;
                customerAccount.Balance += usdAmount;

                var transaction = new Transaction
                {
                    Id = Guid.NewGuid(),
                    Amount = usdAmount,
                    Timestamp = DateTime.UtcNow,
                    Description = $"SWIFT Transfer from {senderBic} (Original: {amount} {currency})",
                    TransactionType = "SWIFT_INBOUND",
                    Status = "Completed",
                    FromAccountId = corrAccount.Id,
                    ToAccountId = customerAccount.Id
                };
                _dbContext.Transactions.Add(transaction);

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("SWIFT inbound processed successfully. USD credited: {UsdAmount}", usdAmount);


                var callbackUrl = Request.Headers["X-SWIFT-Callback-Url"].ToString();
                if (!string.IsNullOrEmpty(callbackUrl))
                {
                    var msgId = doc.Descendants(ns + "MsgId").FirstOrDefault()?.Value ?? "UNKNOWN";
                    var ackJson = new
                    {
                        status = "accepted",
                        bank = "American Bank",
                        received_at = DateTime.UtcNow.ToString("O"),
                        message_id = msgId,
                        uetr = uetr,
                        receiver_account = receiverAccountStr
                    };
                    var client = _httpClientFactory.CreateClient();
                    _ = client.PostAsJsonAsync(callbackUrl, ackJson);
                }

                return Accepted(new { status = "accepted", uetr = uetr, transfer_id = transaction.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing inbound SWIFT");
                return StatusCode(500, "Internal error");
            }
        }

        private async Task<IActionResult> HandleReturnAsync(XDocument doc, XNamespace ns, string uetr)
        {
            var txInf = doc.Descendants(ns + "CdtTrfTxInf").FirstOrDefault();
            if (txInf == null) return BadRequest("Invalid XML");

            var amountElement = txInf.Element(ns + "IntrBkSttlmAmt");
            var amountStr = amountElement?.Value;
            var currency = amountElement?.Attribute("Ccy")?.Value ?? "USD";
            if (!decimal.TryParse(amountStr, System.Globalization.CultureInfo.InvariantCulture, out var amount))
            {
                return BadRequest("Invalid amount");
            }

            var senderBic = txInf.Element(ns + "DbtrAgt")?.Element(ns + "FinInstnId")?.Element(ns + "BICFI")?.Value;
            var corrAccountNumber = $"CORR-{senderBic}";

            var corrAccount = await _dbContext.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == corrAccountNumber);
            if (corrAccount != null)
            {
                decimal rate = GetExchangeRate(currency);
                decimal usdAmount = Math.Round(amount * rate, 2);
                corrAccount.Balance += usdAmount;
                _logger.LogInformation("SWIFT RETURN processed. Refunded correspondent {Corr} by {UsdAmount}", corrAccountNumber, usdAmount);
            }

            var originalTx = await _dbContext.Transactions.FirstOrDefaultAsync(t => t.EndToEndId == uetr && t.TransactionType == "SWIFT");
            if (originalTx != null)
            {
                var userAccount = await _dbContext.Accounts.FirstOrDefaultAsync(a => a.Id == originalTx.FromAccountId);
                if (userAccount != null)
                {
                    userAccount.Balance += originalTx.Amount;
                    originalTx.ExternalStatus = "RJCT";

                    var refundTx = new Transaction
                    {
                        Id = Guid.NewGuid(),
                        FromAccountId = null,
                        ToAccountId = userAccount.Id,
                        Amount = originalTx.Amount,
                        Currency = originalTx.Currency,
                        Description = "SWIFT Transfer Refund",
                        Timestamp = DateTime.UtcNow,
                        Status = "Completed",
                        TransactionType = "Refund",
                        EndToEndId = uetr
                    };
                    _dbContext.Transactions.Add(refundTx);
                    _logger.LogInformation("Refunded user {User} with {Amount} {Currency} for rejected SWIFT transfer.", userAccount.AccountNumber, originalTx.Amount, originalTx.Currency);
                }
            }

            await _dbContext.SaveChangesAsync();

            return Accepted(new { status = "return_received", uetr = uetr });
        }

        private async Task InitiateReturnAsync(XDocument doc, XNamespace ns, string reason)
        {
            var txInf = doc.Descendants(ns + "CdtTrfTxInf").FirstOrDefault();
            if (txInf == null) return;

            var uetr = txInf.Element(ns + "PmtId")?.Element(ns + "UETR")?.Value ?? Guid.NewGuid().ToString();
            var amountElement = txInf.Element(ns + "IntrBkSttlmAmt");
            var originalSenderBic = txInf.Element(ns + "DbtrAgt")?.Element(ns + "FinInstnId")?.Element(ns + "BICFI")?.Value;
            var originalReceiverBic = txInf.Element(ns + "CdtrAgt")?.Element(ns + "FinInstnId")?.Element(ns + "BICFI")?.Value;

            var returnXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Document xmlns=""urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08"">
  <FIToFICstmrCdtTrf>
    <GrpHdr>
      <MsgId>RETURN-{Guid.NewGuid().ToString().Substring(0, 8)}</MsgId>
      <CreDtTm>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</CreDtTm>
    </GrpHdr>
    <CdtTrfTxInf>
      <PmtId>
        <InstrId>RETURN-INST</InstrId>
        <UETR>{uetr}</UETR>
      </PmtId>
      <IntrBkSttlmAmt Ccy=""{amountElement?.Attribute("Ccy")?.Value}"">{amountElement?.Value}</IntrBkSttlmAmt>
      <RmtInf>
        <Ustrd>Zwrot</Ustrd>
      </RmtInf>
      <ReturnInf>
        <Rsn>{reason}</Rsn>
      </ReturnInf>
      <DbtrAgt>
        <FinInstnId><BICFI>{originalReceiverBic}</BICFI></FinInstnId>
      </DbtrAgt>
      <CdtrAgt>
        <FinInstnId><BICFI>{originalSenderBic}</BICFI></FinInstnId>
      </CdtrAgt>
    </CdtTrfTxInf>
  </FIToFICstmrCdtTrf>
</Document>";

            var returnUrl = Request.Headers["X-SWIFT-Return-Url"].ToString();
            if (string.IsNullOrEmpty(returnUrl))
            {
                returnUrl = "http://host.docker.internal:3000/api/bank/return";
            }

            _logger.LogInformation("Sending SWIFT RETURN to {Url}", returnUrl);

            var request = new HttpRequestMessage(HttpMethod.Post, returnUrl);
            request.Content = new StringContent(returnXml, Encoding.UTF8, "application/xml");
            request.Headers.Add("X-SWIFT-Message-Type", "RETURN");
            var client = _httpClientFactory.CreateClient();
            _ = client.SendAsync(request);
        }

        private decimal GetExchangeRate(string currency)
        {
            return currency.ToUpper() switch
            {
                "EUR" => 1.10m,
                "GBP" => 1.25m,
                "PLN" => 0.25m,
                "USD" => 1.00m,
                _ => 1.00m
            };
        }
    }
}
