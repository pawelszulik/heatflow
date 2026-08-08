# 9. Rozwoj i testy

Ten dokument jest przeznaczony dla deweloperow chcacych rozwijac kod HeatFlow, uruchamiac testy, dodawac nowe funkcje lub modyfikowac algorytm.

---

## Srodowisko deweloperskie

### Wymagania

- **.NET 10 SDK**
- **Visual Studio 2022** (lub Rider / VS Code z C# Dev Kit)
- **SQL Server** (lokalny lub zdalny)
- **Docker** (opcjonalnie, do izolowanej bazy danych)

### Przygotowanie repozytorium

```powershell
git clone <url-repozytorium> D:\programowanie\HeatFlow
cd D:\programowanie\HeatFlow
dotnet restore
```

---

## Struktura testow

Solution zawiera 4 projekty testowe:

| Projekt | Liczba testow | Co testuje |
|---------|--------------|------------|
| **HeatFlow.Core.Tests** | 28 | Wszystkie fazy algorytmu (0–4), helpery (`ScheduleHelper`, `TemperatureHelper`) |
| **HeatFlow.Infrastructure.Tests** | 4 | Klient Home Assistant (`HomeAssistantClient`) |
| **HeatFlow.Application.Tests** | 2 | Orkiestracja (`OrchestrationService`) i zapis wynikow (`DataPersistenceService`) |
| **HeatFlow.Api.Tests** | – | Kontrolery API (testy integracyjne) |

Lacznie: **34+ testow jednostkowych**.

---

## Uruchamianie testow

### Wszystkie testy

```powershell
dotnet test
```

### Testy konkretnego projektu

```powershell
dotnet test tests\HeatFlow.Core.Tests
dotnet test tests\HeatFlow.Infrastructure.Tests
dotnet test tests\HeatFlow.Application.Tests
```

### Z pokryciem kodu (opcjonalnie)

Jesli masz zainstalowane narzedzie `dotnet-coverage`:

```powershell
dotnet-coverage collect "dotnet test" -f xml -o coverage.xml
```

---

## Uruchamianie lokalnie

### Console w trybie ciaglym (debugowanie)

```powershell
cd src\HeatFlow.Console
dotnet run -- continuous
```

Aplikacja bedzie wykonywac algorytm co 5 minut, az do przerwania `Ctrl+C`.

### Api (samodzielnie)

```powershell
cd src\HeatFlow.Api
dotnet run
```

Api uruchomi sie na porcie skonfigurowanym w `appsettings.json` (domyslnie 5000).

### Api + InMemoryDatabase (szybkie testy)

W `appsettings.json` Api ustaw:

```json
{
  "UseInMemoryDatabase": true
}
```

W tym trybie Api nie wymaga SQL Server – dane sa przechowywane w pamieci. Nie zaleca sie tego do produkcji.

---

## Tworzenie migracji bazy danych

Gdy dodajesz nowe encje lub modyfikujesz istniejace w `HeatFlowDbContext`:

1. Upewnij sie, ze `src/HeatFlow.Console/appsettings.json` ma poprawny connection string.
2. Przejdz do katalogu Infrastructure:
   ```powershell
   cd src\HeatFlow.Infrastructure
   ```
3. Utworz migracje:
   ```powershell
   dotnet ef migrations add NazwaMigracji --startup-project ..\HeatFlow.Console
   ```
4. Zastosuj migracje:
   ```powershell
   dotnet ef database update --startup-project ..\HeatFlow.Console
   ```

> **Wazne:** Nazwa migracji powinna opisywac zmiane, np. `AddNewColumnToRoomConfiguration`.

Szczegolowa instrukcja znajduje sie w [MIGRATIONS.md](../MIGRATIONS.md).

---

## Dodawanie nowej fazy algorytmu

Chociaz obecnie sa zaimplementowane fazy 0–4, architektura pozwala na latwe dodanie nowej fazy:

1. Utworz klase w `src/HeatFlow.Core/Phases/` implementujaca `IPhaseService`.
2. Zaimplementuj wlasciwosci:
   - `int PhaseNumber { get; }` – unikalny numer fazy.
   - `Task<PhaseResult> ExecuteAsync(HeatingState state, HeatingParameters parameters, CancellationToken cancellationToken)`
3. Zarejestruj klase w kontenerze DI w `Program.cs` Console:
   ```csharp
   services.AddScoped<IPhaseService, TwojaNowaFazaService>();
   ```
4. Dodaj testy jednostkowe w `tests/HeatFlow.Core.Tests/`.

`OrchestrationService` automatycznie wykryje nowa faze na podstawie `PhaseNumber` i wykona ja we wlasciwej kolejnosci.

---

## Modyfikowanie parametrow algorytmu

Parametry sa zdefiniowane w dwoch miejscach:

1. **Model domenowy** – `HeatFlow.Domain/HeatingParameters.cs`
2. **Encja bazodanowa** – `HeatFlow.Domain/HeatingParametersEntity.cs`

Gdy dodajesz nowy parametr:

1. Dodaj pole do `HeatingParameters`.
2. Dodaj odpowiadajace mu pole do `HeatingParametersEntity`.
3. Zaktualizuj metody `ToHeatingParameters()` i `UpdateFrom()` w `HeatingParametersEntity`.
4. Dodaj domyslna wartosc w `ConfigurationSeed`.
5. Jesli parametr ma byc edytowalny przez API/HA, upewnij sie, ze API go serializuje (kontrolery automatycznie obsluguja wszystkie publiczne wlasciwosci).
6. Utworz i zastosuj migracje EF Core.

---

## Architektura testow

### Testy faz (HeatFlow.Core.Tests)

Kazda faza ma dedykowana klase testowa, np. `Phase0ForecastServiceTests`. Testy sa izolowane – uzywaja mockow (`Moq` lub recznych stubow) dla zaleznosci zewnetrznych (HA, baza, pogoda).

Przyklad struktury testu:

```csharp
[Fact]
public async Task ExecuteAsync_PreHeatingCondition_SetsDeficitMultipliers()
{
    // Arrange
    var service = new Phase0ForecastService(...);
    var state = CreateHeatingState();
    var parameters = CreateHeatingParameters();

    // Act
    var result = await service.ExecuteAsync(state, parameters, CancellationToken.None);

    // Assert
    Assert.Equal(ForecastMode.PreHeating, state.BoilerState.ForecastMode);
    Assert.True(parameters.DeficitHighP1 < parameters.DeficitHighP1Base);
}
```

### Testy integracyjne (Infrastructure / Api)

- `HomeAssistantClientTests` – weryfikuja parsowanie odpowiedzi HA i obsluge bledow.
- `HeatFlow.Api.Tests` – testuja kontrolery z uzyciem `WebApplicationFactory` (opcjonalnie z InMemoryDatabase).

---

## Debugowanie testow w Visual Studio

1. Otworz `HeatFlow.sln` w Visual Studio.
2. Przejdz do Eksploratora testow (Test > Test Explorer).
3. Kliknij prawym przyciskiem na test > **Debug**.
4. Mozesz ustawiac pulapki (breakpoints) w kodzie zrodlowym faz i helperow.

---

## Wskazowki kodowania

- **Jezyk:** C# 13 / .NET 10.
- **Styl:** Domyslny styl .NET. Uzywaj `var` tam, gdzie typ jest oczywisty.
- **DI:** Wszystkie zaleznosci zewnetrzne powinny byc wstrzykiwane przez konstruktor. Unikaj `new` dla serwisow infrastrukturalnych.
- **Async:** Wszystkie operacje I/O (HA, baza) sa asynchroniczne (`async/await`).
- **Logowanie:** Uzywaj `ILogger<T>` do logowania. Unikaj `Console.WriteLine`.
- **Wyjatki:** Nie lap wyjatkow w logice biznesowej, jesli nie musisz. W infrastrukturze uzywaj `IApplicationErrorLogger` do logowania bledow bez przerywania pracy systemu.

---

## Nastepny krok

Pelna dokumentacje uzytkowa znajdziesz w poprzednich rozdzialach:

- [01-przeglad-systemu.md](01-przeglad-systemu.md) – ogolny opis systemu
- [04-konfiguracja.md](04-konfiguracja.md) – jak dzialaja parametry
- [05-uzytkowanie.md](05-uzytkowanie.md) – opis algorytmu
