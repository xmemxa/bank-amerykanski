using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using bank.Data;
using bank.Models;
using Microsoft.EntityFrameworkCore;

namespace bank.Services.ExternalPayments
{
    public class PaymentPollingBackgroundService : BackgroundService
    {
        private readonly ILogger<PaymentPollingBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public PaymentPollingBackgroundService(ILogger<PaymentPollingBackgroundService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Small delay to let the app fully start
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            int pollCounter = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var rtpService = scope.ServiceProvider.GetRequiredService<RtpService>();
                        var fedNowService = scope.ServiceProvider.GetRequiredService<FedNowService>();
                        var achService = scope.ServiceProvider.GetRequiredService<AchService>();
                        var dbContext = scope.ServiceProvider.GetRequiredService<BankDbContext>();
                        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                        // ──── Poll FedNow MQ (every cycle ~2s) ────
                        await PollFedNow(fedNowService, dbContext, config);

                        // ──── Poll RTP (every cycle ~2s) ────
                        await PollRtp(rtpService, dbContext);

                        // ──── Poll ACH SFTP outbound (every ~30s = 15 cycles) ────
                        if (pollCounter % 15 == 0)
                        {
                            await PollAchOutbound(achService, dbContext, config);
                            await PollAchOutboundAcks(achService, dbContext);
                        }

                        // ──── Auto-settle ACH transactions older than 3 days (every ~60s = 30 cycles) ────
                        if (pollCounter % 30 == 0)
                        {
                            await AutoSettleAchTransactions(dbContext);
                        }

                        pollCounter++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during payment polling.");
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // FedNow Polling
        // ═══════════════════════════════════════════════════════════════

        private async Task PollFedNow(FedNowService fedNowService, BankDbContext dbContext, IConfiguration config)
        {
            try
            {
                var incoming = await fedNowService.FetchIncomingFedNowAsync();
                if (string.IsNullOrEmpty(incoming)) return;

                _logger.LogInformation("Received FedNow incoming message ({Length} bytes)", incoming.Length);

                var parsed = FedNowService.ParseIncomingXml(incoming);
                if (parsed == null)
                {
                    _logger.LogWarning("Could not parse incoming FedNow XML.");
                    return;
                }

                var (messageType, doc, root) = parsed.Value;
                XNamespace ns = root.Name.Namespace;

                switch (messageType)
                {
                    case "pacs.008":
                        await HandleIncomingPacs008(fedNowService, dbContext, config, root, ns);
                        break;
                    case "pacs.002":
                        await HandleIncomingPacs002(dbContext, root, ns);
                        break;
                    case "pain.013":
                        await HandleIncomingPain013(fedNowService, dbContext, config, root, ns);
                        break;
                    case "pain.014":
                        HandleIncomingPain014(root, ns);
                        break;
                    default:
                        _logger.LogWarning("Unknown FedNow message type: {Type}", messageType);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing FedNow message.");
            }
        }

        /// <summary>
        /// Handle incoming pacs.008: Someone is sending us money.
        /// 1. Find the target account in our bank
        /// 2. Credit the account
        /// 3. Record the transaction
        /// 4. Send pacs.002 ACCP back
        /// </summary>
        private async Task HandleIncomingPacs008(FedNowService fedNowService, BankDbContext dbContext, IConfiguration config, XElement root, XNamespace ns)
        {
            var msgId = root.Descendants(ns + "MsgId").FirstOrDefault()?.Value ?? "";
            var endToEndId = root.Descendants(ns + "EndToEndId").FirstOrDefault()?.Value ?? "";
            var amountStr = root.Descendants(ns + "IntrBkSttlmAmt").FirstOrDefault()?.Value ?? "0";
            var amount = decimal.Parse(amountStr, System.Globalization.CultureInfo.InvariantCulture);

            var dbtrRtn = root.Descendants(ns + "DbtrAgt").FirstOrDefault()
                ?.Descendants(ns + "MmbId").FirstOrDefault()?.Value ?? "";
            var dbtrName = root.Descendants(ns + "DbtrAgt").FirstOrDefault()
                ?.Descendants(ns + "nm").FirstOrDefault()?.Value ?? "Unknown";
            var dbtrAcctId = root.Descendants(ns + "DbtrAcct").FirstOrDefault()
                ?.Descendants(ns + "Id").FirstOrDefault()
                ?.Descendants(ns + "Othr").FirstOrDefault()
                ?.Descendants(ns + "Id").FirstOrDefault()?.Value ?? "";

            var cdtrRtn = root.Descendants(ns + "CdtrAgt").FirstOrDefault()
                ?.Descendants(ns + "MmbId").FirstOrDefault()?.Value ?? "";
            var cdtrName = root.Descendants(ns + "CdtrAgt").FirstOrDefault()
                ?.Descendants(ns + "nm").FirstOrDefault()?.Value ?? "Unknown";
            var cdtrAcctId = root.Descendants(ns + "CdtrAcct").FirstOrDefault()
                ?.Descendants(ns + "Id").FirstOrDefault()
                ?.Descendants(ns + "Othr").FirstOrDefault()
                ?.Descendants(ns + "Id").FirstOrDefault()?.Value ?? "";

            _logger.LogInformation("Incoming pacs.008: E2E={E2E}, Amount={Amount}, From={From} To={To}",
                endToEndId, amount, dbtrRtn, cdtrAcctId);

            // Find target account in our bank by account number
            var targetAccount = await dbContext.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == cdtrAcctId);

            string status = "ACCP";
            if (targetAccount == null)
            {
                _logger.LogWarning("pacs.008: Target account {AcctId} not found in our bank. Accepting anyway (balance won't change).", cdtrAcctId);
                // Still accept for FedNow settlement purposes, but log the warning
            }
            else
            {
                targetAccount.Balance += amount;
                _logger.LogInformation("Credited {Amount} to account {AcctId}. New balance: {Balance}", amount, cdtrAcctId, targetAccount.Balance);
            }

            // Record transaction
            var txRecord = new Transaction
            {
                Id = Guid.NewGuid(),
                ToAccountId = targetAccount?.Id,
                ExternalSourceAccount = dbtrAcctId,
                Amount = amount,
                Currency = "USD",
                Description = $"FedNow incoming from {dbtrName}",
                Timestamp = DateTime.UtcNow,
                Status = "Completed",
                TransactionType = "FedNow",
                EndToEndId = endToEndId,
                ExternalStatus = status
            };
            dbContext.Transactions.Add(txRecord);
            await dbContext.SaveChangesAsync();

            // Send pacs.002 ACCP response
            var pacs002 = fedNowService.GeneratePacs002Xml(
                msgId, endToEndId, status, amount,
                dbtrRtn, dbtrName, dbtrAcctId,
                cdtrRtn, cdtrName, cdtrAcctId);

            var sent = await fedNowService.SendPacs002Async(pacs002);
            if (sent)
                _logger.LogInformation("Sent pacs.002 ACCP for E2E={E2E}", endToEndId);
            else
                _logger.LogError("Failed to send pacs.002 for E2E={E2E}", endToEndId);
        }

        /// <summary>
        /// Handle incoming pacs.002: Status report for our outgoing transfer.
        /// Update our transaction record based on EndToEndId.
        /// </summary>
        private async Task HandleIncomingPacs002(BankDbContext dbContext, XElement root, XNamespace ns)
        {
            var endToEndId = root.Descendants(ns + "OrgnlEndToEndId").FirstOrDefault()?.Value ?? "";
            var txStatus = root.Descendants(ns + "TxSts").FirstOrDefault()?.Value ?? "";

            _logger.LogInformation("Incoming pacs.002: E2E={E2E}, Status={Status}", endToEndId, txStatus);

            // Find our outgoing transaction by EndToEndId
            var transaction = await dbContext.Transactions
                .FirstOrDefaultAsync(t => t.EndToEndId == endToEndId);

            if (transaction != null)
            {
                transaction.ExternalStatus = txStatus;

                if (txStatus == "ACCP" || txStatus == "ACTC")
                {
                    transaction.Status = "Completed";
                    _logger.LogInformation("Transaction {E2E} settled successfully.", endToEndId);
                }
                else if (txStatus == "RJCT" || txStatus == "CANC" || txStatus == "BLCK")
                {
                    transaction.Status = "Failed";
                    _logger.LogWarning("Transaction {E2E} was rejected/blocked. Reversing balance.", endToEndId);

                    // Reverse the deduction from sender's account
                    if (transaction.FromAccountId.HasValue)
                    {
                        var account = await dbContext.Accounts.FindAsync(transaction.FromAccountId.Value);
                        if (account != null)
                        {
                            account.Balance += transaction.Amount;
                            _logger.LogInformation("Reversed {Amount} to account {AcctId}", transaction.Amount, account.AccountNumber);
                        }
                    }
                }

                await dbContext.SaveChangesAsync();
            }
            else
            {
                _logger.LogWarning("pacs.002: No matching transaction found for E2E={E2E}", endToEndId);
            }
        }

        /// <summary>
        /// Handle incoming pain.013: Payment request from a merchant/other party.
        /// Auto-accept: generate pain.014 ACCP + pacs.008.
        /// </summary>
        private async Task HandleIncomingPain013(FedNowService fedNowService, BankDbContext dbContext, IConfiguration config, XElement root, XNamespace ns)
        {
            var msgId = root.Descendants(ns + "MsgId").FirstOrDefault()?.Value ?? "";
            var endToEndId = root.Descendants(ns + "EndToEndId").FirstOrDefault()?.Value ?? "";
            var amountStr = root.Descendants(ns + "InstdAmt").FirstOrDefault()?.Value ?? "0";
            var amount = decimal.Parse(amountStr, System.Globalization.CultureInfo.InvariantCulture);

            var dbtrRtn = root.Descendants(ns + "DbtrAgt").FirstOrDefault()
                ?.Descendants(ns + "MmbId").FirstOrDefault()?.Value ?? "";
            var dbtrName = root.Descendants(ns + "DbtrAgt").FirstOrDefault()
                ?.Descendants(ns + "nm").FirstOrDefault()?.Value ?? "Unknown";
            var dbtrAcctId = root.Descendants(ns + "DbtrAcct").FirstOrDefault()
                ?.Descendants(ns + "Id").FirstOrDefault()
                ?.Descendants(ns + "Othr").FirstOrDefault()
                ?.Descendants(ns + "Id").FirstOrDefault()?.Value ?? "";

            var cdtrRtn = root.Descendants(ns + "CdtrAgt").FirstOrDefault()
                ?.Descendants(ns + "MmbId").FirstOrDefault()?.Value ?? "";
            var cdtrName = root.Descendants(ns + "CdtrAgt").FirstOrDefault()
                ?.Descendants(ns + "nm").FirstOrDefault()?.Value ?? "Unknown";
            var cdtrAcctId = root.Descendants(ns + "CdtrAcct").FirstOrDefault()
                ?.Descendants(ns + "Id").FirstOrDefault()
                ?.Descendants(ns + "Othr").FirstOrDefault()
                ?.Descendants(ns + "Id").FirstOrDefault()?.Value ?? "";

            var memo = root.Descendants(ns + "Ustrd").FirstOrDefault()?.Value ?? "Payment request";

            _logger.LogInformation("Incoming pain.013: E2E={E2E}, Amount={Amount}, Dbtr={Dbtr}, Cdtr={Cdtr}",
                endToEndId, amount, dbtrRtn, cdtrRtn);

            // Check if debtor account exists in our bank
            var debtorAccount = await dbContext.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == dbtrAcctId);

            if (debtorAccount == null || debtorAccount.Balance < amount)
            {
                _logger.LogWarning("pain.013: Debtor account {AcctId} not found or insufficient funds. Rejecting.", dbtrAcctId);
                // Send pain.014 RJCT
                var pain014Reject = fedNowService.GeneratePain014Xml(msgId, endToEndId, "RJCT");
                await fedNowService.SendPacs002Async(pain014Reject); // reuse send method (same /send endpoint)
                return;
            }

            // 1. Send pain.014 ACCP
            var pain014 = fedNowService.GeneratePain014Xml(msgId, endToEndId, "ACCP");
            await fedNowService.SendPacs002Async(pain014);
            _logger.LogInformation("Sent pain.014 ACCP for E2E={E2E}", endToEndId);

            // 2. Deduct from debtor account
            debtorAccount.Balance -= amount;

            // 3. Record transaction
            var txRecord = new Transaction
            {
                Id = Guid.NewGuid(),
                FromAccountId = debtorAccount.Id,
                ExternalDestinationAccount = cdtrAcctId,
                Amount = amount,
                Currency = "USD",
                Description = $"FedNow payment request: {memo}",
                Timestamp = DateTime.UtcNow,
                Status = "Pending",
                TransactionType = "FedNow",
                EndToEndId = endToEndId,
                ExternalStatus = "PDNG"
            };
            dbContext.Transactions.Add(txRecord);
            await dbContext.SaveChangesAsync();

            // 4. Generate and send pacs.008 (with same EndToEndId)
            var pacs008 = fedNowService.GeneratePacs008FromPain013(
                endToEndId, amount,
                dbtrRtn, dbtrName, dbtrAcctId,
                cdtrRtn, cdtrName, cdtrAcctId,
                memo);
            var sent = await fedNowService.SendFedNowTransferAsync(pacs008);
            if (sent)
                _logger.LogInformation("Sent pacs.008 from pain.013 for E2E={E2E}", endToEndId);
            else
                _logger.LogError("Failed to send pacs.008 from pain.013 for E2E={E2E}", endToEndId);
        }

        /// <summary>
        /// Handle incoming pain.014: Response to a pain.013 we sent (not typically used for banks).
        /// </summary>
        private void HandleIncomingPain014(XElement root, XNamespace ns)
        {
            var endToEndId = root.Descendants(ns + "OrgnlEndToEndId").FirstOrDefault()?.Value ?? "";
            var status = root.Descendants(ns + "TxSts").FirstOrDefault()?.Value ?? "";
            _logger.LogInformation("Incoming pain.014: E2E={E2E}, Status={Status}", endToEndId, status);
        }

        // ═══════════════════════════════════════════════════════════════
        // RTP Polling
        // ═══════════════════════════════════════════════════════════════

        private async Task PollRtp(RtpService rtpService, BankDbContext dbContext)
        {
            try
            {
                var rtpIncoming = await rtpService.FetchIncomingRtpAsync();
                if (string.IsNullOrEmpty(rtpIncoming)) return;

                var data = System.Text.Json.JsonDocument.Parse(rtpIncoming);
                if (data.RootElement.TryGetProperty("messages", out var msgs) && msgs.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var msg in msgs.EnumerateArray())
                    {
                        var type = msg.GetProperty("type").GetString();
                        var payload = msg.GetProperty("payload").GetString();

                        if (type == "pacs.002" && !string.IsNullOrEmpty(payload))
                        {
                            try
                            {
                                var doc = System.Xml.Linq.XDocument.Parse(payload);
                                var root = doc.Root;
                                if (root != null)
                                {
                                    var ns = root.Name.Namespace;
                                    var endToEndId = root.Descendants(ns + "OrgnlEndToEndId").FirstOrDefault()?.Value;
                                    var txSts = root.Descendants(ns + "TxSts").FirstOrDefault()?.Value ?? root.Descendants(ns + "GrpSts").FirstOrDefault()?.Value;

                                    if (!string.IsNullOrEmpty(endToEndId))
                                    {
                                        var tx = await dbContext.Transactions.FirstOrDefaultAsync(t => t.EndToEndId == endToEndId);
                                        if (tx != null && tx.Status == "Pending")
                                        {
                                            if (txSts == "ACCP" || txSts == "ACTC")
                                            {
                                                tx.Status = "Completed";
                                                tx.ExternalStatus = txSts;
                                            }
                                            else if (txSts == "RJCT")
                                            {
                                                tx.Status = "Failed";
                                                tx.ExternalStatus = txSts;
                                                if (tx.FromAccountId.HasValue)
                                                {
                                                    var account = await dbContext.Accounts.FindAsync(tx.FromAccountId.Value);
                                                    if (account != null) account.Balance += tx.Amount;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception parseEx)
                            {
                                _logger.LogError(parseEx, "Failed to parse RTP pacs.002 payload");
                            }
                        }
                    }
                    await dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing RTP message.");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // ACH Polling (SFTP outbound)
        // ═══════════════════════════════════════════════════════════════

        private async Task PollAchOutbound(AchService achService, BankDbContext dbContext, IConfiguration config)
        {
            try
            {
                var achFiles = await achService.FetchOutboundAchFilesAsync();
                if (achFiles == null || achFiles.Count == 0) return;

                foreach (var (fileName, content) in achFiles)
                {
                    _logger.LogInformation("Processing ACH outbound file: {FileName}", fileName);

                    var transactions = await achService.ParseAchFileAsync(content);
                    if (transactions == null) continue;

                    foreach (var tx in transactions)
                    {
                        // Find target account in our bank
                        var targetAccount = await dbContext.Accounts
                            .FirstOrDefaultAsync(a => a.AccountNumber == tx.AccountNumber);

                        if (targetAccount != null)
                        {
                            if (tx.IsCredit)
                                targetAccount.Balance += tx.Amount;
                            else
                                targetAccount.Balance -= tx.Amount;

                            var txRecord = new Transaction
                            {
                                Id = Guid.NewGuid(),
                                ToAccountId = tx.IsCredit ? targetAccount.Id : null,
                                FromAccountId = tx.IsCredit ? null : targetAccount.Id,
                                ExternalSourceAccount = tx.IsCredit ? tx.OriginatingRtn : null,
                                ExternalDestinationAccount = tx.IsCredit ? null : tx.OriginatingRtn,
                                Amount = tx.Amount,
                                Currency = "USD",
                                Description = $"ACH {(tx.IsCredit ? "Credit" : "Debit")}: {tx.IndividualName}",
                                Timestamp = DateTime.UtcNow,
                                Status = "Completed",
                                TransactionType = "ACH",
                                ExternalStatus = "ACCP"
                            };
                            dbContext.Transactions.Add(txRecord);
                            _logger.LogInformation("ACH {Type} {Amount} for account {Acct}", 
                                tx.IsCredit ? "credit" : "debit", tx.Amount, tx.AccountNumber);
                        }
                        else
                        {
                            _logger.LogWarning("ACH: Account {Acct} not found in our bank.", tx.AccountNumber);
                        }
                    }

                    await dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ACH outbound files.");
            }
        }

        private async Task PollAchOutboundAcks(AchService achService, BankDbContext dbContext)
        {
            try
            {
                var ackFiles = await achService.FetchOutboundAckFilesAsync();
                if (ackFiles == null || ackFiles.Count == 0) return;

                foreach (var (fileName, content) in ackFiles)
                {
                    _logger.LogInformation("Processing ACH ack file: {FileName}", fileName);
                    
                    var contentStr = System.Text.Encoding.UTF8.GetString(content);
                    if (contentStr.Contains("R,1,10") || contentStr.Contains("LIVE"))
                    {
                        // Match with our transaction
                        var originalFileName = fileName.Replace(".ack", ".ach", StringComparison.OrdinalIgnoreCase);
                        var tx = await dbContext.Transactions
                            .FirstOrDefaultAsync(t => t.TransactionType == "ACH" && t.Status == "Pending" && t.EndToEndId == originalFileName);

                        if (tx != null)
                        {
                            tx.Status = "Completed";
                            tx.ExternalStatus = "ACCP";
                            await dbContext.SaveChangesAsync();
                            _logger.LogInformation("Marked ACH transaction {Id} as Completed based on ACK.", tx.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ACH outbound ack files.");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // ACH Auto-Settlement (3 days)
        // ═══════════════════════════════════════════════════════════════

        private async Task AutoSettleAchTransactions(BankDbContext dbContext)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-3);
                var pendingAch = await dbContext.Transactions
                    .Where(t => t.TransactionType == "ACH" 
                        && t.Status == "Pending" 
                        && t.Timestamp < cutoff)
                    .ToListAsync();

                if (pendingAch.Count > 0)
                {
                    foreach (var tx in pendingAch)
                    {
                        tx.Status = "Completed";
                        tx.ExternalStatus = "ACCP";
                    }
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation("Auto-settled {Count} ACH transactions older than 3 days.", pendingAch.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error auto-settling ACH transactions.");
            }
        }
    }
}
