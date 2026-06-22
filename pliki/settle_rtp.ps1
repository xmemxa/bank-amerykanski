$txs = Invoke-RestMethod -Uri 'http://localhost:8000/transactions'
$pending = $txs | Where-Object { $_.status -eq 'PENDING' -and $_.receiver -eq 'BANKB' } | Select-Object -First 1

if ($pending) {
    $e2e = $pending.message_id
    $xml = @"
<?xml version="1.0" encoding="UTF-8"?>
<Document xmlns="urn:iso:std:iso:20022:tech:xsd:pacs.002.001.10">
  <FIToFIPmtStsRpt>
    <GrpHdr><MsgId>MSG1</MsgId></GrpHdr>
    <OrgnlGrpInfAndSts><GrpSts>ACCP</GrpSts></OrgnlGrpInfAndSts>
    <TxInfAndSts>
      <OrgnlEndToEndId>$e2e</OrgnlEndToEndId>
      <TxSts>ACCP</TxSts>
    </TxInfAndSts>
  </FIToFIPmtStsRpt>
</Document>
"@

    $keyResponse = Invoke-RestMethod -Uri 'http://localhost:8000/banks/BANKB/reset-key' -Method Post
    $apiKey = $keyResponse.new_api_key

    Invoke-RestMethod -Uri 'http://localhost:8000/transfers/settle' -Method Post -Headers @{'x-api-key'=$apiKey; 'Content-Type'='application/xml'} -Body $xml | Out-Null
    
    Write-Host "Zatwierdzono przelew RTP: $e2e" -ForegroundColor Green
} else {
    Write-Host "Brak oczekujacych przelewow RTP do zatwierdzenia" -ForegroundColor Yellow
}
