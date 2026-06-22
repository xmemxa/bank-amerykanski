# American Bank (Bank Amerykański)

Aplikacja webowa symulująca działanie nowoczesnego banku amerykańskiego. Głównym zadaniem platformy jest orkiestracja płatności oraz integracja z zewnętrznymi dostawcami infrastruktury clearingowej i autoryzacyjnej w USA (i globalnie).

## 1. Zakres funkcjonalny

- **FedNow** — natychmiastowe płatności krajowe w USA (24/7/365) poprzez sieć Rezerwy Federalnej.
- **RTP (Real-Time Payments)** — sieć płatności natychmiastowych dostarczana przez The Clearing House (TCH).
- **ACH (Automated Clearing House)** — standardowe przelewy krajowe, rozliczenie w sesjach.
- **SWIFT** — globalna sieć komunikacji finansowej do przelewów zagranicznych.
- **Klik** — system przelewów natychmiastowych P2P wzorowany na rozwiązaniach typu Zelle/Blik, integrujący wiele amerykańskich banków.
- **Karty płatnicze** — integracja z symulowanym operatorem kart i siecią akceptantów.
- **Konta Junior** — konta powiązane z kontem rodzica, umożliwiające kontrolę wydatków.

## 2. Architektura i Stos Technologiczny

| **Warstwa**        | **Technologia**                               |
| ------------------ | --------------------------------------------- |
| **Backend / UI**   | C# 12 + .NET 8 (Blazor Interactive Server)    |
| **Baza danych**    | PostgreSQL 16                                 |
| **ORM**            | Entity Framework Core                         |
| **Auth**           | Custom AuthenticationStateProvider (Cookies)  |
| **Konteneryzacja** | Docker + Docker Compose                       |
| **Architektura**   | Monolit z komunikacją asynchroniczną          |

## 3. Wiedza Domenowa (Architektura Płatności)

System opiera się o kilka głównych dróg przetwarzania transakcji. Poprawne mapowanie logiki ma kluczowe znaczenie.

### 3.1. FedNow
System obsługiwany przez Rezerwę Federalną, pozwalający na realizację przelewów w czasie rzeczywistym.
- Wymaga stałej dostępności API po stronie banku (Webhooks/Message Queue).
- Oparty na formacie komunikatów **ISO 20022**.

### 3.2. RTP (The Clearing House)
Prywatna sieć rozliczeń natychmiastowych (RTP).
- Wykorzystuje komunikaty pacs.008 (przelew) oraz pacs.002 (potwierdzenie).
- Działa w trybie dwuetapowym: bank nadawcy wysyła żądanie do TCH, sieć RTP przesyła je do banku docelowego i po zatwierdzeniu generuje finalne potwierdzenie.

### 3.3. ACH (Automated Clearing House)
Rozliczenia w paczkach (batch settlement). Transakcje są zbierane przez cały dzień i przetwarzane wieczorem. Transakcje te są z reguły darmowe lub bardzo tanie, ale mogą zajmować 1-3 dni robocze.

### 3.4. SWIFT (Cross-Border Payments)
Przelewy o zasięgu globalnym, przechodzące przez banki korespondentów (Nostro/Vostro).
- Komunikaty formatu MT lub MX (ISO 20022).
- Wiążą się z dodatkowymi opłatami i przewalutowaniem (Forex).

## 4. Diagramy Architektoniczne

Poniższe diagramy stworzono przy pomocy Mermaid, dzięki czemu mogą być renderowane i edytowane bezpośrednio w IDE.

### Model domenowy (UML Class Diagram)

```mermaid
classDiagram
    direction TB

    class User {
        +String Id
        +String Username
        +String PasswordHash
        +String Email
        +String Role
        +String FirstName
        +String LastName
    }

    class Account {
        +String Id
        +String UserId
        +String AccountNumber
        +String AccountType
        +Decimal Balance
        +String Currency
        +String ParentAccountId
        +DateTime CreatedAt
    }

    class Transaction {
        +String Id
        +String SourceAccountId
        +String DestinationAccountNumber
        +Decimal Amount
        +String Currency
        +String Description
        +String Status
        +String Type
        +DateTime Timestamp
    }

    class Card {
        +String Id
        +String AccountId
        +String CardNumber
        +String ExpiryDate
        +String Cvv
        +String Status
        +String Pin
    }

    User "1" *-- "0..*" Account : owns
    Account "0..1" --> "0..*" Account : Parent/Junior
    Account "1" *-- "0..*" Card : has
    Account "1" --> "0..*" Transaction : creates
```

### Przepływ procesu RTP (BPMN - Sequence Diagram)

```mermaid
sequenceDiagram
    participant U as "User (Sender)"
    participant B1 as "Bank Amerykański (API)"
    participant RTP as "Sieć TCH RTP"
    participant B2 as "Bank Odbiorcy"

    U->>B1: Inicjacja przelewu (Submit Transfer)
    activate B1
    B1->>B1: Walidacja salda i limitów
    B1->>B1: Ustawienie statusu: PENDING
    B1->>RTP: pacs.008 (Payment Request)
    deactivate B1
    
    activate RTP
    RTP->>B2: pacs.008 (Forward to Receiver)
    activate B2
    B2->>B2: Weryfikacja konta docelowego
    B2-->>RTP: pacs.002 (Acceptance)
    deactivate B2
    
    RTP-->>B1: pacs.002 (Final Status / Settle)
    deactivate RTP
    
    activate B1
    B1->>B1: Pobranie środków z konta nadawcy
    B1->>B1: Ustawienie statusu: COMPLETED
    B1-->>U: Potwierdzenie wysłania
    deactivate B1
```

## 5. Konfiguracja Środowiska i Uruchomienie

1. Wymagany Docker oraz Docker Compose.
2. Sklonuj repozytorium.
3. Uzupełnij konfigurację `.env` na wzór swoich środowisk zewnętrznych (w repozytorium udostępniono `.env.example` lub zaszyte dane pod lokalnego docker'a).
4. Uruchom polecenie:
   `docker compose up -d --build`
5. Aplikacja Blazor Server będzie dostępna lokalnie na zmapowanym porcie (domyślnie `http://localhost:8080`). Z poziomu tej aplikacji serwowany jest zarówno backend, jak i frontend w nowym trybie .NET 8 Interactive Server.

## 6. Zespół i Repozytoria Integracyjne

- **American Bank (Bank Główny)**: Główne repozytorium z interfejsem dla klientów detalicznych i pracowników.
- **Karty Płatnicze (Payment Gateway)**: Niezależny moduł/kontener obsługujący procesowanie transakcji kartowych.
- **Systemy Fed (FedNow / ACH)**: Środowisko udające procesy amerykańskich systemów płatności wewnątrz sieci Fed.
- **Klik**: Zewnętrzna sieć wzorowana na BLIK / Zelle do płatności mobilnych i natychmiastowych (kody P2P i pay-by-link).
- **RTP**: Oddzielne środowisko emulujące The Clearing House.

Szczegółowa instrukcja i wykaz tego, co jak testować w przypadku powyższych integracji, znajduje się w osobnym pliku `INTEGRATIONS.md`.