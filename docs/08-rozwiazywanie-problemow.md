# 8. Rozwiazywanie problemow (FAQ)

Ten dokument zawiera najczestsze problemy i sposoby ich rozwiazania. Przed zgloszeniem bledu upewnij sie, ze sprawdziles logi i stan bazy danych.

---

## 1. HeatFlow.Console nie startuje lub natychmiast konczy prace

### Objawy
- Zadanie w Task Scheduler ma status `Running` i od razu `Ready`, ale nic sie nie dzieje.
- Brak nowych wpisow w logach.

### Diagnostyka
1. Sprawdz logi w folderze `logs/` aplikacji Console.
2. Uruchom recznie z terminala, aby zobaczyc komunikat bledu:
   ```powershell
   C:\HeatFlow\Console\HeatFlow.Console.exe
   ```
3. Sprawdz `appsettings.json` – czy `ConnectionStrings:DefaultConnection` jest poprawny.

### Rozwiazania
- **Brak `appsettings.json`** – skopiuj szablon i uzupelnij dane.
- **Blad polaczenia z SQL Server** – sprawdz, czy serwer dziala, czy uzytkownik ma uprawnienia, czy w connection string jest `TrustServerCertificate=True`.
- **Brak migracji** – wykonaj `dotnet ef database update` (patrz [MIGRATIONS.md](../MIGRATIONS.md)).
- **`SystemEnabled = false`** – Console celowo pomija wykonanie. Sprawdz `SystemConfiguration` w bazie.

---

## 2. Api zwraca 401 Unauthorized

### Objawy
- Zadania do `/api/rooms` lub `/api/health` zwracaja `401`.
- Integracja HA nie moze sie polaczyc.

### Diagnostyka
1. Sprawdz, czy w zadaniu HTTP jest naglowek `X-API-Key`.
2. Sprawdz, czy wartosc klucza zgadza sie z `HeatFlow:ApiKey` w `appsettings.json` Api.

### Rozwiazania
- Dodaj naglowek `X-API-Key: TWOJ_KLUCZ` do kazdego zadania.
- Jesli zmieniales klucz, zrestartuj usluge Api.
- Upewnij sie, ze nie ma ukrytych spacji na koncu klucza.

---

## 3. Nie widze zmian w Home Assistant po edycji w integracji

### Objawy
- Zmieniles temperature w suwaku HA, ale Console nadal uzywa starej wartosci.

### Diagnostyka
1. Sprawdz w API, czy zmiana zostala zapisana:
   ```bash
   curl -H "X-API-Key: TWOJ_KLUCZ" http://localhost:5000/api/rooms/Sypialnia
   ```
2. Sprawdz audyt:
   ```bash
   curl -H "X-API-Key: TWOJ_KLUCZ" http://localhost:5000/api/configuration-changes?limit=5
   ```
3. Sprawdz logi Console – czy odczytuje nowa konfiguracje z bazy.

### Rozwiazania
- Jesli zmiana nie jest w API – problem lezy po stronie integracji HA (sprawdz logi HA).
- Jesli zmiana jest w API, ale Console jej nie widzi – sprawdz, czy Console ma poprawny connection string (ta sama baza co Api).
- Pamietaj, ze Console odczytuje konfiguracje przy kazdym uruchomieniu, wiec zmiana zostanie uwzgledniona w nastepnym cyklu (maksymalnie za 1 minute w trybie Scheduled Task).

---

## 4. Bledy migracji EF Core

### Objawy
- `dotnet ef database update` zwraca blad.
- Aplikacja zglasza wyjatek przy starcie (`__EFMigrationsHistory` nie istnieje lub jest nieaktualna).

### Diagnostyka
1. Sprawdz, czy `dotnet ef` jest zainstalowane:
   ```powershell
   dotnet tool list --global
   ```
2. Sprawdz, czy w `src/HeatFlow.Console/appsettings.json` jest ustawiony `ConnectionStrings:DefaultConnection`.
3. Sprawdz, czy SQL Server jest dostepny i czy uzytkownik ma uprawnienia `db_owner` lub `db_ddladmin`.

### Rozwiazania
| Blad | Rozwiazanie |
|------|-------------|
| `No design-time services found` | Upewnij sie, ze wykonujesz polecenie z katalogu `src/HeatFlow.Infrastructure` i ze istnieje `DesignTimeDbContextFactory.cs`. |
| `Login failed for user` | Sprawdz login/haslo w connection stringu. Upewnij sie, ze SQL Server Authentication jest wlaczona. |
| `Cannot open database` | Baza moze nie istniec. Sprawdz, czy uzytkownik ma prawo do jej utworzenia, lub utworz ja recznie w SSMS. |
| `TrustServerCertificate=True` | Dla SQL Server bez pelnego certyfikatu SSL ta opcja jest wymagana w connection stringu. |

Szczegolowa instrukcja migracji znajduje sie w [MIGRATIONS.md](../MIGRATIONS.md).

---

## 5. Zawory sie nie ustawiaja (Faza 3)

### Objawy
- W logach Console wida `Faza 3: sukces 0/3` lub bledy retry.
- Temperatury na zaworach w HA nie zmieniaja sie.

### Diagnostyka
1. Sprawdz logi Console – czy sa bledy HTTP przy komunikacji z HA.
2. Sprawdz, czy encje zaworow (`ValveEntityId`) w `RoomConfiguration` sa poprawne.
3. Sprawdz, czy token HA (`HomeAssistant:AccessToken`) jest wazny i ma uprawnienia do wywolywania uslug.
4. Sprawdz w HA, czy encje zaworow sa dostepne i mozna nimi sterowac recznie.

### Rozwiazania
- **Bledny `ValveEntityId`** – popraw nazwe encji w `RoomConfiguration`.
- **Token wygasl** – wygeneruj nowy Long-Lived Access Token w HA.
- **Firewall blokuje polaczenie** – upewnij sie, ze Console ma dostep do adresu HA.
- **Encja `number.*` zamiast `climate.*`** – Console obsluguje oba typy, ale musi byc poprawnie zmapowana w konfiguracji.

---

## 6. Prognoza pogody nie dziala (Faza 0)

### Objawy
- Faza 0 zawsze zwraca `Normal`, niezaleznie od pogody.
- W logach brak informacji o wywolaniu OpenWeatherMap.

### Diagnostyka
1. Sprawdz, czy w `appsettings.json` Console jest ustawiony `OpenWeatherMap:ApiKey`.
2. Sprawdz, czy `SystemConfiguration.Latitude` i `Longitude` sa wypelnione.
3. Sprawdz cache w tabeli `ForecastDataCache` – jesli rekord jest swiezszy niz 1 godzina, API nie jest wywolywane.

### Rozwiazania
- Uzupelnij klucz API OpenWeatherMap.
- Ustaw wspolrzedne geograficzne w `SystemConfiguration` (przez API lub bezposrednio w bazie).
- Usun rekord z `ForecastDataCache`, aby wymusic odswiezenie.

---

## 7. Piec nie przechodzi w tryb letni (Faza 4)

### Objawy
- Mimo cieplych dni piec nadal pracuje w trybie zimnym.

### Diagnostyka
1. Sprawdz w logach Console komunikaty z Fazy 4.
2. Sprawdz, czy encja `switch.kociol_tryb_zima_lato` istnieje w HA.
3. Sprawdz temperature zewnetrzna – czy `BoilerState.TempExternal` jest poprawnie odczytywana z HA.
4. Sprawdz `SummerModeLog` – czy dziennie nie bylo juz aktywacji.

### Rozwiazania
- **Temperatura zewnetrzna <= 10C** – tryb letni sie nie wlaczy.
- **Godzina poza 06:00-13:59** – aktywacja dziala tylko rano.
- **Jakis pokoj ma Max deficyt** – dopoki pokoje wymagaja grzania, piec pozostaje w trybie zimnym.
- **Brak encji w HA** – utworz przełącznik `switch.kociol_tryb_zima_lato`.

---

## 8. Api dziala, ale integracja HA go nie widzi

### Objawy
- Podczas dodawania integracji w HA pojawia sie blad polaczenia.
- `curl /api/health` z terminala dziala, ale HA zglasza timeout.

### Diagnostyka
1. Sprawdz, czy adres URL w konfiguracji integracji jest poprawny (z protokolem i portem).
2. Sprawdz, czy Api nasluchuje na interfejsie sieciowym, a nie tylko `localhost`.
3. Sprawdz firewall na serwerze z Api.

### Rozwiazania
- Uzyj adresu IP zamiast `localhost`, np. `http://192.168.1.50:5000`.
- Sprawdz konfiguracje Kestrel w `appsettings.json` – domyslnie nasluchuje na wszystkich interfejsach.
- Sprawdz CORS – jesli frontend HA jest na innym adresie, dodaj go do `Cors:AllowedOrigins`.

---

## 9. Logi sa puste lub nie sa zapisywane

### Objawy
- Brak plikow w folderze `logs/`.

### Diagnostyka
1. Sprawdz, czy folder `logs/` istnieje w katalogu roboczym aplikacji.
2. Sprawdz uprawnienia do zapisu (zwlaszcza gdy aplikacja dziala jako usluga Windows lub Scheduled Task).
3. Sprawdz konfiguracje logowania w `appsettings.json`.

### Rozwiazania
- Utworz folder `logs/` recznie.
- W przypadku uslug Windows – upewnij sie, ze konto uslugi ma uprawnienia do zapisu w katalogu aplikacji.
- Logi konsoli (stdout) sa zawsze widoczne przy recznym uruchomieniu.

---

## 10. Jak debugowac recznie

Gdy zaden z powyzszych scenariuszy nie pasuje:

1. **Uruchom Console w trybie ciaglym** i obserwuj logi na zywo:
   ```powershell
   cd C:\HeatFlow\Console
   .\HeatFlow.Console.exe continuous
   ```
2. **Sprawdz stan bazy** – zapytania SQL do kluczowych tabel:
   ```sql
   SELECT TOP 10 * FROM ExecutionHistory ORDER BY ExecutionTime DESC;
   SELECT * FROM SystemConfiguration WHERE Id = 1;
   SELECT Name, TempTarget, Priority, SensorTemperatureEntityId, ValveEntityId FROM RoomConfiguration;
   SELECT TOP 10 * FROM ApplicationErrorLog ORDER BY OccurredAtUtc DESC;
   ```
3. **Testuj API recznie** przez curl lub Postmana:
   ```bash
   curl -H "X-API-Key: TWOJ_KLUCZ" http://localhost:5000/api/health
   curl -H "X-API-Key: TWOJ_KLUCZ" http://localhost:5000/api/rooms
   ```
4. **Sprawdz Home Assistant** – w Narzedziach deweloperskich (Developer Tools > States) sprawdz, czy encje sa dostepne i czy zmieniaja stan.

---

## Nastepny krok

Jesli problem nadal wystepuje, przejrzyj logi w `ApplicationErrorLog` i `ExecutionHistory`, a nastepnie zapoznaj sie z dokumentem [09-rozwoj-i-testy.md](09-rozwoj-i-testy.md), aby uruchomic testy jednostkowe i zweryfikowac logike biznesowa.
