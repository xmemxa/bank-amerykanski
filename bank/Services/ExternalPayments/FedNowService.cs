using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
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
        private readonly string _bankRtn;
        private readonly string _bankName;

        public FedNowService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<FedNowService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _fedNowApiUrl = _configuration["ExternalPayments:FedNowApiUrl"] ?? "http://localhost:8770";
            _bankRtn = _configuration["ExternalPayments:RoutingNumber"] ?? "040104018";
            _bankName = _configuration["ExternalPayments:BankName"] ?? "Bank A";
        }

        /// <summary>
        /// Send a pacs.008 XML transfer to FedNow MQ /send endpoint.
        /// </summary>
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
                
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to send FedNow transfer. Status: {StatusCode}, Body: {Body}", response.StatusCode, errorBody);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while sending FedNow transfer.");
                return false;
            }
        }

        /// <summary>
        /// Fetch the oldest incoming XML from the MQ FIFO queue.
        /// Returns null if no files are in the queue (404).
        /// </summary>
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

        /// <summary>
        /// Generate a pacs.002 XML response for an incoming pacs.008.
        /// </summary>
        public string GeneratePacs002Xml(
            string originalMsgId,
            string endToEndId,
            string status, // ACCP or RJCT
            decimal amount,
            string dbtrRtn,
            string dbtrName,
            string dbtrAcctId,
            string cdtrRtn,
            string cdtrName,
            string cdtrAcctId)
        {
            var newMsgId = $"MSG-{DateTime.UtcNow:yyyyMMdd}-STS-{Guid.NewGuid().ToString().Substring(0, 8)}";
            var refId = $"REF-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 4)}";
            var dtTm = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Document xmlns=""urn:iso:std:iso:20022:tech:xsd:pacs.002.001.10"">
	<FIToFIPmtStsRpt>
		<GrpHdr>
			<MsgId>{newMsgId}</MsgId>
			<CreDtTm>{dtTm}</CreDtTm>
			<InstgAgt>
				<FinInstnId>
					<Nm>{cdtrName}</Nm>
				</FinInstnId>
			</InstgAgt>
			<InstdAgt>
				<FinInstnId>
					<Nm>{dbtrName}</Nm>
				</FinInstnId>
			</InstdAgt>
		</GrpHdr>
		<OrgnlGrpInfAndSts>
			<OrgnlMsgId>{originalMsgId}</OrgnlMsgId>
			<GrpSts>{status}</GrpSts>
		</OrgnlGrpInfAndSts>
		<TxInfAndSts>
			<OrgnlEndToEndId>{endToEndId}</OrgnlEndToEndId>
			<TxSts>{status}</TxSts>
			<AcctSvcrRef>{refId}</AcctSvcrRef>
			<OrgnlTxRef>
				<IntrBkSttlmAmt Ccy=""USD"">{amount:0.00}</IntrBkSttlmAmt>
				<DbtrAgt>
					<FinInstnId>
						<ClrSysMmbId>
							<nm>{dbtrName}</nm>
							<MmbId>{dbtrRtn}</MmbId>
						</ClrSysMmbId>
					</FinInstnId>
				</DbtrAgt>
				<Dbtr>
					<Nm>{dbtrName}</Nm>
				</Dbtr>
				<DbtrAcct>
					<Id>
						<Othr>
							<Id>{dbtrAcctId}</Id>
                            <SchmeNm><Prtry>US_ACCT</Prtry></SchmeNm>
						</Othr>
					</Id>
				</DbtrAcct>
				<CdtrAgt>
					<FinInstnId>
						<ClrSysMmbId>
							<nm>{cdtrName}</nm>
							<MmbId>{cdtrRtn}</MmbId>
						</ClrSysMmbId>
					</FinInstnId>
				</CdtrAgt>
				<Cdtr>
					<Nm>{cdtrName}</Nm>
				</Cdtr>
				<CdtrAcct>
					<Id>
						<Othr>
							<Id>{cdtrAcctId}</Id>
                            <SchmeNm><Prtry>US_ACCT</Prtry></SchmeNm>
						</Othr>
					</Id>
				</CdtrAcct>
			</OrgnlTxRef>
		</TxInfAndSts>
	</FIToFIPmtStsRpt>
</Document>";
        }

        /// <summary>
        /// Send a pacs.002 response to the FedNow MQ.
        /// </summary>
        public async Task<bool> SendPacs002Async(string pacs002Xml)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var content = new MultipartFormDataContent();
                var fileContent = new StringContent(pacs002Xml, Encoding.UTF8, "application/xml");
                content.Add(fileContent, "file", $"pacs002_{Guid.NewGuid()}.xml");
                
                var response = await client.PostAsync($"{_fedNowApiUrl}/send", content);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully sent pacs.002 response.");
                    return true;
                }
                
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to send pacs.002. Status: {StatusCode}, Body: {Body}", response.StatusCode, errorBody);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while sending pacs.002.");
                return false;
            }
        }

        /// <summary>
        /// Generate a pain.014 XML response for an incoming pain.013.
        /// </summary>
        public string GeneratePain014Xml(string originalMsgId, string endToEndId, string status)
        {
            var newMsgId = $"MSG-{DateTime.UtcNow:yyyyMMdd}-RPT-{Guid.NewGuid().ToString().Substring(0, 8)}";
            var dtTm = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
            var stsId = $"STS-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 4)}";

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Document xmlns=""urn:iso:std:iso:20022:tech:xsd:pain.014.001.07"">
  <CdtrPmtActvtnRpt>
    <GrpHdr>
      <MsgId>{newMsgId}</MsgId>
      <CreDtTm>{dtTm}</CreDtTm>
    </GrpHdr>
    <OrgnlGrpInfAndSts>
      <OrgnlMsgId>{originalMsgId}</OrgnlMsgId>
      <GrpSts>{status}</GrpSts>
    </OrgnlGrpInfAndSts>
    <TxInfAndSts>
      <StsId>{stsId}</StsId>
      <OrgnlEndToEndId>{endToEndId}</OrgnlEndToEndId>
      <TxSts>{status}</TxSts>
      <AccptncDtTm>{dtTm}</AccptncDtTm>
    </TxInfAndSts>
  </CdtrPmtActvtnRpt>
</Document>";
        }

        /// <summary>
        /// Generate a pacs.008 XML from an incoming pain.013 (payment request).
        /// Uses our bank as the debtor (we are being asked to pay).
        /// </summary>
        public string GeneratePacs008FromPain013(
            string endToEndId,
            decimal amount,
            string dbtrRtn,
            string dbtrName,
            string dbtrAcctId,
            string cdtrRtn,
            string cdtrName,
            string cdtrAcctId,
            string memo)
        {
            var msgId = $"MSG-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 8)}";
            var dtTm = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Document xmlns=""urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08"">
  <FIToFICstmrCdtTrf>
    <GrpHdr>
      <MsgId>{msgId}</MsgId>
      <CreDtTm>{dtTm}</CreDtTm>
    </GrpHdr>
    <CdtTrfTxInf>
      <PmtId>
        <EndToEndId>{endToEndId}</EndToEndId>
      </PmtId>
      <IntrBkSttlmAmt Ccy=""USD"">{amount:0.00}</IntrBkSttlmAmt>
      <DbtrAgt>
        <FinInstnId>
          <ClrSysMmbId>
            <nm>{dbtrName}</nm>
            <MmbId>{dbtrRtn}</MmbId>
          </ClrSysMmbId>
        </FinInstnId>
      </DbtrAgt>
      <Dbtr>
        <Nm>{dbtrName}</Nm>
      </Dbtr>
      <DbtrAcct>
        <Id>
          <Othr>
            <Id>{dbtrAcctId}</Id>
            <SchmeNm><Prtry>US_ACCT</Prtry></SchmeNm>
          </Othr>
        </Id>
      </DbtrAcct>
      <CdtrAgt>
        <FinInstnId>
          <ClrSysMmbId>
            <nm>{cdtrName}</nm>
            <MmbId>{cdtrRtn}</MmbId>
          </ClrSysMmbId>
        </FinInstnId>
      </CdtrAgt>
      <Cdtr>
        <Nm>{cdtrName}</Nm>
      </Cdtr>
      <CdtrAcct>
        <Id>
          <Othr>
            <Id>{cdtrAcctId}</Id>
            <SchmeNm><Prtry>US_ACCT</Prtry></SchmeNm>
          </Othr>
        </Id>
      </CdtrAcct>
      <RmtInf>
        <Ustrd>{memo}</Ustrd>
      </RmtInf>
    </CdtTrfTxInf>
  </FIToFICstmrCdtTrf>
</Document>";
        }

        /// <summary>
        /// Parse incoming XML and return the detected message type and extracted data.
        /// </summary>
        public static (string messageType, XDocument doc, XElement root)? ParseIncomingXml(string xml)
        {
            try
            {
                var doc = XDocument.Parse(xml);
                var root = doc.Root;
                if (root == null) return null;

                var ns = root.Name.Namespace.NamespaceName;
                
                if (ns.Contains("pacs.008"))
                    return ("pacs.008", doc, root);
                else if (ns.Contains("pacs.002"))
                    return ("pacs.002", doc, root);
                else if (ns.Contains("pain.013"))
                    return ("pain.013", doc, root);
                else if (ns.Contains("pain.014"))
                    return ("pain.014", doc, root);
                
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
