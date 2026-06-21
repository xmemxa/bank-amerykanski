using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using bank.Data;
using bank.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace bank.Controllers
{
    [ApiController]
    [Route("webhook")]
    public class KlikWebhookController : ControllerBase
    {
        private readonly BankDbContext _dbContext;
        private readonly ILogger<KlikWebhookController> _logger;

        public KlikWebhookController(BankDbContext dbContext, ILogger<KlikWebhookController> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public class AuthorizeRequest
        {
            [JsonPropertyName("transaction_id")]
            public string TransactionId { get; set; } = string.Empty;

            [JsonPropertyName("user_id")]
            public string UserId { get; set; } = string.Empty;

            [JsonPropertyName("amount")]
            public string Amount { get; set; } = string.Empty;

            [JsonPropertyName("currency")]
            public string Currency { get; set; } = string.Empty;

            [JsonPropertyName("merchant_name")]
            public string MerchantName { get; set; } = string.Empty;

            [JsonPropertyName("is_on_us")]
            public bool IsOnUs { get; set; }

            [JsonPropertyName("expiry_time")]
            public DateTime ExpiryTime { get; set; }

            [JsonPropertyName("zone")]
            public string Zone { get; set; } = string.Empty;
        }

        [HttpPost("authorize")]
        public async Task<IActionResult> Authorize([FromBody] AuthorizeRequest request)
        {
            _logger.LogInformation("Received KLIK authorize webhook for TransactionId: {TxId}", request.TransactionId);

            if (!decimal.TryParse(request.Amount, out var parsedAmount))
            {
                return BadRequest("Invalid amount format");
            }

            var pendingAuth = new KlikPendingAuthorization
            {
                Id = Guid.NewGuid(),
                TransactionId = request.TransactionId,
                UserId = request.UserId,
                Amount = parsedAmount,
                Currency = request.Currency,
                MerchantName = request.MerchantName,
                IsOnUs = request.IsOnUs,
                ExpiryTime = request.ExpiryTime.ToUniversalTime(),
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.KlikPendingAuthorizations.Add(pendingAuth);
            await _dbContext.SaveChangesAsync();

            return Ok(new { received = true, will_prompt_user = true });
        }

        public class PingRequest
        {
            [JsonPropertyName("timestamp")]
            public string Timestamp { get; set; } = string.Empty;

            [JsonPropertyName("nonce")]
            public string Nonce { get; set; } = string.Empty;
        }

        [HttpPost("ping")]
        public IActionResult Ping([FromBody] PingRequest request)
        {
            _logger.LogInformation("Received KLIK ping webhook");
            return Ok(new
            {
                timestamp = request.Timestamp,
                nonce = request.Nonce,
                pong = true
            });
        }
    }
}
