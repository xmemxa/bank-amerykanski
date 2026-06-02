using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace bank.Services.ExternalPayments
{
    public class ExternalPaymentConfig
    {
        public static string RtpApiKey { get; set; } = string.Empty;
    }

    public class RtpRegistrationService : IHostedService
    {
        private readonly ILogger<RtpRegistrationService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public RtpRegistrationService(ILogger<RtpRegistrationService> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var rtpApiUrl = _configuration["ExternalPayments:RtpApiUrl"];
            var bankCode = _configuration["ExternalPayments:BankCode"];

            if (string.IsNullOrEmpty(rtpApiUrl) || string.IsNullOrEmpty(bankCode))
            {
                _logger.LogWarning("RTP configuration is missing. Skipping dynamic registration.");
                return;
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                var payload = new
                {
                    bank_code = bankCode,
                    balance = 1000000000,
                    debt_limit = 500000000
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                // Try to register
                var response = await client.PostAsync($"{rtpApiUrl}/banks", content, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseData = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                    ExternalPaymentConfig.RtpApiKey = responseData.GetProperty("api_key").GetString() ?? "";
                    _logger.LogInformation("Successfully registered bank in RTP. API Key obtained.");
                }
                else
                {
                    // If it fails, maybe it already exists. Try resetting the key.
                    _logger.LogInformation("RTP registration returned {StatusCode}. Attempting to reset key.", response.StatusCode);
                    var resetResponse = await client.PostAsync($"{rtpApiUrl}/banks/{bankCode}/reset-key", null, cancellationToken);
                    if (resetResponse.IsSuccessStatusCode)
                    {
                        var resetData = await resetResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                        ExternalPaymentConfig.RtpApiKey = resetData.GetProperty("new_api_key").GetString() ?? resetData.GetProperty("api_key").GetString() ?? "";
                        _logger.LogInformation("Successfully reset RTP API Key.");
                    }
                    else
                    {
                        _logger.LogError("Failed to register or reset RTP API Key. {Status}", resetResponse.StatusCode);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during RTP dynamic registration.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
