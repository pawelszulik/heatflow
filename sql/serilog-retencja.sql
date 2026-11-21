-- Jednorazowe sprzątanie SerilogLog (HeatFlow) + indeks pod stałą retencję.
-- Uruchom w SSMS / sqlcmd na bazie HeatFlow (VM 200). Wykonuj kroki po kolei.
-- Stan z 2026-11-21: tabela 44 GB, ~12,2 mln wpisów od 2026-07-22, ~185 tys./dobę.

-- KROK 0 (podgląd, może trwać kilkadziesiąt sekund - skan po kluczu Id):
-- Id = 6 800 000 to 2026-10-22 07:40, czyli ok. 30 dni wstecz od 21.11.
SELECT COUNT(*) AS wpisow, MIN([TimeStamp]) AS od, MAX([TimeStamp]) AS do_
FROM dbo.SerilogLog
WHERE Id <= 6800000;
GO

-- KROK 1: kasowanie partiami po kluczu Id (szybkie, bez indeksu na TimeStamp).
-- Partia 20 000 + CHECKPOINT, żeby nie rozdąć dziennika transakcji (HeatFlow_log).
SET NOCOUNT ON;
DECLARE @n INT = 1;
WHILE @n > 0
BEGIN
    DELETE TOP (20000) FROM dbo.SerilogLog WHERE Id <= 6800000;
    SET @n = @@ROWCOUNT;
    CHECKPOINT;
END
GO

-- KROK 2: indeks pod stałą retencję (po kroku 1 tabela jest mała, więc budowa jest szybka).
-- Aplikacja (HeatFlow.Console, PurgeOldLogsAsync) sama kasuje wpisy > 30 dni, ale TYLKO gdy ten indeks istnieje.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SerilogLog_TimeStamp' AND object_id = OBJECT_ID('dbo.SerilogLog'))
    CREATE NONCLUSTERED INDEX IX_SerilogLog_TimeStamp ON dbo.SerilogLog ([TimeStamp]);
GO

-- KROK 3 (osobna decyzja, wymaga okna - fragmentuje indeksy): odzysk miejsca z pliku .mdf.
-- Stan z 2026-11-21 po kroku 1: plik 44,7 GB, dane 20,2 GB (SerilogLog 19,9 GB). Cel 24 GB = dane + zapas.
-- Uwaga: NIE zmniejszaj do 8 GB (wczesniejsza wersja skryptu) - dane zajmuja 20 GB, shrink by sie nie udal.
DBCC SHRINKFILE (N'HeatFlow', 24576);
GO
-- Shrink rozfragmentowuje indeksy. REORGANIZE nie potrzebuje wolnego miejsca w pliku (REBUILD na 20 GB tabeli
-- zażądałby ~20 GB i plik urósłby z powrotem).
ALTER INDEX ALL ON dbo.SerilogLog REORGANIZE;
GO
-- Kontrola:
SELECT name, size/128.0 AS plik_mb, FILEPROPERTY(name,'SpaceUsed')/128.0 AS uzyte_mb FROM sys.database_files;
