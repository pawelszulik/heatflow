# 1. Przegląd systemu HeatFlow

## Czym jest HeatFlow

**HeatFlow** to inteligentny system sterowania ogrzewaniem centralnym (CO) zintegrowany z platformą automatyki domowej **Home Assistant**. System został napisany w języku **C# (.NET 10)** i działa jako zestienie dwóch aplikacji: konsolowej wykonującej algorytm grzania oraz REST API służącego do konfiguracji.

Zamiast ręcznego ustawiania temperatur na termostatach, HeatFlow automatycznie analizuje:

- aktualne temperatury w pokojach,
- prognozę pogody (z Home Assistant lub OpenWeatherMap),
- harmonogramy użytkowania i grzania,
- priorytety i wrażliwość poszczególnych pomieszczeń,

a następnie samodzielnie decyduje, które pokoje należy ogrzewać, jak ustawić zawory termostatyczne i jaką temperaturę zadać piecowi.

---

## Dla kogo jest ten system

System jest przeznaczony dla użytkowników domowych, którzy:

- posiadają instalację CO z piecykiem (w tym z podajnikiem paliwa, np. ekopiec) i zaworami termostatycznymi na grzejnikach lub matach grzewczych,
- korzystają z **Home Assistant** jako platformy automatyki domowej,
- chcą zautomatyzować sterowanie ogrzewaniem i zmniejszyć zużycie paliwa przy zachowaniu komfortu cieplnego.

---

## Główne komponenty systemu

| Komponent | Rodzaj | Rola |
|-----------|--------|------|
| **HeatFlow.Console** | Aplikacja konsolowa (.NET 10) | Wykonuje algorytm sterowania grzaniem (fazy 0–4). Uruchamiana przez Windows Task Scheduler co minutę lub w pętli ciągłej. |
| **HeatFlow.Api** | Aplikacja webowa ASP.NET Core | Udostępnia REST API do odczytu i zapisu konfiguracji pokojów oraz parametrów algorytmu. Działa jako usługa Windows lub samodzielnie. |
| **HeatFlow (custom component)** | Integracja Home Assistant | Łączy Home Assistant z API, tworząc encje (sensory, przełączniki, suwaki) do wygodnej konfiguracji z poziomu interfejsu HA. |
| **Baza danych SQL Server** | Microsoft SQL Server / Express | Przechowuje całą konfigurację systemu, historię wykonania, logi błędów i audyt zmian. |

---

## Jak to działa w skrócie

1. **Co minutę** (lub w trybie ciągłym co 5 minut) uruchamiana jest aplikacja **HeatFlow.Console**.
2. Console odczytuje z bazy danych konfigurację pokojów, parametry algorytmu oraz stany z Home Assistant (temperatury, prognoza pogody).
3. Wykonywany jest **5-fazowy algorytm**:
   - **Faza 0** – analiza prognozy pogody i dostosowanie parametrów,
   - **Faza 1** – diagnoza zapotrzebowania (obliczenie deficytów temperatur w pokojach),
   - **Faza 2** – arbitraż (wybór maksymalnie 5 pokoi do grzania),
   - **Faza 3** – sterowanie zaworami termostatycznymi,
   - **Faza 4** – przełączanie trybu letni/zimny w zależności od temperatury zewnętrznej.
4. Wyniki (historia, stany pokojów, pieca, zaworów) są zapisywane do bazy danych.
5. Użytkownik może modyfikować konfigurację przez interfejs Home Assistant (integracja custom component) lub bezpośrednio przez REST API.

Szczegółowy opis każdej fazy znajduje się w dokumencie [05-uzytkowanie.md](05-uzytkowanie.md).

---

## Wymagania systemowe

### Wymagane oprogramowanie

| Wymaganie | Wersja / uwagi |
|-----------|----------------|
| **.NET 10 SDK** | Wymagany do kompilacji i uruchomienia. |
| **Microsoft SQL Server** | SQL Server 2019 lub nowszy, lub darmowy SQL Server Express. Przechowuje konfigurację i historię. |
| **Home Assistant** | Działająca instancja z dostępem przez REST API oraz **Long-Lived Access Token**. |
| **Windows 10/11** | Wymagany do uruchomienia Task Scheduler i usług Windows (Console + Api). |
| **Dostęp do internetu** | Opcjonalny – tylko jeśli używasz OpenWeatherMap jako źródła prognozy. |

### Wymagany sprzęt / encje w Home Assistant

- Czujniki temperatury w każdym pokoju (`sensor.*_temperature` lub `climate.*`)
- Zawory termostatyczne (`climate.*` lub `number.*_valve`)
- Encja pogody (`weather.home` lub `weather.openweathermap`)
- Czujnik temperatury powrotu z kotła (`sensor.temp_return`)
- Czujnik pozycji zaworu mieszającego 4D (`sensor.mixer_4d_position`)
- Przełącznik trybu letni/zimny (`switch.kociol_tryb_zima_lato`)

---

## Słownik pojęć

| Pojęcie | Definicja |
|---------|-----------|
| **Pokój (Room)** | Pomieszczenie zdefiniowane w konfiguracji systemu, posiadające czujnik temperatury, zawór, temperatury docelowe oraz harmonogramy. |
| **Deficyt temperatury** | Różnica między temperaturą docelową a rzeczywistą w danym pokoju. Im większy deficyt, tym bardziej pokój potrzebuje ogrzania. |
| **Klasyfikacja deficytu** | Kategoryzacja potrzeb grzewczych pokoju na podstawie wyniku (score): **None** (brak danych), **Disabled** (wyłączony), **Stay** (utrzymanie), **Max** (maksymalne grzanie). |
| **Faza (Phase)** | Jedna z 5 jednostek algorytmu (0–4) wykonywanych w każdym cyklu. |
| **Tryb prognozy** | Stan wyznaczany przez Fazę 0 na podstawie prognozy pogody: **Normal** (bez zmian), **PreHeating** (przygotowanie do ochłodzenia), **Reduction** (redukcja przed ociepleniem). |
| **Score pokoju** | Wartość liczbowa obliczana na podstawie priorytetu, deficytu, wrażliwości pokoju i harmonogramów. Decyduje o kolejności wyboru pokojów do grzania. |
| **Pokój bezpieczeństwa (safety room)** | Jeśli żaden pokój nie kwalifikuje się do grzania, system wymusza grzanie w najzimniejszym pokoju, aby uniknąć zamarzania instalacji. |
| **Tryb letni/zimny** | Automatyczne przełączanie pieca w tryb letni (wyłączenie CO) przy temperaturze zewnętrznej powyżej 10°C oraz braku zapotrzebowania, i powrót do zimnego przy ochłodzeniu. |

---

## Dokumentacja w tej kolekcji

| Dokument | Opis |
|----------|------|
| [01-przeglad-systemu.md](01-przeglad-systemu.md) | Ten dokument. Ogólny opis systemu. |
| [02-architektura-i-komponenty.md](02-architektura-i-komponenty.md) | Architektura warstwowa, opis projektów, przepływ danych. |
| [03-instalacja-i-wdrozenie.md](03-instalacja-i-wdrozenie.md) | Instrukcja instalacji krok po kroku. |
| [04-konfiguracja.md](04-konfiguracja.md) | Konfiguracja pokojów, parametrów algorytmu i encji HA. |
| [05-uzytkowanie.md](05-uzytkowanie.md) | Opis algorytmu, faz, trybów pracy i logów. |
| [06-api-rest.md](06-api-rest.md) | Opis REST API (odniesienie do `HeatFlow.Api-Kontrakt.md`). |
| [07-integracja-home-assistant.md](07-integracja-home-assistant.md) | Instalacja i użytkowanie custom component w HA. |
| [08-rozwiazywanie-problemow.md](08-rozwiazywanie-problemow.md) | FAQ i debugowanie. |
| [09-rozwoj-i-testy.md](09-rozwoj-i-testy.md) | Jak rozwijać kod, uruchamiać testy i tworzyć migracje. |
