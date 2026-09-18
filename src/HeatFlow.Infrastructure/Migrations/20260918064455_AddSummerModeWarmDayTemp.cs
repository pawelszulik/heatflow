using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeatFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSummerModeWarmDayTemp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // WAZNE: defaultValue backfilluje istniejacy wiersz konfiguracji. Zero oznaczaloby
            // "kazdy dzien jest cieply" - tryb lato nigdy nie wrocilby do zimy.
            migrationBuilder.AddColumn<decimal>(
                name: "SummerModeWarmDayTemp",
                table: "HeatingParameters",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 20m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SummerModeWarmDayTemp",
                table: "HeatingParameters");
        }
    }
}
