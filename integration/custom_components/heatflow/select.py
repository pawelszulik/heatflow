"""Encje select dla priorytetu pokoju (1–4)."""

from __future__ import annotations

import aiohttp
from homeassistant.components.select import SelectEntity
from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant
from homeassistant.helpers.entity_platform import AddEntitiesCallback
from homeassistant.helpers.update_coordinator import CoordinatorEntity

from .const import DATA_COORDINATOR, DOMAIN

PRIORITY_OPTIONS = ["1", "2", "3", "4"]


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
    entities = [RoomPrioritySelect(coordinator, entry, r) for r in rooms if r.get("name") or r.get("Name")]
    async_add_entities(entities)


class RoomPrioritySelect(CoordinatorEntity, SelectEntity):
    _attr_has_entity_name = True
    _attr_options = PRIORITY_OPTIONS

    def __init__(self, coordinator, entry: ConfigEntry, room: dict) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._room_name = room.get("name") or room.get("Name") or "unknown"
        self._attr_unique_id = f"{entry.entry_id}_{self._room_name}_priority"
        self._attr_device_info = {"identifiers": {(DOMAIN, f"{entry.entry_id}_{self._room_name}")}, "name": f"HeatFlow {self._room_name}"}

    @property
    def current_option(self) -> str | None:
        data = self.coordinator.data or {}
        for r in (data.get("rooms") or []):
            if (r.get("name") or r.get("Name")) == self._room_name:
                p = r.get("priority")
                return str(p) if p is not None else None
        return None

    async def async_select_option(self, option: str) -> None:
        data = self.coordinator.data or {}
        rooms = list(data.get("rooms") or [])
        room = None
        for r in rooms:
            if (r.get("name") or r.get("Name")) == self._room_name:
                room = dict(r)
                break
        if not room:
            return
        room["priority"] = int(option)
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
