using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
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

        /// <summary>
        /// Send an ACH transfer by converting JSON to .ach and uploading via SFTP.
        /// Returns the generated filename on success, or null on failure.
        /// </summary>
        public async Task<string?> SendAchTransferAsync(object transferData)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                
                // Call json-to-ach to get the file content
                var response = await client.PostAsJsonAsync($"{_achApiUrl}/json-to-ach", transferData);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to convert JSON to ACH. Status: {StatusCode}", response.StatusCode);
                    return null;
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
                return fileName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while sending ACH transfer.");
                return null;
            }
        }

        /// <summary>
        /// Fetch .ach files from SFTP outbound directory and delete them after downloading.
        /// Returns a list of (filename, content) tuples.
        /// </summary>
        public async Task<List<(string FileName, byte[] Content)>?> FetchOutboundAchFilesAsync()
        {
            try
            {
                using var sftp = new SftpClient(GetSftpConnectionInfo());
                sftp.Connect();

                var files = sftp.ListDirectory("/outbound")
                    .Where(f => f.Name.EndsWith(".ach", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (files.Count == 0)
                {
                    sftp.Disconnect();
                    return null;
                }

                var results = new List<(string, byte[])>();

                foreach (var file in files)
                {
                    using var ms = new MemoryStream();
                    sftp.DownloadFile(file.FullName, ms);
                    results.Add((file.Name, ms.ToArray()));

                    // Delete file after downloading to avoid reprocessing
                    try { sftp.DeleteFile(file.FullName); }
                    catch { _logger.LogWarning("Could not delete ACH file {Name} after processing.", file.Name); }
                }

                sftp.Disconnect();
                _logger.LogInformation("Fetched {Count} ACH files from outbound.", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching ACH outbound files.");
                return null;
            }
        }

        /// <summary>
        /// Fetch .ack files from SFTP outbound directory and delete them after downloading.
        /// Returns a list of (filename, content) tuples.
        /// </summary>
        public async Task<List<(string FileName, byte[] Content)>?> FetchOutboundAckFilesAsync()
        {
            try
            {
                using var sftp = new SftpClient(GetSftpConnectionInfo());
                sftp.Connect();

                var files = sftp.ListDirectory("/outbound")
                    .Where(f => f.Name.EndsWith(".ack", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (files.Count == 0)
                {
                    sftp.Disconnect();
                    return null;
                }

                var results = new List<(string, byte[])>();

                foreach (var file in files)
                {
                    using var ms = new MemoryStream();
                    sftp.DownloadFile(file.FullName, ms);
                    results.Add((file.Name, ms.ToArray()));

                    // Delete file after downloading to avoid reprocessing
                    try { sftp.DeleteFile(file.FullName); }
                    catch { _logger.LogWarning("Could not delete ACH ack file {Name} after processing.", file.Name); }
                }

                sftp.Disconnect();
                _logger.LogInformation("Fetched {Count} ACH ack files from outbound.", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching ACH outbound ack files.");
                return null;
            }
        }

        /// <summary>
        /// Parse an .ach file using the ach-to-json API and extract transaction entries.
        /// </summary>
        public async Task<List<AchTransactionEntry>?> ParseAchFileAsync(byte[] achContent)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();

                var content = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(achContent);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                content.Add(fileContent, "file", "incoming.ach");

                var response = await client.PostAsync($"{_achApiUrl}/ach-to-json", content);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to parse ACH file. Status: {StatusCode}", response.StatusCode);
                    return null;
                }

                var jsonStr = await response.Content.ReadAsStringAsync();
                var jsonDoc = JsonDocument.Parse(jsonStr);

                var entries = new List<AchTransactionEntry>();

                // Navigate JSON: data.batches[].entries[]
                if (jsonDoc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("batches", out var batches))
                {
                    foreach (var batch in batches.EnumerateArray())
                    {
                        // Get originating RTN from batch header
                        var originRtn = "";
                        if (batch.TryGetProperty("header", out var batchHeader) &&
                            batchHeader.TryGetProperty("originating_dfi_identification", out var origDfi))
                        {
                            originRtn = origDfi.GetString() ?? "";
                        }

                        if (batch.TryGetProperty("entries", out var batchEntries))
                        {
                            foreach (var entry in batchEntries.EnumerateArray())
                            {
                                var txCode = entry.TryGetProperty("transaction_code", out var tc) ? tc.GetString() ?? "" : "";
                                var acctNum = entry.TryGetProperty("dfi_account_number", out var acct) ? acct.GetString()?.Trim() ?? "" : "";
                                var amountCents = entry.TryGetProperty("amount_cents", out var amt) ? amt.GetInt64() : 0;
                                var name = entry.TryGetProperty("individual_name", out var nm) ? nm.GetString()?.Trim() ?? "" : "";

                                // Transaction codes 22,23,32,33 = credit (money IN)
                                // Transaction codes 27,28,37,38 = debit (money OUT)
                                bool isCredit = new[] { "22", "23", "32", "33" }.Contains(txCode);

                                entries.Add(new AchTransactionEntry
                                {
                                    AccountNumber = acctNum,
                                    Amount = amountCents / 100m,
                                    IsCredit = isCredit,
                                    IndividualName = name,
                                    TransactionCode = txCode,
                                    OriginatingRtn = originRtn
                                });
                            }
                        }
                    }
                }

                _logger.LogInformation("Parsed {Count} ACH entries.", entries.Count);
                return entries;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing ACH file.");
                return null;
            }
        }
    }

    /// <summary>
    /// Represents a single entry from a parsed ACH file.
    /// </summary>
    public class AchTransactionEntry
    {
        public string AccountNumber { get; set; } = "";
        public decimal Amount { get; set; }
        public bool IsCredit { get; set; }
        public string IndividualName { get; set; } = "";
        public string TransactionCode { get; set; } = "";
        public string OriginatingRtn { get; set; } = "";
    }
}
