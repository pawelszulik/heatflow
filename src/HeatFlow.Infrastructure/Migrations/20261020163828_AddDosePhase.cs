using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeatFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDosePhase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeederTimeEntityId",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "BoilerRetryDelay",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "BoilerTempTolerance",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeederBoostMultiplier",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeederEconomyMultiplier",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeederNormalMultiplier",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeederTimeDefault",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeederTimeTolerance",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "Mixer4DDefault",
                table: "HeatingParameters");

            migrationBuilder.AddColumn<decimal>(
                name: "IndoorTargetForFeed",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 24m);

            migrationBuilder.DropColumn(
                name: "MinTempDiff",
                table: "HeatingParameters");

            migrationBuilder.AddColumn<decimal>(
                name: "DoseTrimUnderpowerK",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 3m);

            migrationBuilder.DropColumn(
                name: "MinMixer4D",
                table: "HeatingParameters");

            migrationBuilder.AddColumn<decimal>(
                name: "DoseCeilingDayHeatingHours",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 22.5m);

            migrationBuilder.DropColumn(
                name: "FrostCompensationFactor",
                table: "HeatingParameters");

            migrationBuilder.AddColumn<decimal>(
                name: "DoseBoilerColdK",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 10m);

            migrationBuilder.DropColumn(
                name: "FeederEconomyThreshold",
                table: "HeatingParameters");

            migrationBuilder.AddColumn<int>(
                name: "FeederWorkMin",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.DropColumn(
                name: "FeederBoostThreshold",
                table: "HeatingParameters");

            migrationBuilder.AddColumn<int>(
                name: "FeederWorkMax",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.DropColumn(
                name: "BoilerRetryCount",
                table: "HeatingParameters");

            migrationBuilder.AddColumn<int>(
                name: "FeederPauseMin",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<string>(
                name: "DoseBlowerActualEntityId",
                table: "SystemConfiguration",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DoseBlowerEntityId",
                table: "SystemConfiguration",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DoseBoilerSetpointEntityId",
                table: "SystemConfiguration",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DoseControllerClockEntityId",
                table: "SystemConfiguration",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DoseControllerModeEntityId",
                table: "SystemConfiguration",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DoseFeederCounterEntityId",
                table: "SystemConfiguration",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DoseFeederPauseEntityId",
                table: "SystemConfiguration",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DoseFeederWorkEntityId",
                table: "SystemConfiguration",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DoseFlueSafetyAutomationEntityId",
                table: "SystemConfiguration",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DoseFlueTempEntityId",
                table: "SystemConfiguration",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DoseNotifyService",
                table: "SystemConfiguration",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DoseNotifyTarget",
                table: "SystemConfiguration",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DoseOutdoorTempEntityId",
                table: "SystemConfiguration",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DoseSummerSwitchEntityId",
                table: "SystemConfiguration",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "DoseUseActual",
                table: "RoomConfiguration",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HeatLossW",
                table: "RoomConfiguration",
                type: "decimal(7,1)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "UseForDoseIndoorMean",
                table: "RoomConfiguration",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BlowerMax",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 35);

            migrationBuilder.AddColumn<int>(
                name: "BlowerMin",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<decimal>(
                name: "DoseActualMaxAboveTargetsK",
                table: "HeatingParameters",
                type: "decimal(4,2)",
                nullable: false,
                defaultValue: 2m);

            migrationBuilder.AddColumn<decimal>(
                name: "DoseActualSmoothingHours",
                table: "HeatingParameters",
                type: "decimal(5,1)",
                nullable: false,
                defaultValue: 24m);

            migrationBuilder.AddColumn<decimal>(
                name: "DoseBlowerLowDuty",
                table: "HeatingParameters",
                type: "decimal(6,4)",
                nullable: false,
                defaultValue: 0.034m);

            migrationBuilder.AddColumn<int>(
                name: "DoseBoilerColdMinutes",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DoseBoilerColdWarnMinutes",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "DoseBoilerStaleMinutes",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "DoseFlueBlowerCutPct",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "DoseFlueMaxTemp",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 200);

            migrationBuilder.AddColumn<int>(
                name: "DoseFlueNoRaiseTemp",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 185);

            migrationBuilder.AddColumn<int>(
                name: "DoseFlueSafetyHoldHours",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.AddColumn<bool>(
                name: "DoseIndoorMeasureWeighted",
                table: "HeatingParameters",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DoseIndoorMinSensors",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "DoseOutdoorStaleMinutes",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AddColumn<bool>(
                name: "DoseRestoreOnDisable",
                table: "HeatingParameters",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "DoseStorageBonusMaxSec",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 2600);

            migrationBuilder.AddColumn<bool>(
                name: "DoseTargetFromRooms",
                table: "HeatingParameters",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DoseTargetRiseKPerHour",
                table: "HeatingParameters",
                type: "decimal(5,3)",
                nullable: false,
                defaultValue: 0.07m);

            migrationBuilder.AddColumn<bool>(
                name: "DoseTargetUseActual",
                table: "HeatingParameters",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "DoseTrimLimitSec",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "DoseTrimStandbyEntries",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "DoseTrimStandbyHours",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.AddColumn<int>(
                name: "DoseTrimStepSec",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "DoseTrimUnderpowerHours",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "FeedCorrectionK",
                table: "HeatingParameters",
                type: "decimal(5,3)",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<int>(
                name: "FeedDeadbandSec",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<decimal>(
                name: "FeedKGain",
                table: "HeatingParameters",
                type: "decimal(5,3)",
                nullable: false,
                defaultValue: 0.3m);

            migrationBuilder.AddColumn<decimal>(
                name: "FeedKMax",
                table: "HeatingParameters",
                type: "decimal(5,3)",
                nullable: false,
                defaultValue: 1.3m);

            migrationBuilder.AddColumn<decimal>(
                name: "FeedKMaxStep",
                table: "HeatingParameters",
                type: "decimal(5,3)",
                nullable: false,
                defaultValue: 0.05m);

            migrationBuilder.AddColumn<decimal>(
                name: "FeedKMin",
                table: "HeatingParameters",
                type: "decimal(5,3)",
                nullable: false,
                defaultValue: 0.6m);

            migrationBuilder.AddColumn<int>(
                name: "FeedMaxStepSec",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "FeedMinIntervalHours",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "FeedOffset",
                table: "HeatingParameters",
                type: "decimal(8,2)",
                nullable: false,
                defaultValue: -934m);

            migrationBuilder.AddColumn<decimal>(
                name: "FeedPerKelvin",
                table: "HeatingParameters",
                type: "decimal(7,2)",
                nullable: false,
                defaultValue: 255m);

            migrationBuilder.AddColumn<int>(
                name: "FeedSmoothingForecastHours",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "FeedSmoothingPastHours",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "FeedWorkUpHysteresisSec",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 130);

            migrationBuilder.AddColumn<int>(
                name: "FeederPauseMax",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 320);

            migrationBuilder.AddColumn<decimal>(
                name: "HouseCapacity",
                table: "HeatingParameters",
                type: "decimal(8,2)",
                nullable: false,
                defaultValue: 1549m);

            migrationBuilder.CreateTable(
                name: "DoseAutomatSwitch",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoseAutomatSwitch", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DoseSample",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OutdoorTemp = table.Column<double>(type: "float", nullable: true),
                    IndoorTemp = table.Column<double>(type: "float", nullable: true),
                    BoilerTemp = table.Column<double>(type: "float", nullable: true),
                    BoilerSetpoint = table.Column<double>(type: "float", nullable: true),
                    FlueTemp = table.Column<double>(type: "float", nullable: true),
                    BlowerActual = table.Column<double>(type: "float", nullable: true),
                    FeederCounterSec = table.Column<int>(type: "int", nullable: true),
                    SummerMode = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoseSample", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DoseState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    AutomatActive = table.Column<bool>(type: "bit", nullable: false),
                    SavedWork = table.Column<int>(type: "int", nullable: true),
                    SavedPause = table.Column<int>(type: "int", nullable: true),
                    SavedBlower = table.Column<int>(type: "int", nullable: true),
                    ControllerWork = table.Column<int>(type: "int", nullable: true),
                    ControllerPause = table.Column<int>(type: "int", nullable: true),
                    ControllerBlower = table.Column<int>(type: "int", nullable: true),
                    LastMapperOkUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastBoilerDataUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastOutdoorDataUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlannedWork = table.Column<int>(type: "int", nullable: true),
                    PlannedPause = table.Column<int>(type: "int", nullable: true),
                    PlannedBlower = table.Column<int>(type: "int", nullable: true),
                    ModelPause = table.Column<double>(type: "float", nullable: true),
                    OutdoorSmoothed = table.Column<double>(type: "float", nullable: true),
                    NeedSecPerDay = table.Column<double>(type: "float", nullable: true),
                    HouseTarget = table.Column<double>(type: "float", nullable: true),
                    HouseTargetUsed = table.Column<double>(type: "float", nullable: true),
                    HouseTargetSource = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    StorageBonusSec = table.Column<double>(type: "float", nullable: false),
                    HouseTargetLogged = table.Column<double>(type: "float", nullable: true),
                    RoomTargetsSnapshot = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    HouseTargetBase = table.Column<double>(type: "float", nullable: true),
                    RoomActualSmoothed = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RoomTargetsLastCalc = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    HouseTargetActualRooms = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Ceiling = table.Column<bool>(type: "bit", nullable: false),
                    ShortfallPct = table.Column<double>(type: "float", nullable: false),
                    TrimSec = table.Column<int>(type: "int", nullable: false),
                    LastTrimUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastChangeUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastChangeOutdoorSmoothed = table.Column<double>(type: "float", nullable: true),
                    LastTrimCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastLoggedBlockKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastChangeDirection = table.Column<int>(type: "int", nullable: false),
                    BoilerColdWarned = table.Column<bool>(type: "bit", nullable: false),
                    ShadowWork = table.Column<int>(type: "int", nullable: true),
                    ShadowPause = table.Column<int>(type: "int", nullable: true),
                    ShadowBlower = table.Column<int>(type: "int", nullable: true),
                    ShadowLastChangeUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastCalcUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastKCorrectionDate = table.Column<DateTime>(type: "date", nullable: true),
                    LastReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LastSelfDisableReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastSelfDisableUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoseState", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeedChangeLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Parameter = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OldValue = table.Column<double>(type: "float", nullable: true),
                    NewValue = table.Column<double>(type: "float", nullable: true),
                    Direction = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    MapperConfirmation = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AutomatActive = table.Column<bool>(type: "bit", nullable: false),
                    OutdoorSmoothed = table.Column<double>(type: "float", nullable: true),
                    OutdoorAtPreviousChange = table.Column<double>(type: "float", nullable: true),
                    ForecastNextHour = table.Column<double>(type: "float", nullable: true),
                    K = table.Column<double>(type: "float", nullable: true),
                    HouseTarget = table.Column<double>(type: "float", nullable: true),
                    HouseTargetSource = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    StorageBonusSec = table.Column<double>(type: "float", nullable: true),
                    NeedSecPerDay = table.Column<double>(type: "float", nullable: true),
                    TrimSec = table.Column<int>(type: "int", nullable: false),
                    BoilerTemp = table.Column<double>(type: "float", nullable: true),
                    BoilerSetpoint = table.Column<double>(type: "float", nullable: true),
                    FlueTemp = table.Column<double>(type: "float", nullable: true),
                    HeatingShare2hPct = table.Column<double>(type: "float", nullable: true),
                    StandbyEntries6h = table.Column<int>(type: "int", nullable: true),
                    ControllerWork = table.Column<int>(type: "int", nullable: true),
                    ControllerPause = table.Column<int>(type: "int", nullable: true),
                    ControllerBlower = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedChangeLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoseSample_TimestampUtc",
                table: "DoseSample",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FeedChangeLog_TimestampUtc",
                table: "FeedChangeLog",
                column: "TimestampUtc");
            // WAZNE: defaultValue powyzej backfilluje istniejacy wiersz parametrow wartosciami z analizy
            // (analiza-kociol-nastawy.md par. 9). Zero oznaczaloby nastawy poza granicami Pawla.
            // Usuniete kolumny (Feeder*, FrostCompensationFactor, Mixer4DDefault, MinMixer4D, MinTempDiff,
            // BoilerRetry*, BoilerTempTolerance, FeederTimeEntityId) to pozostalosci po usunietej fazie
            // kotla ekopiec - nic ich nie czyta. Celowo Drop+Add, nie Rename (EF zgadywal zmiany nazw
            // i przenioslby np. FeederBoostThreshold = 5 do FeederWorkMax).
            // Dane w EXEC, bo skrypt idempotentny to jedna paczka - bez niego SQL Server nie zna nowych kolumn.

            // Encje HA Fazy 5 i kanal powiadomien u nas (= ConfigurationSeed.DoseEntityDefaults).
            migrationBuilder.Sql(@"EXEC(N'
UPDATE SystemConfiguration SET
    BoilerTempEntityId = COALESCE(NULLIF(BoilerTempEntityId, ''''), ''sensor.kociol_temperatura_kotla''),
    DoseOutdoorTempEntityId = ''sensor.temperatura_ogrod_temperature'',
    DoseBoilerSetpointEntityId = ''number.kociol_temperatura_zadana_kotla'',
    DoseFlueTempEntityId = ''sensor.kociol_temperatura_spalin'',
    DoseBlowerActualEntityId = ''sensor.kociol_aktualna_moc_dmuchawy'',
    DoseFeederCounterEntityId = ''sensor.kociol_czas_pracy_podajnika_2'',
    DoseSummerSwitchEntityId = ''switch.kociol_tryb_zima_lato'',
    DoseControllerModeEntityId = ''switch.kociol_tryb_pracy'',
    DoseControllerClockEntityId = ''sensor.kociol_aktualna_data'',
    DoseFlueSafetyAutomationEntityId = ''automation.kociol_bezpiecznik_spalin_dmuchawa_na_minimum'',
    DoseFeederWorkEntityId = ''number.kociol_czas_pracy_podajnika'',
    DoseFeederPauseEntityId = ''number.kociol_czas_postoju_podajnika'',
    DoseBlowerEntityId = ''number.kociol_moc_dmuchawy'',
    DoseNotifyService = ''homeintegrator'',
    DoseNotifyTarget = ''1458898223593689120''
WHERE Id = 1;')");

            // Czujniki do sredniej w domu: 7x BMx280 z kalibracji modelu (analiza par. 2).
            migrationBuilder.Sql(@"EXEC(N'
UPDATE RoomConfiguration SET UseForDoseIndoorMean = 1
WHERE Name IN (''kuchnia'', ''salon'', ''spizarnia'', ''suszarnia'', ''edyta'', ''hol'', ''przejsciowy'');')");

            // Wagi celow pokoi w celu domu dla dawki = zapotrzebowanie pokoi [W] z inwentaryzacji
            // (plan-heatflow-algorytm.md, „Wagi pokoi z zapotrzebowania”).
            migrationBuilder.Sql(@"EXEC(N'
UPDATE RoomConfiguration SET HeatLossW = CASE Name WHEN ''balkonowy'' THEN 1344 WHEN ''garaz'' THEN 965 WHEN ''salon'' THEN 626 WHEN ''jadalnia'' THEN 625 WHEN ''hol'' THEN 622 WHEN ''edyta'' THEN 604 WHEN ''sypialnia'' THEN 584 WHEN ''grzes'' THEN 500 WHEN ''przejsciowy'' THEN 484 WHEN ''suszarnia'' THEN 448 WHEN ''kuchnia'' THEN 430 WHEN ''sien'' THEN 340 WHEN ''lazienka'' THEN 293 WHEN ''spizarnia'' THEN 159 WHEN ''toaleta'' THEN 159 ELSE 0 END;')");
        }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoseAutomatSwitch");

            migrationBuilder.DropTable(
                name: "DoseSample");

            migrationBuilder.DropTable(
                name: "DoseState");

            migrationBuilder.DropTable(
                name: "FeedChangeLog");

            migrationBuilder.DropColumn(
                name: "DoseBlowerActualEntityId",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "DoseBlowerEntityId",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "DoseBoilerSetpointEntityId",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "DoseControllerClockEntityId",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "DoseControllerModeEntityId",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "DoseFeederCounterEntityId",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "DoseFeederPauseEntityId",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "DoseFeederWorkEntityId",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "DoseFlueSafetyAutomationEntityId",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "DoseFlueTempEntityId",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "DoseNotifyService",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "DoseNotifyTarget",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "DoseOutdoorTempEntityId",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "DoseSummerSwitchEntityId",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "DoseUseActual",
                table: "RoomConfiguration");

            migrationBuilder.DropColumn(
                name: "HeatLossW",
                table: "RoomConfiguration");

            migrationBuilder.DropColumn(
                name: "UseForDoseIndoorMean",
                table: "RoomConfiguration");

            migrationBuilder.DropColumn(
                name: "BlowerMax",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "BlowerMin",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseActualMaxAboveTargetsK",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseActualSmoothingHours",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseBlowerLowDuty",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseBoilerColdMinutes",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseBoilerColdWarnMinutes",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseBoilerStaleMinutes",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseFlueBlowerCutPct",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseFlueMaxTemp",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseFlueNoRaiseTemp",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseFlueSafetyHoldHours",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseIndoorMeasureWeighted",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseIndoorMinSensors",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseOutdoorStaleMinutes",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseRestoreOnDisable",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseStorageBonusMaxSec",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseTargetFromRooms",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseTargetRiseKPerHour",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseTargetUseActual",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseTrimLimitSec",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseTrimStandbyEntries",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseTrimStandbyHours",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseTrimStepSec",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseTrimUnderpowerHours",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeedCorrectionK",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeedDeadbandSec",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeedKGain",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeedKMax",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeedKMaxStep",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeedKMin",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeedMaxStepSec",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeedMinIntervalHours",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeedOffset",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeedPerKelvin",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeedSmoothingForecastHours",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeedSmoothingPastHours",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeedWorkUpHysteresisSec",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeederPauseMax",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "HouseCapacity",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "IndoorTargetForFeed",
                table: "HeatingParameters");

            migrationBuilder.AddColumn<decimal>(
                name: "Mixer4DDefault",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.DropColumn(
                name: "FeederWorkMin",
                table: "HeatingParameters");

            migrationBuilder.AddColumn<int>(
                name: "FeederEconomyThreshold",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.DropColumn(
                name: "FeederWorkMax",
                table: "HeatingParameters");

            migrationBuilder.AddColumn<int>(
                name: "FeederBoostThreshold",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.DropColumn(
                name: "FeederPauseMin",
                table: "HeatingParameters");

            migrationBuilder.AddColumn<int>(
                name: "BoilerRetryCount",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.DropColumn(
                name: "DoseTrimUnderpowerK",
                table: "HeatingParameters");

            migrationBuilder.AddColumn<decimal>(
                name: "MinTempDiff",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.DropColumn(
                name: "DoseCeilingDayHeatingHours",
                table: "HeatingParameters");

            migrationBuilder.AddColumn<decimal>(
                name: "MinMixer4D",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.DropColumn(
                name: "DoseBoilerColdK",
                table: "HeatingParameters");

            migrationBuilder.AddColumn<decimal>(
                name: "FrostCompensationFactor",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "FeederTimeEntityId",
                table: "SystemConfiguration",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BoilerRetryDelay",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BoilerTempTolerance",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FeederBoostMultiplier",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FeederEconomyMultiplier",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FeederNormalMultiplier",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FeederTimeDefault",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FeederTimeTolerance",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
