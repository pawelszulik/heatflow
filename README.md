# System Inteligentnego Sterowania Grzaniem dla Home Assistant

Kompleksowy system automatyzacji sterowania ogrzewaniem oparty na 6-fazowym algorytmie optymalizacji, przeznaczony dla systemu z 14 zaworami termostatycznymi, zaworem mieszającym 4D i piecem ekopiec.

## Funkcje

- **Faza 0**: Predykcja pogody (co 60 min) - przygotowanie systemu na zmiany pogodowe
- **Faza 1**: Diagnoza zapotrzebowania (co 5 min) - obliczanie deficytów temperatur
- **Faza 2**: Arbitraż i priorytetyzacja (co 5 min) - wybór maksymalnie 5 pokoi do grzania
- **Faza 3**: Sterowanie zaworami (co 5 min) - ustawianie temperatur na potencjometrach
- **Faza 4**: Sterowanie piecem i zaworem 4D (co 5 min) - regulacja zaworu mieszającego i mocy pieca
- **Faza 5**: Histereza i bezpieczeństwo (co 5 min) - monitoring warunków krytycznych

## Struktura plików

```
├── automations/
│   ├── heating_phase0_forecast.yaml      # Faza 0 - predykcja pogody
│   ├── heating_main_loop.yaml            # Główna pętla faz 1-5
│   └── heating_safety.yaml               # Monitoring bezpieczeństwa
├── scripts/
│   ├── heating_phase1_diagnose.yaml     # Faza 1 - diagnoza
│   ├── heating_phase2_arbitrate.yaml    # Faza 2 - arbitraż
│   ├── heating_phase3_valves.yaml       # Faza 3 - zawory
│   ├── heating_phase4_boiler_mixer.yaml # Faza 4 - piec i zawór 4D
│   └── heating_phase5_hysteresis.yaml   # Faza 5 - histereza
├── input_helpers/
│   ├── heating_rooms_config.yaml        # Konfiguracja 14 pokoi
│   ├── heating_parameters.yaml          # Parametry algorytmu
│   └── heating_switches.yaml            # Przełączniki systemu
├── sensors/
│   └── heating_calculated.yaml          # Sensory obliczeniowe
├── CONFIGURATION.md                      # Szczegółowa dokumentacja konfiguracji
├── configuration_weather.yaml           # Przykład konfiguracji pogody
└── README.md                            # Ten plik
```

## Szybki start

1. **Skopiuj pliki** do katalogu konfiguracyjnego Home Assistant
2. **Dodaj do `configuration.yaml`**:
   ```yaml
   automation: !include_dir_merge_list automations/
   script: !include_dir_merge_list scripts/
   input_number: !include_dir_merge_list input_helpers/
   input_boolean: !include_dir_merge_list input_helpers/
   input_select: !include_dir_merge_list input_helpers/
   sensor: !include_dir_merge_list sensors/
   ```
3. **Skonfiguruj encje** zgodnie z `CONFIGURATION.md`
4. **Uruchom ponownie** Home Assistant
5. **Włącz system**: `input_boolean.heating_system_enabled`

## Wymagania

- Home Assistant (najnowsza wersja)
- 14 zaworów termostatycznych (potencjometry)
- Zawór mieszający 4D (sterowany automatycznie przez piec)
- Piec ekopiec z integracją Home Assistant
- Czujniki temperatury w każdym pokoju
- Integracja z prognozą pogody:
  - **OpenWeatherMap** (rekomendowane, darmowy plan: 1000 zapytań/dzień)
  - AccuWeather (darmowy plan: 50 zapytań/dzień)
  - Met.no (bezpłatne, bez API key, tylko Europa)
  - Weather Underground (tylko dla właścicieli stacji PWS)

## Dokumentacja

Szczegółowa dokumentacja konfiguracji znajduje się w pliku `CONFIGURATION.md`.

## Algorytm

Pełny opis algorytmu znajduje się w pliku `Algorytm.md`.

## Autor

System opracowany na podstawie szczegółowej analizy wymagań i algorytmu optymalizacji sterowania grzaniem.

