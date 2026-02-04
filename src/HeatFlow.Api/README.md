# HeatFlow.Api

REST API do odczytu i zapisu konfiguracji HeatFlow (RoomConfiguration, HeatingParameters). Uwierzytelnienie: nagłówek `X-API-Key`.

## Migracja bazy danych

Przed pierwszym uruchomieniem API **należy zastosować migracje EF** na bazę SQL Server. Instrukcja krok po kroku: **[MIGRATIONS.md](../../MIGRATIONS.md#instrukcja-migracji-krok-po-kroku)** (w katalogu głównym repozytorium). W skrócie: z katalogu `src/HeatFlow.Infrastructure` uruchom `dotnet ef database update --startup-project ../HeatFlow.Console` (w `HeatFlow.Console/appsettings.json` musi być ustawiony `ConnectionStrings:DefaultConnection`).

## Skrypty instalacji

W katalogu `scripts/` są skrypty PowerShell (uruchamiaj **jako Administrator**):

- **Instalacja** (publikacja, rejestracja i uruchomienie usługi):
  ```powershell
  cd src\HeatFlow.Api\scripts
  .\Install-HeatFlowApi.ps1
  ```
  Opcje: `-InstallPath C:\HeatFlow.Api` (domyślna), `-DoNotStart` (tylko zarejestruj), `-Force` (zastąp istniejącą usługę).

- **Deinstalacja** (zatrzymanie i usunięcie usługi, opcjonalnie katalog z plikami):
  ```powershell
  cd src\HeatFlow.Api\scripts
  .\Uninstall-HeatFlowApi.ps1
  .\Uninstall-HeatFlowApi.ps1 -RemoveFiles   # usuń też C:\HeatFlow.Api
  ```

Przed pierwszą instalacją uzupełnij `appsettings.json` w katalogu publikacji (lub skopiuj go tam po instalacji) – ConnectionStrings, ApiKey itd.

## Instalacja ręczna (jako usługa Windows)

1. Opublikuj aplikację:
   ```bash
   dotnet publish -c Release -o C:\HeatFlow.Api
   ```

2. Skopiuj `appsettings.json` do katalogu publikacji i uzupełnij:
   - `ConnectionStrings:DefaultConnection` – connection string do bazy HeatFlow
   - `HeatFlow:ApiKey` – klucz API (np. długi losowy ciąg)
   - `Kestrel:Port` – port, na którym API nasłuchuje (domyślnie 5000)
   - `Cors:AllowedOrigins` – opcjonalnie lista originów HA (np. `["http://192.168.1.10:8123"]`)

3. Zarejestruj usługę (PowerShell jako Administrator):
   ```powershell
   New-Service -Name "HeatFlowApi" -BinaryPathName "C:\HeatFlow.Api\HeatFlow.Api.exe" -DisplayName "HeatFlow API" -StartupType Automatic
   ```

   Lub przez `sc`:
   ```cmd
   sc create HeatFlowApi binPath= "C:\HeatFlow.Api\HeatFlow.Api.exe" start= auto
   ```

4. Ustaw zmienne środowiskowe dla usługi (opcjonalnie, zamiast wpisywania w appsettings):
   - W rejestrze: `HKLM\SYSTEM\CurrentControlSet\Services\HeatFlowApi` → ImagePath można zostawić; zmienne env ustawiane są np. przez narzędzia do zarządzania usługami.
   - Lub używaj tylko `appsettings.json` w katalogu aplikacji.

5. Uruchom usługę:
   ```powershell
   Start-Service HeatFlowApi
   ```

### Deinstalacja (ręcznie)

Zatrzymaj i usuń usługę (PowerShell jako Administrator):

```powershell
Stop-Service HeatFlowApi -Force
sc delete HeatFlowApi
```

### Konfiguracja portu

Port ustawiasz w `appsettings.json` w sekcji `Kestrel` → `Port`. Domyślnie 5000. Po zmianie uruchom ponownie API lub usługę. W integracji HA podaj URL: `http://adres-serwera:PORT` (np. `http://adres-serwera:5000`).
