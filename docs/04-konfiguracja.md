# 4. Konfiguracja

HeatFlow przechowuje całą konfigurację w bazie danych SQL Server. Home Assistant jest używane wyłącznie jako źródło danych bieżących (temperatury) i aktuator (zawory, piec). Zmiany wprowadzasz przez REST API (np. z poziomu integracji Home Assistant) lub bezpośrednio w bazie.

---

## Tabele konfiguracyjne

### SystemConfiguration

Tabela zawiera **jeden rekord** (`Id = 1`) opisujący cały system.

| Pole | Typ | Opis |
|------|-----|------|
| `RoomsList` | string | Lista pokojów oddzielonych przecinkami, np. `sypialnia,salon,lazienka`. |
| `EkoPiecDeviceSn` | string | Numer seryjny pieca (używany do budowania nazw encji HA). |
| `TempReturnEntityId` | string | ID encji HA z temperaturą powrotu, np. `sensor.kociol_temperatura_powrotu`. |
| `Mixer4DPositionEntityId` | string | ID encji HA z pozycją zaworu 4D. |
| `BoilerTempEntityId` | string | ID encji HA z temperaturą pieca (opcjonalne). |
| `SystemEnabled` | bool | Główny włącznik systemu. Gdy `false`, Console pomija wykonanie. |
| `Latitude` | double | Szerokość geograficzna (do prognozy OpenWeatherMap). |
| `Longitude` | double | Długość geograficzna. |

### RoomConfiguration

Każdy pokój to osobny rekord (klucz główny: `Name`).

| Pole | Typ | Opis |
|------|-----|------|
| `Name` | string | Unikalna nazwa pokoju (np. `sypialnia`). |
| `TempTarget` | double | Temperatura docelowa podstawowa. |
| `TempTargetActive` | double | Temperatura w godzinach aktywnych (harmonogram grzania). |
| `TempTargetInactive` | double | Temperatura poza godzinami aktywnymi. |
| `Priority` | int | Priorytet grzania: **1 = najwyższy**, 4 = najniższy. |
| `Sensitive` | bool | Czy pokój jest wrażliwy (sypialnia, pokój dzieci) – dodatkowy bonus w score. |
| `AutomationDisabled` | bool | Gdy `true`, pokój jest pomijany przez algorytm. |
| `UsageSchedule` | string | Harmonogram użytkowania, format: `HH:MM-HH:MM,HH:MM-HH:MM` lub `Brak`. Pierwsza część = dni robocze, druga = weekend. |
| `HeatingSchedule` | string | Harmonogram grzania (ten sam format). |
| `SensorTemperatureEntityId` | string | ID encji HA z temperaturą pokoju. |
| `ValveEntityId` | string | ID encji HA zaworu (`climate.*` lub `number.*_valve`). |
| `MinimalSetTemperature` | int | Minimalna temperatura zaworu (domyślnie 5°C). |
| `MaximalSetTemperature` | int | Maksymalna temperatura zaworu (domyślnie 35°C). |

> **Format harmonogramu:** `weekday|weekend`. Przykład: `06:00-23:00|Brak` oznacza grzanie w dni robocze od 6:00 do 23:00, w weekend wyłączone. Wartość `Brak` oznacza brak harmonogramu.

### HeatingParameters

Tabela zawiera **jeden rekord** (`Id = 1`) ze wszystkimi parametrami algorytmu.

#### Progi deficytów (HIGH)

| Pole | Domyślnie | Opis |
|------|-----------|------|
| `DeficitHighP1` | 1.0 | Próg deficytu HIGH dla priorytetu 1. |
| `DeficitHighP2` | 2.0 | Próg deficytu HIGH dla priorytetu 2. |
| `DeficitHighP3` | 3.0 | Próg deficytu HIGH dla priorytetu 3. |

> W kodzie obecnie używane są wartości bazowe (`DeficitHighP1Base` itd.), które są modyfikowane przez Fazę 0 w zależności od prognozy.

#### Parametry bufora i prognozy

| Pole | Domyślnie | Opis |
|------|-----------|------|
| `BufferPreparation` | 0.8 | Wartość bufora przygotowania (dodawana do deficytu). |
| `BufferHeatingTime` | 60 | Czas wyprzedzenia grzania w minutach (przed planowanym użyciem pokoju). |
| `ForecastTempDropThreshold` | 5.0 | Różnica temperatury (°C), przy której włącza się tryb PreHeating. |
| `ForecastTempRiseThreshold` | 3.0 | Różnica temperatury (°C), przy której włącza się tryb Reduction. |
| `ForecastHoursCount` | 8 | Liczba godzin prognozy branej pod uwagę. |
| `ForecastPreHeatingP1Multiplier` | 0.8 | Mnożnik deficytu P1 w trybie PreHeating. |
| `ForecastPreHeatingP2Multiplier` | 0.9 | Mnożnik deficytu P2 w trybie PreHeating. |
| `ForecastPreHeatingP3Multiplier` | 0.9 | Mnożnik deficytu P3 w trybie PreHeating. |
| `ForecastPreHeatingBufferMultiplier` | 1.2 | Mnożnik bufora w trybie PreHeating. |
| `ForecastReductionP1Multiplier` | 1.2 | Mnożnik deficytu P1 w trybie Reduction. |
| `ForecastReductionP2Multiplier` | 1.2 | Mnożnik deficytu P2 w trybie Reduction. |
| `ForecastReductionP3Multiplier` | 1.2 | Mnożnik deficytu P3 w trybie Reduction. |
| `ForecastReductionBufferMultiplier` | 0.8 | Mnożnik bufora w trybie Reduction. |

#### Parametry arbitrażu (Faza 2)

| Pole | Domyślnie | Opis |
|------|-----------|------|
| `MaxValvesOpen` | 5 | Maksymalna liczba pokojów grzanych jednocześnie. |
| `MinValvesOpen` | 1 | Minimalna liczba otwartych zaworów. |
| `UsageSoonMinutes` | 30 | Ile minut przed planowanym użyciem uwzględniać bonus. |
| `ScorePriorityMultiplier` | 100 | Waga priorytetu w obliczaniu score. |
| `ScoreDeficitMultiplier` | 10 | Waga deficytu w obliczaniu score. |
| `ScoreSensitiveBonus` | 50 | Dodatkowe punkty dla pokoju wrażliwego. |
| `ScoreUsageSoonBonus` | 20 | Dodatkowe punkty, gdy pokój będzie używany wkrótce. |
| `ScoreHeatingScheduleBonus` | 50 | Dodatkowe punkty, gdy obowiązuje harmonogram grzania. |

#### Parametry zaworów (Faza 3)

| Pole | Domyślnie | Opis |
|------|-----------|------|
| `ValveTolerance` | 0.1 | Tolerancja temperatury zaworu (°C) przy weryfikacji ustawienia. |
| `ValveRetryCount` | 3 | Liczba prób ustawienia zaworu w razie niepowodzenia. |
| `ValveRetryDelay` | 1.0 | Opóźnienie między próbami (sekundy). |

#### Parametry trybu lato (Faza 4)

| Pole | Domyślnie | Opis |
|------|-----------|------|
| `SummerModeWarmDayTemp` | 20.0 | Próg „ciepłego dnia" (°C). Max temperatura z najbliższych `SummerModeForecastHours` godzin prognozy (lub temperatura zewnętrzna, gdy brak prognozy) >= tej wartości blokuje wyjście z trybu lato i jest wymagana do wejścia w tryb lato. Edytowalny z HA. |
| `SummerModeForecastHours` | 6 | Okno prognozy „ciepłego dnia" w godzinach od teraz – to samo przy wejściu i wyjściu z trybu lato (decyzja Pawła 2026-10-20, wcześniej aktywacja 24h). Zakres 1–48, poza nim 6. Edytowalny z HA. |

#### Parametry pieca (nieużywane bezpośrednio – logika w Fazie 0/4)

| Pole | Domyślnie | Opis |
|------|-----------|------|
| `BoilerNominalTemp` | 70.0 | Nominalna temperatura pieca (°C). |
| `MinReturnTemp` | 45.0 | Minimalna temperatura powrotu (°C). |

#### Parametry bezpieczeństwa

| Pole | Domyślnie | Opis |
|------|-----------|------|
| `Hysteresis` | 0.5 | Histereza wyłączania grzania (°C). |
| `HysteresisSafetyThreshold` | 2.0 | Próg bezpieczeństwa histerezy. |
| `TempValidationMin` | 0.0 | Dolna granica akceptowalnej temperatury. |
| `TempValidationMax` | 40.0 | Górna granica akceptowalnej temperatury. |

---

## Jak mapować encje Home Assistant

W tabeli `RoomConfiguration` musisz wypełnić dwa kluczowe pola dla każdego pokoju:

- **`SensorTemperatureEntityId`** – skąd Console czyta temperaturę.
- **`ValveEntityId`** – gdzie Console zapisuje temperaturę zaworu.

### Przykład konfiguracji pokoju

Załóżmy, że w Home Assistant masz:

- czujnik temperatury: `sensor.sypialnia_czujnik_temperature`
- zawór: `climate.sypialnia` (lub `number.sypialnia_valve`)

Wówczas w `RoomConfiguration` dla pokoju `sypialnia` ustawiasz:

```sql
UPDATE RoomConfiguration
SET SensorTemperatureEntityId = 'sensor.sypialnia_czujnik_temperature',
    ValveEntityId = 'climate.sypialnia'
WHERE Name = 'sypialnia';
```

### Encje systemowe w SystemConfiguration

| Encja HA | Pole w SystemConfiguration | Przykład |
|----------|---------------------------|----------|
| Temperatura powrotu | `TempReturnEntityId` | `sensor.kociol_temperatura_powrotu` |
| Pozycja zaworu 4D | `Mixer4DPositionEntityId` | `sensor.kociol_pozycja_zaworu_4d` |
| Przełącznik letni/zimny | – (stała w kodzie) | `switch.kociol_tryb_zima_lato` |
| Pogoda | – (automatyczne wykrywanie) | `weather.home` lub `weather.openweathermap` |

---

## Wypełnianie danych początkowych (seed)

Przy pierwszym uruchomieniu Console lub Api baza jest automatycznie wypełniana domyślnymi danymi przez klasę `ConfigurationSeed`:

- **HeatingParameters** – wartości domyślne wszystkich parametrów.
- **SystemConfiguration** – domyślna konfiguracja systemu (wymaga dostosowania!).
- **RoomConfiguration** – przykładowe pokoje (wymagają dostosowania encji HA!).

**Po pierwszym uruchomieniu MUSISZ dostosować:**

1. `SystemConfiguration.RoomsList` – lista Twoich pokojów.
2. `SystemConfiguration.EkoPiecDeviceSn` – numer seryjny pieca.
3. `SystemConfiguration.TempReturnEntityId` – encja temperatury powrotu.
4. `SystemConfiguration.Latitude` / `Longitude` – współrzędne (jeśli używasz OpenWeatherMap).
5. Dla każdego pokoju w `RoomConfiguration`:
   - `SensorTemperatureEntityId`
   - `ValveEntityId`
   - `TempTarget`, `TempTargetActive`, `TempTargetInactive`
   - `Priority`, `Sensitive`
   - `UsageSchedule`, `HeatingSchedule`

---

## Zmiana konfiguracji przez API

Zamiast ręcznie edytować bazę, możesz użyć REST API. Szczegóły znajdziesz w:

- [06-api-rest.md](06-api-rest.md) – opis ogólny API,
- [HeatFlow.Api-Kontrakt.md](HeatFlow.Api-Kontrakt.md) – pełna specyfikacja endpointów.

Przykład aktualizacji pokoju przez curl:

```bash
curl -X PUT -H "X-API-Key: TWOJ_KLUCZ" -H "Content-Type: application/json" \
  -d '{"name":"sypialnia","tempTarget":21.0,"tempTargetActive":22.0,"tempTargetInactive":19.0,"priority":1,"sensitive":true,"automationDisabled":false,"usageSchedule":"22:00-07:00|Brak","heatingSchedule":"Brak|Brak","sensorTemperatureEntityId":"sensor.sypialnia_temperature","valveEntityId":"climate.sypialnia"}' \
  http://localhost:5000/api/rooms/sypialnia
```

Każda zmiana przez API jest automatycznie rejestrowana w tabeli `ConfigurationChangeLog` (audyt).

---

## Następny krok

Gdy konfiguracja jest gotowa, przejdź do [05-uzytkowanie.md](05-uzytkowanie.md), aby zrozumieć, jak system pracuje na co dzień.

## Faza 5 „Dawka” - konfiguracja w bazie

Nic z tego nie jest w kodzie - w kodzie zostają tylko twarde granice Pawła (`DoseLimits`: praca 7-10 s,
postój 100-320 s, dmuchawa 33-40 %; podniesienie dmuchawy ponad 40 % = zmiana kodu).

**`SystemConfiguration`** (wartości u nas wpisuje migracja `AddDosePhase`, wzór: `ConfigurationSeed.DoseEntityDefaults`):

| Pole | U nas |
|---|---|
| `BoilerTempEntityId` | `sensor.kociol_temperatura_kotla` |
| `DoseOutdoorTempEntityId` | `sensor.temperatura_ogrod_temperature` |
| `DoseOutdoorTemp2EntityId` (opcjonalne) | `sensor.temperatura_przodek_temperature` - Tz do dawki to średnia z czujników z odczytem nie starszym niż `DoseOutdoorStaleMinutes` (360 min; wiek = last_updated albo last_reported); gdy żaden nie ma świeżego, temperatura z prognozy; `BRAK_TZ` tylko bez obu |
| `DoseBoilerSetpointEntityId` | `number.kociol_temperatura_zadana_kotla` (tylko odczyt) |
| `DoseFlueTempEntityId` | `sensor.kociol_temperatura_spalin` |
| `DoseBlowerActualEntityId` | `sensor.kociol_aktualna_moc_dmuchawy` |
| `DoseFeederCounterEntityId` | `sensor.kociol_czas_pracy_podajnika_2` |
| `DoseSummerSwitchEntityId` | `switch.kociol_tryb_zima_lato` |
| `DoseControllerModeEntityId` | `switch.kociol_tryb_pracy` |
| `DoseControllerClockEntityId` | `sensor.kociol_aktualna_data` |
| `DoseFlueSafetyAutomationEntityId` | `automation.kociol_bezpiecznik_spalin_dmuchawa_na_minimum` (opcjonalne) |
| `DoseFeederWorkEntityId` / `DoseFeederPauseEntityId` / `DoseBlowerEntityId` | `number.kociol_czas_pracy_podajnika` / `number.kociol_czas_postoju_podajnika` / `number.kociol_moc_dmuchawy` - jedyne zapisywane; muszą być `number.*`, różne i ≠ nastawa kotła |
| `DoseNotifyService` / `DoseNotifyTarget` | `homeintegrator` / `1458898223593689120` (Discord, ten sam cel co alarmy kotła w HA) |

Brak albo błąd którejś encji: Faza 5 kończy się błędem z listą braków, a włączony automat sam się wyłącza
(bez przywracania - nie wiadomo, gdzie pisać).

**`RoomConfiguration.UseForDoseIndoorMean`** - czy czujnik pokoju wchodzi do średniej temperatury w domu
(bilans dobowy, korekta k). U nas 7× BMx280: kuchnia, salon, spiżarnia, suszarnia, edyta, hol, przejściowy.
W HA: switch „Dawka: średnia w domu” w urządzeniu pokoju.

**`RoomConfiguration.HeatLossW`** - zapotrzebowanie pokoju [W] z inwentaryzacji = waga celu pokoju w **celu domu**,
z którego liczona jest dawka (decyzja Pawła 2026-10-20): cel domu = Σ(waga · cel pokoju po harmonogramie) / Σ wag
(u nas: balkonowy 1344, garaż 965, salon 626, jadalnia 625, hol 622, edyta 604, sypialnia 584, grześ 500,
przejściowy 484, suszarnia 448, kuchnia 430, sień 340, łazienka 293, spiżarnia 159, toaleta 159 W). Pokoje z
wyłączoną automatyką też się liczą; waga 0 = pokój nie wchodzi. W HA: liczba „Dawka: straty ciepła (W)” w pokoju.
Spadek celu działa od razu; wzrost z szybkością `DoseTargetRiseKPerHour` (0,07 K/h ≈ 1 K w 14 h) z dodatkiem na
dogrzanie domu `HouseCapacity · tempo` (≤ `DoseStorageBonusMaxSec`). `DoseTargetFromRooms` = wył. → stała
`IndoorTargetForFeed` (też awaryjnie, gdy żaden pokój nie ma wagi). Pomiar do korekty k: 7 czujników z flagą
(domyślnie) albo `DoseIndoorMeasureWeighted` = wszystkie pokoje ważone HeatLossW.

**Cel pokoju = wyższa z dwóch: cel albo faktyczna** (`DoseTargetUseActual`, domyślnie wł., decyzja Pawła
2026-10-20) - pokój cieplejszy niż cel i tak zabiera ciepło, więc liczy się po faktycznej temperaturze
(odczyt jak w Fazie 1: sensor albo `current_temperature` głowicy). Faktyczna wygładzona średnią wykładniczą
`DoseActualSmoothingHours` (24 h - tłumi dobowe wahania od słońca ~6×; 3 h tłumi je tylko ~1,3×). Przycięcie
od góry, żeby nadmiar paliwa nie podbijał celu sam siebie: faktyczna pokoju najwyżej do najwyższego celu wśród
pokoi z wagą (dziś 26 °C), a cały cel domu ≤ cel z samych celów + `DoseActualMaxAboveTargetsK` (2 K).
Wzrost celu od faktycznych działa od razu i bez dodatku na dogrzanie (dom już ma tę temperaturę); ograniczenie
`DoseTargetRiseKPerHour` i dodatek dotyczą tylko wzrostu celów. Wpis CEL_DOMU w dzienniku podaje, które pokoje
weszły po faktycznej, a które po celu. **`RoomConfiguration.DoseUseActual`** (domyślnie wł.; w HA switch
„Dawka: cel z faktycznej” w pokoju) = wył. → pokój zawsze po celu - np. garaż, ciepły od cudzego ciepła
(średnio ~16 °C przy celu 6 °C, zawór zamknięty), podnosi cel domu o 0,9–1,2 K.

**`HeatingParameters`** - wszystkie progi Fazy 5 (`Feed*`, `Dose*`, `Blower*`, `Feeder*Min/Max`), z walidacją
w `DoseLimits.Validate` i gałkami w HA (`number.heatflow_parametry_dawka_*`).

**appsettings Console:** `EsterownikMapper:BaseUrl` (u nas `http://172.17.137.33:5000/`).

