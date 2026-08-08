using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeatFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScoreThresholdsDwellAndClassificationSince : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClassificationSince",
                table: "RoomState",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // WAZNE: defaultValue backfilluje istniejacy wiersz konfiguracji, wiec musi byc
            // rowny dotychczasowemu zachowaniu zaszytemu w kodzie (Score > 50 -> Max, < 0 -> Disabled).
            // Zero dalo by "kazdy pokoj ze Score >= 0 na pelne grzanie".
            migrationBuilder.AddColumn<int>(
                name: "MinDwellMinutes",
                table: "HeatingParameters",
                type: "int",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<decimal>(
                name: "ScoreThresholdDisabled",
                table: "HeatingParameters",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ScoreThresholdMax",
                table: "HeatingParameters",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 50m);

            // Backfill istniejacego wiersza konfiguracji robi samo DEFAULT przy ADD COLUMN
            // NOT NULL - dlatego zadnego UPDATE tutaj nie ma. Osobny UPDATE w tej samej
            // partii nie przeszedlby parsera ("Invalid column name"), bo kolumny powstaja
            // dopiero w trakcie jej wykonania.

            migrationBuilder.CreateIndex(
                name: "IX_RoomState_RoomName_RecordedAt",
                table: "RoomState",
                columns: new[] { "RoomName", "RecordedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoomState_RoomName_RecordedAt",
                table: "RoomState");

            migrationBuilder.DropColumn(
                name: "ClassificationSince",
                table: "RoomState");

            migrationBuilder.DropColumn(
                name: "MinDwellMinutes",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "ScoreThresholdDisabled",
                table: "HeatingParameters");

            migrationBuilder.DropColumn(
                name: "ScoreThresholdMax",
                table: "HeatingParameters");
        }
    }
}
