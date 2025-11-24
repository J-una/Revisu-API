using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Revisu.Migrations
{
    /// <inheritdoc />
    public partial class ColunaTipoObras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tipo",
                table: "Obras",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tipo",
                table: "Obras");
        }
    }
}
