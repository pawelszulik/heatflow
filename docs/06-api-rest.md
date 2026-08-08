# 6. REST API (HeatFlow.Api)

**HeatFlow.Api** to aplikacja ASP.NET Core udostepniajaca REST API do odczytu i modyfikacji konfiguracji systemu. Dziala jako osobna usluga (Windows Service lub Kestrel) i jest jedynym oficjalnym punktem wejscia do zmiany ustawien pokojow oraz parametrow algorytmu.

> **Uwaga:** Pelna specyfikacja endpointow (formaty JSON, kodowanie, przyklady curl) znajduje sie w osobnym dokumencie: **[HeatFlow.Api-Kontrakt.md](HeatFlow.Api-Kontrakt.md)**. Ponizszy dokument stanowi uzupelnienie kontekstowe.

---

## Rola API w systemie

Api **nie wykonuje algorytmu grzania** i nie komunikuje sie bezposrednio z Home Assistant. Jego jedynym zadaniem jest:

- udostepnienie konfiguracji pokojow i parametrow algorytmu,
- przyjmowanie zmian konfiguracyjnych z zewnatrz,
- rejestrowanie historii zmian (audyt),
- udostepnianie logow bledow i statusu systemu.

Glownym konsumentem API jest **integracja Home Assistant (custom component)**, ale mozesz rowniez korzystac z API bezposrednio (np. z innych systemow automatyki, skryptow lub aplikacji frontendowych).

---

## Uwierzytelnienie

Wszystkie zadania do sciezek `/api/*` wymagaja naglowka HTTP:

```
X-API-Key: TWOJ_KLUCZ_API
```

Klucz jest definiowany w konfiguracji Api (`appsettings.json`):

```json
{
  "HeatFlow": {
    "ApiKey": "DLOUGI_LOSOWY_KLUCZ_MINIMUM_32_ZNAKOW"
  }
}
```

Brak klucza lub bledna wartosc skutkuje odpowiedzia **401 Unauthorized**.

Opcjonalny naglowek `X-Source` (np. `home_assistant`) jest zapisywany w audycie jako zrodlo zmiany.

---

## Endpointy – przeglad

| Metoda | Sciezka | Opis |
|--------|---------|------|
| GET | `/api/rooms` | Lista wszystkich pokojow. |
| GET | `/api/rooms/{name}` | Szczegoly jednego pokoju. |
| PUT | `/api/rooms/{name}` | Pelna aktualizacja pokoju. |
| GET | `/api/heating-parameters` | Biezace parametry algorytmu. |
| PUT | `/api/heating-parameters` | Pelna aktualizacja parametrow. |
| PATCH | `/api/heating-parameters` | Czesciowa aktualizacja (tylko podane pola). |
| GET | `/api/configuration-changes` | Historia zmian konfiguracji (audyt). |
| GET | `/api/error-logs` | Dziennik bledow z Console i Api. |
| GET | `/api/health` | Health check systemu. |

Szczegolowe opisy, przyklady zadan i odpowiedzi JSON znajduja sie w **[HeatFlow.Api-Kontrakt.md](HeatFlow.Api-Kontrakt.md)**.

---

## Audyt zmian

Kazde pomyslne wywolanie `PUT` lub `PATCH` na pokojach lub parametrach jest automatycznie rejestrowane w tabeli `ConfigurationChangeLog`.

Api porownuje obiekt przed i po zmianie (refleksja, property-by-property) i zapisuje tylko roznice.

Przykladowy rekord audytu:

```json
{
  "id": 1,
  "timestamp": "2025-02-04T12:00:00Z",
  "entityType": "Room",
  "entityId": "Sypialnia",
  "fieldName": "TempTarget",
  "oldValue": "20",
  "newValue": "21",
  "source": "home_assistant"
}
```

Audyt mozna odczytac przez endpoint `GET /api/configuration-changes` lub bezposrednio w bazie danych.

---

## Logi bledow

Aplikacje Console i Api zapisuja wyjatki do wspolnej tabeli `ApplicationErrorLog`. Api udostepnia je przez endpoint:

```
GET /api/error-logs?from=2025-01-01T00:00:00Z&to=2025-12-31T23:59:59Z&limit=100
```

Dostepne parametry filtrowania:

- `from`, `to` – zakres dat (UTC),
- `phase` – numer fazy (0–4),
- `source` – nazwa komponentu,
- `origin` – `Console` lub `Api`,
- `limit` – liczba wynikow (1–500, domyslnie 100).

Przykladowa odpowiedz:

```json
[
  {
    "id": 42,
    "occurredAtUtc": "2025-02-04T08:15:00Z",
    "source": "Phase3ValvesService",
    "phase": 3,
    "message": "Nie udalo sie ustawic zaworu climate.sypialnia",
    "exceptionType": "HttpRequestException",
    "stackTrace": "...",
    "severity": "Error",
    "origin": "Console"
  }
]
```

---

## CORS

Api obsluguje CORS, co umozliwia wywolywanie go bezposrednio z przegladarki (np. z frontendu Home Assistant). Dozwolone originy konfiguruje sie w `appsettings.json`:

```json
{
  "Cors": {
    "AllowedOrigins": [ "http://192.168.1.100:8123", "http://localhost:8123" ]
  }
}
```

Domyslnie CORS jest otwarte (dla developmentu). W produkcji zaleca sie jawne wskazanie adresow HA.

---

## Nastepny krok

Aby polaczyc API z Home Assistant, przejdz do dokumentu [07-integracja-home-assistant.md](07-integracja-home-assistant.md).
