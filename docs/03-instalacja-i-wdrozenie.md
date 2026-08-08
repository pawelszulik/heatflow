# 3. Instalacja i wdrożenie

Ten dokument opisuje kompletny proces instalacji systemu HeatFlow od zera – od przygotowania środowiska, przez bazę danych, aż po uruchomienie aplikacji Console i Api w produkcji.

---

## Wymagania wstępne

Przed rozpoczęciem upewnij się, że masz zainstalowane i skonfigurowane:

1. **.NET 10 SDK**
   - Pobierz z: https://dotnet.microsoft.com/download/dotnet/10.0
   - Zweryfikuj w terminalu:
     ```powershell
     dotnet --version
     ```
     Powinno zwrócić wersję `10.x.x`.

2. **Microsoft SQL Server** (lub SQL Server Express)
   - Wersja 2019 lub nowsza.
   - Włączona autoryzacja SQL Server + Windows (jeśli używasz loginu/hasła).
   - Użytkownik bazy danych musi mieć uprawnienia: `db_datareader`, `db_datawriter`, `db_ddladmin` (lub rola `db_owner`).

3. **Home Assistant**
   - Działająca instancja z dostępem sieciowym.
   - Wygenerowany **Long-Lived Access Token** (Profile → Long-Lived Access Tokens → Create Token).
   - Skonfigurowane encje: czujniki temperatur, zawory, pogoda, przełącznik letni/zimny.

4. **Windows 10/11**
   - Uprawnienia administratora (do instalacji usług i Task Scheduler).

---

## Krok 1: Przygotowanie kodu źródłowego

1. Sklonuj lub pobierz repozytorium do folderu roboczego, np.:
   ```powershell
   git clone <url-repozytorium> D:\programowanie\HeatFlow
   cd D:\programowanie\HeatFlow
   ```

2. Przywróć pakiety NuGet:
   ```powershell
   dotnet restore
   ```

---

## Krok 2: Konfiguracja `appsettings.json`

Pliki `appsettings.json` nie są commitowane do repozytorium (znajdują się w `.gitignore`). Musisz utworzyć je na podstawie szablonów `appsettings.Example.json`.

### Aplikacja Console (`src/HeatFlow.Console/appsettings.json`)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=HeatFlow;User ID=heatflow_user;Password=TWOJE_HASLO;TrustServerCertificate=True"
  },
  "HomeAssistant": {
    "BaseUrl": "http://adres-twojego-ha:8123",
    "AccessToken": "TWOJ_LONG_LIVED_TOKEN",
    "TimeoutSeconds": 30
  },
  "OpenWeatherMap": {
    "ApiKey": "TWOJ_KLUCZ_OWM"
  },
  "Heating": {
    "MainLoopIntervalMinutes": 5,
    "ForecastIntervalHours": 1,
    "RetryCount": 3,
    "RetryDelaySeconds": 1
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

### Aplikacja Api (`src/HeatFlow.Api/appsettings.json`)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=HeatFlow;User ID=heatflow_user;Password=TWOJE_HASLO;TrustServerCertificate=True"
  },
  "HeatFlow": {
    "ApiKey": "DLOUGI_LOSOWY_KLUCZ_API_MIN_32_ZNAKI"
  },
  "Kestrel": {
    "Port": 5000
  },
  "Cors": {
    "AllowedOrigins": [ "http://adres-twojego-ha:8123" ]
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

> **Bezpieczeństwo:** Nie commituj plików `appsettings.json` z hasłami i tokenami. Jeśli musisz przechowywać hasła poza plikiem, użyj zmiennych środowiskowych (np. `ConnectionStrings__DefaultConnection`) lub `appsettings.Development.json` (też jest w `.gitignore`).

---

## Krok 3: Migracje bazy danych

Przed pierwszym uruchomieniem należy utworzyć strukturę bazy danych za pomocą Entity Framework Core.

1. Zainstaluj narzędzie EF Core (jeśli jeszcze nie masz):
   ```powershell
   dotnet tool install --global dotnet-ef
   ```

2. Przejdź do katalogu Infrastructure i wykonaj migracje:
   ```powershell
   cd src\HeatFlow.Infrastructure
   dotnet ef database update --startup-project ..\HeatFlow.Console
   ```

   Narzędzie `dotnet ef` odczyta connection string z `src/HeatFlow.Console/appsettings.json` i utworzy wszystkie tabele.

3. Sprawdź, czy baza została utworzona (np. w SQL Server Management Studio). Powinny pojawić się tabele:
   - `SystemConfiguration`
   - `RoomConfiguration`
   - `HeatingParameters`
   - `ExecutionHistory`
   - `RoomState`, `BoilerState`, `ValveState`
   - `ConfigurationChangeLog`
   - `ApplicationErrorLog`
   - `ForecastDataCache`
   - `SummerModeLog`

> Szczegółowa instrukcja migracji znajduje się również w pliku [MIGRATIONS.md](../MIGRATIONS.md) w katalogu głównym.

---

## Krok 4: Uruchomienie aplikacji Console

### Opcja A – Tryb Scheduled Task (produkcja)

W tym trybie aplikacja wykonuje się **jednorazowo** i kończy działanie. Wywoływana jest przez Windows Task Scheduler co minutę.

1. Opublikuj aplikację:
   ```powershell
   cd src\HeatFlow.Console
   dotnet publish -c Release -o C:\HeatFlow\Console
   ```

2. Upewnij się, że w folderze docelowym znajduje się `appsettings.json` z poprawną konfiguracją.

3. Utwórz zadanie w **Task Scheduler** (Harmonogram zadań):
   - **Nazwa:** `HeatFlow Console`
   - **Wyzwalacz:** `Co minutę` (w zakładce Triggers → New → On a schedule → One time, a potem Repeat task every 1 minute)
   - **Akcja:** Uruchom program `C:\HeatFlow\Console\HeatFlow.Console.exe`
   - **Katalog roboczy (Start in):** `C:\HeatFlow\Console`
   - **Uprawnienia:** Uruchom niezależnie od logowania użytkownika (opcja `Run whether user is logged on or not`)

4. Przetestuj ręczne uruchomienie zadania i sprawdź logi w folderze `logs/`.

### Opcja B – Tryb ciągły (testy / debugowanie)

W tym trybie aplikacja działa w pętli i wykonuje algorytm co 5 minut, aż do przerwania `Ctrl+C`.

```powershell
cd src\HeatFlow.Console
dotnet run -- continuous
```

Lub po publikacji:
```powershell
C:\HeatFlow\Console\HeatFlow.Console.exe continuous
```

---

## Krok 5: Uruchomienie aplikacji Api

Api może działać jako samodzielna aplikacja konsolowa (Kestrel) lub jako **usługa Windows**.

### Instalacja jako usługa Windows (zalecane)

W repozytorium znajdują się gotowe skrypty PowerShell w `src/HeatFlow.Api/scripts/`.

1. Otwórz PowerShell jako **Administrator**.

2. Uruchom skrypt instalacyjny:
   ```powershell
   cd src\HeatFlow.Api\scripts
   .\Install-HeatFlowApi.ps1
   ```
   Skrypt opublikuje aplikację, zarejestruje usługę `HeatFlowApi` i ją uruchomi.

   Opcjonalne parametry:
   - `-InstallPath C:\HeatFlow.Api` – inny katalog instalacji,
   - `-DoNotStart` – tylko zarejestruj, nie uruchamiaj,
   - `-Force` – zastąp istniejącą usługę.

3. Po instalacji upewnij się, że w folderze instalacji znajduje się `appsettings.json` z właściwym `ApiKey` i connection stringiem.

4. Sprawdź, czy usługa działa:
   ```powershell
   Get-Service HeatFlowApi
   ```

5. Przetestuj API w przeglądarce lub curl:
   ```powershell
   curl -H "X-API-Key: TWOJ_KLUCZ_API" http://localhost:5000/api/health
   ```
   Oczekiwana odpowiedź: `{ "status": "ok" }`.

### Deinstalacja usługi

```powershell
cd src\HeatFlow.Api\scripts
.\Uninstall-HeatFlowApi.ps1
.\Uninstall-HeatFlowApi.ps1 -RemoveFiles  # usuwa też katalog C:\HeatFlow.Api
```

---

## Krok 6: Weryfikacja instalacji

Po uruchomieniu obu aplikacji wykonaj poniższe kontrole:

| Sprawdzam | Jak |
|-----------|-----|
| **Console łączy się z bazą** | Sprawdź logi `logs/heatflow-YYYY-MM-DD.log` – powinny pojawić się wpisy o załadowaniu konfiguracji. |
| **Console łączy się z HA** | W logach powinien być komunikat o pobraniu temperatur pokojów. |
| **Api odpowiada** | `curl /api/health` zwraca `{"status":"ok"}`. |
| **Api autoryzuje** | Żądanie bez nagłówka `X-API-Key` zwraca `401 Unauthorized`. |
| **Baza zawiera dane** | Po kilku minutach w tabeli `ExecutionHistory` powinny pojawić się rekordy z fazami. |

---

## Instalacja integracji Home Assistant

Szczegółowy opis znajduje się w dokumencie [07-integracja-home-assistant.md](07-integracja-home-assistant.md). W skrócie:

1. Skopiuj folder `integration/custom_components/heatflow` do `config/custom_components/heatflow` w Home Assistant.
2. Zrestartuj Home Assistant.
3. Dodaj integrację przez UI: Ustawienia → Urządzenia i usługi → Dodaj integrację → **HeatFlow**.
4. Podaj adres URL Api oraz klucz API.

---

## Podsumowanie kroków instalacyjnych

1. ✅ Zainstaluj .NET 10 SDK i SQL Server.
2. ✅ Skonfiguruj `appsettings.json` dla Console i Api.
3. ✅ Wykonaj migracje EF Core (`dotnet ef database update`).
4. ✅ Opublikuj i skonfiguruj Console jako Scheduled Task.
5. ✅ Opublikuj i zainstaluj Api jako usługę Windows.
6. ✅ Zainstaluj custom component w Home Assistant.
7. ✅ Zweryfikuj logi, bazę danych i odpowiedzi API.

---

## Następny krok

Po zainstalowaniu systemu przejdź do dokumentu [04-konfiguracja.md](04-konfiguracja.md), aby skonfigurować pokoje i parametry algorytmu.
