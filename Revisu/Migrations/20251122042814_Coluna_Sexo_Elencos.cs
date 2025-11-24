using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Revisu.Migrations
{
    /// <inheritdoc />
    public partial class Coluna_Sexo_Elencos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Sexo",
                table: "Elencos",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Sexo",
                table: "Elencos");
        }
    }
}
