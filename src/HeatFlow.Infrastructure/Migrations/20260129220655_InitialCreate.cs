using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeatFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BoilerState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExecutionId = table.Column<int>(type: "int", nullable: false),
                    TempExternal = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    TempReturn = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    TempTarget = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    FeederTime = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Mixer4DPosition = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    RoomsHeatedCount = table.Column<int>(type: "int", nullable: false),
                    ForecastMode = table.Column<int>(type: "int", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoilerState", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExecutionTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HeatingParameters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DeficitHighP1 = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    DeficitHighP2 = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    DeficitHighP3 = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    DeficitMediumP1 = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    DeficitMediumP2 = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    DeficitMediumP3 = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    DeficitHighP1Base = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    DeficitHighP2Base = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    DeficitHighP3Base = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    BufferPreparationBase = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    BufferPreparation = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    BufferHeatingTime = table.Column<int>(type: "int", nullable: false),
                    ForecastTempDropThreshold = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ForecastTempRiseThreshold = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ForecastHoursCount = table.Column<int>(type: "int", nullable: false),
                    ForecastPreHeatingP1Multiplier = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ForecastPreHeatingP2Multiplier = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ForecastPreHeatingP3Multiplier = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ForecastPreHeatingBufferMultiplier = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ForecastReductionP1Multiplier = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ForecastReductionP2Multiplier = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ForecastReductionP3Multiplier = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ForecastReductionBufferMultiplier = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ForecastExtremeColdThreshold = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ForecastExtremeWarmThreshold = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    MaxValvesOpen = table.Column<int>(type: "int", nullable: false),
                    MinValvesOpen = table.Column<int>(type: "int", nullable: false),
                    UsageSoonMinutes = table.Column<int>(type: "int", nullable: false),
                    ScorePriorityMultiplier = table.Column<int>(type: "int", nullable: false),
                    ScoreDeficitMultiplier = table.Column<int>(type: "int", nullable: false),
                    ScoreSensitiveBonus = table.Column<int>(type: "int", nullable: false),
                    ScoreUsageSoonBonus = table.Column<int>(type: "int", nullable: false),
                    ScoreHeatingScheduleBonus = table.Column<int>(type: "int", nullable: false),
                    TopRoomsCount = table.Column<int>(type: "int", nullable: false),
                    ValveTempOffset = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ValveTolerance = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ValveClosedTemp = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    ValveRetryCount = table.Column<int>(type: "int", nullable: false),
                    ValveRetryDelay = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    MinReturnTemp = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    BoilerNominalTemp = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    FrostCompensationFactor = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Mixer4DDefault = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    FeederTimeDefault = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    FeederBoostMultiplier = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    FeederEconomyMultiplier = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    FeederNormalMultiplier = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    FeederBoostThreshold = table.Column<int>(type: "int", nullable: false),
                    FeederEconomyThreshold = table.Column<int>(type: "int", nullable: false),
                    BoilerTempTolerance = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    FeederTimeTolerance = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    BoilerRetryCount = table.Column<int>(type: "int", nullable: false),
                    BoilerRetryDelay = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    MinTempDiff = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    MinMixer4D = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Hysteresis = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    HysteresisSafetyThreshold = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    TempValidationMin = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    TempValidationMax = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeatingParameters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoomConfiguration",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TempTarget = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    TempTargetActive = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    TempTargetInactive = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Sensitive = table.Column<bool>(type: "bit", nullable: false),
                    AutomationDisabled = table.Column<bool>(type: "bit", nullable: false),
                    UsageSchedule = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    HeatingSchedule = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SensorTemperatureEntityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ValveEntityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomConfiguration", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "RoomState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExecutionId = table.Column<int>(type: "int", nullable: false),
                    RoomName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TempActual = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    TempTarget = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    TempDeficit = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Classification = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    HeatingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomState", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemConfiguration",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    RoomsList = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EkoPiecDeviceSn = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TempReturnEntityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Mixer4DPositionEntityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BoilerTempEntityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FeederTimeEntityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SystemEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemConfiguration", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ValveState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExecutionId = table.Column<int>(type: "int", nullable: false),
                    RoomName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ValveEntityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TempSet = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    TempActual = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValveState", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BoilerState_ExecutionId",
                table: "BoilerState",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_BoilerState_RecordedAt",
                table: "BoilerState",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionHistory_ExecutionTime",
                table: "ExecutionHistory",
                column: "ExecutionTime");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionHistory_Phase",
                table: "ExecutionHistory",
                column: "Phase");

            migrationBuilder.CreateIndex(
                name: "IX_RoomConfiguration_Name",
                table: "RoomConfiguration",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_RoomState_ExecutionId",
                table: "RoomState",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomState_RecordedAt",
                table: "RoomState",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RoomState_RoomName",
                table: "RoomState",
                column: "RoomName");

            migrationBuilder.CreateIndex(
                name: "IX_ValveState_ExecutionId",
                table: "ValveState",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ValveState_RecordedAt",
                table: "ValveState",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ValveState_RoomName",
                table: "ValveState",
                column: "RoomName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BoilerState");

            migrationBuilder.DropTable(
                name: "ExecutionHistory");

            migrationBuilder.DropTable(
                name: "HeatingParameters");

            migrationBuilder.DropTable(
                name: "RoomConfiguration");

            migrationBuilder.DropTable(
                name: "RoomState");

            migrationBuilder.DropTable(
                name: "SystemConfiguration");

            migrationBuilder.DropTable(
                name: "ValveState");
        }
    }
}
