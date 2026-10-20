"""Encje number: temperatury pokoi + parametry grzania (HeatingParameters)."""

from __future__ import annotations

import aiohttp
from homeassistant.components.number import NumberEntity
from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant
from homeassistant.helpers.entity_platform import AddEntitiesCallback
from homeassistant.helpers.update_coordinator import CoordinatorEntity

from .const import DATA_COORDINATOR, DOMAIN

ROOM_FIELD_NAMES = {
    "tempTarget": "Temperatura docelowa",
    "tempTargetActive": "Temperatura docelowa (aktywny)",
    "tempTargetInactive": "Temperatura docelowa (nieaktywny)",
}


def _pole(dane: dict, klucz: str):
    """Wartość pola z API, tolerująca camelCase i PascalCase.

    Nie wolno tu użyć `dane.get(a) or dane.get(A)` - zero jest falsy, więc parametr
    o wartości 0 (np. scoreThresholdDisabled) wyglądałby na brakujący i encja
    pokazywała `unknown`.
    """
    wartosc = dane.get(klucz)
    if wartosc is not None:
        return wartosc
    return dane.get(klucz[0].upper() + klucz[1:])


PARAM_FIELDS = [
    ("deficitHighP1", 0, 10, 0.1),
    ("deficitHighP2", 0, 10, 0.1),
    ("deficitHighP3", 0, 10, 0.1),
    ("bufferPreparation", 0, 2, 0.1),
    ("bufferHeatingTime", 1, 240, 1),
    ("forecastTempDropThreshold", 0, 20, 0.5),
    ("forecastHoursCount", 1, 24, 1),
    ("maxValvesOpen", 1, 20, 1),
    ("minValvesOpen", 0, 10, 1),
    ("boilerNominalTemp", 40, 90, 1),
    ("minReturnTemp", 20, 60, 1),
    ("hysteresis", 0, 5, 0.1),
    ("hysteresisSafetyThreshold", 0, 10, 0.1),
    ("scoreThresholdMax", -200, 500, 1),
    ("scoreThresholdDisabled", -200, 500, 1),
    ("minDwellMinutes", 0, 120, 1),
    ("summerModeWarmDayTemp", 0, 40, 0.5),
    # Faza 5 „Dawka” - zakresy = twarde granice Pawła (DoseLimits w API, które i tak odrzuci resztę).
    ("feederWorkMin", 7, 10, 1),
    ("feederWorkMax", 7, 10, 1),
    ("feederPauseMin", 100, 320, 5),
    ("feederPauseMax", 100, 320, 5),
    ("blowerMin", 28, 35, 1),
    ("blowerMax", 28, 35, 1),
    ("feedPerKelvin", 100, 400, 1),
    ("feedOffset", -5000, 5000, 1),
    ("houseCapacity", 500, 3000, 1),
    ("indoorTargetForFeed", 18, 26, 0.5),
    ("feedCorrectionK", 0.6, 1.3, 0.01),
    ("feedSmoothingPastHours", 1, 24, 1),
    ("feedSmoothingForecastHours", 0, 24, 1),
    ("feedMinIntervalHours", 2, 24, 1),
    ("feedDeadbandSec", 5, 60, 1),
    ("feedMaxStepSec", 5, 60, 1),
    ("feedWorkUpHysteresisSec", 100, 320, 5),
    ("doseBoilerStaleMinutes", 10, 120, 1),
    ("doseBoilerColdMinutes", 0, 360, 5),
    ("doseOutdoorStaleMinutes", 30, 360, 5),
    ("doseIndoorMinSensors", 1, 20, 1),
    ("doseFlueNoRaiseTemp", 100, 250, 1),
    ("doseFlueMaxTemp", 150, 250, 1),
    ("doseFlueBlowerCutPct", 1, 5, 1),
    ("doseFlueSafetyHoldHours", 0, 48, 1),
    ("doseTrimUnderpowerK", 1, 15, 0.5),
    ("doseTrimUnderpowerHours", 1, 12, 1),
    ("doseTrimStandbyEntries", 1, 10, 1),
    ("doseTrimStandbyHours", 1, 24, 1),
    ("doseTrimStepSec", 5, 60, 5),
    ("doseTrimLimitSec", 0, 200, 5),
    ("doseBoilerColdK", 3, 30, 0.5),
    ("doseBoilerColdWarnMinutes", 15, 360, 5),
    ("feedKGain", 0.05, 1.0, 0.05),
    ("feedKMaxStep", 0.01, 0.2, 0.01),
    ("feedKMin", 0.3, 1.0, 0.05),
    ("feedKMax", 1.0, 2.0, 0.05),
    ("doseCeilingDayHeatingHours", 12, 24, 0.5),
    ("doseBlowerLowDuty", 0.01, 0.09, 0.001),
]

PARAM_DISPLAY_NAMES = {
    "deficitHighP1": "Deficit wysoki P1",
    "deficitHighP2": "Deficit wysoki P2",
    "deficitHighP3": "Deficit wysoki P3",
    "bufferPreparation": "Bufor przygotowania",
    "bufferHeatingTime": "Czas grzania bufora (min)",
    "forecastTempDropThreshold": "Próg spadku temp. prognozy",
    "forecastHoursCount": "Liczba godzin prognozy",
    "maxValvesOpen": "Maks. liczba otwartych zaworów",
    "minValvesOpen": "Min. liczba otwartych zaworów",
    "boilerNominalTemp": "Temperatura nominalna pieca",
    "minReturnTemp": "Min. temp. powrotu",
    "hysteresis": "Histereza",
    "hysteresisSafetyThreshold": "Próg histerezy (bezpieczeństwo)",
    "scoreThresholdMax": "Próg Score - pełne grzanie",
    "scoreThresholdDisabled": "Próg Score - zawór zamknięty",
    "minDwellMinutes": "Min. czas trzymania zaworu (min)",
    "summerModeWarmDayTemp": "Tryb lato - próg ciepłego dnia (°C)",
    "feederWorkMin": "Dawka - praca podajnika min (s)",
    "feederWorkMax": "Dawka - praca podajnika max (s)",
    "feederPauseMin": "Dawka - postój podajnika min (s)",
    "feederPauseMax": "Dawka - postój podajnika max (s)",
    "blowerMin": "Dawka - dmuchawa min (%)",
    "blowerMax": "Dawka - dmuchawa max (%)",
    "feedPerKelvin": "Dawka - podajnik na 1 K (s na dobę)",
    "feedOffset": "Dawka - wyraz wolny (s na dobę)",
    "houseCapacity": "Dawka - pojemność domu (s na 1 K)",
    "indoorTargetForFeed": "Dawka - temperatura w domu (°C)",
    "feedCorrectionK": "Dawka - korekta k",
    "feedSmoothingPastHours": "Dawka - Tz godzin wstecz",
    "feedSmoothingForecastHours": "Dawka - Tz godzin prognozy",
    "feedMinIntervalHours": "Dawka - min. odstęp zmian (h)",
    "feedDeadbandSec": "Dawka - martwa strefa (s)",
    "feedMaxStepSec": "Dawka - max krok postoju (s)",
    "feedWorkUpHysteresisSec": "Dawka - histereza czasu pracy (s)",
    "doseBoilerStaleMinutes": "Dawka - brak danych kotła (min)",
    "doseBoilerColdMinutes": "Dawka - kocioł zimny: wyłącz po (min, 0 = tylko ostrzeżenie)",
    "doseOutdoorStaleMinutes": "Dawka - brak temp. zewn. (min)",
    "doseIndoorMinSensors": "Dawka - min. czujników w domu",
    "doseFlueNoRaiseTemp": "Dawka - spaliny: nie podnoś dmuchawy od (°C)",
    "doseFlueMaxTemp": "Dawka - spaliny: dmuchawa w dół i wyłączenie od (°C)",
    "doseFlueBlowerCutPct": "Dawka - spaliny: zbicie dmuchawy (pkt)",
    "doseFlueSafetyHoldHours": "Dawka - po bezpieczniku #24 bez podnoszenia (h)",
    "doseTrimUnderpowerK": "Dawka - trym: kocioł pod nastawą o (K)",
    "doseTrimUnderpowerHours": "Dawka - trym: pod nastawą przez (h)",
    "doseTrimStandbyEntries": "Dawka - trym: liczba podtrzymań",
    "doseTrimStandbyHours": "Dawka - trym: podtrzymania w oknie (h)",
    "doseTrimStepSec": "Dawka - trym: krok (s)",
    "doseTrimLimitSec": "Dawka - trym: granica (s)",
    "doseBoilerColdK": "Dawka - kocioł zimny: pod nastawą o (K)",
    "doseBoilerColdWarnMinutes": "Dawka - kocioł zimny: ostrzeżenie po (min)",
    "feedKGain": "Dawka - korekta k: wzmocnienie",
    "feedKMaxStep": "Dawka - korekta k: krok na dobę",
    "feedKMin": "Dawka - korekta k: minimum",
    "feedKMax": "Dawka - korekta k: maksimum",
    "doseCeilingDayHeatingHours": "Dawka - doba na suficie od (h grzania)",
    "doseBlowerLowDuty": "Dawka - dmuchawa minimum przy udziale pracy",
}


async def async_setup_entry(
    hass: HomeAssistant,
    entry: ConfigEntry,
    async_add_entities: AddEntitiesCallback,
) -> None:
    data = hass.data[DOMAIN].get(entry.entry_id)
    if not data or not data.get(DATA_COORDINATOR):
        return
    coordinator = data[DATA_COORDINATOR]
    entities = []
    rooms = (coordinator.data or {}).get("rooms") or []
    for room in rooms:
        name = room.get("name") or room.get("Name")
        if not name:
            continue
        for key in ["tempTarget", "tempTargetActive", "tempTargetInactive"]:
            val = _pole(room, key)
            if val is not None:
                entities.append(RoomNumberEntity(coordinator, entry, name, key, float(val)))
    params = (coordinator.data or {}).get("heating_parameters") or {}
    for key, min_v, max_v, step in PARAM_FIELDS:
        if key in params or (key[0].upper() + key[1:] in params):
            entities.append(HeatingParameterNumber(coordinator, entry, key, min_v, max_v, step))
    async_add_entities(entities)


class RoomNumberEntity(CoordinatorEntity, NumberEntity):
    _attr_has_entity_name = True
    _attr_native_min_value = 5.0
    _attr_native_max_value = 30.0
    _attr_native_step = 0.5
    _attr_native_unit_of_measurement = "°C"

    def __init__(self, coordinator, entry: ConfigEntry, room_name: str, field: str, initial: float) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._room_name = room_name
        self._field = field
        self._attr_unique_id = f"{entry.entry_id}_{room_name}_{field}"
        self._attr_native_value = initial
        self._attr_name = ROOM_FIELD_NAMES.get(field, field)
        self._attr_device_info = {"identifiers": {(DOMAIN, f"{entry.entry_id}_{room_name}")}, "name": f"HeatFlow {room_name}"}

    @property
    def native_value(self) -> float | None:
        data = self.coordinator.data or {}
        for r in (data.get("rooms") or []):
            rn = r.get("name") or r.get("Name")
            if rn == self._room_name:
                v = _pole(r, self._field)
                return float(v) if v is not None else None
        return self._attr_native_value

    async def async_set_native_value(self, value: float) -> None:
        data = self.coordinator.data or {}
        rooms = list(data.get("rooms") or [])
        room = None
        for r in rooms:
            if (r.get("name") or r.get("Name")) == self._room_name:
                room = dict(r)
                break
        if not room:
            return
        key = self._field
        if key not in room and (key[0].upper() + key[1:]) in room:
            key = key[0].upper() + key[1:]
        room[key] = value
        api_url = self.coordinator.api_url
        api_key = self.coordinator.api_key
        async with aiohttp.ClientSession() as session:
            async with session.put(
                f"{api_url}/api/rooms/{self._room_name}",
                json=room,
                headers={"X-API-Key": api_key, "Content-Type": "application/json"},
                timeout=aiohttp.ClientTimeout(total=15),
            ) as resp:
                if resp.status in (200, 204):
                    await self.coordinator.async_request_refresh()


class HeatingParameterNumber(CoordinatorEntity, NumberEntity):
    _attr_has_entity_name = True

    def __init__(self, coordinator, entry: ConfigEntry, field: str, min_v: float, max_v: float, step: float) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._field = field
        self._attr_unique_id = f"{entry.entry_id}_params_{field}"
        self._attr_native_min_value = min_v
        self._attr_native_max_value = max_v
        self._attr_native_step = step
        self._attr_name = PARAM_DISPLAY_NAMES.get(field, field)
        self._attr_device_info = {"identifiers": {(DOMAIN, f"{entry.entry_id}_params")}, "name": "HeatFlow Parametry"}

    @property
    def native_value(self) -> float | None:
        params = (self.coordinator.data or {}).get("heating_parameters") or {}
        v = _pole(params, self._field)
        if v is None:
            return None
        return float(v)

    async def async_set_native_value(self, value: float) -> None:
        api_url = self.coordinator.api_url
        api_key = self.coordinator.api_key
        step = self._attr_native_step
        body = {self._field: int(value) if step >= 1 else value}
        async with aiohttp.ClientSession() as session:
            async with session.patch(
                f"{api_url}/api/heating-parameters",
                json=body,
                headers={"X-API-Key": api_key, "Content-Type": "application/json"},
                timeout=aiohttp.ClientTimeout(total=15),
            ) as resp:
                if resp.status in (200, 204):
                    await self.coordinator.async_request_refresh()
