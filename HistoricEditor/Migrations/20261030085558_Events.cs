using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class Events : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Eventi",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Titolo = table.Column<string>(type: "TEXT", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", nullable: false),
                    DataQualificatore = table.Column<string>(type: "TEXT", nullable: false),
                    Data1 = table.Column<string>(type: "TEXT", nullable: false),
                    Data2 = table.Column<string>(type: "TEXT", nullable: false),
                    Descrizione = table.Column<string>(type: "TEXT", nullable: false),
                    WikidataId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Eventi", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventiPartecipanti",
                columns: table => new
                {
                    EventoId = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Ruolo = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventiPartecipanti", x => new { x.EventoId, x.PersonaId });
                    table.ForeignKey(
                        name: "FK_EventiPartecipanti_Eventi_EventoId",
                        column: x => x.EventoId,
                        principalTable: "Eventi",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventiPartecipanti_People_PersonaId",
                        column: x => x.PersonaId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Eventi_Tipo",
                table: "Eventi",
                column: "Tipo");

            migrationBuilder.CreateIndex(
                name: "IX_EventiPartecipanti_PersonaId",
                table: "EventiPartecipanti",
                column: "PersonaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventiPartecipanti");

            migrationBuilder.DropTable(
                name: "Eventi");
        }
    }
}
