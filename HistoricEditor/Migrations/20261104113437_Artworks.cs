using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class Artworks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Opere",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Titolo = table.Column<string>(type: "TEXT", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: false),
                    Tecnica = table.Column<string>(type: "TEXT", nullable: false),
                    Dimensioni = table.Column<string>(type: "TEXT", nullable: false),
                    LuogoId = table.Column<int>(type: "INTEGER", nullable: true),
                    LuogoSpecificoId = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: false),
                    Fonti = table.Column<string>(type: "TEXT", nullable: false),
                    Immagine = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Opere", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Opere_LuoghiSpecifici_LuogoSpecificoId",
                        column: x => x.LuogoSpecificoId,
                        principalTable: "LuoghiSpecifici",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Opere_Luoghi_LuogoId",
                        column: x => x.LuogoId,
                        principalTable: "Luoghi",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OperePersone",
                columns: table => new
                {
                    OperaId = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Ruolo = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperePersone", x => new { x.OperaId, x.PersonaId, x.Ruolo });
                    table.ForeignKey(
                        name: "FK_OperePersone_Opere_OperaId",
                        column: x => x.OperaId,
                        principalTable: "Opere",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OperePersone_People_PersonaId",
                        column: x => x.PersonaId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Opere_LuogoId",
                table: "Opere",
                column: "LuogoId");

            migrationBuilder.CreateIndex(
                name: "IX_Opere_LuogoSpecificoId",
                table: "Opere",
                column: "LuogoSpecificoId");

            migrationBuilder.CreateIndex(
                name: "IX_Opere_Tipo",
                table: "Opere",
                column: "Tipo");

            migrationBuilder.CreateIndex(
                name: "IX_OperePersone_PersonaId",
                table: "OperePersone",
                column: "PersonaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperePersone");

            migrationBuilder.DropTable(
                name: "Opere");
        }
    }
}
