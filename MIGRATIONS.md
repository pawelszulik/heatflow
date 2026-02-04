# Migracje bazy danych

## Wymagania

Przed utworzeniem migracji upewnij się, że:

1. **Projekt startup (`HeatFlow.Console`) ma pakiet `Microsoft.EntityFrameworkCore.Design`** - jest to wymagane przez narzędzia EF Core
2. **Narzędzie `dotnet ef` jest zainstalowane** - sprawdź: `dotnet tool list --global`
   - Jeśli nie jest zainstalowane: `dotnet tool install --global dotnet-ef`
3. **Projekt `HeatFlow.Infrastructure` ma `DesignTimeDbContextFactory`** - już utworzony w `Database/DesignTimeDbContextFactory.cs`

## Tworzenie migracji

Aby utworzyć migrację EF Core:

```bash
cd src/HeatFlow.Infrastructure
dotnet ef migrations add InitialCreate --startup-project ../HeatFlow.Console
```

**Uwaga:** Jeśli wystąpi błąd o braku pakietu `Microsoft.EntityFrameworkCore.Design` w projekcie startup, dodaj go do `HeatFlow.Console.csproj`.

## Aplikowanie migracji

### Automatycznie przy starcie aplikacji

Obecnie automatyczne aplikowanie migracji przy starcie jest **wyłączone** w kodzie. Migracje należy wykonać **ręcznie** przed pierwszym uruchomieniem API lub po dodaniu nowej migracji (patrz poniżej).

### Ręcznie

```bash
cd src/HeatFlow.Infrastructure
dotnet ef database update --startup-project ../HeatFlow.Console
```

---

## Instrukcja migracji krok po kroku

Poniższa instrukcja dotyczy **aplikowania** istniejących migracji na bazę SQL Server (np. przed uruchomieniem HeatFlow.Api lub po wdrożeniu nowej wersji z migracjami).

### Krok 1: Zainstaluj narzędzie EF Core (jednorazowo)

W terminalu (PowerShell lub cmd):

```bash
dotnet tool list --global
```

Jeśli na liście nie ma `dotnet-ef`:

```bash
dotnet tool install --global dotnet-ef
```

Zamknij i otwórz terminal, żeby ścieżka była widoczna.

### Krok 2: Upewnij się, że connection string jest ustawiony

Narzędzie `dotnet ef` używa **DesignTimeDbContextFactory**, która ładuje konfigurację z pliku **`src/HeatFlow.Console/appsettings.json`**.

- Otwórz `src/HeatFlow.Console/appsettings.json`.
- W sekcji `ConnectionStrings` ustaw `DefaultConnection` na docelową bazę (ta sama baza, z której korzysta HeatFlow.Api lub HeatFlow.Console), np.:

  ```json
  "ConnectionStrings": {
    "DefaultConnection": "Server=NAZWA_SERWERA;Database=HeatFlow;User ID=użytkownik;Password=hasło;TrustServerCertificate=True"
  }
  ```

**Jeśli migrujesz bazę dla API wdrożonego na innym komputerze:** ustaw connection string w `HeatFlow.Console/appsettings.json` tak, aby wskazywał na tę samą bazę (np. zdalny serwer SQL). Migrację możesz uruchomić z komputera deweloperskiego.

### Krok 3: Przejdź do katalogu Infrastructure

W katalogu głównym repozytorium (np. `d:\programowanie\HeatFlow`):

```bash
cd src/HeatFlow.Infrastructure
```

### Krok 4: Uruchom aktualizację bazy

```bash
dotnet ef database update --startup-project ../HeatFlow.Console
```

- `--startup-project ../HeatFlow.Console` – projekt, przy którym EF ładuje konfigurację (appsettings) i DesignTimeDbContextFactory z Infrastructure.
- Polecenie połączy się z bazą z `DefaultConnection` i zastosuje wszystkie migracje, które jeszcze nie zostały zastosowane.

### Krok 5: Sprawdzenie wyniku

- W terminalu powinna pojawić się informacja o zastosowanych migracjach (np. `Applying migration '20260203232005_AddConfigurationChangeLog'`).
- W bazie w tabeli `__EFMigrationsHistory` będą wpisy dla każdej zastosowanej migracji.

### Typowe błędy

| Błąd | Rozwiązanie |
|------|-------------|
| `No design-time services found` / brak `IDesignTimeDbContextFactory` | Upewnij się, że w `HeatFlow.Infrastructure` jest plik `DesignTimeDbContextFactory.cs` i że polecenie uruchamiasz z katalogu `HeatFlow.Infrastructure`. |
| `Brak konfiguracji ConnectionStrings:DefaultConnection` | Uzupełnij `DefaultConnection` w `src/HeatFlow.Console/appsettings.json` (patrz Krok 2). |
| `dotnet ef` nie jest rozpoznawane | Zainstaluj narzędzie globalnie (Krok 1) i uruchom nową sesję terminala. |
| Błąd połączenia z SQL Server | Sprawdź nazwę serwera, dostępność sieciową, login/hasło i czy w connection string jest `TrustServerCertificate=True` (dla SQL bez certyfikatu). |

### Kolejność przy wdrożeniu HeatFlow.Api

1. Wykonaj migrację bazy (kroki 1–4 powyżej) na docelową bazę.
2. Opublikuj i skonfiguruj API (patrz `src/HeatFlow.Api/README.md`) z tym samym connection string.
3. Uruchom usługę HeatFlow.Api.

---

## Struktura bazy danych

### ExecutionHistory
- Id (PK, int, identity)
- ExecutionTime (datetime2)
- Phase (int) - 0-5
- Status (string) - Success/Error/Warning
- DurationMs (bigint)
- ErrorMessage (nvarchar(max), nullable)
- Details (nvarchar(max), nullable)

### RoomState
- Id (PK, int, identity)
- ExecutionId (FK, int)
- RoomName (nvarchar(100))
- TempActual (decimal(5,2))
- TempTarget (decimal(5,2))
- TempDeficit (decimal(5,2))
- Classification (int)
- Score (decimal(10,2))
- HeatingEnabled (bit)
- RecordedAt (datetime2)

### BoilerState
- Id (PK, int, identity)
- ExecutionId (FK, int)
- TempExternal (decimal(5,2))
- TempReturn (decimal(5,2))
- TempTarget (decimal(5,2))
- FeederTime (decimal(5,2))
- Mixer4DPosition (decimal(5,2))
- RoomsHeatedCount (int)
- ForecastMode (int)
- RecordedAt (datetime2)

### ValveState
- Id (PK, int, identity)
- ExecutionId (FK, int)
- RoomName (nvarchar(100))
- ValveEntityId (nvarchar(200))
- TempSet (decimal(5,2))
- TempActual (decimal(5,2), nullable)
- Success (bit)
- RetryCount (int)
- RecordedAt (datetime2)
