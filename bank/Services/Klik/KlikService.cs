using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace bank.Services.Klik
{
    public class KlikService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly ILogger<KlikService> _logger;

        public KlikService(HttpClient httpClient, IConfiguration config, ILogger<KlikService> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
            _httpClient.BaseAddress = new Uri(_config["Klik:ApiUrl"] ?? "http://host.docker.internal:8001");
            _httpClient.DefaultRequestHeaders.Add("X-KLIK-Bank-Api-Key", _config["Klik:ApiKey"]);
        }

        public async Task<(string Code, int ExpiresIn)> GenerateCodeAsync(string userId)
        {
            var zone = _config["Klik:Zone"] ?? "US";
            var request = new Dictionary<string, string>
            {
                { "user_id", userId },
                { "zone", zone }
            };

            var idempotencyKey = Guid.NewGuid().ToString();
            var reqMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/codes/generate");
            reqMsg.Headers.Add("Idempotency-Key", idempotencyKey);
            var jsonStr = JsonSerializer.Serialize(request);
            _logger.LogInformation("Sending KLIK request: {Json}", jsonStr);
            reqMsg.Content = new StringContent(jsonStr, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(reqMsg);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                var code = result.GetProperty("code").GetString()!;
                var expiresIn = result.GetProperty("expires_in").GetInt32();
                return (code, expiresIn);
            }
            
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Error generating KLIK code: {Error}", error);
            throw new Exception("Nie udało się wygenerować kodu KLIK.");
        }

        public async Task<bool> ConfirmPaymentAsync(string transactionId, string status, string? rejectReason = null)
        {
            var request = new Dictionary<string, string>
            {
                { "transaction_id", transactionId },
                { "status", status },
                { "reject_reason", rejectReason ?? string.Empty }
            };

            var idempotencyKey = Guid.NewGuid().ToString();
            var reqMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/confirm");
            reqMsg.Headers.Add("Idempotency-Key", idempotencyKey);
            var jsonStr = JsonSerializer.Serialize(request);
            reqMsg.Content = new StringContent(jsonStr, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(reqMsg);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }
            
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Error confirming KLIK payment: {Error}", error);
            return false;
        }
    }
}
