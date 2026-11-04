using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistoricEditor.Migrations
{
    /// <inheritdoc />
    public partial class WikipediaLeads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WikipediaLeads",
                table: "People",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WikipediaLeads",
                table: "People");
        }
    }
}
