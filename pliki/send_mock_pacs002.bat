@echo off
echo Wysylanie pliku mock_pacs002.xml do symulatora FedNow...
curl.exe -X POST "http://localhost:8771/send" -H "accept: application/json" -H "Content-Type: multipart/form-data" -F "file=@%~dp0mock_pacs002.xml;type=text/xml"
echo.
echo.
pause
