using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace bank.Services.ExternalPayments
{
    public class RtpService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RtpService> _logger;
        private readonly string _rtpApiUrl;

        public RtpService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<RtpService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _rtpApiUrl = _configuration["ExternalPayments:RtpApiUrl"] ?? "http://localhost:8000";
        }

        public async Task<bool> SendRtpTransferAsync(string xmlPayload)
        {
            try
            {
                if (string.IsNullOrEmpty(ExternalPaymentConfig.RtpApiKey))
                {
                    _logger.LogError("RTP API Key is missing.");
                    return false;
                }

                var client = _httpClientFactory.CreateClient();
                var content = new StringContent(xmlPayload, Encoding.UTF8, "application/xml");
                
                // Set the required API key header
                client.DefaultRequestHeaders.Add("x-api-key", ExternalPaymentConfig.RtpApiKey);
                
                var response = await client.PostAsync($"{_rtpApiUrl}/transfers", content);
                
                if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Accepted)
                {
                    _logger.LogInformation("Successfully initiated RTP transfer.");
                    return true;
                }
                
                _logger.LogError("Failed to send RTP transfer. Status: {StatusCode}", response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while sending RTP transfer.");
                return false;
            }
        }

        public async Task<string?> FetchIncomingRtpAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(ExternalPaymentConfig.RtpApiKey)) return null;

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("x-api-key", ExternalPaymentConfig.RtpApiKey);
                
                var response = await client.GetAsync($"{_rtpApiUrl}/queue/incoming");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while fetching RTP queue.");
                return null;
            }
        }
    }
}
