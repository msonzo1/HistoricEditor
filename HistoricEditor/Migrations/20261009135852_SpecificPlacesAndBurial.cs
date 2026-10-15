using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class SpecificPlacesAndBurial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BirthLuogoSpecificoId",
                table: "People",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BurialLuogoId",
                table: "People",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BurialLuogoSpecificoId",
                table: "People",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BurialPlace",
                table: "People",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BurialPlaces",
                table: "People",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeathLuogoSpecificoId",
                table: "People",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SenzaLivelloInferiore",
                table: "Luoghi",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "LuoghiSpecifici",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", nullable: false),
                    LuogoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Indirizzo = table.Column<string>(type: "TEXT", nullable: false),
                    Condizione = table.Column<string>(type: "TEXT", nullable: false),
                    DataDistruzione = table.Column<string>(type: "TEXT", nullable: false),
                    Latitudine = table.Column<double>(type: "REAL", nullable: true),
                    Longitudine = table.Column<double>(type: "REAL", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: false),
                    WikidataId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LuoghiSpecifici", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LuoghiSpecifici_Luoghi_LuogoId",
                        column: x => x.LuogoId,
                        principalTable: "Luoghi",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_People_BirthLuogoSpecificoId",
                table: "People",
                column: "BirthLuogoSpecificoId");

            migrationBuilder.CreateIndex(
                name: "IX_People_BurialLuogoId",
                table: "People",
                column: "BurialLuogoId");

            migrationBuilder.CreateIndex(
                name: "IX_People_BurialLuogoSpecificoId",
                table: "People",
                column: "BurialLuogoSpecificoId");

            migrationBuilder.CreateIndex(
                name: "IX_People_DeathLuogoSpecificoId",
                table: "People",
                column: "DeathLuogoSpecificoId");

            migrationBuilder.CreateIndex(
                name: "IX_LuoghiSpecifici_LuogoId_Nome",
                table: "LuoghiSpecifici",
                columns: new[] { "LuogoId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LuoghiSpecifici_WikidataId",
                table: "LuoghiSpecifici",
                column: "WikidataId");

            migrationBuilder.AddForeignKey(
                name: "FK_People_LuoghiSpecifici_BirthLuogoSpecificoId",
                table: "People",
                column: "BirthLuogoSpecificoId",
                principalTable: "LuoghiSpecifici",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_People_LuoghiSpecifici_BurialLuogoSpecificoId",
                table: "People",
                column: "BurialLuogoSpecificoId",
                principalTable: "LuoghiSpecifici",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_People_LuoghiSpecifici_DeathLuogoSpecificoId",
                table: "People",
                column: "DeathLuogoSpecificoId",
                principalTable: "LuoghiSpecifici",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_People_Luoghi_BurialLuogoId",
                table: "People",
                column: "BurialLuogoId",
                principalTable: "Luoghi",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_People_LuoghiSpecifici_BirthLuogoSpecificoId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_People_LuoghiSpecifici_BurialLuogoSpecificoId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_People_LuoghiSpecifici_DeathLuogoSpecificoId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_People_Luoghi_BurialLuogoId",
                table: "People");

            migrationBuilder.DropTable(
                name: "LuoghiSpecifici");

            migrationBuilder.DropIndex(
                name: "IX_People_BirthLuogoSpecificoId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_BurialLuogoId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_BurialLuogoSpecificoId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_DeathLuogoSpecificoId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "BirthLuogoSpecificoId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "BurialLuogoId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "BurialLuogoSpecificoId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "BurialPlace",
                table: "People");

            migrationBuilder.DropColumn(
                name: "BurialPlaces",
                table: "People");

            migrationBuilder.DropColumn(
                name: "DeathLuogoSpecificoId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "SenzaLivelloInferiore",
                table: "Luoghi");
        }
    }
}
