"""hms.csv (t_utc,entity,value) -> migawki.csv: wiersz na migawkę (minuta UTC), kolumny potrzebne Fazie 5."""
import csv, sys, collections
src, dst = sys.argv[1], sys.argv[2]
COLS = {
 "sensor.temperatura_ogrod_temperature": "tz_ogrod", "sensor.t_h_sensor_2_temperatura": "tz_thsensor",
 "sensor.kuchnia_czujnik_bm280_kuchnia_bmx280_temperature": "in_kuchnia",
 "sensor.salon_czujnik_bm280_salon_bmx280_temperature": "in_salon",
 "sensor.spizarnia_czujnik_bm280_spizarnia_bmx280_temperature": "in_spizarnia",
 "sensor.suszarnia_czujnik_bm280_suszarnia_bmx280_temperature": "in_suszarnia",
 "sensor.edyta_czujnik_bm280_edyta_bmx280_temperature": "in_edyta",
 "sensor.hol_czujnik_bm280_hol_bmx280_temperature": "in_hol",
 "sensor.przejsciowy_czujnik_bm280_przejsciowy_bmx280_temperature": "in_przejsciowy",
 "sensor.kociol_temperatura_kotla": "kociol", "number.kociol_temperatura_zadana_kotla": "nastawa",
 "sensor.kociol_temperatura_spalin": "spaliny", "sensor.kociol_aktualna_moc_dmuchawy": "dmuchawa_akt",
 "sensor.kociol_czas_pracy_podajnika_2": "licznik", "switch.kociol_tryb_zima_lato": "lato",
 "switch.kociol_tryb_pracy": "tryb_auto", "number.kociol_czas_pracy_podajnika": "praca",
 "number.kociol_czas_postoju_podajnika": "postoj", "number.kociol_moc_dmuchawy": "dmuchawa",
}
by = collections.defaultdict(dict)
for r in csv.DictReader(open(src)):
    c = COLS.get(r["entity"])
    if c: by[r["t_utc"]][c] = r["value"]
names = ["t_utc"] + list(COLS.values())
w = csv.writer(open(dst, "w", newline=""))
w.writerow(names)
for t in sorted(by):
    w.writerow([t] + [by[t].get(n, "") for n in names[1:]])
print(len(by), "migawek")
