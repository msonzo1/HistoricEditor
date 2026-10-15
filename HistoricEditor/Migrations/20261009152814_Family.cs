using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class Family : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FatherId",
                table: "People",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MotherId",
                table: "People",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sesso",
                table: "People",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Unioni",
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
                    table.PrimaryKey("PK_Unioni", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Unioni_People_Persona1Id",
                        column: x => x.Persona1Id,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Unioni_People_Persona2Id",
                        column: x => x.Persona2Id,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_People_FatherId",
                table: "People",
                column: "FatherId");

            migrationBuilder.CreateIndex(
                name: "IX_People_MotherId",
                table: "People",
                column: "MotherId");

            migrationBuilder.CreateIndex(
                name: "IX_Unioni_Persona1Id",
                table: "Unioni",
                column: "Persona1Id");

            migrationBuilder.CreateIndex(
                name: "IX_Unioni_Persona2Id",
                table: "Unioni",
                column: "Persona2Id");

            migrationBuilder.AddForeignKey(
                name: "FK_People_People_FatherId",
                table: "People",
                column: "FatherId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_People_People_MotherId",
                table: "People",
                column: "MotherId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_People_People_FatherId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_People_People_MotherId",
                table: "People");

            migrationBuilder.DropTable(
                name: "Unioni");

            migrationBuilder.DropIndex(
                name: "IX_People_FatherId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_MotherId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "FatherId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "MotherId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Sesso",
                table: "People");
        }
    }
}
