"""Encje switch dla Sensitive i AutomationDisabled pokoju."""

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
    entities = []
    for room in rooms:
        name = room.get("name") or room.get("Name")
        if not name:
            continue
        if "sensitive" in room or "Sensitive" in room:
            entities.append(RoomSwitchEntity(coordinator, entry, name, "sensitive"))
        if "automationDisabled" in room or "AutomationDisabled" in room:
            entities.append(RoomSwitchEntity(coordinator, entry, name, "automation_disabled"))
    async_add_entities(entities)


class RoomSwitchEntity(CoordinatorEntity, SwitchEntity):
    _attr_has_entity_name = True

    def __init__(self, coordinator, entry: ConfigEntry, room_name: str, field: str) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._room_name = room_name
        self._field = field
        key = "sensitive" if field == "sensitive" else "automationDisabled"
        self._attr_unique_id = f"{entry.entry_id}_{room_name}_{key}"
        self._attr_name = "Wrażliwy" if field == "sensitive" else "Automatyka wyłączona"
        self._attr_device_info = {"identifiers": {(DOMAIN, f"{entry.entry_id}_{room_name}")}, "name": f"HeatFlow {room_name}"}

    @property
    def is_on(self) -> bool:
        data = self.coordinator.data or {}
        for r in (data.get("rooms") or []):
            if (r.get("name") or r.get("Name")) == self._room_name:
                if self._field == "sensitive":
                    return bool(r.get("sensitive", r.get("Sensitive", False)))
                return bool(r.get("automationDisabled", r.get("AutomationDisabled", False)))
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
        if self._field == "sensitive":
            room["sensitive"] = value
        else:
            room["automationDisabled"] = value
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
