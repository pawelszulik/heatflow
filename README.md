# HeatFlow - System Sterowania Grzaniem dla Home Assistant

Aplikacja konsolowa .NET 10 realizująca 6-fazowy algorytm sterowania grzaniem z integracją Home Assistant API i SQL Server.

## Funkcje

- **Faza 0**: Predykcja pogody (co godzinę) - przygotowanie systemu na zmiany pogodowe
- **Faza 1**: Diagnoza zapotrzebowania (co 5 min) - obliczanie deficytów temperatur
- **Faza 2**: Arbitraż i priorytetyzacja (co 5 min) - wybór maksymalnie 5 pokoi do grzania
- **Faza 3**: Sterowanie zaworami (co 5 min) - ustawianie temperatur na potencjometrach
- **Faza 4**: Sterowanie piecem i zaworem 4D (co 5 min) - regulacja zaworu mieszającego i mocy pieca
- **Faza 5**: Histereza i bezpieczeństwo (co 5 min) - monitoring warunków krytycznych

## Wymagania

- .NET 10 SDK
- SQL Server (opcjonalnie, dla przechowywania historii)
- Home Assistant z Long-Lived Access Token
- Windows Scheduled Task (dla automatycznego uruchamiania co minutę)

## Instalacja

1. **Sklonuj repozytorium** lub pobierz kod źródłowy

2. **Skonfiguruj appsettings.json** (plik nie jest w repo – skopiuj szablon):
   - Console: skopiuj `src/HeatFlow.Console/appsettings.Example.json` → `appsettings.json`
   - API: skopiuj `src/HeatFlow.Api/appsettings.Example.json` → `appsettings.json`
   - Uzupełnij hasła, klucze API i token HA. Szczegóły: [INSTALACJA.md](INSTALACJA.md)
   - **Nie commituj** `appsettings.json` – zawiera sekrety (jest w `.gitignore`).

3. **Zbuduj projekt**:
   ```bash
   dotnet build
   ```

4. **Skonfiguruj Windows Scheduled Task**:
   - Otwórz Task Scheduler
   - Utwórz nowe zadanie
   - Ustaw trigger: co minutę
   - Akcja: uruchom `HeatFlow.Console.exe` (bez argumentów)
   - Ustaw katalog roboczy na folder z aplikacją

## Konfiguracja

### Baza danych (wymagana)

**Wszystka konfiguracja systemu jest przechowywana w bazie danych SQL Server**, która jest teraz głównym źródłem prawdy. Home Assistant jest używane tylko do:
- **Odczytu** aktualnych stanów (temperatury pokoi, prognoza pogody, temperatura powrotu)
- **Zapisu** komend sterujących (temperatury zaworów, parametry pieca)

### Struktura konfiguracji w bazie danych

1. **SystemConfiguration** - konfiguracja systemowa:
   - Lista pokoi (`RoomsList`)
   - Numer seryjny pieca (`EkoPiecDeviceSn`)
   - Encje Home Assistant dla systemu (`TempReturnEntityId`, `Mixer4DPositionEntityId`, itp.)
   - Status włączenia systemu (`SystemEnabled`)

2. **RoomConfiguration** - konfiguracja każdego pokoju:
   - Temperatury docelowe (`TempTarget`, `TempTargetActive`, `TempTargetInactive`)
   - Priorytet (`Priority`)
   - Flagi (`Sensitive`, `AutomationDisabled`)
   - Harmonogramy (`UsageSchedule`, `HeatingSchedule`)
   - Encje Home Assistant (`SensorTemperatureEntityId`, `ValveEntityId`)

3. **HeatingParametersEntity** - parametry algorytmu:
   - Progi deficytów temperatur
   - Parametry prognozy pogody
   - Parametry arbitrażu
   - Parametry zaworów i pieca
   - Parametry bezpieczeństwa

### Home Assistant - tylko odczyt/zapis stanów

Aplikacja używa Home Assistant **tylko** do:

**Odczytu:**
- `sensor.{pokoj}_temperature` lub `climate.{pokoj}` - temperatury pokoi (z konfiguracji w bazie)
- `weather.*` - prognoza pogody
- `sensor.temp_return` - temperatura powrotu (z konfiguracji w bazie)
- `sensor.mixer_4d_position` - pozycja zaworu 4D (z konfiguracji w bazie)

**Zapisu:**
- `climate.{pokoj}` lub `number.{pokoj}_valve` - zawory termostatyczne (z konfiguracji w bazie)
- `number.ekopiec_{sn}_kot_tzad` - temperatura zadana pieca (z konfiguracji w bazie)
- `number.ekopiec_{sn}_p_pod_on` - czas pracy podajnika (z konfiguracji w bazie)
- `input_number.forecast_mode` - tryb prognozy (wartość runtime)

**Wartości runtime** (generowane przez algorytm) są zapisywane **tylko do bazy danych**, nie do Home Assistant.

## Konfiguracja bazy danych (wymagana)

**Baza danych SQL Server jest teraz wymagana** - przechowuje całą konfigurację systemu oraz historię wykonania.

### Krok 1: Skonfiguruj connection string

W pliku `appsettings.json` użyj sekcji `ConnectionStrings:DefaultConnection`:

**SQL Server Authentication (login i hasło):**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=HeatFlow;User ID=twoj_login;Password=twoje_haslo;TrustServerCertificate=True"
  }
}
```

**Windows Authentication:**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=HeatFlow;Integrated Security=True;TrustServerCertificate=True"
  }
}
```

**WAŻNE:** Hasło nie powinno być przechowywane bezpośrednio w `appsettings.json`. Użyj zmiennych środowiskowych lub `appsettings.Development.json`. Zobacz szczegóły w `INSTALACJA.md`.

### Krok 2: Utwórz migrację

```bash
cd src/HeatFlow.Infrastructure
dotnet ef migrations add AddConfigurationTables --startup-project ../HeatFlow.Console
```

### Krok 3: Zastosuj migrację i seed danych

Aplikacja **automatycznie** zastosuje migracje i wypełni bazę danymi domyślnymi przy pierwszym uruchomieniu.

Lub ręcznie:
```bash
dotnet ef database update --startup-project ../HeatFlow.Console
```

### Seed danych domyślnych

Przy pierwszym uruchomieniu aplikacja automatycznie wypełnia bazę:
- **HeatingParametersEntity** - wartości domyślne parametrów algorytmu
- **SystemConfiguration** - domyślna konfiguracja systemowa (wymaga dostosowania encji HA)
- **RoomConfiguration** - przykładowe pokoje z wartościami z `inputs/*.yaml`

**Uwaga:** Po seedzie musisz dostosować:
- `SystemConfiguration.EkoPiecDeviceSn` - numer seryjny Twojego pieca
- `SystemConfiguration.RoomsList` - lista Twoich pokoi
- `RoomConfiguration` - encje HA dla każdego pokoju (czujniki temperatury i zawory)
- Parametry algorytmu - jeśli chcesz zmienić wartości domyślne

Szczegóły w pliku `MIGRATIONS.md`.

## Uruchomienie

### Tryb Scheduled Task (produkcja)
Aplikacja uruchamiana przez Windows Scheduled Task co minutę wykonuje:
1. **Przy pierwszym uruchomieniu:** automatycznie aplikuje migracje i seed danych domyślnych
2. Sprawdza czy minęła godzina od ostatniego wykonania Fazę 0
3. Jeśli tak, wykonuje Fazę 0 (prognoza pogody) - aktualizuje parametry w bazie danych
4. Wykonuje główną pętlę (fazy 1-5):
   - Pobiera konfigurację z bazy danych
   - Odczytuje stany z Home Assistant
   - Zapisuje komendy sterujące do Home Assistant
   - Zapisuje wartości runtime do bazy danych
5. Kończy działanie

### Tryb ciągły (testy)
Uruchom z argumentem:
```bash
dotnet run -- continuous
```

Aplikacja będzie działać w pętli, wykonując główną pętlę co 5 minut.

## Struktura projektu

```
csharp/
├── src/
│   ├── HeatFlow.Console/          # Aplikacja konsolowa
│   ├── HeatFlow.Core/              # Logika biznesowa (6 faz)
│   ├── HeatFlow.Infrastructure/    # Integracje (HA API, SQL Server)
│   ├── HeatFlow.Domain/            # Modele domenowe
│   └── HeatFlow.Application/       # Serwisy aplikacyjne
└── tests/                          # Testy jednostkowe
```

## Logi

Logi są zapisywane do:
- Konsola (stdout)
- Plik `logs/heatflow-YYYY-MM-DD.log` (rolling daily)

Poziomy logowania można skonfigurować w `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

## Obsługa błędów

Aplikacja zawiera zaawansowaną obsługę błędów:
- **Retry z exponential backoff** - automatyczne ponawianie operacji HA API
- **Fallbacki** - użycie wartości domyślnych przy braku encji
- **Graceful degradation** - kontynuacja działania przy częściowych błędach
- **Szczegółowe logowanie** - wszystkie błędy są logowane z kontekstem

## Testy

Uruchom wszystkie testy:
```bash
dotnet test
```

Uruchom testy dla konkretnego projektu:
```bash
dotnet test tests/HeatFlow.Core.Tests
```

### Pokrycie testami

Zaimplementowane testy jednostkowe:

**HeatFlow.Core.Tests:**
- `Phase0ForecastServiceTests` - testy Fazę 0 (prognoza pogody)
- `Phase1DiagnoseServiceTests` - testy Fazę 1 (diagnoza)
- `Phase2ArbitrateServiceTests` - testy Fazę 2 (arbitraż)
- `Phase3ValvesServiceTests` - testy Fazę 3 (zawory)
- `Phase4BoilerServiceTests` - testy Fazę 4 (piec)
- `Phase5HysteresisServiceTests` - testy Fazę 5 (histereza)
- `ScheduleHelperTests` - testy pomocniczych funkcji harmonogramów
- `TemperatureHelperTests` - testy pomocniczych funkcji temperatur

**HeatFlow.Infrastructure.Tests:**
- `HomeAssistantClientTests` - testy klienta HA API

**HeatFlow.Application.Tests:**
- `OrchestrationServiceTests` - testy głównej pętli wykonania

**Łącznie: 34 testy** pokrywające wszystkie fazy algorytmu i kluczowe komponenty systemu.

### Statystyki testów

```
HeatFlow.Core.Tests:        28 testów ✅
HeatFlow.Infrastructure.Tests: 4 testy ✅
HeatFlow.Application.Tests:    2 testy ✅
───────────────────────────────────────
RAZEM:                       34 testy ✅
```

Wszystkie testy przechodzą pomyślnie.

## Dokumentacja

Szczegółowa dokumentacja algorytmu znajduje się w głównym projekcie Python:
- `README.md` - przegląd systemu
- `Algorytm.md` - szczegółowy opis algorytmu
- `CONFIGURATION.md` - konfiguracja encji Home Assistant
- `ARCHITECTURE.md` - architektura systemu

## Licencja

[Określ licencję]

## Autor

System opracowany na podstawie szczegółowej analizy wymagań i algorytmu optymalizacji sterowania grzaniem.
