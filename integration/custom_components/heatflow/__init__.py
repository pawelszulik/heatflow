"""Integracja HeatFlow z Home Assistant."""

from __future__ import annotations

from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant

from .const import CONF_API_KEY, CONF_API_URL, DATA_COORDINATOR, DOMAIN
from .coordinator import HeatFlowDataUpdateCoordinator

PLATFORMS = ["sensor", "number", "select", "switch"]


async def async_setup_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    """Konfiguracja integracji z wpisu config."""
    hass.data.setdefault(DOMAIN, {})
    coordinator = HeatFlowDataUpdateCoordinator(
        hass,
        entry.entry_id,
        entry.data[CONF_API_URL],
        entry.data[CONF_API_KEY],
    )
    await coordinator.async_config_entry_first_refresh()
    hass.data[DOMAIN][entry.entry_id] = {
        CONF_API_URL: entry.data[CONF_API_URL],
        CONF_API_KEY: entry.data[CONF_API_KEY],
        DATA_COORDINATOR: coordinator,
    }
    await hass.config_entries.async_forward_entry_setups(entry, PLATFORMS)
    return True


async def async_unload_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    """Odładowanie integracji.

    Musi byc async_unload_platforms - to udokumentowane API HA do zwalniania platform
    wpisu. Wczesniej bylo tu async_unload_entries, po ktorym przeladowanie wpisu nie
    odtwarzalo encji (np. nowe parametry z API nie pojawialy sie bez restartu HA).
    """
    unload_ok = await hass.config_entries.async_unload_platforms(entry, PLATFORMS)
    if unload_ok:
        hass.data[DOMAIN].pop(entry.entry_id, None)
    return unload_ok
