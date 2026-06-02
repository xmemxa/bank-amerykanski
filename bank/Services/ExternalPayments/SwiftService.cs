using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using bank.Models;

namespace bank.Services.ExternalPayments
{
    public class SwiftService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SwiftService> _logger;

        public SwiftService(HttpClient httpClient, IConfiguration configuration, ILogger<SwiftService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<(bool Success, string ErrorMessage)> SendSwiftTransferAsync(TransferRequestDto request, string currency)
        {
            try
            {
                var swiftApiUrl = _configuration["ExternalPayments:SwiftApiUrl"] ?? "http://host.docker.internal:3001";
                var clientId = _configuration["ExternalPayments:SwiftClientId"] ?? "test-client";
                var clientSecret = _configuration["ExternalPayments:SwiftClientSecret"] ?? "test-secret";
                var senderBic = _configuration["ExternalPayments:SwiftSenderBic"] ?? "USBKUS01XXX";

                _logger.LogInformation("Attempting to get SWIFT auth token...");

                // 1. Get Auth Token
                var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"{swiftApiUrl}/auth/token");
                var tokenContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", clientId),
                    new KeyValuePair<string, string>("client_secret", clientSecret)
                });
                tokenRequest.Content = tokenContent;

                var tokenResponse = await _httpClient.SendAsync(tokenRequest);
                if (!tokenResponse.IsSuccessStatusCode)
                {
                    var errorMsg = await tokenResponse.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to get SWIFT auth token: {Error}", errorMsg);
                    return (false, "Auth failure with SWIFT network");
                }

                var tokenResponseString = await tokenResponse.Content.ReadAsStringAsync();
                var tokenData = JsonDocument.Parse(tokenResponseString);
                var token = tokenData.RootElement.GetProperty("access_token").GetString();

                // 2. Prepare XML
                string msgId = $"MSG-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 8)}";
                string instrId = $"INST-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 8)}";
                string uetr = Guid.NewGuid().ToString();
                string date = DateTime.UtcNow.ToString("yyyy-MM-dd");
                string dtTm = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

                string xmlPayload = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Document xmlns=""urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08"">
  <FIToFICstmrCdtTrf>
    <GrpHdr>
      <MsgId>{msgId}</MsgId>
      <CreDtTm>{dtTm}</CreDtTm>
      <NbOfTxs>1</NbOfTxs>
      <SttlmInf>
        <SttlmMtd>INDA</SttlmMtd>
      </SttlmInf>
    </GrpHdr>
    <CdtTrfTxInf>
      <PmtId>
        <InstrId>{instrId}</InstrId>
        <EndToEndId>NOTPROVIDED</EndToEndId>
        <UETR>{uetr}</UETR>
      </PmtId>
      <IntrBkSttlmAmt Ccy=""{currency}"">{request.Amount:0.00}</IntrBkSttlmAmt>
      <IntrBkSttlmDt>{date}</IntrBkSttlmDt>
      <InstdAmt Ccy=""{currency}"">{request.Amount:0.00}</InstdAmt>
      <ChrgBr>SHAR</ChrgBr>
      <InstgAgt>
        <FinInstnId><BICFI>{senderBic}</BICFI></FinInstnId>
      </InstgAgt>
      <InstdAgt>
        <FinInstnId><BICFI>{request.TargetRoutingNumber}</BICFI></FinInstnId>
      </InstdAgt>
      <Dbtr>
        <Nm>American Bank Customer</Nm>
      </Dbtr>
      <DbtrAcct>
        <Id><Othr><Id>{request.FromAccount}</Id></Othr></Id>
      </DbtrAcct>
      <DbtrAgt>
        <FinInstnId><BICFI>{senderBic}</BICFI></FinInstnId>
      </DbtrAgt>
      <CdtrAgt>
        <FinInstnId><BICFI>{request.TargetRoutingNumber}</BICFI></FinInstnId>
      </CdtrAgt>
      <Cdtr>
        <Nm>{request.RecipientName ?? "External Recipient"}</Nm>
      </Cdtr>
      <CdtrAcct>
        <Id><Othr><Id>{request.ExternalAccountNumber ?? "0000000000"}</Id></Othr></Id>
      </CdtrAcct>
    </CdtTrfTxInf>
  </FIToFICstmrCdtTrf>
</Document>";

                _logger.LogInformation("Sending SWIFT MT103/ISO20022 message with UETR: {Uetr}", uetr);

                // 3. Post XML
                var xmlRequest = new HttpRequestMessage(HttpMethod.Post, $"{swiftApiUrl}/swift/message");
                xmlRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                xmlRequest.Content = new StringContent(xmlPayload, Encoding.UTF8, "application/xml");

                var xmlResponse = await _httpClient.SendAsync(xmlRequest);
                if (xmlResponse.IsSuccessStatusCode)
                {
                    _logger.LogInformation("SWIFT transfer accepted by network.");
                    return (true, string.Empty);
                }
                else
                {
                    var responseBody = await xmlResponse.Content.ReadAsStringAsync();
                    _logger.LogError("SWIFT transfer failed. Status: {Status}, Body: {Body}", xmlResponse.StatusCode, responseBody);
                    try 
                    {
                        var errorJson = JsonDocument.Parse(responseBody);
                        if (errorJson.RootElement.TryGetProperty("error", out var errorProp))
                        {
                            return (false, errorProp.GetString() ?? "Unknown SWIFT error");
                        }
                    }
                    catch { }
                    return (false, responseBody);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred during SWIFT transfer.");
                return (false, "Internal server error connecting to SWIFT.");
            }
        }
    }
}
