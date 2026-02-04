"""Platform sensor – status + audit log (ostatnie zmiany konfiguracji)."""

from __future__ import annotations

from homeassistant.components.sensor import SensorEntity
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
    """Konfiguracja sensorów HeatFlow."""
    data = hass.data[DOMAIN].get(entry.entry_id)
    if not data:
        return
    coordinator = data.get(DATA_COORDINATOR)
    if not coordinator:
        return
    async_add_entities([HeatFlowStatusSensor(coordinator, entry), HeatFlowConfigurationChangesSensor(coordinator, entry)])


class HeatFlowStatusSensor(CoordinatorEntity, SensorEntity):
    """Sensor statusu połączenia z API (liczba pokoi, itp.)."""

    _attr_has_entity_name = True
    _attr_native_value = "ok"

    def __init__(self, coordinator, entry: ConfigEntry) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._attr_unique_id = f"{entry.entry_id}_status"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "HeatFlow",
        }

    @property
    def extra_state_attributes(self) -> dict:
        """Liczba pokoi i dostępność parametrów."""
        data = self.coordinator.data or {}
        rooms = data.get("rooms") or []
        params = data.get("heating_parameters")
        return {
            "rooms_count": len(rooms),
            "heating_parameters_loaded": params is not None,
        }


class HeatFlowConfigurationChangesSensor(CoordinatorEntity, SensorEntity):
    """Sensor z ostatnimi zmianami konfiguracji (audit log)."""

    _attr_has_entity_name = True
    _attr_native_value = "0"

    def __init__(self, coordinator, entry: ConfigEntry) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._attr_unique_id = f"{entry.entry_id}_configuration_changes"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "HeatFlow",
        }

    @property
    def native_value(self) -> str:
        data = self.coordinator.data or {}
        changes = data.get("configuration_changes") or []
        return str(len(changes))

    @property
    def extra_state_attributes(self) -> dict:
        """Ostatnie zmiany: lista stringów 'data | encja | pole | stara → nowa' oraz surowe wpisy."""
        data = self.coordinator.data or {}
        changes = data.get("configuration_changes") or []
        lines = []
        for c in changes:
            ts = c.get("timestamp", c.get("Timestamp", ""))
            eid = c.get("entityId", c.get("EntityId", ""))
            field = c.get("fieldName", c.get("FieldName", ""))
            old = c.get("oldValue", c.get("OldValue", ""))
            new = c.get("newValue", c.get("NewValue", ""))
            lines.append(f"{ts} | {eid} | {field} | {old} → {new}")
        return {"last_changes": lines, "changes_count": len(changes)}
