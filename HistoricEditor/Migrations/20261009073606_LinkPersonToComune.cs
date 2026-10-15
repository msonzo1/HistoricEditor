using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class LinkPersonToComune : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BirthComuneId",
                table: "People",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeathComuneId",
                table: "People",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "Comuni",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Periodo",
                table: "Comuni",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Storico",
                table: "Comuni",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_People_BirthComuneId",
                table: "People",
                column: "BirthComuneId");

            migrationBuilder.CreateIndex(
                name: "IX_People_DeathComuneId",
                table: "People",
                column: "DeathComuneId");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_People_Comuni_BirthComuneId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_People_Comuni_DeathComuneId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_BirthComuneId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_DeathComuneId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "BirthComuneId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "DeathComuneId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "Comuni");

            migrationBuilder.DropColumn(
                name: "Periodo",
                table: "Comuni");

            migrationBuilder.DropColumn(
                name: "Storico",
                table: "Comuni");
        }
    }
}
