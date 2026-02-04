"""Coordinator do okresowego pobierania danych z HeatFlow API."""

from __future__ import annotations

import aiohttp
from datetime import timedelta

from homeassistant.core import HomeAssistant
from homeassistant.helpers.update_coordinator import DataUpdateCoordinator, UpdateFailed

from .const import CONF_API_KEY, CONF_API_URL, DOMAIN


def _headers(api_key: str) -> dict[str, str]:
    return {"X-API-Key": api_key}


class HeatFlowDataUpdateCoordinator(DataUpdateCoordinator[dict]):
    """Pobiera rooms i heating_parameters z API."""

    def __init__(self, hass: HomeAssistant, entry_id: str, api_url: str, api_key: str) -> None:
        super().__init__(
            hass,
            logger=__import__("logging").getLogger(__name__),
            name=DOMAIN,
            update_interval=timedelta(minutes=2),
        )
        self._entry_id = entry_id
        self._api_url = api_url.rstrip("/")
        self._api_key = api_key

    async def _async_update_data(self) -> dict:
        """Pobiera listę pokoi i parametry grzania z API."""
        async with aiohttp.ClientSession() as session:
            try:
                rooms_resp = await session.get(
                    f"{self._api_url}/api/rooms",
                    headers=_headers(self._api_key),
                    timeout=aiohttp.ClientTimeout(total=15),
                )
                if rooms_resp.status == 401:
                    raise UpdateFailed("Nieprawidłowy klucz API")
                if rooms_resp.status != 200:
                    raise UpdateFailed(f"API rooms: {rooms_resp.status}")
                rooms = await rooms_resp.json()

                params_resp = await session.get(
                    f"{self._api_url}/api/heating-parameters",
                    headers=_headers(self._api_key),
                    timeout=aiohttp.ClientTimeout(total=15),
                )
                if params_resp.status != 200:
                    raise UpdateFailed(f"API heating-parameters: {params_resp.status}")
                params = await params_resp.json()

                changes_resp = await session.get(
                    f"{self._api_url}/api/configuration-changes",
                    headers=_headers(self._api_key),
                    params={"limit": 20},
                    timeout=aiohttp.ClientTimeout(total=10),
                )
                changes = await changes_resp.json() if changes_resp.status == 200 else []

                return {"rooms": rooms, "heating_parameters": params, "configuration_changes": changes}
            except aiohttp.ClientError as e:
                raise UpdateFailed(f"Błąd połączenia: {e}") from e

    @property
    def api_url(self) -> str:
        return self._api_url

    @property
    def api_key(self) -> str:
        return self._api_key
