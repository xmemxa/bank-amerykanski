# Integracje - Przewodnik Operacyjny

Dokument ten opisuje w jaki sposób testować i uruchamiać poszczególne integracje między naszym "Amerykańskim Bankiem", a systemami stworzonymi w oddzielnych repozytoriach. Zawiera wskazówki informujące w jakich plikach szukać danych rozwiązań i jak wywoływać testowe zachowania.

## 1. System Kart Płatniczych (Moduł Zewnętrzny)

Integracja zakłada weryfikację transakcji w zewnętrznym systemie `Karty Platnicze Aplikacje Biznesowe`.

*   **Jak to działa:**
    - Karta wygenerowana w naszym systemie Blazor posiada unikalny numer i jest podpięta pod dany rachunek bankowy.
    - W panelu **Cards** (karty) użytkownik może przeglądać stan i numery swoich kart. Pod spodem karty są trzymane w naszej własnej bazie oraz zgłoszone do systemu zewnętrznego Gateway'a.
*   **Jak przetestować transakcję u sprzedawcy:**
    1. Aby symulować płatność, wyślij żądanie autoryzacyjne w Swaggerze serwisu zewnętrznego.
    2. Zewnętrzny system wysyła Webhook pod nasz adres, my sprawdzamy czy konto powiązane z kartą ma na to wystarczające środki.
    3. Transakcje trafiają do naszej bazy początkowo jako zablokowane środki (Hold).
*   **Kluczowe pliki w tym projekcie (związane z tą pracą):**
    - `Controllers/CardWebhookController.cs` (tu trafiają wywołania z systemu kartowego)
    - `Services/ExternalPayments/CardService.cs` (logika zarządzająca komunikacją)
    - `Components/Pages/Cards.razor` (interfejs użytkownika do zarządzania kartami)

## 2. Przelewy SWIFT (Cross-Border)

Globalna sieć do przelewów za granicę wykorzystująca banki korespondencyjne.
*   **Jak przetestować:**
    1. Zaloguj się na konto "Customer" w naszym banku.
    2. W panelu zrób nowy przelew wybierając opcję **SWIFT**. Podaj nazwę odbiorcy oraz kod BIC/SWIFT.
    3. Pieniądze pobierane są z Twojego konta i zamieniane na status `PENDING_SWIFT_NETWORK`.
    4. W tle komunikacja odbywa się z kontenerem `swift-app` pod zmapowanym adresem (domyślnie `http://host.docker.internal:3000`). Po udanym rozliczeniu międzybankowym status transakcji ulega zmianie na ukończony i naliczana jest opłata manipulacyjna banku korespondenta.
*   **Kluczowe pliki do modyfikacji i zarządzania:**
    - `Controllers/SwiftWebhookController.cs` (Odbiornik komunikatów potwierdzających ze SWIFT)
    - `Services/ExternalPayments/SwiftService.cs` (Inicjacja przelewu na zewnątrz)

## 3. Przelewy Natychmiastowe "Klik" (Model P2P / Blik / Zelle)

System Klik działa w trybie natychmiastowym i potrafi łączyć użytkowników po numerze telefonu (Zelle) lub kodzie (BLIK).
*   **Wysyłanie pieniędzy (P2P na numer telefonu):**
    1. Wykonaj przelew typu "Klik" podając w oknie numer telefonu odbiorcy zaczynający się od `+48...` lub `+1...` zamiast numeru konta.
    2. Moduł zewnętrzny rozwiąże ten numer telefonu i znajdzie bank docelowy (np. Polish Bank B lub inny, jeśli mają Klik'a).
*   **Użycie Kodu 6-cyfrowego (Proces z autoryzacją):**
    1. Kliknij sekcję "Klik Code" w naszym UI. Skrypt w `KlikCode.razor` wygeneruje ważny krótko token autoryzacyjny i wyświetli go na ekranie.
    2. W zewnętrznym sklepie podajesz ten kod. Sklep pinguje system Klik, a system Klik uderza pod nasz Webhook.
    3. W aplikacji bankowej wyskakuje ekran `KlikAuthorize.razor`, w którym musisz zaakceptować zrzuconą na Ciebie płatność, aby poszła ona dalej.
*   **Kluczowe pliki u nas:**
    - `Services/ExternalPayments/KlikService.cs` (rdzeń integracji HTTP z Klikiem)
    - `Components/Pages/KlikCode.razor` (widok generowania kodu)
    - `Components/Pages/KlikAuthorize.razor` (widok akceptacji transakcji POS / e-Commerce)
    - `Controllers/KlikWebhookController.cs` (kontroler wywoływany przez agenta rozliczeniowego)

## 4. Przelewy RTP (The Clearing House) oraz ręczna symulacja

*   RTP korzysta z formatu XML ISO 20022. Rozliczenie dwufazowe oznacza, że nie możemy od razu ustalić statusu COMPLETED na przelewie bez wyraźnej zwrotki (`pacs.002`) ze strony banku przeciwnego.
*   **Gdy przelew zatrzymał się na PENDING (Co trzeba kliknąć?):**
    1. Stworzyliśmy zautomatyzowane skrypty do debugowania i pchania transakcji do przodu. Odszukaj na Pulpicie systemowym dwa pliki: `mock_pacs002.xml` oraz `settle_rtp.bat`.
    2. Otwórz `mock_pacs002.xml` w IDE. Znajdź sekcję `<OrgnlEndToEndId>`. Jeśli masz w systemie "zawieszony" przelew RTP (PENDING), skopiuj jego Id z bazy (lub z panelu logów pracownika) i wklej w to miejsce.
    3. Zapisz XML i uruchom dwuklikiem plik `settle_rtp.bat`.
    4. Ten skrypt `.bat` symuluje odpowiedź od zewnętrznego banku (tak zwaną Clearing House Network) o akceptacji przelewu i uderza z XML-em prosto w nasz `RtpWebhookController`.
    5. Po odświeżeniu przeglądarki przelew w "Banku Amerykańskim" natychmiastowo zmieni status z PENDING na COMPLETED.

*   **Kluczowe pliki w naszym projekcie:**
    - `Controllers/RtpWebhookController.cs` (Odbiera pliki XML z potwierdzeniami)
    - `Services/ExternalPayments/RtpService.cs` (Wysyła XML-e `pacs.008` o żądaniu zapłaty)

## 5. FedNow & ACH

*   **FedNow:** To państwowy odpowiednik RTP. Posiada kolejkę wiadomości HTTP na zdefiniowanych portach w `appsettings.json` (np. `http://host.docker.internal:8770/FIFO/out`). Działa automatycznie jeśli lokalne usługi FedSystems są podniesione (nie potrzebuje .batów).
*   **ACH:** Używa tradycyjnych procesów SFTP do zrzutu dziennych paczek `.txt` zgodnych z formatem NACHA. Pliki przesyłane są przez protokół SSH do lokalnego kontenera `fedsystems-sftp`.
*   **Co i gdzie zmieniać?** Moduły za to odpowiedzialne to `FedNowService.cs` i `AchService.cs`. Tam zbudowane są handlery do bibliotek sFTP.

## 6. Lista kroków - Uruchomienie na nowym komputerze

Poniżej znajduje się skrócona lista konkretnych zmian w kodzie/konfiguracji, które musisz wykonać na świeżym środowisku, aby spiąć wszystkie mikroserwisy.

### A. System Kart Płatniczych (`Karty-Platnicze-Aplikacje-Biznesowe`)

**W repozytorium systemu kart (`Karty-Platnicze-Aplikacje-Biznesowe/docker-compose.yaml`):**
Jeśli pobierasz z chmury świeżą, "czystą" wersję systemu kart, musisz dostosować ją do naszego banku, upewniając się, że kontener widzi nasz bank w sieci zewnętrznej:

1. Otwórz plik `docker-compose.yaml` w głównym katalogu `Karty-Platnicze-Aplikacje-Biznesowe`.
2. Zmień URL webhooka banku na właściwy port i ścieżkę API naszego głównego backendu:
   ```yaml
   BANK_CAPTURE_URL_US_BANK_A: "http://us-bank-a:8080/api/v1"
   ```
3. Zmień typ sieci aplikacyjnej (`cards-backend` lub dodaj osobną sieć w której działa bank) na zewnętrzną:
   ```yaml
   networks:
     cards-backend:
       external: true
   ```

### B. KLIK-payments (Przelewy P2P i BLIK)

**W repozytorium KLIK (`KLIK-payments`):**
1. Wygeneruj nowy klucz API (skryptem lub w panelu admina) i oblicz jego hash (SHA-256).
2. Dodaj bank bezpośrednio do bazy danych KLIK (np. używając polecenia w terminalu):
   ```bash
   docker exec -it klik-payments-db-1 psql -U klik -d klik
   ```
3. Wykonaj zapytanie SQL, uzupełniając swoje dane:
   ```sql
   INSERT INTO banks_bank (id, created_at, updated_at, name, api_key_hash, zone, currency, debt_limit, active, bic, settlement_iban, fednow_routing_number, fednow_account_number, webhook_url, c2b_enabled, p2p_enabled, p2p_lookup_fee, cheques_enabled, cheques_webhook_url, recurring_enabled, recurring_webhook_url)
   VALUES (gen_random_uuid(), NOW(), NOW(), 'BANK_AMERYKANSKI', 'TUTAJ_TWÓJ_HASH_SHA256', 'US', 'USD', 1000000, true, '', '', '040104018', 'TUTAJ_NUMER_KONTA', 'http://host.docker.internal:8080/webhook', true, true, 0, false, '', false, '');
   ```

**W repozytorium banku (`bank-amerykanski/docker-compose.yaml`):**
1. Otwórz plik `docker-compose.yaml` w głównym katalogu Banku Amerykańskiego.
2. W sekcji `environment` kontenera backendowego wklej wygenerowany klucz (czystym tekstem, bez hashowania):
   ```yaml
   - Klik__ApiKey=twój_niezaszyfrowany_klucz
   ```

### C. FedSystems (FedNow, RTP, ACH)

**W repozytorium testowego banku B (`us-bank-system/docker-compose.yaml`):**
1. Otwórz plik `docker-compose.yaml` w głównym katalogu `us-bank-system`.
2. Zaktualizuj numery rozliczeniowe i porty, aby banki nie kolidowały ze sobą (podmień poniższe wartości na swoje):
   ```yaml
   - Bank__RoutingNumber=010101012
   - PaymentSessions__FedNow__BankRtn=010101012
   - PaymentSessions__FedNow__BankLegalName=Bank B
   - PaymentSessions__Rtp__BankCode=bank-b
   - PaymentSessions__Rtp__BankRtn=010101012
   - PaymentSessions__Rtp__BankLegalName=Bank B
   - Ach__RoutingNumber=010101012
   - Ach__LegalName=Bank B
   - Integrations__FedNowMqUrl=http://host.docker.internal:8771
   ```
3. **Konfiguracja kluczy SFTP dla ACH:** W sekcji `environment` musisz określić nazwę użytkownika (np. `leek-bank`) oraz wyłączyć sprawdzanie kluczy hosta, jeśli stawiasz środowisko od zera i klucze serwera SFTP uległy zmianie:
   ```yaml
   - Ach__Sftp__Username=leek-bank
   - Ach__Sftp__AllowUncheckedFingerprint=true
   ```
4. **Wstrzyknięcie klucza prywatnego (wolumeny):** Kontener banku musi otrzymać fizyczny klucz kryptograficzny `id_rsa`. Upewnij się, że w sekcji `volumes` masz podpiętą właściwą ścieżkę z Twojego dysku:
   ```yaml
   volumes:
     - ../payment-settlement-systems/FedSystems/SFTP_Keys/leek-bank:/sftp_keys:ro
   ```
   *(Pamiętaj, że w tym katalogu na dysku, np. `.../FedSystems/SFTP_Keys/leek-bank`, musi realnie leżeć pobrany od operatora plik `id_rsa`)*

### D. SWIFT

**W repozytorium systemu SWIFT (`SWIFT-Aplikacje-Biznesowe`):**
Jeśli uruchamiasz czyste środowisko SWIFT, musisz wskazać mu lokalizację naszego banku:
1. Otwórz plik `app/core/config.py`.
2. W słowniku `BANK_METADATA` zlokalizuj wpis `"USBKUS01XXX"` (przypisany naszemu bankowi) i podmień go na:
   ```python
   "USBKUS01XXX": {"name": "Bank Amerykanski", "country": "US", "url": "http://host.docker.internal:8080/api/swift/receive", "external": True},
   ```
   *(Najważniejsze zmiany to `url` webhooka skierowany na port `8080` oraz włączenie flagi `"external": True`)*

**W repozytorium banku (`bank-amerykanski/docker-compose.yaml`):**
1. Otwórz plik `docker-compose.yaml` w głównym katalogu Banku Amerykańskiego.
2. Sprawdź, czy dane dostępowe pokrywają się z lokalną instancją zewnętrznej bramki `swift-app`:
   ```yaml
   - ExternalPayments__SwiftClientId=bank-usbkus01
   - ExternalPayments__SwiftClientSecret=secret-usbkus01
   ```

## Konfiguracja Zmienna dla Środowisk Integracyjnych

Jeśli którykolwiek z serwerów innej grupy zmieni swój port podczas uruchomienia z Docker'a, w `Bank Amerykańskim` musisz wejść do pliku `docker-compose.yaml` (w katalogu głównym) i odszukać sekcję zmiennych konfiguracyjnych w kontenerze `backend`:

```yaml
    environment:
      - ExternalPayments__FedNowApiUrl=http://host.docker.internal:8770
      - ExternalPayments__RtpApiUrl=http://host.docker.internal:8000
      - ExternalPayments__SwiftApiUrl=http://host.docker.internal:3000
      - ExternalPayments__SwiftClientId=bank-usbkus01
      - ExternalPayments__SwiftClientSecret=secret-usbkus01
      - ExternalPayments__AchApiUrl=http://host.docker.internal:8310
      - ExternalPayments__AchSftpHost=host.docker.internal
      - Klik__ApiUrl=http://host.docker.internal:8001
      - Klik__ApiKey=twoj-klik-api-key
```

Zapisz zmiany w `docker-compose.yaml` i przebuduj instancję poleceniem:
`docker compose up -d` 
(jeśli zmieniły się kodowe wdrożenia: `docker compose up -d --build`).
