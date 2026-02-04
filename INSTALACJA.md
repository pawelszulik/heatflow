# Przewodnik instalacji i uruchomienia projektu C# HeatFlow

## Spis treści

1. [Wymagania systemowe](#wymagania-systemowe)
2. [Instalacja zależności](#instalacja-zależności)
3. [Konfiguracja Home Assistant](#konfiguracja-home-assistant)
4. [Konfiguracja aplikacji](#konfiguracja-aplikacji)
5. [Konfiguracja bazy danych (opcjonalnie)](#konfiguracja-bazy-danych-opcjonalnie)
6. [Budowanie projektu](#budowanie-projektu)
7. [Testowanie aplikacji](#testowanie-aplikacji)
8. [Konfiguracja Windows Scheduled Task](#konfiguracja-windows-scheduled-task)
9. [Weryfikacja działania](#weryfikacja-działania)
10. [Rozwiązywanie problemów](#rozwiązywanie-problemów)

---

## Wymagania systemowe

### Wymagane oprogramowanie:

1. **.NET 10 SDK**
   - Pobierz z: https://dotnet.microsoft.com/download/dotnet/10.0
   - Wersja minimalna: .NET 10.0 SDK
   - Sprawdź instalację: `dotnet --version` (powinno zwrócić 10.x.x)

2. **Windows 10/11** (dla Windows Scheduled Task)
   - Lub inny system operacyjny z odpowiednim schedulerem

3. **SQL Server** (wymagane)
   - SQL Server 2019 lub nowszy
   - Lub SQL Server Express (darmowa wersja)
   - Baza danych przechowuje całą konfigurację systemu oraz historię wykonania

4. **Home Assistant**
   - Działająca instancja Home Assistant
   - Long-Lived Access Token (jak utworzyć - patrz sekcja poniżej)

### Konfiguracja w bazie danych:

**Wszystka konfiguracja jest przechowywana w bazie danych SQL Server:**
- Lista pokoi i ich konfiguracja (`RoomConfiguration`)
- Parametry algorytmu (`HeatingParametersEntity`)
- Konfiguracja systemowa (`SystemConfiguration`)

### Encje Home Assistant (tylko odczyt/zapis stanów):

Home Assistant jest używane **tylko** do:
- **Odczytu** aktualnych stanów (temperatury pokoi, prognoza pogody, temperatura powrotu)
- **Zapisu** komend sterujących (temperatury zaworów, parametry pieca)

Encje HA są konfigurowane w bazie danych (`RoomConfiguration.SensorTemperatureEntityId`, `RoomConfiguration.ValveEntityId`, itp.)

---

## Instalacja zależności

### Krok 1: Zainstaluj .NET 10 SDK

1. Pobierz instalator z https://dotnet.microsoft.com/download/dotnet/10.0
2. Uruchom instalator i postępuj zgodnie z instrukcjami
3. Sprawdź instalację:
   ```powershell
   dotnet --version
   ```
   Powinno zwrócić: `10.x.x`

### Krok 2: Zainstaluj Entity Framework Core Tools (jeśli używasz bazy danych)

```powershell
dotnet tool install --global dotnet-ef
```

Sprawdź instalację:
```powershell
dotnet ef --version
```

### Krok 3: Sklonuj/pobierz kod źródłowy

Jeśli kod jest w repozytorium Git:
```powershell
git clone <url-repozytorium>
cd HeatFlow/csharp
```

Lub po prostu upewnij się, że masz dostęp do folderu `csharp` z kodem źródłowym.

---

## Konfiguracja Home Assistant

### Krok 1: Utwórz Long-Lived Access Token

1. Zaloguj się do Home Assistant
2. Przejdź do: **Settings** → **People & Zones** → **Long-Lived Access Tokens**
3. Kliknij **Create Token**
4. Nadaj nazwę (np. "HeatFlow System")
5. Skopiuj wygenerowany token (będzie potrzebny w konfiguracji aplikacji)
6. **WAŻNE:** Token jest wyświetlany tylko raz - zapisz go bezpiecznie!

### Krok 2: Skonfiguruj podstawowe helpery

#### 2.1. Lista pokoi (`input_text.heating_rooms_list`)

1. Przejdź do: **Settings** → **Devices & Services** → **Helpers**
2. Utwórz nowy helper typu **Text**:
   - **Name:** `heating_rooms_list`
   - **Value:** Lista pokoi oddzielonych przecinkami (np. `sypialnia,lazienka,salon,kuchnia`)
   - **Przykład:** `sypialnia,lazienka,salon,kuchnia,pokoj_dzieci,gabinet`

#### 2.2. Numer seryjny pieca (`input_text.system_ekopiec_device_sn`)

1. Utwórz nowy helper typu **Text**:
   - **Name:** `system_ekopiec_device_sn`
   - **Value:** Tylko numer seryjny pieca (np. `ABC123`) - **BEZ** prefiksów ani sufiksów
   - System automatycznie zbuduje nazwy encji: `number.ekopiec_ABC123_kot_tzad`, `number.ekopiec_ABC123_p_pod_on`

#### 2.3. Przełącznik systemu (`input_boolean.heating_system_enabled`)

1. Utwórz nowy helper typu **Toggle**:
   - **Name:** `heating_system_enabled`
   - **Initial state:** `off` (wyłączony)
   - **Opis:** Główny przełącznik włączający/wyłączający system

### Krok 3: Skonfiguruj parametry algorytmu

Utwórz wszystkie wymagane parametry jako `input_number.*`. Pełna lista znajduje się w `CONFIGURATION.md` w głównym folderze projektu.

**Minimalne wymagane parametry:**

```yaml
# Progi deficytów
input_number.deficit_high_p1: 1.0
input_number.deficit_high_p2: 2.0
input_number.deficit_high_p3: 3.0
input_number.deficit_medium_p1: 0.5
input_number.deficit_medium_p2: 1.0
input_number.deficit_medium_p3: 2.0

# Wartości bazowe (używane przez Fazę 0)
input_number.deficit_high_p1_base: 1.0
input_number.deficit_high_p2_base: 2.0
input_number.deficit_high_p3_base: 3.0
input_number.buffer_preparation_base: 0.8

# Bufor przygotowania
input_number.buffer_preparation: 0.8
input_number.buffer_heating_time: 60

# Parametry prognozy
input_number.forecast_temp_drop_threshold: 5.0
input_number.forecast_temp_rise_threshold: 3.0
input_number.forecast_hours_count: 8
input_number.forecast_pre_heating_p1_multiplier: 0.8
input_number.forecast_pre_heating_p2_multiplier: 0.9
input_number.forecast_pre_heating_p3_multiplier: 0.9
input_number.forecast_pre_heating_buffer_multiplier: 1.2
input_number.forecast_reduction_p1_multiplier: 1.2
input_number.forecast_reduction_p2_multiplier: 1.2
input_number.forecast_reduction_p3_multiplier: 1.2
input_number.forecast_reduction_buffer_multiplier: 0.8

# Parametry arbitrażu
input_number.max_valves_open: 5
input_number.min_valves_open: 1
input_number.usage_soon_minutes: 30
input_number.score_priority_multiplier: 100
input_number.score_deficit_multiplier: 10
input_number.score_sensitive_bonus: 50
input_number.score_usage_soon_bonus: 20
input_number.score_heating_schedule_bonus: 50

# Parametry zaworów
input_number.valve_temp_offset: 5.0
input_number.valve_tolerance: 0.1
input_number.valve_closed_temp: 0.0
input_number.valve_retry_count: 3
input_number.valve_retry_delay: 1.0

# Parametry pieca
input_number.min_return_temp: 50.0
input_number.boiler_nominal_temp: 70.0
input_number.frost_compensation_factor: 0.5
input_number.mixer_4d_default: 50.0
input_number.feeder_time_default: 30.0
input_number.feeder_boost_multiplier: 1.2
input_number.feeder_economy_multiplier: 0.8
input_number.feeder_normal_multiplier: 1.0
input_number.feeder_boost_threshold: 5
input_number.feeder_economy_threshold: 2
input_number.boiler_temp_tolerance: 0.5
input_number.feeder_time_tolerance: 1.0
input_number.boiler_retry_count: 3
input_number.boiler_retry_delay: 1.0

# Parametry bezpieczeństwa
input_number.min_temp_diff: 15.0
input_number.min_mixer_4d: 20.0
input_number.hysteresis: 0.5
input_number.hysteresis_safety_threshold: 2.0
input_number.temp_validation_min: 0.0
input_number.temp_validation_max: 40.0
```

**Uwaga:** Możesz użyć pliku `inputs/input_numbers.yaml` z głównego projektu jako szablonu.

### Krok 4: Skonfiguruj pokoje

Dla każdego pokoju z listy `heating_rooms_list` utwórz:

#### 4.1. Parametry pokoju (`input_number.*`)

- `{pokoj}_temp_target` - temperatura docelowa podstawowa (np. 21.0)
- `{pokoj}_temp_target_active` - temperatura docelowa w godzinach grzania (np. 22.0)
- `{pokoj}_temp_target_inactive` - temperatura docelowa poza godzinami grzania (np. 20.0)
- `{pokoj}_priority` - priorytet (1-4, gdzie 1 = najwyższy)

#### 4.2. Flagi pokoju (`input_boolean.*`)

- `{pokoj}_sensitive` - czy pokój jest wrażliwy (sypialnia, łazienka, pokój dzieci)
- `{pokoj}_automation_disabled` - wyłączenie pokoju z automatyzacji

#### 4.3. Harmonogramy (`input_select.*`)

- `{pokoj}_usage_schedule` - harmonogram użytkowania (format: `"HH:MM-HH:MM|HH:MM-HH:MM"` lub `"Brak"`)
- `{pokoj}_heating_schedule` - harmonogram grzania (format: `"HH:MM-HH:MM|HH:MM-HH:MM"` lub `"Brak"`)

**Przykład konfiguracji pokoju "sypialnia":**

```yaml
input_number.sypialnia_temp_target: 21.0
input_number.sypialnia_temp_target_active: 22.0
input_number.sypialnia_temp_target_inactive: 20.0
input_number.sypialnia_priority: 1
input_boolean.sypialnia_sensitive: true
input_boolean.sypialnia_automation_disabled: false
input_select.sypialnia_usage_schedule: "22:00-07:00|23:00-09:00"
input_select.sypialnia_heating_schedule: "Brak"
```

### Krok 5: Skonfiguruj encje odczytu

#### 5.1. Temperatury pokoi

Dla każdego pokoju potrzebujesz encji z temperaturą:

**Opcja 1:** `sensor.{pokoj}_temperature` (preferowane)
**Opcja 2:** `climate.{pokoj}` z atrybutem `current_temperature`

#### 5.2. Prognoza pogody

Potrzebujesz encji typu `weather.*`:
- `weather.home`
- `weather.openweathermap`
- `weather.accuweather`

System automatycznie znajdzie pierwszą dostępną.

#### 5.3. Temperatura powrotu

- `sensor.temp_return` (preferowane)
- Lub `sensor.ekopiec_{sn}_temp_return`

#### 5.4. Pozycja zaworu 4D

- `sensor.mixer_4d_position` (preferowane)
- Lub `number.mixer_4d_position`

### Krok 6: Skonfiguruj encje zapisu

#### 6.1. Zawory termostatyczne

Dla każdego pokoju potrzebujesz encji do sterowania zaworem:

**Opcja 1:** `climate.{pokoj}` (preferowane)
**Opcja 2:** `number.{pokoj}_valve`

#### 6.2. Piec ekopiec

System automatycznie zbuduje nazwy encji na podstawie numeru seryjnego:

- `number.ekopiec_{sn}_kot_tzad` - temperatura zadana pieca
- `number.ekopiec_{sn}_p_pod_on` - czas pracy podajnika

**Przykład:** Jeśli numer seryjny = `ABC123`, encje będą:
- `number.ekopiec_ABC123_kot_tzad`
- `number.ekopiec_ABC123_p_pod_on`

---

## Konfiguracja aplikacji

### Krok 1: Skonfiguruj appsettings.json

Pliki z hasłami i kluczami API **nie są** commitowane do repozytorium (są w `.gitignore`). Użyj szablonów:

- **API:** skopiuj `src/HeatFlow.Api/appsettings.Example.json` jako `src/HeatFlow.Api/appsettings.json`
- **Console:** skopiuj `src/HeatFlow.Console/appsettings.Example.json` jako `src/HeatFlow.Console/appsettings.json`

Następnie uzupełnij w nich prawdziwe wartości (hasło bazy, klucze API, token Home Assistant). Plik `appsettings.json` zostaje tylko na Twoim dysku i nie trafia do Git.

1. Otwórz plik: `src/HeatFlow.Console/appsettings.json` (po skopiowaniu szablonu)

2. Zaktualizuj konfigurację Home Assistant:

```json
{
  "HomeAssistant": {
    "BaseUrl": "http://twoj-home-assistant:8123",
    "AccessToken": "TWÓJ_LONG_LIVED_TOKEN_TUTAJ",
    "TimeoutSeconds": 30
  }
}
```

**WAŻNE:**
- `BaseUrl` - adres URL Twojego Home Assistant (może być `http://localhost:8123` lub `http://192.168.1.100:8123`)
- `AccessToken` - wklej Long-Lived Access Token utworzony wcześniej
- `TimeoutSeconds` - timeout dla żądań HTTP (domyślnie 30 sekund)

### Krok 2: Skonfiguruj logowanie (opcjonalnie)

Możesz dostosować poziomy logowania:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

**Poziomy logowania:**
- `Trace` - najwięcej szczegółów
- `Debug` - szczegóły debugowania
- `Information` - informacje ogólne (domyślne)
- `Warning` - ostrzeżenia
- `Error` - błędy
- `Critical` - tylko krytyczne błędy

### Krok 3: Utwórz folder na logi

Aplikacja zapisuje logi do folderu `logs/` w katalogu roboczym. Utwórz folder:

```powershell
cd csharp/src/HeatFlow.Console
mkdir logs
```

---

## Konfiguracja bazy danych (wymagana)

Jeśli chcesz zapisywać historię wykonania do bazy danych:

### Krok 1: Skonfiguruj connection string

W pliku `appsettings.json` dodaj/zmodyfikuj sekcję `ConnectionStrings`:

**Opcja A: SQL Server Authentication (login i hasło) - zalecane:**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=HeatFlow;User ID=twoj_login;Password=twoje_haslo;TrustServerCertificate=True"
  }
}
```

**Opcja B: Windows Authentication:**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=HeatFlow;Integrated Security=True;TrustServerCertificate=True"
  }
}
```

**Parametry connection stringa:**
- `Server` - nazwa serwera SQL Server (lub `localhost` dla lokalnego, `.\SQLEXPRESS` dla SQL Express)
- `Database` - nazwa bazy danych (może być `HeatFlow`)
- `User ID` - login użytkownika SQL Server (tylko dla SQL Server Authentication)
- `Password` - hasło użytkownika SQL Server (tylko dla SQL Server Authentication)
- `Integrated Security=True` - używa Windows Authentication (zamiast User ID/Password)
- `TrustServerCertificate=True` - wymagane dla EF Core 7+ aby uniknąć problemów z certyfikatami

**WAŻNE - Bezpieczne przechowywanie hasła:**

Hasło nie powinno być przechowywane bezpośrednio w `appsettings.json` (plik może być commitowany do repozytorium). Użyj jednej z opcji:

**1. Zmienne środowiskowe (zalecane dla produkcji):**

Windows PowerShell:
```powershell
$env:ConnectionStrings__DefaultConnection = "Server=localhost;Database=HeatFlow;User ID=twoj_login;Password=twoje_haslo;TrustServerCertificate=True"
```

Windows CMD:
```cmd
set ConnectionStrings__DefaultConnection=Server=localhost;Database=HeatFlow;User ID=twoj_login;Password=twoje_haslo;TrustServerCertificate=True
```

W `appsettings.json` możesz użyć placeholder:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=HeatFlow;User ID=twoj_login;Password=PLACEHOLDER;TrustServerCertificate=True"
  }
}
```

**2. appsettings.Development.json (tylko dla developmentu):**

Utwórz plik `appsettings.Development.json` (już jest w `.gitignore`):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=HeatFlow;User ID=twoj_login;Password=twoje_haslo;TrustServerCertificate=True"
  }
}
```

**Przed użyciem SQL Server Authentication upewnij się, że:**
1. SQL Server Authentication jest włączona w SQL Server (SQL Server Management Studio → Properties → Security → "SQL Server and Windows Authentication mode")
2. Użytkownik SQL Server istnieje i ma uprawnienia do bazy danych `HeatFlow`
3. Użytkownik ma role: `db_datareader`, `db_datawriter`, `db_ddladmin` (lub `db_owner`)

### Krok 2: Utwórz migrację

```powershell
cd csharp/src/HeatFlow.Infrastructure
dotnet ef migrations add AddConfigurationTables --startup-project ../HeatFlow.Console
```

**Uwaga:** Jeśli masz już istniejącą bazę z poprzednimi migracjami, użyj nazwy migracji zgodnej z Twoją sytuacją.

### Krok 3: Zastosuj migrację i seed danych

**Opcja 1: Automatycznie przy starcie aplikacji (zalecane)**
- Aplikacja automatycznie zastosuje migracje i wypełni bazę danymi domyślnymi przy pierwszym uruchomieniu
- Seed danych jest wykonywany tylko jeśli baza jest pusta

**Opcja 2: Ręcznie**

```powershell
cd csharp/src/HeatFlow.Infrastructure
dotnet ef database update --startup-project ../HeatFlow.Console
```

Następnie uruchom aplikację raz, aby wykonać seed danych domyślnych.

### Krok 4: Sprawdź czy baza działa

Po uruchomieniu aplikacji sprawdź czy tabele zostały utworzone:
- `ExecutionHistory` - historia wykonania
- `RoomState` - stany pokoi (wartości runtime)
- `BoilerState` - stany pieca (wartości runtime)
- `ValveState` - stany zaworów (wartości runtime)
- **`RoomConfiguration`** - konfiguracja pokoi
- **`SystemConfiguration`** - konfiguracja systemowa
- **`HeatingParameters`** - parametry algorytmu

### Krok 5: Dostosuj konfigurację w bazie danych

Po pierwszym uruchomieniu aplikacja wypełnia bazę danymi domyślnymi. Musisz dostosować:

1. **SystemConfiguration:**
   - `EkoPiecDeviceSn` - numer seryjny Twojego pieca
   - `RoomsList` - lista Twoich pokoi (oddzielone przecinkami)
   - `TempReturnEntityId`, `Mixer4DPositionEntityId` - encje HA dla systemu

2. **RoomConfiguration:**
   - Dla każdego pokoju ustaw `SensorTemperatureEntityId` i `ValveEntityId` zgodnie z Twoimi encjami HA
   - Dostosuj temperatury docelowe, priorytety, harmonogramy

3. **HeatingParameters:**
   - Jeśli chcesz zmienić wartości domyślne parametrów algorytmu

Możesz edytować te wartości bezpośrednio w bazie danych lub przez aplikację (jeśli zostanie dodany interfejs).

---

## Budowanie projektu

### Krok 1: Przywróć pakiety NuGet

```powershell
cd csharp
dotnet restore
```

### Krok 2: Zbuduj projekt

```powershell
dotnet build
```

Lub zbuduj tylko aplikację konsolową:

```powershell
cd csharp/src/HeatFlow.Console
dotnet build
```

### Krok 3: Sprawdź czy build się powiódł

Powinieneś zobaczyć:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Krok 4: (Opcjonalnie) Uruchom testy

```powershell
cd csharp
dotnet test
```

Wszystkie 34 testy powinny przejść pomyślnie.

---

## Testowanie aplikacji

### Krok 1: Uruchom w trybie ciągłym (testy)

```powershell
cd csharp/src/HeatFlow.Console
dotnet run -- continuous
```

Aplikacja będzie działać w pętli, wykonując główną pętlę co 5 minut.

**Co sprawdzić:**
- Czy aplikacja łączy się z Home Assistant
- Czy pobiera dane z HA (pokoje, parametry)
- Czy wykonuje fazy 1-5
- Czy zapisuje logi do konsoli i pliku `logs/heatflow-YYYY-MM-DD.log`
- Czy nie ma błędów w logach

**Zatrzymanie:** Naciśnij `Ctrl+C`

### Krok 2: Uruchom jednorazowo (testy)

```powershell
cd csharp/src/HeatFlow.Console
dotnet run
```

Aplikacja wykona:
1. Sprawdzanie czy minęła godzina od ostatniego wykonania Fazę 0
2. Jeśli tak, wykonanie Fazę 0
3. Wykonanie głównej pętli (fazy 1-5)
4. Zapis wyników do bazy danych (jeśli skonfigurowana)
5. Zakończenie działania

**Sprawdź logi:**
- Otwórz `logs/heatflow-YYYY-MM-DD.log`
- Sprawdź czy wszystkie fazy zostały wykonane pomyślnie
- Sprawdź czy nie ma błędów

### Krok 3: Sprawdź działanie w Home Assistant

Po uruchomieniu aplikacji sprawdź w Home Assistant:

1. **Helpery powinny być aktualizowane:**
   - `input_number.{pokoj}_temp_deficit` - deficyt temperatury
   - `input_boolean.{pokoj}_needs_heating_high` - czy wymaga grzania HIGH
   - `input_boolean.{pokoj}_needs_heating_medium` - czy wymaga grzania MEDIUM
   - `input_boolean.{pokoj}_heating_enabled` - czy grzanie jest włączone
   - `input_number.{pokoj}_score` - score pokoju
   - `input_number.forecast_mode` - tryb prognozy (0=NORMAL, 1=PRE_HEATING, 2=REDUCTION)

2. **Zawory powinny być ustawiane:**
   - `climate.{pokoj}` lub `number.{pokoj}_valve` - temperatura zaworu

3. **Piec powinien być sterowany:**
   - `number.ekopiec_{sn}_kot_tzad` - temperatura zadana pieca
   - `number.ekopiec_{sn}_p_pod_on` - czas pracy podajnika

---

## Konfiguracja Windows Scheduled Task

### Krok 1: Zbuduj aplikację w trybie Release

```powershell
cd csharp/src/HeatFlow.Console
dotnet build -c Release
```

### Krok 2: Znajdź ścieżkę do pliku wykonywalnego

Plik wykonywalny będzie w:
```
csharp/src/HeatFlow.Console/bin/Release/net10.0/HeatFlow.Console.exe
```

**Lub skopiuj cały folder `bin/Release/net10.0/` do docelowej lokalizacji** (np. `C:\HeatFlow\`)

### Krok 3: Skopiuj plik konfiguracyjny

Skopiuj `appsettings.json` do tego samego folderu co `HeatFlow.Console.exe`

### Krok 4: Utwórz Windows Scheduled Task

1. Otwórz **Task Scheduler** (harmonogram zadań)
   - Wyszukaj "Task Scheduler" w menu Start
   - Lub uruchom: `taskschd.msc`

2. Kliknij **Create Basic Task** (lub **Create Task** dla zaawansowanych opcji)

3. **General (Ogólne):**
   - **Name:** `HeatFlow Heating System`
   - **Description:** `Automatyczne sterowanie grzaniem - wykonuje się co minutę`
   - **Run whether user is logged on or not:** ✅ (zaznacz)
   - **Run with highest privileges:** ✅ (zaznacz, jeśli potrzebne)

4. **Triggers (Wyzwalacze):**
   - Kliknij **New**
   - **Begin the task:** `On a schedule`
   - **Settings:** `One time` → zmień na `Repeat task every:`
   - **Repeat task every:** `1 minute`
   - **For a duration of:** `Indefinitely`
   - **Enabled:** ✅ (zaznacz)
   - Kliknij **OK**

5. **Actions (Akcje):**
   - Kliknij **New**
   - **Action:** `Start a program`
   - **Program/script:** Wskaż pełną ścieżkę do `HeatFlow.Console.exe`
     - Przykład: `C:\HeatFlow\HeatFlow.Console.exe`
   - **Add arguments (optional):** Zostaw puste (bez argumentów = tryb Scheduled Task)
   - **Start in (optional):** Wskaż folder z aplikacją
     - Przykład: `C:\HeatFlow\`
   - Kliknij **OK**

6. **Conditions (Warunki):**
   - **Start the task only if the computer is on AC power:** ❌ (odznacz, jeśli chcesz działać na baterii)
   - **Wake the computer to run this task:** ❌ (odznacz)

7. **Settings (Ustawienia):**
   - **Allow task to be run on demand:** ✅ (zaznacz)
   - **If the task fails, restart every:** `1 minute`
   - **Attempt to restart up to:** `3 times`
   - **Stop the task if it runs longer than:** ❌ (odznacz lub ustaw np. 5 minut)

8. Kliknij **OK** i wprowadź hasło administratora jeśli wymagane

### Krok 5: Przetestuj zadanie

1. Kliknij prawym przyciskiem na zadanie → **Run**
2. Sprawdź czy zadanie się wykonało:
   - Status powinien być "Running" a potem "Ready"
3. Sprawdź logi w folderze `logs/` aplikacji
4. Sprawdź czy encje w Home Assistant zostały zaktualizowane

### Krok 6: Sprawdź historię wykonania

W Task Scheduler:
1. Kliknij na zadanie
2. Przejdź do zakładki **History**
3. Sprawdź czy zadania są wykonywane co minutę
4. Sprawdź czy nie ma błędów

---

## Weryfikacja działania

### Sprawdź logi

1. Otwórz plik: `logs/heatflow-YYYY-MM-DD.log`
2. Sprawdź czy:
   - Aplikacja łączy się z Home Assistant
   - Wszystkie fazy są wykonywane pomyślnie
   - Nie ma błędów krytycznych

**Przykładowe logi sukcesu:**
```
[2026-01-29 10:00:00] [Information] Uruchamianie aplikacji HeatFlow
[2026-01-29 10:00:01] [Information] Tryb Scheduled Task - wykonanie jednorazowe
[2026-01-29 10:00:02] [Information] Faza 0 wykonana: Normal, różnica temp: 2.5°C
[2026-01-29 10:00:05] [Information] Faza 1 wykonana: przetworzono 6 pokoi
[2026-01-29 10:00:08] [Information] Faza 2 wykonana: wybrano 5 pokoi do grzania
[2026-01-29 10:00:12] [Information] Faza 3 wykonana: sukces 5, błędy 0
[2026-01-29 10:00:15] [Information] Faza 4 wykonana: temp 70°C, podajnik 30s
[2026-01-29 10:00:18] [Information] Faza 5 wykonana: wyłączono 0 pokoi
[2026-01-29 10:00:18] [Information] Wykonanie zakończone sukcesem
```

### Sprawdź Home Assistant

1. **Helpery powinny być aktualizowane:**
   - Sprawdź `input_number.{pokoj}_temp_deficit` - powinny mieć wartości
   - Sprawdź `input_boolean.{pokoj}_heating_enabled` - powinny być aktualizowane
   - Sprawdź `input_number.forecast_mode` - powinien być 0, 1 lub 2

2. **Zawory powinny być sterowane:**
   - Sprawdź `climate.{pokoj}` lub `number.{pokoj}_valve` - temperatura powinna być ustawiana

3. **Piec powinien być sterowany:**
   - Sprawdź `number.ekopiec_{sn}_kot_tzad` - temperatura powinna być ustawiana
   - Sprawdź `number.ekopiec_{sn}_p_pod_on` - czas podajnika powinien być ustawiany

### Sprawdź bazę danych (jeśli skonfigurowana)

1. Połącz się z SQL Server
2. Sprawdź tabele:
   - `ExecutionHistory` - powinna zawierać rekordy wykonania faz
   - `RoomState` - powinna zawierać stany pokoi
   - `BoilerState` - powinna zawierać stany pieca

---

## Rozwiązywanie problemów

### Problem: Aplikacja nie łączy się z Home Assistant

**Objawy:**
- Błędy w logach: "Brak konfiguracji HomeAssistant:BaseUrl" lub "Brak konfiguracji HomeAssistant:AccessToken"
- Błędy HTTP w logach

**Rozwiązanie:**
1. Sprawdź `appsettings.json` - czy `BaseUrl` i `AccessToken` są poprawne
2. Sprawdź czy Home Assistant jest dostępny pod podanym adresem
3. Sprawdź czy Long-Lived Access Token jest poprawny i nie wygasł
4. Sprawdź czy firewall nie blokuje połączenia

### Problem: Brakuje encji w Home Assistant

**Objawy:**
- Ostrzeżenia w logach: "Nie znaleziono encji..."
- Aplikacja działa, ale niektóre funkcje nie działają

**Rozwiązanie:**
1. Sprawdź `CONFIGURATION.md` - lista wszystkich wymaganych encji
2. Utwórz brakujące encje w Home Assistant
3. Sprawdź czy nazwy encji są zgodne z konwencją (np. `sensor.{pokoj}_temperature`)

### Problem: Zadanie Scheduled Task nie działa

**Objawy:**
- Zadanie nie wykonuje się
- Status zadania pokazuje błędy

**Rozwiązanie:**
1. Sprawdź czy ścieżka do `HeatFlow.Console.exe` jest poprawna
2. Sprawdź czy folder "Start in" jest poprawny
3. Sprawdź czy użytkownik ma uprawnienia do uruchomienia zadania
4. Sprawdź historię zadania w Task Scheduler
5. Spróbuj uruchomić zadanie ręcznie (Run)

### Problem: Baza danych nie działa

**Objawy:**
- Błędy w logach związane z bazą danych
- Tabele nie są tworzone

**Rozwiązanie:**
1. Sprawdź connection string w `appsettings.json`
2. Sprawdź czy SQL Server jest uruchomiony
3. Sprawdź czy użytkownik ma uprawnienia do utworzenia bazy danych
4. Spróbuj zastosować migracje ręcznie: `dotnet ef database update`

### Problem: Aplikacja działa, ale nie steruje zaworami/piecem

**Objawy:**
- Logi pokazują sukces, ale zawory/piec nie są sterowane

**Rozwiązanie:**
1. Sprawdź czy `input_boolean.heating_system_enabled` jest włączony (`on`)
2. Sprawdź czy encje zaworów/pieca istnieją w Home Assistant
3. Sprawdź czy nazwy encji są poprawne (np. `number.ekopiec_{sn}_kot_tzad`)
4. Sprawdź logi - mogą być błędy retry przy ustawianiu wartości

### Problem: Logi są puste lub nie są zapisywane

**Objawy:**
- Brak plików logów w folderze `logs/`

**Rozwiązanie:**
1. Sprawdź czy folder `logs/` istnieje w katalogu roboczym aplikacji
2. Sprawdź czy aplikacja ma uprawnienia do zapisu w folderze
3. Sprawdź konfigurację logowania w `appsettings.json`

---

## Podsumowanie kroków

1. ✅ Zainstaluj .NET 10 SDK
2. ✅ Zainstaluj Entity Framework Core Tools (opcjonalnie)
3. ✅ Skonfiguruj Home Assistant (helpery, parametry, pokoje, encje)
4. ✅ Utwórz Long-Lived Access Token w Home Assistant
5. ✅ Skonfiguruj `appsettings.json` (BaseUrl, AccessToken)
6. ✅ Skonfiguruj bazę danych (opcjonalnie)
7. ✅ Zbuduj projekt (`dotnet build`)
8. ✅ Przetestuj aplikację (`dotnet run` lub `dotnet run -- continuous`)
9. ✅ Skonfiguruj Windows Scheduled Task
10. ✅ Zweryfikuj działanie (logi, Home Assistant, baza danych)

**Po wykonaniu wszystkich kroków system powinien działać automatycznie!**

---

## Dodatkowe zasoby

- **Dokumentacja algorytmu:** `Algorytm.md` (główny folder projektu)
- **Konfiguracja encji:** `CONFIGURATION.md` (główny folder projektu)
- **Architektura systemu:** `ARCHITECTURE.md` (główny folder projektu)
- **Migracje bazy danych:** `csharp/MIGRATIONS.md`
- **Raport weryfikacji:** `csharp/WERYFIKACJA.md`

---

**Powodzenia z instalacją! 🚀**
