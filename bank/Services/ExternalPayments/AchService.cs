using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace bank.Services.ExternalPayments
{
    public class AchService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AchService> _logger;
        private readonly string _achApiUrl;
        private readonly string _sftpHost;
        private readonly int _sftpPort;
        private readonly string _sftpUsername;

        public AchService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<AchService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _achApiUrl = _configuration["ExternalPayments:AchApiUrl"] ?? "http://localhost:8310";
            _sftpHost = _configuration["ExternalPayments:AchSftpHost"] ?? "localhost";
            _sftpPort = int.Parse(_configuration["ExternalPayments:AchSftpPort"] ?? "2221");
            _sftpUsername = _configuration["ExternalPayments:AchSftpUsername"] ?? "bank-amerykanski";
        }

        private Renci.SshNet.ConnectionInfo GetSftpConnectionInfo()
        {
            var keyPath = Path.Combine(Directory.GetCurrentDirectory(), "Keys", "id_rsa");
            if (!File.Exists(keyPath))
            {
                throw new FileNotFoundException($"Private key not found at {keyPath}");
            }

            var privateKey = new PrivateKeyFile(keyPath);
            return new Renci.SshNet.ConnectionInfo(_sftpHost, _sftpPort, _sftpUsername, new PrivateKeyAuthenticationMethod(_sftpUsername, privateKey));
        }

        public async Task<bool> SendAchTransferAsync(object transferData)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                
                // Call json-to-ach to get the file content
                var response = await client.PostAsJsonAsync($"{_achApiUrl}/json-to-ach", transferData);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to convert JSON to ACH. Status: {StatusCode}", response.StatusCode);
                    return false;
                }

                var achFileBytes = await response.Content.ReadAsByteArrayAsync();
                var fileName = $"transfer_{DateTime.UtcNow:yyyyMMddHHmmssfff}.ach";

                // Upload to SFTP
                using var sftp = new SftpClient(GetSftpConnectionInfo());
                sftp.Connect();
                
                using var ms = new MemoryStream(achFileBytes);
                sftp.UploadFile(ms, $"/inbound/{fileName}");
                
                sftp.Disconnect();
                _logger.LogInformation("Successfully sent ACH file {FileName} to inbound folder.", fileName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while sending ACH transfer.");
                return false;
            }
        }
    }
}
