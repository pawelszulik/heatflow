# Scenariusze weryfikacji ręcznej HeatFlow.Api

Wymagania: uruchomione API (`dotnet run` w katalogu HeatFlow.Api z uzupełnionym `appsettings.json`: ConnectionStrings, HeatFlow:ApiKey) oraz narzędzie typu curl lub Postman.

1. **Brak klucza → 401**  
   `GET /api/rooms` bez nagłówka `X-API-Key` → odpowiedź 401 Unauthorized.

2. **Z kluczem → 200**  
   `GET /api/rooms` z nagłówkiem `X-API-Key: <wartość z config>` → 200, body to tablica JSON (pokoje).

3. **GET heating-parameters**  
   `GET /api/heating-parameters` z kluczem → 200, obiekt parametrów.

4. **PUT room**  
   `PUT /api/rooms/Sypialnia` z kluczem i body JSON (RoomConfiguration). Nazwa w URL = name w body. Po zapisie → 200. Sprawdzenie: `GET /api/rooms/Sypialnia` zwraca zaktualizowane dane.

5. **Audit log**  
   Po wykonaniu PUT room lub PUT heating-parameters: `GET /api/configuration-changes?limit=20` z kluczem → 200, tablica wpisów z polami id, timestamp, entityType, entityId, fieldName, oldValue, newValue.

6. **PUT heating-parameters**  
   `PUT /api/heating-parameters` z kluczem i pełnym body HeatingParameters → 200. Audit log zawiera wpisy dla zmienionych pól.

7. **PATCH heating-parameters**  
   `PATCH /api/heating-parameters` z kluczem i body `{"boilerNominalTemp":72}` → 200. Tylko podane pole jest zmienione; audit log zawiera jeden (lub więcej) wpis.

8. **GET /api/health**  
   Z kluczem → 200, body `{"status":"ok"}`.
