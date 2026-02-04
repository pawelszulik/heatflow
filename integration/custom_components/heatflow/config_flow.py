"""Config flow dla integracji HeatFlow."""

from __future__ import annotations

import aiohttp
import voluptuous as vol

from homeassistant import config_entries
from homeassistant.data_entry_flow import FlowResult

from .const import CONF_API_KEY, CONF_API_URL, DOMAIN


async def validate_connection(api_url: str, api_key: str) -> str | None:
    """Weryfikacja połączenia z API. Zwraca None przy sukcesie, komunikat błędu w przeciwnym razie."""
    url = api_url.rstrip("/") + "/api/health"
    headers = {"X-API-Key": api_key}
    try:
        async with aiohttp.ClientSession() as session:
            async with session.get(url, headers=headers, timeout=aiohttp.ClientTimeout(total=10)) as resp:
                if resp.status == 401:
                    return "Nieprawidłowy klucz API"
                if resp.status != 200:
                    return f"Błąd API: {resp.status}"
                return None
    except aiohttp.ClientError as e:
        return f"Błąd połączenia: {e}"
    except Exception as e:
        return str(e)


class HeatFlowConfigFlow(config_entries.ConfigFlow, domain=DOMAIN):
    """Konfiguracja HeatFlow."""

    VERSION = 1

    async def async_step_user(self, user_input: dict | None = None) -> FlowResult:
        """Krok użytkownika: URL API i klucz."""
        errors: dict[str, str] = {}
        if user_input is not None:
            api_url = user_input.get(CONF_API_URL, "").strip()
            api_key = user_input.get(CONF_API_KEY, "").strip()
            if not api_url:
                errors["base"] = "Podaj URL API"
            elif not api_key:
                errors["base"] = "Podaj klucz API"
            else:
                if not api_url.startswith(("http://", "https://")):
                    api_url = "http://" + api_url
                err = await validate_connection(api_url, api_key)
                if err:
                    errors["base"] = err
                else:
                    await self.async_set_unique_id(api_url)
                    self._abort_if_unique_id_configured()
                    return self.async_create_entry(
                        title="HeatFlow",
                        data={CONF_API_URL: api_url, CONF_API_KEY: api_key},
                    )
        return self.async_show_form(
            step_id="user",
            data_schema=vol.Schema({
                vol.Required(CONF_API_URL, default=user_input.get(CONF_API_URL) if user_input else "http://localhost:5000"): str,
                vol.Required(CONF_API_KEY, default=user_input.get(CONF_API_KEY) if user_input else ""): str,
            }),
            errors=errors,
        )
