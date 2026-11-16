using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class GentesRami : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Gens",
                table: "People");

            migrationBuilder.DropColumn(
                name: "GensAdozione",
                table: "People");

            migrationBuilder.AddColumn<int>(
                name: "GensAdozioneId",
                table: "People",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GensId",
                table: "People",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RamoGensAdozioneId",
                table: "People",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RamoGensId",
                table: "People",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Gentes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Nomen = table.Column<string>(type: "TEXT", nullable: false),
                    Plurale = table.Column<string>(type: "TEXT", nullable: false),
                    NomeItaliano = table.Column<string>(type: "TEXT", nullable: false),
                    PluraleItaliano = table.Column<string>(type: "TEXT", nullable: false),
                    WikidataId = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Gentes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RamiGens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GensId = table.Column<int>(type: "INTEGER", nullable: false),
                    Cognomen = table.Column<string>(type: "TEXT", nullable: false),
                    Plurale = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    NomeItaliano = table.Column<string>(type: "TEXT", nullable: false),
                    WikidataId = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RamiGens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RamiGens_Gentes_GensId",
                        column: x => x.GensId,
                        principalTable: "Gentes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_People_GensAdozioneId",
                table: "People",
                column: "GensAdozioneId");

            migrationBuilder.CreateIndex(
                name: "IX_People_GensId",
                table: "People",
                column: "GensId");

            migrationBuilder.CreateIndex(
                name: "IX_People_RamoGensAdozioneId",
                table: "People",
                column: "RamoGensAdozioneId");

            migrationBuilder.CreateIndex(
                name: "IX_People_RamoGensId",
                table: "People",
                column: "RamoGensId");

            migrationBuilder.CreateIndex(
                name: "IX_Gentes_Nome",
                table: "Gentes",
                column: "Nome");

            migrationBuilder.CreateIndex(
                name: "IX_Gentes_WikidataId",
                table: "Gentes",
                column: "WikidataId");

            migrationBuilder.CreateIndex(
                name: "IX_RamiGens_GensId_Plurale",
                table: "RamiGens",
                columns: new[] { "GensId", "Plurale" });

            migrationBuilder.AddForeignKey(
                name: "FK_People_Gentes_GensAdozioneId",
                table: "People",
                column: "GensAdozioneId",
                principalTable: "Gentes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_People_Gentes_GensId",
                table: "People",
                column: "GensId",
                principalTable: "Gentes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_People_RamiGens_RamoGensAdozioneId",
                table: "People",
                column: "RamoGensAdozioneId",
                principalTable: "RamiGens",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_People_RamiGens_RamoGensId",
                table: "People",
                column: "RamoGensId",
                principalTable: "RamiGens",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_People_Gentes_GensAdozioneId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_People_Gentes_GensId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_People_RamiGens_RamoGensAdozioneId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_People_RamiGens_RamoGensId",
                table: "People");

            migrationBuilder.DropTable(
                name: "RamiGens");

            migrationBuilder.DropTable(
                name: "Gentes");

            migrationBuilder.DropIndex(
                name: "IX_People_GensAdozioneId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_GensId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_RamoGensAdozioneId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_RamoGensId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "GensAdozioneId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "GensId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "RamoGensAdozioneId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "RamoGensId",
                table: "People");

            migrationBuilder.AddColumn<string>(
                name: "Gens",
                table: "People",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GensAdozione",
                table: "People",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
