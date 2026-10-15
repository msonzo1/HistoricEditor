using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class AddGeography : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Stati",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stati", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Regioni",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", nullable: false),
                    StatoId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regioni", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Regioni_Stati_StatoId",
                        column: x => x.StatoId,
                        principalTable: "Stati",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Province",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", nullable: false),
                    RegioneId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Province", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Province_Regioni_RegioneId",
                        column: x => x.RegioneId,
                        principalTable: "Regioni",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Comuni",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", nullable: false),
                    ProvinciaId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comuni", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comuni_Province_ProvinciaId",
                        column: x => x.ProvinciaId,
                        principalTable: "Province",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Comuni_ProvinciaId_Nome",
                table: "Comuni",
                columns: new[] { "ProvinciaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Province_RegioneId_Nome",
                table: "Province",
                columns: new[] { "RegioneId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Regioni_StatoId_Nome",
                table: "Regioni",
                columns: new[] { "StatoId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stati_Nome",
                table: "Stati",
                column: "Nome",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Comuni");

            migrationBuilder.DropTable(
                name: "Province");

            migrationBuilder.DropTable(
                name: "Regioni");

            migrationBuilder.DropTable(
                name: "Stati");
        }
    }
}
