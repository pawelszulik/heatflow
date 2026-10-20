# Faza 5 „Dawka” — wirtualne testy

Nic tu nie łączy się z Home Assistantem, eSterownikMapperem ani bazą: prawdziwy `Phase5DoseService`
i `DoseControllerWriter` pracują na atrapach z `tools/HeatFlow.DoseSim` (HA, sterownik + mapper,
repozytorium, konfiguracja, Discord — wszystko w pamięci).

## Dwa poziomy

1. **Replay historii** (`ReplayRunner`) — migawki HMS co ~5 min (I–X 2026) karmią Fazę 5 tak, jakby
   to był HA; prognoza = faktyczna Tz z kolejnych godzin (idealna). Pętla otwarta: kocioł i dom
   w danych reagowały na nastawy Pawła, nie automatu. Warianty: automat włączony, „kocioł neutralny”
   (bez sygnałów od kotła — do porównania z `a7_sucho2.py` z analizy) i tryb cienia (przełącznik OFF).
2. **Pętla zamknięta** (`ClosedLoopRunner` + `VirtualPlant`) — wirtualny kocioł i dom w sekundach
   podajnika (C = 1549 s/K, strata f·(255·(Tin − Tz) − 934)), prawdziwa Tz, usterki wstrzykiwane
   (spaliny 210 °C, zgubiony zapis, brak danych, Tz −15 K, tryb lato, tryb ręczny, wyłączenie
   przełącznika). Te same przebiegi są testami xUnit: `tests/HeatFlow.Core.Tests/DoseClosedLoopTests.cs`
   (Tz z `tests/HeatFlow.Core.Tests/Data/tz-*.csv`).

## Uruchomienie

```bash
# 1. eksport HMS (t_utc,entity,value) → migawki.csv (wiersz na migawkę); dane NIE idą do repo
python3 -I tools/HeatFlow.DoseReplay/szeroki.py <hms.csv> <migawki.csv>
# 2. raport (markdown na stdout i do pliku); trzeci argument = katalog na wycinki Tz do testów
dotnet run -c Release --project tools/HeatFlow.DoseReplay -- <migawki.csv> raport.md [katalog_tz]
```

`hms.csv` powstaje skryptem `eksport.py` z analizy (`analiza-kociol-nastawy/`, HMS przez API Grafany).
