"""Config flow dla integracji HeatFlow."""

from __future__ import annotations

import aiohttp
import voluptuous as vol

from homeassistant import config_entries
from homeassistant.data_entry_flow import FlowResult

from .const import CONF_API_KEY, CONF_API_URL, DOMAIN

DEFAULT_API_URL = "http://172.17.137.33:56789"


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

    async def _validate_input(self, user_input: dict) -> tuple[str, str, dict[str, str]]:
        """Normalizuje dane z formularza i sprawdza połączenie. Zwraca (url, klucz, błędy)."""
        api_url = user_input.get(CONF_API_URL, "").strip()
        api_key = user_input.get(CONF_API_KEY, "").strip()
        errors: dict[str, str] = {}
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
        return api_url, api_key, errors

    def _show_form(self, step_id: str, api_url: str, api_key: str, errors: dict[str, str]) -> FlowResult:
        """Formularz URL + klucz API, wspólny dla dodawania i rekonfiguracji."""
        return self.async_show_form(
            step_id=step_id,
            data_schema=vol.Schema({
                vol.Required(CONF_API_URL, default=api_url): str,
                vol.Required(CONF_API_KEY, default=api_key): str,
            }),
            errors=errors,
        )

    async def async_step_user(self, user_input: dict | None = None) -> FlowResult:
        """Krok użytkownika: URL API i klucz."""
        errors: dict[str, str] = {}
        api_url, api_key = DEFAULT_API_URL, ""
        if user_input is not None:
            api_url, api_key, errors = await self._validate_input(user_input)
            if not errors:
                await self.async_set_unique_id(api_url)
                self._abort_if_unique_id_configured()
                return self.async_create_entry(
                    title="HeatFlow",
                    data={CONF_API_URL: api_url, CONF_API_KEY: api_key},
                )
        return self._show_form("user", api_url, api_key, errors)

    async def async_step_reconfigure(self, user_input: dict | None = None) -> FlowResult:
        """Zmiana adresu/klucza API na istniejącym wpisie.

        Aktualizuje wpis w miejscu (ten sam entry_id), bo unique_id encji zawiera entry_id -
        usunięcie i dodanie integracji od nowa zmieniłoby entity_id wszystkich encji
        i rozsypało karty na dashboardach.
        """
        entry = self._get_reconfigure_entry()
        errors: dict[str, str] = {}
        api_url = entry.data.get(CONF_API_URL, DEFAULT_API_URL)
        api_key = entry.data.get(CONF_API_KEY, "")
        if user_input is not None:
            api_url, api_key, errors = await self._validate_input(user_input)
            if not errors:
                # unique_id wpisu to URL API, więc zmienia się razem z adresem
                # (dlatego bez _abort_if_unique_id_mismatch).
                return self.async_update_reload_and_abort(
                    entry,
                    data_updates={CONF_API_URL: api_url, CONF_API_KEY: api_key},
                    unique_id=api_url,
                )
        return self._show_form("reconfigure", api_url, api_key, errors)
