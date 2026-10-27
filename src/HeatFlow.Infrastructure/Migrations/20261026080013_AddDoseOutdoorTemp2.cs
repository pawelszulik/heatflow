using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeatFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDoseOutdoorTemp2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DoseOutdoorTemp2EntityId",
                table: "SystemConfiguration",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            // Dane w EXEC: skrypt idempotentny to jedna paczka, bez EXEC SQL Server nie zna jeszcze nowej kolumny.
            // Drugi czujnik zewnetrzny u nas (= ConfigurationSeed.DoseEntityDefaults).
            migrationBuilder.Sql(@"EXEC(N'
UPDATE SystemConfiguration SET DoseOutdoorTemp2EntityId = ''sensor.temperatura_przodek_temperature''
WHERE DoseOutdoorTemp2EntityId IS NULL')");

            // Nowe progi (decyzja Pawla 2026-10-26): czujniki zewnetrzne 6 h (zamiast 120 min), mapper i dane kotla 3 h
            // (zamiast 30 min). Tylko tam, gdzie jeszcze stoja stare domyslne - wlasnych wartosci nie ruszamy.
            migrationBuilder.Sql("UPDATE HeatingParameters SET DoseOutdoorStaleMinutes = 360 WHERE DoseOutdoorStaleMinutes = 120");
            migrationBuilder.Sql("UPDATE HeatingParameters SET DoseBoilerStaleMinutes = 180 WHERE DoseBoilerStaleMinutes = 30");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DoseOutdoorTemp2EntityId",
                table: "SystemConfiguration");
        }
    }
}
