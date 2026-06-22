using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace bank.Services.ExternalPayments
{
    public class CardService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey = "bank-key-us-a";
        private readonly string _hmacSecret = "secret-us-a-hmac";
        private readonly string _gatewayUrl = "http://cards_gateway_app:8000";

        public CardService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        private void SignRequest(HttpRequestMessage request, string jsonBody)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var payload = timestamp + jsonBody;

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_hmacSecret));
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            
            var signature = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            request.Headers.Add("X-API-Key", _apiKey);
            request.Headers.Add("X-Signature", signature);
            request.Headers.Add("X-Timestamp", timestamp);
        }

        public async Task<IssueCardResponse?> IssueCardAsync(string userId, string accountId, string cardType, decimal initialBalance = 0)
        {
            var requestBody = new
            {
                account_id = accountId,
                card_type = cardType,
                initial_balance = initialBalance,
                user_id = userId
            };

            var jsonBody = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_gatewayUrl}/api/v1/cards/issue")
            {
                Content = content
            };

            SignRequest(request, jsonBody);

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<IssueCardResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error issuing card: {error}");
                return null;
            }
        }

        public class FullPanResponse
        {
            public string card_token { get; set; } = string.Empty;
            public string full_pan { get; set; } = string.Empty;
            public string masked_pan { get; set; } = string.Empty;
            public string cvv { get; set; } = string.Empty;
            public int expiry_month { get; set; }
            public int expiry_year { get; set; }
        }

        public class CardStatusResponse
        {
            public string card_token { get; set; } = string.Empty;
            public string masked_pan { get; set; } = string.Empty;
            public string status { get; set; } = string.Empty;
            public string card_type { get; set; } = string.Empty;
            public decimal balance { get; set; }
            public decimal daily_limit { get; set; }
            public string bank_id { get; set; } = string.Empty;
        }

        public async Task<FullPanResponse?> GetFullPanAsync(string cardToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_gatewayUrl}/api/v1/cards/{cardToken}/full-pan");
            request.Headers.Add("X-Admin-Key", "admin-secret-key-2026");

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<FullPanResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            return null;
        }

        public async Task<CardStatusResponse?> GetCardStatusAsync(string cardToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_gatewayUrl}/api/v1/cards/{cardToken}");
            
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<CardStatusResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            return null;
        }

        public async Task<bool> UpdateCardStatusAsync(string cardToken, string newStatus, string reason = "User requested")
        {
            var requestBody = new
            {
                reason = reason,
                status = newStatus
            };

            var jsonBody = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Patch, $"{_gatewayUrl}/api/v1/cards/{cardToken}/status")
            {
                Content = content
            };

            SignRequest(request, jsonBody);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error updating card status: {error}");
            }
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> TopUpPrepaidAsync(string cardToken, decimal amount, string currency = "PLN")
        {
            var requestBody = new
            {
                amount = amount,
                currency = currency
            };

            var jsonBody = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_gatewayUrl}/api/v1/cards/{cardToken}/topup")
            {
                Content = content
            };

            SignRequest(request, jsonBody);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error topping up card: {error}");
            }
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ActivateCardAsync(string cardToken, string activatedBy = "customer")
        {
            var requestBody = new
            {
                activated_by = activatedBy
            };

            var jsonBody = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_gatewayUrl}/api/v1/cards/{cardToken}/activate")
            {
                Content = content
            };

            SignRequest(request, jsonBody);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error activating card: {error}");
            }
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> SimulateLifecycleAsync(string cardToken, string newStatus)
        {
            var requestBody = new
            {
                changed_by = "bank_operator",
                new_status = newStatus
            };

            var jsonBody = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Patch, $"{_gatewayUrl}/api/v1/cards/{cardToken}/lifecycle")
            {
                Content = content
            };

            request.Headers.Add("X-Admin-Key", "admin-secret-key-2026");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error simulating lifecycle: {error}");
            }
            return response.IsSuccessStatusCode;
        }
    }

    public class IssueCardResponse
    {
        public string Card_Token { get; set; } = string.Empty;
        public string Masked_Pan { get; set; } = string.Empty;
        public string Full_Pan { get; set; } = string.Empty;
        public string Cvv { get; set; } = string.Empty;
        public int Expiry_Month { get; set; }
        public int Expiry_Year { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Card_Type { get; set; } = string.Empty;
    }
}
