using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using bank.Data;
using bank.Models;

namespace bank.Controllers
{
    [ApiController]
    [Route("api/v1/[action]")]
    public class CardWebhookController : ControllerBase
    {
        private readonly BankDbContext _dbContext;
        private readonly bank.Services.ExchangeRateService _exchangeService;

        public CardWebhookController(BankDbContext dbContext, bank.Services.ExchangeRateService exchangeService)
        {
            _dbContext = dbContext;
            _exchangeService = exchangeService;
        }

        public class AuthorizeRequest
        {
            public Guid account_id { get; set; }
            public decimal amount { get; set; }
            public string currency { get; set; } = string.Empty;
            public Guid transaction_id { get; set; }
            public string merchant_name { get; set; } = string.Empty;
        }

        [HttpPost]
        [ActionName("authorize")]
        public async Task<IActionResult> Authorize([FromBody] AuthorizeRequest request)
        {
            var account = await _dbContext.Accounts.FirstOrDefaultAsync(a => a.Id == request.account_id);
            if (account == null)
            {
                return Ok(new { authorization_code = (string)null, status = "DECLINED", decline_reason = "ACCOUNT_NOT_FOUND" });
            }

            var conversion = _exchangeService.ConvertForCardTransaction(request.amount, request.currency, account.Currency);
            var totalDeduction = conversion.totalDeduction;

            if (account.Balance < totalDeduction)
            {
                return Ok(new { authorization_code = (string)null, status = "DECLINED", decline_reason = "INSUFFICIENT_FUNDS" });
            }

            account.Balance -= totalDeduction;
            account.HeldBalance += totalDeduction;

            await _dbContext.SaveChangesAsync();

            var authCode = $"AUTH-{Guid.NewGuid().ToString().Substring(0, 6).ToUpper()}";

            return Ok(new
            {
                authorization_code = authCode,
                status = "APPROVED",
                decline_reason = (string)null
            });
        }

        public class CaptureRequest
        {
            public string authorization_code { get; set; } = string.Empty;
            public string transaction_id { get; set; } = string.Empty;
            public decimal amount { get; set; }
            public string card_token { get; set; } = string.Empty;
            public string currency { get; set; } = "USD";
            public string merchant_id { get; set; } = string.Empty;
        }

        [HttpPost]
        [ActionName("capture")]
        public async Task<IActionResult> Capture([FromBody] CaptureRequest request)
        {
            var card = await _dbContext.Cards.Include(c => c.Account).FirstOrDefaultAsync(c => c.CardToken == request.card_token);
            if (card == null)
            {
                return BadRequest(new { error = "Card not found" });
            }

            var account = card.Account;
            var conversion = _exchangeService.ConvertForCardTransaction(request.amount, request.currency, account.Currency);
            var totalDeduction = conversion.totalDeduction;

            if (card.PerTransactionLimit.HasValue && totalDeduction > card.PerTransactionLimit.Value)
            {
                return BadRequest(new { error = "Per-transaction limit exceeded" });
            }

            if (card.DailyLimit.HasValue)
            {
                var today = DateTime.UtcNow.Date;
                var dailySpent = await _dbContext.Transactions
                    .Where(t => t.Description.Contains($"[{card.MaskedPan}]") && t.Timestamp >= today)
                    .SumAsync(t => t.Amount);

                if (dailySpent + totalDeduction > card.DailyLimit.Value)
                {
                    return BadRequest(new { error = "Daily limit exceeded" });
                }
            }

            if (card.Type != "PREPAID")
            {
                if (account.HeldBalance >= totalDeduction)
                {
                    account.HeldBalance -= totalDeduction;
                }
                else
                {
                    var unheldAmount = totalDeduction - account.HeldBalance;
                    account.HeldBalance = 0;
                    account.Balance -= unheldAmount;
                }
            }

            string feeDetails = conversion.fee > 0 ? $", Fee: {conversion.fee} {account.Currency}" : "";
            string currencyDetails = request.currency != account.Currency ? $" (Orig: {request.amount} {request.currency}{feeDetails})" : "";

            var transaction = new Transaction
            {
                Id = Guid.Parse(request.transaction_id),
                FromAccountId = card.Type == "PREPAID" ? null : account.Id,
                ToAccountId = null,
                Amount = totalDeduction,
                Description = $"Card payment at {request.merchant_id} [{card.MaskedPan}]{currencyDetails}" + (card.Type == "PREPAID" ? " (Prepaid)" : ""),
                Status = "Completed",
                Timestamp = DateTime.UtcNow,
                TransactionType = "Card"
            };

            _dbContext.Transactions.Add(transaction);
            await _dbContext.SaveChangesAsync();

            return Ok(new { status = "SETTLED" });
        }

    }
}
