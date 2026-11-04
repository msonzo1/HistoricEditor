using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class AttachmentsAndHistoricNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "People",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NomiStorici",
                table: "Luoghi",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WikidataId",
                table: "Luoghi",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Allegati",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ambito = table.Column<string>(type: "TEXT", nullable: false),
                    SchedaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Titolo = table.Column<string>(type: "TEXT", nullable: false),
                    Descrizione = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: false),
                    File = table.Column<string>(type: "TEXT", nullable: false),
                    NomeOriginale = table.Column<string>(type: "TEXT", nullable: false),
                    Immagine = table.Column<bool>(type: "INTEGER", nullable: false),
                    Ritratto = table.Column<bool>(type: "INTEGER", nullable: false),
                    Ordine = table.Column<int>(type: "INTEGER", nullable: false),
                    Aggiunto = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Allegati", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Luoghi_WikidataId",
                table: "Luoghi",
                column: "WikidataId");

            migrationBuilder.CreateIndex(
                name: "IX_Allegati_Ambito_SchedaId",
                table: "Allegati",
                columns: new[] { "Ambito", "SchedaId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Allegati");

            migrationBuilder.DropIndex(
                name: "IX_Luoghi_WikidataId",
                table: "Luoghi");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "People");

            migrationBuilder.DropColumn(
                name: "NomiStorici",
                table: "Luoghi");

            migrationBuilder.DropColumn(
                name: "WikidataId",
                table: "Luoghi");
        }
    }
}
