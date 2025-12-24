# Dokumentacja konfiguracji systemu sterowania grzaniem

## Wymagane encje w Home Assistant

### 1. Piec ekopiec

Zastąp `YOUR_DEVICE_SN` numerem seryjnym Twojego pieca ekopiec.

**Temperatura zadana kotła:**
- Encja: `number.ekopiec_{device_sn}_kot_tzad`
- Zakres: 10-85°C
- Ustawiana przez: Fazę 4

**Czas pracy podajnika (moc):**
- Encja: `number.ekopiec_{device_sn}_p_pod_on`
- Zakres: 1-300 s
- Ustawiana przez: Fazę 4

**Temperatura powrotu (jeśli dostępna):**
- Encja: `sensor.ekopiec_{device_sn}_temp_return` lub `sensor.temp_return`
- Odczyt: przez Fazę 4 i 5

**Przełączniki:**
- `switch.ekopiec_{device_sn}_zima_lato` - tryb zima/lato
- `switch.ekopiec_{device_sn}_tryb_auto_state` - tryb automatyczny

### 2. Zawór mieszający 4D

**UWAGA:** Zawór 4D jest sterowany automatycznie przez piec. System tylko monitoruje jego pozycję.

**Pozycja zaworu (odczyt - tylko do monitoringu):**
- Encja: `sensor.mixer_4d_position` lub `number.mixer_4d_position`
- Zakres: 0-100%
- Używana przez: Fazę 4 (monitoring) i Fazę 5 (bezpieczeństwo)

### 3. Zawory termostatyczne (potencjometry)

Dla każdego pokoju potrzebujesz encji do sterowania zaworem:

**Opcja 1: Encje typu climate**
- Format: `climate.{nazwa_pokoju}`
- Przykład: `climate.sypialnia`, `climate.lazienka`
- Sterowanie: `climate.set_temperature`

**Opcja 2: Encje typu number**
- Format: `number.{nazwa_pokoju}_valve`
- Przykład: `number.sypialnia_valve`, `number.lazienka_valve`
- Sterowanie: `number.set_value`

### 4. Czujniki temperatury pokoi

Dla każdego pokoju potrzebujesz odczytu temperatury:

**Opcja 1: Dedykowany sensor**
- Format: `sensor.{nazwa_pokoju}_temperature`
- Przykład: `sensor.sypialnia_temperature`

**Opcja 2: Atrybut encji climate**
- Format: `climate.{nazwa_pokoju}`
- Atrybut: `current_temperature`
- System automatycznie odczytuje z `state_attr('climate.{pokoj}', 'current_temperature')`

### 5. Prognoza pogody

Potrzebujesz jednej encji weather z prognozą:
- Przykład: `weather.home`, `weather.openweathermap`
- System automatycznie wykrywa dostępną encję weather.*
- Wymagane: atrybut `forecast` z prognozą na 24h

**Rekomendowane źródła pogody (darmowe plany):**
- **OpenWeatherMap** (REKOMENDOWANE): 1000 zapytań/dzień, łatwa integracja
- **AccuWeather**: 50 zapytań/dzień, wymaga rejestracji
- **Met.no**: Bezpłatne, bez API key, tylko dla Europy
- **Weather Underground**: Tylko dla właścicieli osobistych stacji pogodowych (PWS)

Szczegóły konfiguracji w pliku `configuration_weather.yaml`

## Konfiguracja pokoi

### Lista pokoi

System domyślnie używa następujących nazw pokoi:
- sypialnia
- lazienka
- pokoj_dzieci
- salon
- kuchnia
- pokoj_gościnny
- biuro
- pokoj_1
- pokoj_2
- pokoj_3
- pokoj_4
- pokoj_5
- pokoj_6
- schowek

**WAŻNE:** Lista pokoi jest przechowywana w jednym miejscu - helperze `input_text.heating_rooms_list` (w pliku `input_helpers/heating_switches.yaml`).

**Aby zmienić nazwy pokoi lub dodać/usunąć pokoje:**
1. Otwórz `input_helpers/heating_switches.yaml`
2. Znajdź `input_text.heating_rooms_list`
3. Zmodyfikuj wartość `initial` - lista pokoi oddzielonych przecinkami
4. Przykład: `"sypialnia,lazienka,pokoj_dzieci,salon,kuchnia,pokoj_gościnny,biuro,pokoj_1,pokoj_2,pokoj_3,pokoj_4,pokoj_5,pokoj_6,schowek"`
5. Po zmianie możesz też zaktualizować wartość przez UI: Settings > Helpers > Text > heating_rooms_list

**UWAGA:** Po zmianie listy pokoi musisz również:
- Utworzyć odpowiednie helpery dla nowych pokoi w `input_helpers/heating_rooms_config.yaml`
- Upewnić się, że istnieją odpowiednie encje (czujniki temperatury, zawory) dla nowych pokoi

### Priorytety pokoi

Ustaw priorytety w helperach `input_number.{pokoj}_priority`:
- **Priorytet 1**: Najważniejsze pokoje (sypialnia, łazienka, pokój dzieci)
- **Priorytet 2**: Ważne pokoje (salon, kuchnia)
- **Priorytet 3**: Mniej ważne pokoje
- **Priorytet 4**: Najmniej ważne pokoje (schowek, pomieszczenia gospodarcze)

### Temperatury docelowe

Ustaw dla każdego pokoju w `input_number.{pokoj}_temp_target`:
- Zakres: 15-26°C
- Przykładowe wartości:
  - Sypialnia: 21°C
  - Łazienka: 24°C
  - Pokój dzieci: 22°C
  - Salon: 21°C
  - Kuchnia: 20°C
  - Pozostałe: 18-20°C

### Harmonogramy użytkowania

Format harmonogramu w `input_select.{pokoj}_usage_schedule`:
```
"HH:MM-HH:MM,HH:MM-HH:MM|HH:MM-HH:MM"
```
- Przed `|` - dni robocze (poniedziałek-piątek)
- Po `|` - weekend (sobota-niedziela)
- Przykład: `"22:00-07:00|23:00-09:00"` - sypialnia
- Przykład: `"06:30-07:30,18:00-20:00|08:00-09:00,19:00-21:00"` - łazienka

## Konfiguracja parametrów algorytmu

### Progi deficytów

Ustaw w `input_helpers/heating_parameters.yaml`:
- `deficit_high_p1`: 0.8-1.0°C (Priorytet 1)
- `deficit_high_p2`: 1.8-2.0°C (Priorytet 2)
- `deficit_high_p3`: 2.8-3.0°C (Priorytet 3)

### Parametry bezpieczeństwa

- `min_return_temp`: 50°C (minimalna temperatura powrotu)
- `min_temp_diff`: 15°C (minimalna różnica temp zadana-powrót)
- `min_valves_open`: 1-2 (minimalna liczba otwartych zaworów)
- `min_mixer_4d`: 20% (minimalne otwarcie zaworu 4D)

### Histereza

- `hysteresis`: 0.5°C (tolerancja przegrzania)

### Bufor przygotowania

- `buffer_preparation`: 0.5-1.0°C (bonus deficytu gdy pokój będzie używany wkrótce)
- `buffer_heating_time`: 40-60 min (wyprzedzenie przed użyciem pokoju)

## Instalacja

1. **Skopiuj pliki do katalogu konfiguracyjnego Home Assistant:**
   ```
   config/
   ├── automations/
   │   ├── heating_phase0_forecast.yaml
   │   ├── heating_main_loop.yaml
   │   └── heating_safety.yaml
   ├── scripts/
   │   ├── heating_phase1_diagnose.yaml
   │   ├── heating_phase2_arbitrate.yaml
   │   ├── heating_phase3_valves.yaml
   │   ├── heating_phase4_boiler_mixer.yaml
   │   └── heating_phase5_hysteresis.yaml
   ├── input_helpers/
   │   ├── heating_rooms_config.yaml
   │   ├── heating_parameters.yaml
   │   └── heating_switches.yaml
   └── sensors/
       └── heating_calculated.yaml
   ```

2. **Dodaj do `configuration.yaml`:**
   ```yaml
   automation: !include_dir_merge_list automations/
   script: !include_dir_merge_list scripts/
   input_number: !include_dir_merge_list input_helpers/
   input_boolean: !include_dir_merge_list input_helpers/
   input_select: !include_dir_merge_list input_helpers/
   sensor: !include_dir_merge_list sensors/
   ```

3. **Skonfiguruj encje:**
   - Zaktualizuj `YOUR_DEVICE_SN` w `scripts/heating_phase4_boiler_mixer.yaml`
   - Zaktualizuj nazwy encji zaworu 4D i temperatury powrotu
   - Zaktualizuj nazwy pokoi jeśli różnią się od domyślnych

4. **Uruchom ponownie Home Assistant**

5. **Skonfiguruj parametry:**
   - Ustaw priorytety pokoi
   - Ustaw temperatury docelowe
   - Skonfiguruj harmonogramy użytkowania
   - Dostosuj parametry algorytmu do swojego systemu

6. **Włącz system:**
   - Włącz `input_boolean.heating_system_enabled`

## Testowanie

1. **Tryb debugowania:**
   - Włącz `input_boolean.heating_debug_mode` dla dodatkowych powiadomień

2. **Wymuszenie trybów:**
   - `input_boolean.heating_force_pre_heating` - wymuś tryb PRE-HEATING
   - `input_boolean.heating_force_reduction` - wymuś tryb REDUCTION

3. **Monitorowanie:**
   - Sprawdź sensory obliczeniowe w `sensors/heating_calculated.yaml`
   - Sprawdź logi automatyzacji w Developer Tools > Logs

## Rozwiązywanie problemów

### System nie działa
- Sprawdź czy `input_boolean.heating_system_enabled` jest włączony
- Sprawdź logi Home Assistant pod kątem błędów YAML
- Sprawdź czy wszystkie encje istnieją i są dostępne

### Zawory nie reagują
- Sprawdź czy nazwy encji zaworów są poprawne
- Sprawdź czy encje są typu `climate.*` czy `number.*`
- Sprawdź uprawnienia do sterowania zaworami

### Temperatura powrotu nie jest odczytywana
- Sprawdź nazwę encji w `scripts/heating_phase4_boiler_mixer.yaml`
- Dodaj alternatywną nazwę encji jeśli potrzeba

### Prognoza pogody nie działa
- Sprawdź czy masz skonfigurowaną encję `weather.*`
- Sprawdź czy encja ma atrybut `forecast`
- Sprawdź logi skryptu `heating_phase0_forecast`

## Kalibracja

Po wdrożeniu systemu zalecana jest kalibracja parametrów:

1. **Obserwuj działanie przez 1-2 tygodnie**
2. **Dostosuj progi deficytów** w zależności od rzeczywistego zachowania
3. **Dostosuj harmonogramy** do rzeczywistych wzorców użytkowania
4. **Dostosuj parametry bezpieczeństwa** w zależności od charakterystyki pieca

## Wsparcie

W razie problemów sprawdź:
- Logi Home Assistant
- Dokumentację algorytmu w `Algorytm.md`
- Konfigurację encji w tym dokumencie

