# HeatFlow.Api – kontrakt API

## Uwierzytelnienie

Wszystkie żądania do ścieżek `/api/*` muszą zawierać nagłówek:

- **X-API-Key** – klucz API (wartość z konfiguracji `HeatFlow:ApiKey` lub zmienna `HeatFlow__ApiKey`).

Brak lub błędny klucz → `401 Unauthorized`.

Opcjonalny nagłówek **X-Source** (np. `home_assistant`) – zapisywany w audit logu jako źródło zmiany.

---

## Endpointy

### Odczyt

| Metoda | Ścieżka | Opis |
|--------|---------|------|
| GET | `/api/rooms` | Lista wszystkich pokoi (`RoomConfiguration[]`). |
| GET | `/api/rooms/{name}` | Jeden pokój. 404 gdy brak. |
| GET | `/api/heating-parameters` | Obiekt parametrów grzania (`HeatingParameters`). |
| GET | `/api/configuration-changes` | Historia zmian (audit log). Parametry zapytania: `entityType`, `entityId`, `from`, `to`, `limit` (domyślnie 100). |
| GET | `/api/health` | Health check. Odpowiedź: `{"status":"ok"}`. |

### Zapis

| Metoda | Ścieżka | Body | Opis |
|--------|---------|------|------|
| PUT | `/api/rooms/{name}` | `RoomConfiguration` (JSON) | Aktualizacja pokoju. Nazwa w URL musi być równa `name` w body. |
| PUT | `/api/heating-parameters` | `HeatingParameters` (JSON) | Pełna aktualizacja parametrów. |
| PATCH | `/api/heating-parameters` | Obiekt JSON z podzbiorem pól | Częściowa aktualizacja – tylko podane właściwości. |

---

## Format JSON

Nazwy właściwości w JSON w konwencji **camelCase** (domyślna serializacja ASP.NET Core).

### RoomConfiguration (przykład)

```json
{
  "name": "Sypialnia",
  "tempTarget": 21.0,
  "tempTargetActive": 21.5,
  "tempTargetInactive": 19.0,
  "priority": 1,
  "sensitive": true,
  "automationDisabled": false,
  "usageSchedule": "07:00-22:00|Brak",
  "heatingSchedule": "06:00-23:00|Brak",
  "sensorTemperatureEntityId": "sensor.sypialnia_temperature",
  "valveEntityId": "climate.sypialnia"
}
```

### HeatingParameters

Obiekt z wieloma polami liczbowymi (m.in. `deficitHighP1`, `deficitHighP2`, `bufferPreparation`, `forecastTempDropThreshold`, `maxValvesOpen`, `boilerNominalTemp` itd.). Pełna lista pól w `HeatFlow.Domain.HeatingParameters`.

### ConfigurationChangeLog (odpowiedź GET /api/configuration-changes)

```json
{
  "id": 1,
  "timestamp": "2025-02-04T12:00:00Z",
  "entityType": "Room",
  "entityId": "Sypialnia",
  "fieldName": "TempTarget",
  "oldValue": "20",
  "newValue": "21",
  "source": "home_assistant"
}
```

---

## Przykłady (curl)

```bash
# Health (bez klucza dla /api/health wymagany jest klucz – /api/*)
curl -H "X-API-Key: YOUR_KEY" http://localhost:5000/api/health

# Lista pokoi
curl -H "X-API-Key: YOUR_KEY" http://localhost:5000/api/rooms

# Aktualizacja temperatury pokoju
curl -X PUT -H "X-API-Key: YOUR_KEY" -H "Content-Type: application/json" \
  -d '{"name":"Sypialnia","tempTarget":21,"tempTargetActive":21.5,"tempTargetInactive":19,"priority":1,"sensitive":true,"automationDisabled":false,"usageSchedule":"07:00-22:00|Brak","heatingSchedule":"06:00-23:00|Brak","sensorTemperatureEntityId":"sensor.sypialnia","valveEntityId":"climate.sypialnia"}' \
  http://localhost:5000/api/rooms/Sypialnia

# Częściowa aktualizacja parametrów (PATCH)
curl -X PATCH -H "X-API-Key: YOUR_KEY" -H "Content-Type: application/json" \
  -d '{"boilerNominalTemp":72}' \
  http://localhost:5000/api/heating-parameters

# Historia zmian
curl -H "X-API-Key: YOUR_KEY" "http://localhost:5000/api/configuration-changes?limit=20"
```
