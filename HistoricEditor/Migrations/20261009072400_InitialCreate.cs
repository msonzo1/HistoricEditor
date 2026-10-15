using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Suffix = table.Column<string>(type: "TEXT", nullable: false),
                    Family = table.Column<string>(type: "TEXT", nullable: false),
                    Birth = table.Column<string>(type: "TEXT", nullable: false),
                    Death = table.Column<string>(type: "TEXT", nullable: false),
                    BirthPlace = table.Column<string>(type: "TEXT", nullable: false),
                    DeathPlace = table.Column<string>(type: "TEXT", nullable: false),
                    WikipediaTitle = table.Column<string>(type: "TEXT", nullable: false),
                    WikipediaUrl = table.Column<string>(type: "TEXT", nullable: false),
                    WikipediaExtract = table.Column<string>(type: "TEXT", nullable: false),
                    WikidataId = table.Column<string>(type: "TEXT", nullable: false),
                    BirthDateEvidence = table.Column<string>(type: "TEXT", nullable: true),
                    BirthPlaces = table.Column<string>(type: "TEXT", nullable: true),
                    DeathDateEvidence = table.Column<string>(type: "TEXT", nullable: true),
                    DeathPlaces = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_People_BirthPlace",
                table: "People",
                column: "BirthPlace");

            migrationBuilder.CreateIndex(
                name: "IX_People_DeathPlace",
                table: "People",
                column: "DeathPlace");

            migrationBuilder.CreateIndex(
                name: "IX_People_Family",
                table: "People",
                column: "Family");

            migrationBuilder.CreateIndex(
                name: "IX_People_Name",
                table: "People",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "People");
        }
    }
}
