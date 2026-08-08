# 2. Architektura i komponenty

## Filozofia architektoniczna

HeatFlow został zbudowany zgodnie z zasadą **separacji odpowiedzialności** i wzorcem **warstwowym** (Layered Architecture). Logika biznesowa jest całkowicie odseparowana od szczegółów technicznych, takich jak komunikacja z Home Assistant, dostęp do bazy danych czy interfejs HTTP. Dzięki temu:

- łatwiej testować logikę biznesową (34 testy jednostkowe),
- można wymieniać źródła danych (np. inny system automatyki) bez zmiany algorytmu,
- kod jest bardziej przejrzysty i łatwiejszy w utrzymaniu.

---

## Struktura rozwiązania (Solution)

Solution `HeatFlow.sln` zawiera projekty podzielone na dwie grupy: `src/` (kod produkcyjny) oraz `tests/` (testy).

### Projekty produkcyjne (`src/`)

| Projekt | Warstwa | Opis |
|---------|---------|------|
| **HeatFlow.Domain** | Domena | Encje, enumy, modele domenowe i interfejsy abstrakcyjne. Nie zależy od żadnego innego projektu. |
| **HeatFlow.Core** | Logika biznesowa | Implementacja 5 faz algorytmu (Phase 0–4) oraz klasy pomocnicze (ScheduleHelper, TemperatureHelper). Zależy tylko od Domain. |
| **HeatFlow.Application** | Aplikacja | Orkiestracja faz (`OrchestrationService`) oraz zapis wyników do bazy (`DataPersistenceService`). Zależy od Domain i Core. |
| **HeatFlow.Infrastructure** | Infrastruktura | Szczegóły techniczne: klient Home Assistant, klient OpenWeatherMap, Entity Framework (SQL Server), repozytoria, serwisy konfiguracyjne, logowanie błędów. Zależy od Domain i Application. |
| **HeatFlow.Console** | Aplikacja hostująca | Konsolowy host uruchamiający algorytm. Rejestruje wszystkie zależności w kontenerze DI i wywołuje `OrchestrationService`. Zależy od wszystkich pozostałych. |
| **HeatFlow.Api** | Aplikacja hostująca | ASP.NET Core REST API do zarządzania konfiguracją. Działa jako usługa Windows lub samodzielnie. Zależy od Infrastructure i Domain. |

### Projekty testowe (`tests/`)

| Projekt | Co testuje |
|---------|------------|
| **HeatFlow.Core.Tests** | 28 testów jednostkowych – wszystkie fazy algorytmu oraz helpery. |
| **HeatFlow.Infrastructure.Tests** | 4 testy – klient Home Assistant. |
| **HeatFlow.Application.Tests** | 2 testy – orkiestracja i zapis wyników. |
| **HeatFlow.Api.Tests** | Testy kontrolerów API. |

---

## Diagram warstw

Zależności między projektami wyglądają następująco (strzałka = „zależy od”):

```
┌─────────────────────────────────────────────┐
│         HeatFlow.Console                    │
│    (Scheduled Task / tryb ciągły)           │
└─────────────┬───────────────────────────────┘
              │
┌─────────────▼───────────────────────────────┐
│         HeatFlow.Api                        │
│    (REST API, Windows Service)              │
└─────────────┬───────────────────────────────┘
              │
┌─────────────▼───────────────────────────────┐
│      HeatFlow.Application                   │
│  (OrchestrationService,                     │
│   DataPersistenceService)                   │
└─────────────┬───────────────────────────────┘
              │
┌─────────────▼───────────────────────────────┐
│        HeatFlow.Core                        │
│  (Phase 0–4, ScheduleHelper,                │
│   TemperatureHelper)                        │
└─────────────┬───────────────────────────────┘
              │
┌─────────────▼───────────────────────────────┐
│       HeatFlow.Domain                       │
│  (Room, BoilerState, HeatingParameters,     │
│   Enums, Interfaces)                        │
└─────────────────────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────┐
│    HeatFlow.Infrastructure                  │
│  (HomeAssistantClient, EF Core,             │
│   Repositories, OpenWeatherMapClient)       │
└─────────────────────────────────────────────┘
```

> **Zasada:** Domain jest na samym dole i nic od nikogo nie importuje. Infrastructure jest na górze – dostarcza implementacje interfejsów zdefiniowanych w Domain. Console i Api są niezależnymi hostami, które łączą wszystko w całość.

---

## Jak Console i Api współdzielą bazę danych

Oba programy korzystają z **tej samej bazy danych SQL Server**, ale pełnią różne role:

| Aspekt | HeatFlow.Console | HeatFlow.Api |
|--------|------------------|--------------|
| **Główna rola** | Wykonawca algorytmu | Panel konfiguracyjny |
| **Co czyta z DB** | Konfigurację pokojów, parametry, cache prognozy | Konfigurację, logi, audyt |
| **Co zapisuje do DB** | Historię wykonania, stany pokojów/pieca/zaworów, logi błędów, letni/zimny | Konfigurację pokojów, parametry, audyt zmian, logi błędów |
| **Czy modyfikuje config** | Nie (tylko Faza 0 mnoży parametry tymczasowo) | Tak (PUT/PATCH pokoi i parametrów) |
| **Czy komunikuje się z HA** | Tak (odczyt sensorów, zapis zaworów/pieca) | Nie |

**Przepływ konfiguracji:**

1. Użytkownik zmienia temperaturę pokoju w interfejsie Home Assistant.
2. Integracja **HeatFlow (custom component)** wysyła żądanie `PUT /api/rooms/{name}` do **HeatFlow.Api**.
3. Api zapisuje nową wartość w tabeli `RoomConfiguration` w bazie danych oraz rejestruję zmianę w `ConfigurationChangeLog`.
4. Przy kolejnym uruchomieniu (co minutę) **HeatFlow.Console** odczytuje zaktualizowaną konfigurację i stosuje ją w algorytmie.

---

## Baza danych jako źródło prawdy

Wcześniejsze wersje systemu przechowywały konfigurację częściowo w Home Assistant (helpery `input_number`, `input_boolean`). Obecnie **cała konfiguracja jest w bazie danych**:

- **`SystemConfiguration`** – lista pokojów, numer seryjny pieca, identyfikatory encji HA, współrzędne geograficzne, włącznik systemu.
- **`RoomConfiguration`** – dla każdego pokoju: temperatury docelowe, priorytet, wrażliwość, harmonogramy, mapowania encji HA.
- **`HeatingParameters`** – wszystkie parametry algorytmu (progi deficytów, mnożniki prognozy, parametry zaworów i pieca, bezpieczeństwo).

Home Assistant jest używane **wyłącznie** jako źródło danych rzeczywistych (temperatury) oraz aktuator (zawory, piec). Nie przechowuje już konfiguracji systemu.

---

## Główne interfejsy abstrakcyjne (z Domain)

| Interfejs | Dostarcza implementację | Rola |
|-----------|------------------------|------|
| `IHomeAssistantClient` | `HomeAssistantClient` (Infrastructure) | Odczyt i zapis encji w Home Assistant przez REST API. |
| `IOpenWeatherMapClient` | `OpenWeatherMapClient` (Infrastructure) | Pobieranie prognozy pogody z API OpenWeatherMap. |
| `IHeatFlowRepository` | `HeatFlowRepository` (Infrastructure) | Dostęp do bazy danych (CRUD + zapytania). |
| `ISummerModeRepository` | `SummerModeRepository` (Infrastructure) | Dostęp do logu trybu letniego. |
| `IConfigurationService` | `ConfigurationService` (Infrastructure) | Pobieranie i zapisywanie konfiguracji z cache'iem w pamięci (5 min). |
| `IConfigurationAuditService` | `ConfigurationAuditService` (Infrastructure) | Rejestrowanie zmian konfiguracji w audycie. |
| `IApplicationErrorLogger` | `ApplicationErrorLogger` (Infrastructure) | Logowanie wyjątków do tabeli `ApplicationErrorLog`. |
| `IPhaseService` | `Phase0ForecastService` … `Phase4SummerModeService` (Core) | Jednolity interfejs dla każdej fazy algorytmu. |

---

## Diagramy

Szczegółowe diagramy znajdują się w katalogu `diagrams/` i można je otworzyć w [draw.io](https://app.diagrams.net):

- `architektura-systemu.drawio` – projekty, warstwy i zależności między nimi.
- `przeplyw-danych.drawio` – end-to-end: czujniki HA → Console → DB + zawory/piec HA oraz HA Custom Component → Api → DB.
- `algorytm-faz.drawio` – sekwencja faz 0→4 z wejściami i wyjściami.
- `model-danych.drawio` – tabele SQL z relacjami.
- `interakcja-home-assistant.drawio` – kto co czyta i zapisuje w Home Assistant.
