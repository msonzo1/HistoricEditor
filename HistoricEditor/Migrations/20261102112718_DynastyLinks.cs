using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class DynastyLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PersoneDinastie",
                columns: table => new
                {
                    PersonaId = table.Column<int>(type: "INTEGER", nullable: false),
                    DinastiaId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersoneDinastie", x => new { x.PersonaId, x.DinastiaId });
                    table.ForeignKey(
                        name: "FK_PersoneDinastie_Dinastie_DinastiaId",
                        column: x => x.DinastiaId,
                        principalTable: "Dinastie",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersoneDinastie_People_PersonaId",
                        column: x => x.PersonaId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersoneDinastie_DinastiaId",
                table: "PersoneDinastie",
                column: "DinastiaId");

            // Collega le schede esistenti alle dinastie indicate per nome nel campo «Famiglia» (separate da «;»).
            migrationBuilder.Sql("""
                WITH RECURSIVE split(pid, part, rest) AS (
                    SELECT Id, '', Family || ';' FROM People WHERE Family IS NOT NULL AND trim(Family) <> ''
                    UNION ALL
                    SELECT pid, trim(substr(rest, 1, instr(rest, ';') - 1)), substr(rest, instr(rest, ';') + 1)
                    FROM split WHERE rest <> ''
                )
                INSERT OR IGNORE INTO PersoneDinastie (PersonaId, DinastiaId)
                SELECT DISTINCT s.pid, d.Id FROM split s JOIN Dinastie d ON trim(d.Nome) = s.part COLLATE NOCASE
                WHERE s.part <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersoneDinastie");
        }
    }
}
