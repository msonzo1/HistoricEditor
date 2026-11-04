using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class Relations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Relazioni",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Persona1Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Persona2Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", nullable: false),
                    Inizio = table.Column<string>(type: "TEXT", nullable: false),
                    Fine = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relazioni", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Relazioni_People_Persona1Id",
                        column: x => x.Persona1Id,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Relazioni_People_Persona2Id",
                        column: x => x.Persona2Id,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Relazioni_Persona1Id",
                table: "Relazioni",
                column: "Persona1Id");

            migrationBuilder.CreateIndex(
                name: "IX_Relazioni_Persona2Id",
                table: "Relazioni",
                column: "Persona2Id");

            migrationBuilder.CreateIndex(
                name: "IX_Relazioni_Tipo",
                table: "Relazioni",
                column: "Tipo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Relazioni");
        }
    }
}
