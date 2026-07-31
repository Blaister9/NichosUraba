using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrabaConecta.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Permite varios tramos de atención por día, es decir jornadas partidas con pausa.
    ///
    /// Es aditiva a propósito y no mueve ni borra ninguna fila: cada horario continuo existente ya
    /// es un tramo válido y se convierte en el único tramo del día gracias al valor por defecto de
    /// SortOrder. Los días cerrados se representaban y se siguen representando sin fila, así que
    /// tampoco cambian. Por eso ningún horario puede perderse al aplicarla.
    ///
    /// Reversión: ejecutar Down sólo es seguro mientras ningún negocio tenga jornada partida,
    /// porque restaura el índice único (BusinessId, Day) y un día con dos tramos lo violaría. Para
    /// revertir con jornadas partidas ya guardadas hay que dejar antes un solo tramo por día:
    ///
    ///   DELETE FROM business_hours a USING business_hours b
    ///    WHERE a."BusinessId" = b."BusinessId" AND a."Day" = b."Day" AND a."SortOrder" > b."SortOrder";
    ///
    /// Esa limpieza sí descarta tramos, así que conviene respaldar la tabla antes.
    /// </summary>
    public partial class AddSplitSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_business_hours_BusinessId_Day",
                table: "business_hours");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "business_hours",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_business_hours_BusinessId_Day_SortOrder",
                table: "business_hours",
                columns: new[] { "BusinessId", "Day", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_business_hours_BusinessId_Day_SortOrder",
                table: "business_hours");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "business_hours");

            migrationBuilder.CreateIndex(
                name: "IX_business_hours_BusinessId_Day",
                table: "business_hours",
                columns: new[] { "BusinessId", "Day" },
                unique: true);
        }
    }
}
