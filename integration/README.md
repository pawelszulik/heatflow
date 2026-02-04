# Integracja HeatFlow dla Home Assistant

Integracja łączy Home Assistant z **HeatFlow.Api** (REST API do konfiguracji pokoi i parametrów grzania). Wymaga działającego API (np. jako usługa Windows – zob. `src/HeatFlow.Api/README.md`).

## Wymagania

- Home Assistant 2023.1 lub nowszy
- Działające **HeatFlow.Api** (adres URL i klucz API)

## Instalacja

### HACS (rekomendowane)

1. Zainstaluj [HACS](https://hacs.xyz/).
2. W HACS: **Integracje** → **⋮** (menu) → **Custom repositories**.
3. Dodaj repozytorium: URL tego repozytorium (np. `https://github.com/TWOJ-USER/HeatFlow`).
4. Kategoria: **Integration**.
5. Wyszukaj **HeatFlow** i zainstaluj.
6. Zrestartuj Home Assistant.

### Ręczna instalacja

Skopiuj folder `custom_components/heatflow` z tego katalogu do `config/custom_components/` w Home Assistant i zrestartuj HA.

### Aktualizacja z terminala (SSH / Add-on Terminal)

Skrypty poniżej uruchamiasz w terminalu na Home Assistant (SSH lub Terminal add-on). Nadpisują dodatek w `config/custom_components/heatflow`; po wykonaniu nie trzeba nic więcej robić.

**Z lokalnej kopii repozytorium** (folder repo w `custom_components` ma nazwę `heatflow-repo`):

```bash
cd /config/custom_components
cp -r heatflow-repo/integration/custom_components/heatflow ./heatflow
rm -rf heatflow-repo
ls -la heatflow/
```

**Przez klonowanie z GitHub** (pobiera repozytorium, wkleja tylko komponent, usuwa klon):

```bash
cd /config/custom_components
rm -rf heatflow
git clone https://github.com/pawelszulik/heatflow heatflow-repo
cp -r heatflow-repo/integration/custom_components/heatflow .
rm -rf heatflow-repo
ls -la heatflow/
```

## Konfiguracja

HeatFlow dodajesz jako **integrację**, nie jako dodatek z Add-on Store.

1. **Ustawienia** → **Urządzenia i usługi** → **Dodaj integrację** (przycisk u dołu).
2. Wyszukaj **HeatFlow**.
3. Podaj:
   - **URL API** – np. `http://adres-serwera:5000` (gdzie działa HeatFlow.Api).
   - **Klucz API** – wartość ustawiona w konfiguracji API (`HeatFlow:ApiKey`).
4. Po pomyślnej weryfikacji połączenia integracja utworzy urządzenia i encje.

## Encje

- **Pokoje** – dla każdego pokoju z API: temperatury docelowe (number), priorytet (select), Sensitive / AutomationDisabled (switch).
- **HeatFlow Parametry** – wybrane parametry algorytmu (number), zapis przez PATCH.
- **Sensory** – status (liczba pokoi, stan parametrów), ostatnie zmiany konfiguracji (audit log w atrybutach).

Zmiany w encjach są zapisywane w API i rejestrowane w audit logu (widoczne w sensorze „Configuration changes” oraz przez `GET /api/configuration-changes`).

## Weryfikacja E2E

1. Uruchom HeatFlow.Api (np. `dotnet run` w `src/HeatFlow.Api` z uzupełnionym `appsettings.json`).
2. Dodaj integrację w HA (URL + klucz).
3. Zmień temperaturę pokoju lub parametr w HA → sprawdź, że w API (np. `GET /api/rooms`, `GET /api/heating-parameters`) i w audit logu (`GET /api/configuration-changes`) pojawia się zmiana.
4. Sprawdź atrybuty sensora „Configuration changes” w HA – lista ostatnich zmian.
