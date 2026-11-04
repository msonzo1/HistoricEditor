using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class EventPlace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LuogoId",
                table: "Eventi",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LuogoSpecificoId",
                table: "Eventi",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Eventi_LuogoId",
                table: "Eventi",
                column: "LuogoId");

            migrationBuilder.CreateIndex(
                name: "IX_Eventi_LuogoSpecificoId",
                table: "Eventi",
                column: "LuogoSpecificoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Eventi_LuoghiSpecifici_LuogoSpecificoId",
                table: "Eventi",
                column: "LuogoSpecificoId",
                principalTable: "LuoghiSpecifici",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Eventi_Luoghi_LuogoId",
                table: "Eventi",
                column: "LuogoId",
                principalTable: "Luoghi",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Eventi_LuoghiSpecifici_LuogoSpecificoId",
                table: "Eventi");

            migrationBuilder.DropForeignKey(
                name: "FK_Eventi_Luoghi_LuogoId",
                table: "Eventi");

            migrationBuilder.DropIndex(
                name: "IX_Eventi_LuogoId",
                table: "Eventi");

            migrationBuilder.DropIndex(
                name: "IX_Eventi_LuogoSpecificoId",
                table: "Eventi");

            migrationBuilder.DropColumn(
                name: "LuogoId",
                table: "Eventi");

            migrationBuilder.DropColumn(
                name: "LuogoSpecificoId",
                table: "Eventi");
        }
    }
}
