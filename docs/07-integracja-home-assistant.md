# 7. Integracja Home Assistant

HeatFlow dostarcza wlasna **custom component** (integracje) dla Home Assistant. Umozliwia ona wygodna konfiguracje pokojow i parametrow algorytmu bezposrednio z interfejsu HA, zamiast recznej edycji bazy danych lub wywolywania API przez curl.

---

## Co robi integracja

Integracja laczy Home Assistant z **HeatFlow.Api** i tworzy encje reprezentujace:

- **pokoje** – temperatury docelowe, priorytet, flagi (wrazliwy, automatyka wylaczona),
- **parametry algorytmu** – wybrane parametry grzania (suwaki),
- **sensory statusu** – liczba pokojow, stan parametrow, historia zmian.

Zmiany wprowadzone w encjach HA sa automatycznie wysylane do API, zapisywane w bazie danych i rejestrowane w audycie (`ConfigurationChangeLog`).

---

## Instalacja

### Metoda 1: HACS (zalecana)

1. Zainstaluj [HACS](https://hacs.xyz/) w swoim Home Assistant.
2. Przejdz do **HACS > Integracje**.
3. Kliknij menu **⋮ > Custom repositories**.
4. Dodaj URL repozytorium HeatFlow i wybierz kategorie **Integration**.
5. Wyszukaj **HeatFlow** i zainstaluj.
6. Zrestartuj Home Assistant.

### Metoda 2: Reczna instalacja

1. Skopiuj folder `integration/custom_components/heatflow` z repozytorium do katalogu `config/custom_components/heatflow` w Twoim Home Assistant.
2. Zrestartuj Home Assistant.

### Metoda 3: Aktualizacja z terminala (SSH / Terminal add-on)

```bash
cd /config/custom_components
rm -rf heatflow
git clone https://github.com/pawelszulik/heatflow heatflow-repo
cp -r heatflow-repo/integration/custom_components/heatflow .
rm -rf heatflow-repo
ls -la heatflow/
```

Po skopiowaniu zrestartuj Home Assistant.

---

## Konfiguracja integracji

1. Przejdz do **Ustawienia > Urzadzenia i uslugi > Dodaj integracje**.
2. Wyszukaj **HeatFlow**.
3. Wprowadz:
   - **URL API** – adres, pod ktorym dziala HeatFlow.Api, np. `http://192.168.1.50:5000`,
   - **Klucz API** – wartosc z `HeatFlow:ApiKey` w konfiguracji Api.
4. Integracja zweryfikuje polaczenie przez `GET /api/health` i utworzy encje.

### Zmiana adresu lub klucza API

Gdy HeatFlow.Api przeniesie sie na inny host albo zmieni sie klucz: **Ustawienia > Urzadzenia i uslugi > HeatFlow > ⋮ > Skonfiguruj ponownie**. Nowy adres jest walidowany tak samo jak przy dodawaniu, a wpis konfiguracyjny jest aktualizowany w miejscu.

**Nie usuwaj i nie dodawaj integracji od nowa, zeby zmienic adres** – `unique_id` encji zawiera `entry_id` wpisu, wiec nowy wpis oznacza nowe `entity_id` dla wszystkich encji i rozsypane karty na dashboardach.

---

## Tworzone encje

### Sensor

| Encja | Wartosc | Atrybuty |
|-------|---------|----------|
| `sensor.heatflow_status` | `ok` | `rooms_count`, `heating_parameters_loaded` |
| `sensor.heatflow_configuration_changes` | liczba zmian | `last_changes` (lista ostatnich zmian), `changes_count` |

### Number (pokoje)

Dla kazdego pokoju tworzone sa 3 suwaki:

| Encja | Zakres | Krok | Opis |
|-------|--------|------|------|
| `number.{pokoj}_temp_target` | 5 – 30°C | 0.5 | Temperatura docelowa |
| `number.{pokoj}_temp_target_active` | 5 – 30°C | 0.5 | Temperatura w godzinach aktywnych |
| `number.{pokoj}_temp_target_inactive` | 5 – 30°C | 0.5 | Temperatura poza godzinami aktywnymi |

Zmiana wartosci wysyla `PUT /api/rooms/{name}` z zaktualizowanym pokojem.

### Number (parametry algorytmu)

Wybrane parametry z `HeatingParameters` sa udostepniane jako globalne suwaki:

- `deficitHighP1`, `deficitHighP2`, `deficitHighP3`
- `bufferPreparation`, `bufferHeatingTime`
- `forecastTempDropThreshold`, `forecastHoursCount`
- `maxValvesOpen`, `minValvesOpen`
- `boilerNominalTemp`, `minReturnTemp`
- `hysteresis`, `hysteresisSafetyThreshold`

Kazdy ma wlasny zakres, krok i jednostke. Zmiana wysyla `PATCH /api/heating-parameters`.

### Select

Dla kazdego pokoju:

- `select.{pokoj}_priority` – opcje: `1`, `2`, `3`, `4`

Zmiana wysyla `PUT /api/rooms/{name}`.

### Switch

Dla kazdego pokoju:

- `switch.{pokoj}_sensitive` – **Wrazliwy** (dodatkowy bonus w score),
- `switch.{pokoj}_automation_disabled` – **Automatyka wylaczona** (pokoj jest ignorowany).

Zmiana wysyla `PUT /api/rooms/{name}`.

---

## Przeplyw danych HA <-> Api <-> DB <-> Console

```
+---------------+       PUT/PATCH        +-------------+       SQL        +-------------+
|  Home Assistant|  ------------------>   | HeatFlow.Api|  ------------>   |   SQL Server |
|  (encje HA)    |                      |             |                  |  (RoomConfig, |
|                |  <------------------  |             |  <------------   |   Parameters)|
|  [coordinator] |   polling co 2 min    |             |                  |              |
+---------------+                       +-------------+                  +-------------+
                                                                                |
                                                                                | SELECT
                                                                                v
+---------------+       REST API         +-------------+                       +-------------+
|  Home Assistant|  <------------------  | HeatFlow.   |  <-----------------   |   SQL Server |
|  (sensory CO)  |   (temperatury,      |  Console    |                       |  (config)    |
|  (zawory, piec)|    zawory, pogoda)   |             |                       |              |
+---------------+                       +-------------+                       +-------------+
        |                                       |
        | ustawia zawory / przełącznik          | zapisuje historie
        v                                       v
+---------------+                       +-------------+
|  Aktualny stan|                       | Execution   |
|  ogrzewania   |                       | History,    |
|  w domu       |                       | RoomState,  |
|               |                       | BoilerState |
+---------------+                       +-------------+
```

### Jak to dziala krok po kroku

1. **Uzytkownik zmienia temperature pokoju** w suwaku HA.
2. Integracja wysyla `PUT /api/rooms/sypialnia` do HeatFlow.Api.
3. Api aktualizuje rekord w `RoomConfiguration` i dodaje wpis do `ConfigurationChangeLog`.
4. Co 2 minuty **koordynator** integracji odpytuje API (`GET /api/rooms`, `GET /api/heating-parameters`) i odswieza stany encji w HA.
5. Co minute (lub co 5 min w trybie ciaglym) **HeatFlow.Console** uruchamia sie, odczytuje aktualna konfiguracje z bazy i wykonuje algorytm.
6. Console ustawia zawory i piec w Home Assistant, a wyniki zapisuje do `ExecutionHistory` i powiazanych tabel.

---

## Weryfikacja E2E

1. Upewnij sie, ze HeatFlow.Api dziala (`curl /api/health`).
2. Dodaj integracje w HA (URL + klucz API).
3. Zmien temperature pokoju w interfejsie HA.
4. Sprawdz w API, czy zmiana zostala zapisana:
   ```bash
   curl -H "X-API-Key: TWOJ_KLUCZ" http://localhost:5000/api/rooms
   ```
5. Sprawdz audyt:
   ```bash
   curl -H "X-API-Key: TWOJ_KLUCZ" http://localhost:5000/api/configuration-changes?limit=5
   ```
6. Sprawdz atrybuty sensora `sensor.heatflow_configuration_changes` w HA – powinien zawierac ostatnia zmiane.

---

## Nastepny krok

W razie problemow z integracja lub API przejdz do dokumentu [08-rozwiazywanie-problemow.md](08-rozwiazywanie-problemow.md).
