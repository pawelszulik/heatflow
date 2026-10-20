"""Encje switch: włącznik całego systemu, automat dawki (Faza 5) oraz Sensitive / AutomationDisabled pokoju."""

from __future__ import annotations

import aiohttp
from homeassistant.components.switch import SwitchEntity
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
    coordinator = data[DATA_COORDINATOR]
    rooms = (coordinator.data or {}).get("rooms") or []
    # Włącznik systemu ma istnieć nawet bez skonfigurowanych pokoi.
    entities = [
        HeatFlowSystemSwitchEntity(coordinator, entry),
        HeatFlowDoseAutomatSwitchEntity(coordinator, entry),
        HeatFlowDoseRestoreSwitchEntity(coordinator, entry),
        HeatFlowParamBoolSwitchEntity(coordinator, entry, "doseTargetFromRooms", "Dawka cel z pokoi", "mdi:home-group"),
        HeatFlowParamBoolSwitchEntity(coordinator, entry, "doseIndoorMeasureWeighted", "Dawka pomiar ważony", "mdi:scale-balance"),
        HeatFlowParamBoolSwitchEntity(coordinator, entry, "doseTargetUseActual", "Dawka cel z faktycznych", "mdi:thermometer-chevron-up"),
    ]
    for room in rooms:
        name = room.get("name") or room.get("Name")
        if not name:
            continue
        if "sensitive" in room or "Sensitive" in room:
            entities.append(RoomSwitchEntity(coordinator, entry, name, "sensitive"))
        if "automationDisabled" in room or "AutomationDisabled" in room:
            entities.append(RoomSwitchEntity(coordinator, entry, name, "automation_disabled"))
        if "useForDoseIndoorMean" in room or "UseForDoseIndoorMean" in room:
            entities.append(RoomSwitchEntity(coordinator, entry, name, "use_for_dose_indoor_mean"))
        if "doseUseActual" in room or "DoseUseActual" in room:
            entities.append(RoomSwitchEntity(coordinator, entry, name, "dose_use_actual"))
    async_add_entities(entities)


class HeatFlowSystemSwitchEntity(CoordinatorEntity, SwitchEntity):
    """Włącznik całego systemu (SystemConfiguration.SystemEnabled przez /api/system).

    Po wyłączeniu HeatFlow.Console pomija wszystkie fazy - zawory, mieszacz i tryb lato
    zostają w ostatnim stanie, a sensor "Status" pokazuje "disabled".
    """

    _attr_has_entity_name = True

    def __init__(self, coordinator, entry: ConfigEntry) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._attr_unique_id = f"{entry.entry_id}_system_enabled"
        self._attr_name = "Sterowanie ogrzewaniem"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "HeatFlow",
        }

    @property
    def available(self) -> bool:
        # Starsze API bez /api/system -> coordinator daje None.
        return super().available and (self.coordinator.data or {}).get("system") is not None

    @property
    def icon(self) -> str:
        return "mdi:radiator" if self.is_on else "mdi:radiator-off"

    @property
    def is_on(self) -> bool:
        system = (self.coordinator.data or {}).get("system") or {}
        return bool(system.get("enabled", system.get("Enabled", False)))

    async def _set_enabled(self, value: bool) -> None:
        api_url = self.coordinator.api_url
        api_key = self.coordinator.api_key
        async with aiohttp.ClientSession() as session:
            async with session.put(
                f"{api_url}/api/system/enabled",
                json={"enabled": value},
                headers={"X-API-Key": api_key, "Content-Type": "application/json"},
                timeout=aiohttp.ClientTimeout(total=15),
            ) as resp:
                if resp.status in (200, 204):
                    await self.coordinator.async_request_refresh()

    async def async_turn_on(self, **kwargs) -> None:
        await self._set_enabled(True)

    async def async_turn_off(self, **kwargs) -> None:
        await self._set_enabled(False)


class HeatFlowDoseAutomatSwitchEntity(CoordinatorEntity, SwitchEntity):
    """Automat dawki kotła (Faza 5) - switch.heatflow_dawka_automat.

    Off = HeatFlow liczy dawkę i pokazuje ją w sensorach „Dawka *”, ale nic nie zapisuje do
    sterownika. On = zapisuje czas pracy / postoju podajnika i dmuchawę (najwyżej co 2 h).
    Przy włączeniu zapamiętuje zastane nastawy, przy wyłączeniu je przywraca. Zmiana działa
    w najbliższym przebiegu HeatFlow.Console (do 5 min). Faza 5 może go sama wyłączyć
    (spaliny, brak danych, tryb lato...) - powód w atrybutach i na Discordzie.
    """

    _attr_has_entity_name = True

    def __init__(self, coordinator, entry: ConfigEntry) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._attr_unique_id = f"{entry.entry_id}_dose_automat"
        self._attr_name = "Dawka automat"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "HeatFlow",
        }

    @property
    def available(self) -> bool:
        return super().available and (self.coordinator.data or {}).get("dose") is not None

    @property
    def icon(self) -> str:
        return "mdi:fire-circle" if self.is_on else "mdi:fire-off"

    @property
    def is_on(self) -> bool:
        dose = (self.coordinator.data or {}).get("dose") or {}
        return bool(dose.get("automatEnabled", False))

    @property
    def extra_state_attributes(self) -> dict:
        dose = (self.coordinator.data or {}).get("dose") or {}
        return {
            "aktywny": dose.get("automatActive"),
            "zmienil": dose.get("automatChangedBy"),
            "zmiana": dose.get("automatChangedAtUtc"),
            "zapamietane_praca": dose.get("savedWork"),
            "zapamietany_postoj": dose.get("savedPause"),
            "zapamietana_dmuchawa": dose.get("savedBlower"),
            "ostatnie_samowylaczenie": dose.get("lastSelfDisableReason"),
            "ostatnie_samowylaczenie_kiedy": dose.get("lastSelfDisableUtc"),
        }

    async def _set_enabled(self, value: bool) -> None:
        async with aiohttp.ClientSession() as session:
            async with session.put(
                f"{self.coordinator.api_url}/api/dose/automat",
                json={"enabled": value},
                headers={"X-API-Key": self.coordinator.api_key, "Content-Type": "application/json", "X-Source": "HA"},
                timeout=aiohttp.ClientTimeout(total=15),
            ) as resp:
                if resp.status in (200, 204):
                    await self.coordinator.async_request_refresh()

    async def async_turn_on(self, **kwargs) -> None:
        await self._set_enabled(True)

    async def async_turn_off(self, **kwargs) -> None:
        await self._set_enabled(False)


class HeatFlowDoseRestoreSwitchEntity(CoordinatorEntity, SwitchEntity):
    """Czy po wyłączeniu automatu dawki przywrócić nastawy zastane przy włączeniu (parametr doseRestoreOnDisable)."""

    _attr_has_entity_name = True
    _attr_icon = "mdi:restore"

    def __init__(self, coordinator, entry: ConfigEntry) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._attr_unique_id = f"{entry.entry_id}_dose_restore_on_disable"
        self._attr_name = "Dawka przywracaj nastawy"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "HeatFlow",
        }

    @property
    def available(self) -> bool:
        params = (self.coordinator.data or {}).get("heating_parameters") or {}
        return super().available and ("doseRestoreOnDisable" in params or "DoseRestoreOnDisable" in params)

    @property
    def is_on(self) -> bool:
        params = (self.coordinator.data or {}).get("heating_parameters") or {}
        value = params.get("doseRestoreOnDisable")
        if value is None:
            value = params.get("DoseRestoreOnDisable", True)
        return bool(value)

    async def _set_value(self, value: bool) -> None:
        async with aiohttp.ClientSession() as session:
            async with session.patch(
                f"{self.coordinator.api_url}/api/heating-parameters",
                json={"doseRestoreOnDisable": value},
                headers={"X-API-Key": self.coordinator.api_key, "Content-Type": "application/json", "X-Source": "HA"},
                timeout=aiohttp.ClientTimeout(total=15),
            ) as resp:
                if resp.status in (200, 204):
                    await self.coordinator.async_request_refresh()

    async def async_turn_on(self, **kwargs) -> None:
        await self._set_value(True)

    async def async_turn_off(self, **kwargs) -> None:
        await self._set_value(False)


class HeatFlowParamBoolSwitchEntity(CoordinatorEntity, SwitchEntity):
    """Parametr bool Fazy 5 (heating-parameters), np. cel domu z pokoi / pomiar ważony do korekty k."""

    _attr_has_entity_name = True

    def __init__(self, coordinator, entry: ConfigEntry, key: str, name: str, icon: str) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._key = key
        self._attr_unique_id = f"{entry.entry_id}_params_{key}"
        self._attr_name = name
        self._attr_icon = icon
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "HeatFlow",
        }

    def _value(self):
        params = (self.coordinator.data or {}).get("heating_parameters") or {}
        value = params.get(self._key)
        return value if value is not None else params.get(self._key[0].upper() + self._key[1:])

    @property
    def available(self) -> bool:
        return super().available and self._value() is not None

    @property
    def is_on(self) -> bool:
        return bool(self._value())

    async def _set_value(self, value: bool) -> None:
        async with aiohttp.ClientSession() as session:
            async with session.patch(
                f"{self.coordinator.api_url}/api/heating-parameters",
                json={self._key: value},
                headers={"X-API-Key": self.coordinator.api_key, "Content-Type": "application/json", "X-Source": "HA"},
                timeout=aiohttp.ClientTimeout(total=15),
            ) as resp:
                if resp.status in (200, 204):
                    await self.coordinator.async_request_refresh()

    async def async_turn_on(self, **kwargs) -> None:
        await self._set_value(True)

    async def async_turn_off(self, **kwargs) -> None:
        await self._set_value(False)


# Pola pokoju wystawiane jako switch: pole HA → (klucz w API, nazwa encji).
ROOM_SWITCH_FIELDS = {
    "sensitive": ("sensitive", "Wrażliwy"),
    "automation_disabled": ("automationDisabled", "Automatyka wyłączona"),
    # Faza 5 „Dawka”: czy czujnik pokoju wchodzi do średniej temperatury w domu (bilans do korekty k).
    "use_for_dose_indoor_mean": ("useForDoseIndoorMean", "Dawka: średnia w domu"),
    # Czy faktyczna temperatura pokoju może podnieść jego wkład do celu domu ponad cel.
    "dose_use_actual": ("doseUseActual", "Dawka: cel z faktycznej"),
}


class RoomSwitchEntity(CoordinatorEntity, SwitchEntity):
    _attr_has_entity_name = True

    def __init__(self, coordinator, entry: ConfigEntry, room_name: str, field: str) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._room_name = room_name
        self._field = field
        key, label = ROOM_SWITCH_FIELDS[field]
        self._key = key
        self._attr_unique_id = f"{entry.entry_id}_{room_name}_{key}"
        self._attr_name = label
        self._attr_device_info = {"identifiers": {(DOMAIN, f"{entry.entry_id}_{room_name}")}, "name": f"HeatFlow {room_name}"}

    @property
    def is_on(self) -> bool:
        data = self.coordinator.data or {}
        for r in (data.get("rooms") or []):
            if (r.get("name") or r.get("Name")) == self._room_name:
                return bool(r.get(self._key, r.get(self._key[0].upper() + self._key[1:], False)))
        return False

    async def _set_value(self, value: bool) -> None:
        data = self.coordinator.data or {}
        rooms = list(data.get("rooms") or [])
        room = None
        for r in rooms:
            if (r.get("name") or r.get("Name")) == self._room_name:
                room = dict(r)
                break
        if not room:
            return
        room[self._key] = value
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

    async def async_turn_on(self, **kwargs) -> None:
        await self._set_value(True)

    async def async_turn_off(self, **kwargs) -> None:
        await self._set_value(False)
