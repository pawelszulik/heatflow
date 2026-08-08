"""Platform sensor – status + audit log (ostatnie zmiany konfiguracji)."""

from __future__ import annotations

from datetime import datetime, timezone

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
    async_add_entities([
        HeatFlowStatusSensor(coordinator, entry),
        HeatFlowLastRunSensor(coordinator, entry),
        HeatFlowConfigurationChangesSensor(coordinator, entry),
    ])


class HeatFlowStatusSensor(CoordinatorEntity, SensorEntity):
    """Stan zdrowia sterownika: ok / stale / error / unknown.

    Czyta /api/status, czyli historię przebiegów HeatFlow.Console. Wcześniej ta encja
    miała wpisane na stałe "ok" i świeciła na zielono nawet przy wyłączonym sterowniku.
    """

    _attr_has_entity_name = True
    _attr_device_class = "enum"
    _attr_options = ["ok", "stale", "error", "unknown"]

    def __init__(self, coordinator, entry: ConfigEntry) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._attr_unique_id = f"{entry.entry_id}_status"
        self._attr_name = "Status"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "HeatFlow",
        }

    @property
    def native_value(self) -> str:
        status = (self.coordinator.data or {}).get("status")
        if not status:
            # API bez endpointu /api/status (starsza wersja) - lepiej "unknown" niż fałszywe "ok".
            return "unknown"
        return status.get("status", "unknown")

    @property
    def extra_state_attributes(self) -> dict:
        data = self.coordinator.data or {}
        rooms = data.get("rooms") or []
        params = data.get("heating_parameters")
        status = data.get("status") or {}
        return {
            "rooms_count": len(rooms),
            "heating_parameters_loaded": params is not None,
            "minutes_since_last_run": status.get("minutesSinceLastRun"),
            "stale_threshold_minutes": status.get("staleThresholdMinutes"),
            "failed_phases": status.get("failedPhases"),
            "valves_total": status.get("valvesTotal"),
            "valves_failed": status.get("valvesFailed"),
            "valves_failed_rooms": status.get("valvesFailedRooms"),
            "rooms_without_sensor": status.get("roomsWithoutSensor"),
            "phase1_details": status.get("phase1Details"),
        }


class HeatFlowLastRunSensor(CoordinatorEntity, SensorEntity):
    """Czas ostatniego przebiegu sterownika - do wykresów i alarmów w HA."""

    _attr_has_entity_name = True
    _attr_device_class = "timestamp"

    def __init__(self, coordinator, entry: ConfigEntry) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._attr_unique_id = f"{entry.entry_id}_last_run"
        self._attr_name = "Ostatni przebieg"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "HeatFlow",
        }

    @property
    def native_value(self):
        status = (self.coordinator.data or {}).get("status") or {}
        raw = status.get("lastRun")
        if not raw:
            return None
        try:
            parsed = datetime.fromisoformat(raw.replace("Z", "+00:00"))
        except ValueError:
            return None
        # ExecutionTime zapisywany jest w UTC, ale bez oznaczenia strefy w JSON.
        return parsed if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)


class HeatFlowConfigurationChangesSensor(CoordinatorEntity, SensorEntity):
    """Sensor z ostatnimi zmianami konfiguracji (audit log)."""

    _attr_has_entity_name = True
    _attr_native_value = "0"

    def __init__(self, coordinator, entry: ConfigEntry) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._attr_unique_id = f"{entry.entry_id}_configuration_changes"
        self._attr_name = "Ostatnie zmiany konfiguracji"
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
