using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class Dynasties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Dinastie",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    WikidataId = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dinastie", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Dinastie_Dinastie_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Dinastie",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Dinastie_Nome",
                table: "Dinastie",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dinastie_ParentId",
                table: "Dinastie",
                column: "ParentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Dinastie");
        }
    }
}
