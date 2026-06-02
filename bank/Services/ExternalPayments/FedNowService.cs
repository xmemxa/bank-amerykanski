using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace bank.Services.ExternalPayments
{
    public class FedNowService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FedNowService> _logger;
        private readonly string _fedNowApiUrl;

        public FedNowService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<FedNowService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _fedNowApiUrl = _configuration["ExternalPayments:FedNowApiUrl"] ?? "http://localhost:8770";
        }

        public async Task<bool> SendFedNowTransferAsync(string xmlPayload)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var content = new MultipartFormDataContent();
                var fileContent = new StringContent(xmlPayload, Encoding.UTF8, "application/xml");
                content.Add(fileContent, "file", $"transfer_{Guid.NewGuid()}.xml");
                
                var response = await client.PostAsync($"{_fedNowApiUrl}/send", content);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully sent FedNow transfer.");
                    return true;
                }
                
                _logger.LogError("Failed to send FedNow transfer. Status: {StatusCode}", response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while sending FedNow transfer.");
                return false;
            }
        }

        public async Task<string?> FetchIncomingFedNowAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var response = await client.GetAsync($"{_fedNowApiUrl}/FIFO/out");
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                return null; // 404 No files in queue
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while fetching FedNow queue.");
                return null;
            }
        }
    }
}
