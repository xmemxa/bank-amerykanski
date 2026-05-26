using System.Net.Http.Json;
using System.Text.Json;

namespace bank.Services
{
    public class AuthService
    {
        private readonly HttpClient _httpClient;
        private readonly Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage.ProtectedSessionStorage _sessionStorage;

        public AuthService(HttpClient httpClient, Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage.ProtectedSessionStorage sessionStorage)
        {
            _httpClient = httpClient;
            _sessionStorage = sessionStorage;
        }

        public async Task<LoginStep1Result> LoginStep1Async(string username, string password)
        {
            var response = await _httpClient.PostAsJsonAsync("api/Auth/login", new { Username = username, Password = password });

            if (response.IsSuccessStatusCode)
            {
                return new LoginStep1Result { Success = true, Requires2FA = true };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new LoginStep1Result { Success = false, ErrorMessage = error };
        }

        public async Task<LoginResult> Verify2FaAsync(string username, string code)
        {
            var response = await _httpClient.PostAsJsonAsync("api/Auth/verify-2fa", new { Username = username, Code = code });

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoginResult>();
                return result ?? new LoginResult { Success = false, ErrorMessage = "Invalid response from server" };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new LoginResult { Success = false, ErrorMessage = error };
        }

        public async Task<RegisterResult> RegisterAsync(object requestData)
        {
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, "api/auth/register")
            {
                Content = JsonContent.Create(requestData)
            };

            try
            {
                var sessionResult = await _sessionStorage.GetAsync<string>("authToken");
                if (sessionResult.Success && !string.IsNullOrEmpty(sessionResult.Value))
                {
                    requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", sessionResult.Value);
                }
            }
            catch (Exception)
            {
                // Ignorujemy błędy pobierania tokena (np. podczas prerenderowania)
            }

            var response = await _httpClient.SendAsync(requestMessage);

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    var result = await response.Content.ReadFromJsonAsync<RegisterResult>();
                    if (result != null)
                    {
                        result.Success = true;
                        return result;
                    }
                }
                catch
                {
                    return new RegisterResult { Success = false, ErrorMessage = "Server returned an error, but no message could be parsed." };
                }
                return new RegisterResult { Success = true };
            }

            var error = await response.Content.ReadAsStringAsync();
            
            if (string.IsNullOrWhiteSpace(error))
            {
                error = $"Error: {(int)response.StatusCode} {response.ReasonPhrase}";
            }
            
            return new RegisterResult { Success = false, ErrorMessage = error };
        }
    }

    public class RegisterResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? AccountNumber { get; set; }
        public string? RoutingNumber { get; set; }
        public decimal InitialBalance { get; set; }
        public string? Currency { get; set; }
    }

    public class LoginStep1Result
    {
        public bool Success { get; set; }
        public bool Requires2FA { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class LoginResult
    {
        public bool Success { get; set; } = true;
        public string? Token { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
