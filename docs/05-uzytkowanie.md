# 5. Użytkowanie

Ten dokument opisuje, co dzieje się w systemie podczas każdego cyklu pracy, jak interpretować jego decyzje oraz gdzie szukać informacji o stanie.

---

## Cykl pracy HeatFlow.Console

Aplikacja Console jest uruchamiana co minutę (Windows Scheduled Task). W każdym przebiegu wykonuje następujące kroki:

1. **Sprawdzenie włącznika** – jeśli `SystemConfiguration.SystemEnabled = false`, cykl jest pomijany.
2. **Faza 0 (opcjonalnie)** – wykonywana maksymalnie raz na godzinę. Analizuje prognozę pogody i modyfikuje parametry algorytmu.
3. **Faza 1** – odczytuje temperatury pokojów, oblicza deficyty i klasyfikacje.
4. **Faza 2** – wybiera pokoje do grzania (maksymalnie 5).
5. **Faza 3** – ustawia temperatury na zaworach.
6. **Faza 4** – ewentualnie przełącza piec w tryb letni lub zimny.
7. **Zapis wyników** – historia wykonania i stany są zapisywane do bazy danych.

Między fazami występuje 2-sekundowe opóźnienie, aby nie przeciążać Home Assistant.

---

## Faza 0 – Prognoza pogody

### Kiedy się wykonuje

Co najwyżej raz na godzinę (śledzony jest czas `_lastPhase0Execution` w `OrchestrationService`).

### Co robi

1. Pobiera współrzędne geograficzne z `SystemConfiguration.Latitude` / `Longitude`.
2. Sprawdza cache w tabeli `ForecastDataCache` (ważny 1 godzinę).
3. Jeśli cache jest nieaktualny lub nieistnieje, wywołuje **OpenWeatherMap API** (`IOpenWeatherMapClient.GetWeatherDataAsync`).
4. Oblicza:
   - **minimalną temperaturę** w ciągu najbliższych `ForecastHoursCount` godzin,
   - **różnicę temperatury**: aktualna – minimalna.
5. Na podstawie różnicy wyznacza **tryb prognozy**:
   - **PreHeating** (`tempDiff <= -ForecastTempDropThreshold`) – zbliża się ochłodzenie. System zmniejsza progi deficytów (mnożniki < 1), żeby zacząć grzać wcześniej.
   - **Reduction** (`tempDiff >= ForecastTempRiseThreshold`) – zbliża się ocieplenie. System zwiększa progi deficytów (mnożniki > 1), żeby ograniczyć grzanie.
   - **Normal** – brak zmian, przywracane są wartości bazowe.
6. Zmodyfikowane parametry są zapisywane do bazy (`IConfigurationService.UpdateHeatingParametersAsync`).
7. Tryb prognozy jest zapisywany do Home Assistant jako `input_number.forecast_mode` (wartości: 0=Normal, 1=PreHeating, 2=Reduction).

### Wyjście

- Zaktualizowana tabela `HeatingParameters` (tymczasowo zmienione progi deficytów i bufor).
- Encja HA `input_number.forecast_mode`.

---

## Faza 1 – Diagnoza zapotrzebowania

### Cel

Określić, które pokoje potrzebują ogrzewania i jak bardzo.

### Co robi (dla każdego włączonego pokoju)

1. **Odczyt temperatury** z encji HA wskazanej w `RoomConfiguration.SensorTemperatureEntityId`.
   - Jeśli encja to `climate.*`, odczytywany jest atrybut `current_temperature`.
   - Jeśli odczyt się nie powiedzie, używana jest temperatura docelowa jako fallback.
2. **Walidacja temperatury** – sprawdzenie, czy mieści się w zakresie `TempValidationMin` – `TempValidationMax`.
3. **Harmonogram grzania** – na podstawie `HeatingSchedule` i dnia tygodnia określa, czy obowiązuje tryb aktywny.
4. **Temperatura docelowa** – wybrana z `TempTargetActive` (gdy aktywny) lub `TempTargetInactive` (gdy nieaktywny).
5. **Czy pokój będzie używany wkrótce?** – sprawdzenie `UsageSchedule` z wyprzedzeniem `BufferHeatingTime` minut.
6. **Obliczenie deficytu**:
   - `deficit = tempTarget - tempActual`
7. **Obliczenie score**:
   ```
   score = (1 / Priority * ScorePriorityMultiplier)
         + (TempDeficit * ScoreDeficitMultiplier)
         + (Sensitive ? ScoreSensitiveBonus : 0)
         + (usageSoon ? ScoreUsageSoonBonus : 0)
         + (heatingActive ? ScoreHeatingScheduleBonus : 0)
   ```
8. **Klasyfikacja deficytu** (`Room.ClassifyDeficit()`):
   - `Score > 50` → **Max** (pokój wymaga maksymalnego grzania)
   - `Score < 0` → **Disabled** (pokój jest za ciepły lub wyłączony)
   - w przeciwnym razie → **Stay** (utrzymanie bieżącej temperatury)
9. **Ustalenie temperatury zaworu** (`ChangeTemperatureToSet`):
   - **Max** → `MaximalSetTemperature` (domyślnie 35°C)
   - **Stay** → `TempTarget` (utrzymanie)
   - **Disabled** → `MinimalSetTemperature` (domyślnie 5°C, zawór zamknięty)

### Wyjście

- Zaktualizowane obiekty `Room` w pamięci (`HeatingState.Rooms`) z polami: `TempActual`, `TempDeficit`, `Score`, `DeficitClassification`, `TemperatureToSet`.

---

## Faza 2 – Arbitraż

### Cel

Wybrać maksymalnie 5 pokojów, które faktycznie będą ogrzewane, aby nie przeciążać pieca.

### Co robi

1. Filtruje pokoje z klasyfikacją **Max**.
2. Sortuje je malejąco według `Score`.
3. Wybiera maksymalnie `MaxValvesOpen` (domyślnie 5) pokoi z listy Max.
4. **Pokój bezpieczeństwa (safety room):**
   - Jeśli żaden pokój nie ma klasyfikacji Max, wybierany jest najzimniejszy pokój i wymuszana jest na nim klasyfikacja Max (`SetSafetyRoom()`).
5. Uzupełnianie pozostałych slotów (do 5) pokojami **Stay**, sortując rosnąco według `Score`.
6. Wszystkie wybrane pokoje dostają flagę `HeatingEnabled = true`.
7. Pozostałe pokoje trafiają do `RoomsToDisable`.

### Wyjście

- `HeatingState.RoomsToHot` – pokoje Max do ogrzania.
- `HeatingState.RoomsToStay` – pokoje Stay utrzymywane.
- `HeatingState.RoomsToDisable` – pokoje wyłączone.

---

## Faza 3 – Sterowanie zaworami

### Cel

Fizycznie ustawić temperatury na zaworach termostatycznych w Home Assistant.

### Co robi

Dla każdego pokoju w grupach `RoomsToHot`, `RoomsToStay`, `RoomsToDisable`:

1. Określa docelową temperaturę zaworu (`TemperatureToSet`).
2. Wysyła komendę do Home Assistant:
   - Dla `climate.*` – wywołanie usługi `climate/set_temperature`.
   - Dla `number.*_valve` – wywołanie usługi `number/set_value`.
3. **Weryfikacja i retry:**
   - Odczytuje aktualną wartość zaworu.
   - Jeśli różnica przekracza `ValveTolerance` (0.1°C), ponawia próbę.
   - Maksymalnie `ValveRetryCount` (3) prób z opóźnieniem `ValveRetryDelay` (1 s).
4. **Fallback bezpieczeństwa:**
   - Jeśli wszystkie zawory w `RoomsToHot` się nie powiodły, wybiera najzimniejszy pokój z `RoomsToStay` lub `RoomsToDisable`, wymusza na nim `SetSafetyRoom()` i ponawia ustawienie.
   - Jeśli to też się nie uda – zwraca błąd krytyczny.

### Wyjście

- Zaktualizowane encje zaworów w Home Assistant.
- Lista `ValveResult` w `PhaseResult` (zapisana potem do `ValveState` w bazie).

---

## Faza 4 – Tryb letni / zimny

### Cel

Automatyczne wyłączenie ogrzewania (przejście w tryb letni) przy ciepłej pogodzie i włączenie przy ochłodzeniu.

### Co robi

1. Odczytuje stan przełącznika `switch.kociol_tryb_zima_lato` z Home Assistant.
2. Sprawdza log dzienny w tabeli `SummerModeLog`.

#### Aktywacja trybu letniego (winter → summer)

Warunki **wszystkie muszą być spełnione**:

- Godzina mieści się w przedziale **06:00 – 13:59**.
- Temperatura zewnętrzna (`BoilerState.TempExternal`) **> 10°C**.
- **Żaden** włączony pokój nie ma klasyfikacji **Max**.
- Dziś nie było jeszcze aktywacji.

Jeśli warunki są spełnione – wywołuje usługę `turn_on` na przełączniku `switch.kociol_tryb_zima_lato` i zapisuje aktywację w `SummerModeLog`.

#### Deaktywacja trybu letniego (summer → winter)

Warunki **wszystkie muszą być spełnione**:

- Co najmniej **2 pokoje** mają klasyfikację **Max**.
- Dla tych pokoi: `TempActual < TempTarget - 1°C`.
- Jeśli dziś była aktywacja – musi minąć co najmniej **3 godziny**.
- Dziś nie było jeszcze deaktywacji.

Jeśli warunki są spełnione – wywołuje usługę `turn_off` na przełączniku i zapisuje deaktywację.

### Wyjście

- Stan przełącznika w Home Assistant.
- Zapis w tabeli `SummerModeLog`.

---

## Logi i monitoring

### Gdzie szukać logów

| Aplikacja | Lokalizacja | Format |
|-----------|-------------|--------|
| **Console** | Konsola (stdout) + plik `logs/heatflow-YYYY-MM-DD.log` | Serilog, tekstowy |
| **Api** | Konsola (stdout) + plik `logs/log-YYYY-MM-DD.log` | Serilog, tekstowy |

### Co można znaleźć w logach

- Informacje o starcie i zakończeniu każdej fazy.
- Odczytane temperatury pokojów i encji systemowych.
- Obliczone deficyty, score i klasyfikacje.
- Wybrane pokoje do grzania (`RoomsToHot`).
- Ustawione temperatury zaworów i wyniki retry.
- Decyzje dotyczące trybu letniego/zimnego.
- Błędy połączeń z Home Assistant lub bazą danych.

### Przykładowy log (Console)

```
[2026-06-23 10:00:00 INF] Uruchamianie HeatFlow.Console
[2026-06-23 10:00:01 INF] Faza 0 wykonana: Normal, różnica temp: 2.5°C
[2026-06-23 10:00:03 INF] Faza 1: przetworzono 6 pokoi, 3 x Max, 2 x Stay, 1 x Disabled
[2026-06-23 10:00:05 INF] Faza 2: wybrano 3 pokoje do grzania
[2026-06-23 10:00:08 INF] Faza 3: sukces 3/3 zaworów
[2026-06-23 10:00:10 INF] Faza 4: tryb zimny, brak zmian
[2026-06-23 10:00:10 INF] Wykonanie zakończone sukcesem
```

### Baza danych jako źródło historyczne

W bazie danych znajdują się tabele telemetryczne:

- **`ExecutionHistory`** – każde wykonanie fazy (czas, status, czas trwania, ewentualny błąd).
- **`RoomState`** – stany pokojów po Fazie 1 (temperatury, deficyty, score, klasyfikacje).
- **`BoilerState`** – stan pieca po Fazie 4 (temperatury, tryb prognozy).
- **`ValveState`** – wyniki ustawiania zaworów po Fazie 3 (temperatura zadana, faktyczna, sukces/porażka).

Możesz je przeglądać bezpośrednio w SQL Server lub przez API (`/api/error-logs`, `/api/configuration-changes`).

---

## Co się dzieje, gdy wyłączę system?

Jeśli ustawisz `SystemConfiguration.SystemEnabled = false` (lub przez API / HA):

- Console przy każdym uruchomieniu natychmiast kończy pracę z komunikatem `Skipped`.
- Żadne zawory ani piec nie są sterowane przez HeatFlow.
- Home Assistant nadal działa samodzielnie – termostaty mogą mieć własne ustawienia.
- Api nadal działa i pozwala na edycję konfiguracji.

---

## Następny krok

Jeśli chcesz integrować system z Home Assistant, przejdź do [07-integracja-home-assistant.md](07-integracja-home-assistant.md). Szczegóły techniczne API znajdziesz w [06-api-rest.md](06-api-rest.md).
