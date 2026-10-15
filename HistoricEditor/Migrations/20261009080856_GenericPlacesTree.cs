using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class GenericPlacesTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_People_Comuni_BirthComuneId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_People_Comuni_DeathComuneId",
                table: "People");

            migrationBuilder.DropTable(
                name: "Comuni");

            migrationBuilder.DropTable(
                name: "Province");

            migrationBuilder.DropTable(
                name: "Regioni");

            migrationBuilder.DropTable(
                name: "Stati");

            migrationBuilder.RenameColumn(
                name: "DeathComuneId",
                table: "People",
                newName: "DeathLuogoId");

            migrationBuilder.RenameColumn(
                name: "BirthComuneId",
                table: "People",
                newName: "BirthLuogoId");

            migrationBuilder.RenameIndex(
                name: "IX_People_DeathComuneId",
                table: "People",
                newName: "IX_People_DeathLuogoId");

            migrationBuilder.RenameIndex(
                name: "IX_People_BirthComuneId",
                table: "People",
                newName: "IX_People_BirthLuogoId");

            migrationBuilder.CreateTable(
                name: "Luoghi",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", nullable: false),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Latitudine = table.Column<double>(type: "REAL", nullable: true),
                    Longitudine = table.Column<double>(type: "REAL", nullable: true),
                    Storico = table.Column<bool>(type: "INTEGER", nullable: false),
                    Periodo = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Luoghi", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Luoghi_Luoghi_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Luoghi",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Luoghi_ParentId_Tipo_Nome",
                table: "Luoghi",
                columns: new[] { "ParentId", "Tipo", "Nome" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_People_Luoghi_BirthLuogoId",
                table: "People",
                column: "BirthLuogoId",
                principalTable: "Luoghi",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_People_Luoghi_DeathLuogoId",
                table: "People",
                column: "DeathLuogoId",
                principalTable: "Luoghi",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_People_Luoghi_BirthLuogoId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_People_Luoghi_DeathLuogoId",
                table: "People");

            migrationBuilder.DropTable(
                name: "Luoghi");

            migrationBuilder.RenameColumn(
                name: "DeathLuogoId",
                table: "People",
                newName: "DeathComuneId");

            migrationBuilder.RenameColumn(
                name: "BirthLuogoId",
                table: "People",
                newName: "BirthComuneId");

            migrationBuilder.RenameIndex(
                name: "IX_People_DeathLuogoId",
                table: "People",
                newName: "IX_People_DeathComuneId");

            migrationBuilder.RenameIndex(
                name: "IX_People_BirthLuogoId",
                table: "People",
                newName: "IX_People_BirthComuneId");

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
                    StatoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", nullable: false)
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
                    RegioneId = table.Column<int>(type: "INTEGER", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", nullable: false)
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
                    ProvinciaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false),
                    Periodo = table.Column<string>(type: "TEXT", nullable: false),
                    Storico = table.Column<bool>(type: "INTEGER", nullable: false)
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

            migrationBuilder.AddForeignKey(
                name: "FK_People_Comuni_BirthComuneId",
                table: "People",
                column: "BirthComuneId",
                principalTable: "Comuni",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_People_Comuni_DeathComuneId",
                table: "People",
                column: "DeathComuneId",
                principalTable: "Comuni",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
