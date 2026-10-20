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
            // WAZNE: defaultValue backfilluje istniejacy wiersz parametrow wartosciami z analizy
            // (analiza-kociol-nastawy.md par. 9). Zero oznaczaloby nastawy poza granicami Pawla
            // (praca 7-10 s, postoj 100-320 s, dmuchawa 28-35 %) - Faza 5 by sie samowylaczala.
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

            migrationBuilder.AddColumn<int>(
                name: "DoseBoilerColdMinutes",
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

            migrationBuilder.AddColumn<int>(
                name: "FeederPauseMin",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<int>(
                name: "FeederWorkMax",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "FeederWorkMin",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<decimal>(
                name: "HouseCapacity",
                table: "HeatingParameters",
                type: "decimal(8,2)",
                nullable: false,
                defaultValue: 1549m);

            migrationBuilder.AddColumn<decimal>(
                name: "IndoorTargetForFeed",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 24m);

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
                name: "BlowerMax",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "BlowerMin",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseBoilerColdMinutes",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseBoilerStaleMinutes",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseOutdoorStaleMinutes",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "DoseRestoreOnDisable",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeedCorrectionK",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeedDeadbandSec",
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
                name: "FeederPauseMin",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeederWorkMax",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "FeederWorkMin",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "HouseCapacity",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "IndoorTargetForFeed",
                table: "HeatingParameters");
        }
    }
}
