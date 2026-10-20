"""Platform button - „Ustaw sugerowane” (Faza 5 „Dawka”)."""

from __future__ import annotations

import aiohttp
from homeassistant.components import persistent_notification
from homeassistant.components.button import ButtonEntity
from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant
from homeassistant.exceptions import HomeAssistantError
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
    async_add_entities([HeatFlowDoseApplyButton(data[DATA_COORDINATOR], entry)])


class HeatFlowDoseApplyButton(CoordinatorEntity, ButtonEntity):
    """button.heatflow_dawka_ustaw_sugerowane - jednorazowo zapisuje do kotła to, co Faza 5 sugeruje
    (sensory „Dawka praca / postój / dmuchawa”), mimo wyłączonego automatu.

    API tylko dopisuje zlecenie; nastawy zapisuje HeatFlow.Console w najbliższym przebiegu (do 5 min):
    te same ściany bezpieczeństwa co automat, potwierdzenie w mapperze, wpis w dzienniku
    (sensor.heatflow_dawka_ostatnia_zmiana) i wiadomość na Discord. Działa tylko przy wyłączonym automacie;
    najwyżej raz na 10 min.
    """

    _attr_has_entity_name = True
    _attr_icon = "mdi:flash-auto"

    def __init__(self, coordinator, entry: ConfigEntry) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._attr_unique_id = f"{entry.entry_id}_dose_apply_suggestion"
        self._attr_name = "Dawka ustaw sugerowane"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "HeatFlow",
        }

    @property
    def available(self) -> bool:
        return super().available and (self.coordinator.data or {}).get("dose") is not None

    @property
    def extra_state_attributes(self) -> dict:
        dose = (self.coordinator.data or {}).get("dose") or {}
        return {
            "sugerowana_praca": dose.get("plannedWork"),
            "sugerowany_postoj": dose.get("plannedPause"),
            "sugerowana_dmuchawa": dose.get("plannedBlower"),
            "w_sterowniku_praca": dose.get("controllerWork"),
            "w_sterowniku_postoj": dose.get("controllerPause"),
            "w_sterowniku_dmuchawa": dose.get("controllerBlower"),
            "automat_wlaczony": dose.get("automatEnabled"),
        }

    async def async_press(self) -> None:
        async with aiohttp.ClientSession() as session:
            async with session.post(
                f"{self.coordinator.api_url}/api/dose/apply-suggestion",
                headers={"X-API-Key": self.coordinator.api_key, "X-Source": "HA"},
                timeout=aiohttp.ClientTimeout(total=15),
            ) as resp:
                try:
                    body = await resp.json(content_type=None)
                except Exception:  # noqa: BLE001 - odpowiedź nie-JSON: pokaż status
                    body = {}
                if resp.status not in (200, 202):
                    error = (body or {}).get("error") or (body or {}).get("detail") or f"HTTP {resp.status}"
                    raise HomeAssistantError(f"Ustaw sugerowane: {error}")

        message = (body or {}).get("message") or "Zlecono zapis sugerowanych nastaw."
        persistent_notification.async_create(
            self.hass,
            message + " Wynik: sensor „Dawka ostatnia zmiana” i Discord.",
            title="HeatFlow: Ustaw sugerowane",
            notification_id="heatflow_dawka_ustaw_sugerowane",
        )
        await self.coordinator.async_request_refresh()
