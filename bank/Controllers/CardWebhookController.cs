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

        public CardWebhookController(BankDbContext dbContext)
        {
            _dbContext = dbContext;
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

            if (request.currency != account.Currency)
            {
                return Ok(new { authorization_code = (string)null, status = "DECLINED", decline_reason = "INVALID_CURRENCY" });
            }

            if (account.Balance < request.amount)
            {
                return Ok(new { authorization_code = (string)null, status = "DECLINED", decline_reason = "INSUFFICIENT_FUNDS" });
            }

            account.Balance -= request.amount;
            account.HeldBalance += request.amount;

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

            if (card.Type != "PREPAID")
            {
                if (account.HeldBalance >= request.amount)
                {
                    account.HeldBalance -= request.amount;
                }
                else
                {
                    var unheldAmount = request.amount - account.HeldBalance;
                    account.HeldBalance = 0;
                    account.Balance -= unheldAmount;
                }
            }

            var transaction = new Transaction
            {
                Id = Guid.Parse(request.transaction_id),
                FromAccountId = card.Type == "PREPAID" ? null : account.Id,
                ToAccountId = null,
                Amount = request.amount,
                Description = $"Card payment at {request.merchant_id}" + (card.Type == "PREPAID" ? " (Prepaid)" : ""),
                Status = "Completed",
                Timestamp = DateTime.UtcNow,
                TransactionType = "Card"
            };

            _dbContext.Transactions.Add(transaction);
            await _dbContext.SaveChangesAsync();

            return Ok(new { status = "SETTLED" });
        }

        public class RefundRequest
        {
            public Guid account_id { get; set; }
            public decimal amount { get; set; }
            public string currency { get; set; } = string.Empty;
            public Guid original_transaction_id { get; set; }
            public string card_token { get; set; } = string.Empty;
        }

        [HttpPost]
        [ActionName("refund")]
        public async Task<IActionResult> Refund([FromBody] RefundRequest request)
        {
            var card = await _dbContext.Cards.Include(c => c.Account).FirstOrDefaultAsync(c => c.CardToken == request.card_token);
            if (card == null)
            {
                return BadRequest(new { error = "Card not found" });
            }

            var account = card.Account;

            if (card.Type != "PREPAID")
            {
                if (account.HeldBalance >= request.amount)
                {
                    account.HeldBalance -= request.amount;
                }
                else
                {
                    account.HeldBalance = 0;
                }
                
                account.Balance += request.amount;
            }

            await _dbContext.SaveChangesAsync();

            return Ok(new { status = "REFUNDED" });
        }
    }
}
