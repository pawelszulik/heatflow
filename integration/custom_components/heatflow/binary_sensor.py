"""Platform binary_sensor - sufit dawki kotła (Faza 5)."""

from __future__ import annotations

from homeassistant.components.binary_sensor import BinarySensorEntity
from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant
from homeassistant.helpers.entity_platform import AddEntitiesCallback
from homeassistant.helpers.update_coordinator import CoordinatorEntity

from .const import DATA_COORDINATOR, DOMAIN


async def async_setup_entry(
    hass: HomeAssistant,
    entry: ConfigEntry,
    async_add_entities: AddEntitiesCallback,
) -> None:
    data = hass.data[DOMAIN].get(entry.entry_id)
    if not data or not data.get(DATA_COORDINATOR):
        return
    async_add_entities([HeatFlowDoseCeilingSensor(data[DATA_COORDINATOR], entry)])


class HeatFlowDoseCeilingSensor(CoordinatorEntity, BinarySensorEntity):
    """binary_sensor.heatflow_dawka_sufit - on, gdy przy maks. pracy / min. postoju (10/100 s)
    dawka nie pokrywa potrzeby z modelu: kocioł nie nadąża, brakuje `brakuje_proc` %."""

    _attr_has_entity_name = True
    _attr_icon = "mdi:gauge-full"

    def __init__(self, coordinator, entry: ConfigEntry) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._attr_unique_id = f"{entry.entry_id}_dose_ceiling"
        self._attr_name = "Dawka sufit"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "HeatFlow",
        }

    @property
    def available(self) -> bool:
        return super().available and (self.coordinator.data or {}).get("dose") is not None

    @property
    def is_on(self) -> bool:
        dose = (self.coordinator.data or {}).get("dose") or {}
        return bool(dose.get("ceiling", False))

    @property
    def extra_state_attributes(self) -> dict:
        dose = (self.coordinator.data or {}).get("dose") or {}
        return {
            "brakuje_proc": dose.get("shortfallPct"),
            "potrzeba_s_na_dobe": dose.get("needSecPerDay"),
            "tz_wygladzona": dose.get("outdoorSmoothed"),
        }
