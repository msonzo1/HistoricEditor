using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class RomanNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Agnomen",
                table: "People",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Cognomen",
                table: "People",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Filiazione",
                table: "People",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NomeLatino",
                table: "People",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Nomen",
                table: "People",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NomiNelTempo",
                table: "People",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Onomastica",
                table: "People",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Praenomen",
                table: "People",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Tribu",
                table: "People",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Tipo",
                table: "Dinastie",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Le schede esistenti ricevono un elenco vuoto invece di NULL.
            migrationBuilder.Sql("UPDATE People SET NomiNelTempo = '[]' WHERE NomiNelTempo IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Agnomen",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Cognomen",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Filiazione",
                table: "People");

            migrationBuilder.DropColumn(
                name: "NomeLatino",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Nomen",
                table: "People");

            migrationBuilder.DropColumn(
                name: "NomiNelTempo",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Onomastica",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Praenomen",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Tribu",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Tipo",
                table: "Dinastie");
        }
    }
}
