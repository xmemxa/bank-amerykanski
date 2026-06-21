using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace bank.Services.Klik
{
    public class KlikP2PService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly ILogger<KlikP2PService> _logger;

        public KlikP2PService(HttpClient httpClient, IConfiguration config, ILogger<KlikP2PService> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
            _httpClient.BaseAddress = new Uri(_config["Klik:ApiUrl"] ?? "http://host.docker.internal:8001");
            _httpClient.DefaultRequestHeaders.Add("X-KLIK-Bank-Api-Key", _config["Klik:ApiKey"]);
        }

        public async Task<bool> RegisterAliasAsync(string phone, string routingNumber, string accountNumber)
        {
            var zone = _config["Klik:Zone"] ?? "US";
            var request = new Dictionary<string, object>
            {
                { "phone", phone },
                { "zone", zone },
                { "account_identifier", new Dictionary<string, string>
                    {
                        { "type", "us_routing" },
                        { "routing_number", routingNumber },
                        { "account_number", accountNumber }
                    }
                }
            };

            var idempotencyKey = Guid.NewGuid().ToString();
            var reqMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/aliases/register");
            reqMsg.Headers.Add("Idempotency-Key", idempotencyKey);
            var jsonStr = JsonSerializer.Serialize(request);
            reqMsg.Content = new StringContent(jsonStr, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(reqMsg);

            if (response.IsSuccessStatusCode) return true;
            
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Error registering alias: {Error}", error);
            return false;
        }

        public async Task<(string RoutingNumber, string AccountNumber)?> LookupAliasAsync(string phone)
        {
            var encodedPhone = Uri.EscapeDataString(phone);
            var response = await _httpClient.GetAsync($"/api/v1/aliases/lookup/{encodedPhone}");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                var identifier = result.GetProperty("account_identifier");
                var r = identifier.GetProperty("routing_number").GetString()!;
                var a = identifier.GetProperty("account_number").GetString()!;
                return (r, a);
            }

            return null;
        }

        public async Task<bool> DeleteAliasAsync(string phone)
        {
            var idempotencyKey = Guid.NewGuid().ToString();
            var encodedPhone = Uri.EscapeDataString(phone);
            var reqMsg = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/aliases/{encodedPhone}");
            reqMsg.Headers.Add("Idempotency-Key", idempotencyKey);

            var response = await _httpClient.SendAsync(reqMsg);
            return response.IsSuccessStatusCode;
        }
    }
}
