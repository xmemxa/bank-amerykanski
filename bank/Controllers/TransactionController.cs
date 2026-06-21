using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using bank.Data;
using bank.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace bank.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TransactionController : ControllerBase
    {
        private readonly BankDbContext _context;

        public TransactionController(BankDbContext context)
        {
            _context = context;
        }

        [HttpPost("transfer")]
        public async Task<IActionResult> Transfer([FromBody] TransferRequestDto request,
            [FromServices] bank.Services.ExternalPayments.AchService achService,
            [FromServices] bank.Services.ExternalPayments.FedNowService fedNowService,
            [FromServices] bank.Services.ExternalPayments.RtpService rtpService)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (request.Amount <= 0)
                return BadRequest(new { Message = "Transfer amount must be greater than zero." });

            var userIdStr = User.FindFirst("id")?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized(new { Message = "Invalid user token." });

            var sourceAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == request.FromAccount);
            if (sourceAccount == null)
                return NotFound(new { Message = "Source account not found." });

            if (sourceAccount.UserId != userId)
                return StatusCode(403, new { Message = "You do not have permission to transfer from this account." });

            decimal feeAmount = 0;
            if (request.TransactionType == "FedNow") feeAmount = 0.50m;
            else if (request.TransactionType == "SWIFT") 
            {
                if (request.ChargeBearer == "DEBT" || request.ChargeBearer == "SHAR")
                {
                    feeAmount = Math.Round(request.Amount * 0.0035m, 2);
                }
                else
                {
                    feeAmount = 0m;
                }
            }

            if (sourceAccount.Balance < (request.Amount + feeAmount))
                return BadRequest(new { Message = "Insufficient funds in the source account including fees." });

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                string? endToEndIdForTransaction = null;

                if (request.TransactionType == "Internal")
                {
                    if (request.FromAccount == request.ToAccount)
                        return BadRequest(new { Message = "Source and destination accounts cannot be the same." });

                    var destinationAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == request.ToAccount);
                    if (destinationAccount == null)
                        return NotFound(new { Message = "Destination account does not exist in our bank." });

                    sourceAccount.Balance -= request.Amount;
                    destinationAccount.Balance += request.Amount;
                }
                else
                {
                    // External transfer
                    sourceAccount.Balance -= (request.Amount + feeAmount);

                    // Depending on type, call the service
                    bool success = false;
                    
                    if (request.TransactionType == "ACH")
                    {
                        var achData = new
                        {
                            data = new
                            {
                                header = new
                                {
                                    immediate_destination = "090000515",
                                    immediate_origin = "040104018",
                                    immediate_destination_name = "External Bank",
                                    immediate_origin_name = "Bank A",
                                    file_creation_date = DateTime.Now.ToString("yyMMdd"),
                                    file_creation_time = DateTime.Now.ToString("HHmm"),
                                    file_id_modifier = "A",
                                    reference_code = "REF" + DateTime.Now.ToString("yyMMdd")
                                },
                                batches = new[]
                                {
                                    new
                                    {
                                        header = new
                                        {
                                            service_class_code = "200",
                                            company_name = "Bank A",
                                            company_discretionary_data = "",
                                            company_identification = "1234567890",
                                            standard_entry_class_code = "PPD",
                                            company_entry_description = "TRANSFER",
                                            company_descriptive_date = DateTime.Now.ToString("yyMMdd"),
                                            effective_entry_date = DateTime.Now.AddDays(1).ToString("yyMMdd"),
                                            settlement_date = "",
                                            originator_status_code = "1",
                                            originating_dfi_identification = "04010401",
                                            batch_number = "0000001"
                                        },
                                        entries = new[]
                                        {
                                            new
                                            {
                                                transaction_code = "22",
                                                receiving_dfi_rtn = request.TargetRoutingNumber ?? "000000000",
                                                dfi_account_number = request.ExternalAccountNumber ?? "000000000",
                                                amount_cents = (int)(request.Amount * 100),
                                                individual_id_number = "ID" + DateTime.Now.Ticks.ToString().Substring(0, 10),
                                                individual_name = "Customer",
                                                trace_number = "040104010000001",
                                                addenda = Array.Empty<object>()
                                            }
                                        }
                                    }
                                }
                            }
                        };
                        var achFileName = await achService.SendAchTransferAsync(achData);
                        if (achFileName != null)
                        {
                            success = true;
                            endToEndIdForTransaction = achFileName;
                        }
                    }
                    else if (request.TransactionType == "FedNow" || request.TransactionType == "RTP")
                    {
                        var endToEndId = $"E2E-{Guid.NewGuid():N}";
                        var msgId = $"MSG-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 8)}";
                        var memo = !string.IsNullOrEmpty(request.Description) ? request.Description : $"{request.TransactionType} Transfer";
                        
                        string dbtrNm = request.TransactionType == "RTP" ? "BANKA" : "Bank A";
                        string cdtrNm = request.TransactionType == "RTP" ? request.TargetRoutingNumber ?? "" : "External Receiver";

                        string xmlPayload = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Document xmlns=""urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08"">
  <FIToFICstmrCdtTrf>
    <GrpHdr>
      <MsgId>{msgId}</MsgId>
      <CreDtTm>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss}</CreDtTm>
    </GrpHdr>
    <CdtTrfTxInf>
      <PmtId>
        <EndToEndId>{endToEndId}</EndToEndId>
      </PmtId>
      <IntrBkSttlmAmt Ccy=""USD"">{request.Amount:0.00}</IntrBkSttlmAmt>
      <DbtrAgt>
        <FinInstnId>
          <ClrSysMmbId>
            <nm>{dbtrNm}</nm>
            <MmbId>040104018</MmbId>
          </ClrSysMmbId>
        </FinInstnId>
      </DbtrAgt>
      <Dbtr>
        <Nm>Bank A Customer</Nm>
      </Dbtr>
      <DbtrAcct>
        <Id>
          <Othr>
            <Id>{request.FromAccount}</Id>
            <SchmeNm><Prtry>US_ACCT</Prtry></SchmeNm>
          </Othr>
        </Id>
      </DbtrAcct>
      <CdtrAgt>
        <FinInstnId>
          <ClrSysMmbId>
            <nm>{cdtrNm}</nm>
            <MmbId>{request.TargetRoutingNumber}</MmbId>
          </ClrSysMmbId>
        </FinInstnId>
      </CdtrAgt>
      <Cdtr>
        <Nm>External Receiver</Nm>
      </Cdtr>
      <CdtrAcct>
        <Id>
          <Othr>
            <Id>{request.ExternalAccountNumber ?? "123456789"}</Id>
            <SchmeNm><Prtry>US_ACCT</Prtry></SchmeNm>
          </Othr>
        </Id>
      </CdtrAcct>
      <RmtInf>
        <Ustrd>{memo}</Ustrd>
      </RmtInf>
    </CdtTrfTxInf>
  </FIToFICstmrCdtTrf>
</Document>";

                        if (request.TransactionType == "FedNow")
                            success = await fedNowService.SendFedNowTransferAsync(xmlPayload);
                        else if (request.TransactionType == "RTP")
                            success = await rtpService.SendRtpTransferAsync(xmlPayload);
                        
                        // Store EndToEndId for later pacs.002 correlation
                        if (success)
                            endToEndIdForTransaction = endToEndId;
                    }
                    else if (request.TransactionType == "SWIFT")
                    {
                        var swiftService = HttpContext.RequestServices.GetRequiredService<bank.Services.ExternalPayments.SwiftService>();
                        


                        string targetCurrency = "USD";
                        decimal exchangeRate = 1.0m;
                        
                        if (request.TargetRoutingNumber?.StartsWith("UK") == true) { targetCurrency = "GBP"; exchangeRate = 1.25m; }
                        else if (request.TargetRoutingNumber?.StartsWith("PL") == true) { targetCurrency = "PLN"; exchangeRate = 0.25m; }
                        else if (request.TargetRoutingNumber?.StartsWith("DE") == true || request.TargetRoutingNumber?.StartsWith("EU") == true) { targetCurrency = "EUR"; exchangeRate = 1.10m; }
                        
                        decimal targetAmount = Math.Round(request.Amount / exchangeRate, 2);

                        var swiftResult = await swiftService.SendSwiftTransferAsync(request, targetCurrency, targetAmount);
                        
                        if (!swiftResult.Success)
                            return BadRequest(new { Message = $"SWIFT rejected: {swiftResult.ErrorMessage}" });
                            

                        var corrAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == $"CORR-{request.TargetRoutingNumber}");
                        if (corrAccount != null)
                        {
                            corrAccount.Balance += request.Amount;
                        }
                        
                        endToEndIdForTransaction = swiftResult.Uetr;
                        success = true;
                    }
                    else
                    {
                        return BadRequest(new { Message = "Invalid transaction type." });
                    }

                    if (!success)
                    {
                        return StatusCode(500, new { Message = "External payment service failed." });
                    }
                }

                var transactionRecord = new Transaction
                {
                    Id = Guid.NewGuid(),
                    FromAccountId = sourceAccount.Id,
                    ToAccountId = request.TransactionType == "Internal" ? (await _context.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == request.ToAccount))?.Id : null,
                    ExternalDestinationAccount = request.TransactionType == "Internal" ? null : request.ExternalAccountNumber,
                    Amount = request.Amount,
                    Currency = sourceAccount.Currency,
                    Description = !string.IsNullOrEmpty(request.Description) ? request.Description : $"{request.TransactionType} Transfer",
                    Timestamp = DateTime.UtcNow,
                    Status = request.TransactionType == "Internal" ? "Completed" : "Pending",
                    TransactionType = request.TransactionType,
                    EndToEndId = endToEndIdForTransaction,
                    ExternalStatus = request.TransactionType == "Internal" ? null : "PDNG"
                };

                _context.Transactions.Add(transactionRecord);

                if (feeAmount > 0)
                {
                    var feeTransaction = new Transaction
                    {
                        Id = Guid.NewGuid(),
                        FromAccountId = sourceAccount.Id,
                        ToAccountId = null,
                        Amount = feeAmount,
                        Currency = sourceAccount.Currency,
                        Description = $"{request.TransactionType} Transfer Fee",
                        Timestamp = DateTime.UtcNow,
                        Status = "Completed",
                        TransactionType = "Fee"
                    };
                    _context.Transactions.Add(feeTransaction);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new
                {
                    Message = "Transfer successful.",
                    NewBalance = sourceAccount.Balance,
                    Currency = sourceAccount.Currency,
                    TransactionId = transactionRecord.Id
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TransactionController] Exception during transfer: {ex}");
                await transaction.RollbackAsync();
                return StatusCode(500, new { Message = "An internal error occurred during transfer.", Error = ex.Message });
            }
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory()
        {
            var userIdStr = User.FindFirst("id")?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var accountIds = await _context.Accounts
                .Where(a => a.UserId == userId)
                .Select(a => a.Id)
                .ToListAsync();

            var transactions = await _context.Transactions
                .Include(t => t.FromAccount)
                .Include(t => t.ToAccount)
                .Where(t => (t.FromAccountId != null && accountIds.Contains(t.FromAccountId.Value)) || 
                            (t.ToAccountId != null && accountIds.Contains(t.ToAccountId.Value)))
                .OrderByDescending(t => t.Timestamp)
                .Select(t => new {
                    t.Id,
                    t.Timestamp,
                    t.Description,
                    t.TransactionType,
                    t.Status,
                    t.Amount,
                    t.Currency,
                    t.EndToEndId,
                    IsCredit = t.ToAccountId != null && accountIds.Contains(t.ToAccountId.Value),
                    AccountDisplay = (t.ToAccountId != null && accountIds.Contains(t.ToAccountId.Value)) 
                                        ? (t.ToAccount != null ? t.ToAccount.AccountNumber : "") 
                                        : (t.FromAccount != null ? t.FromAccount.AccountNumber : "")
                })
                .ToListAsync();

            return Ok(transactions);
        }
    }
}
