using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Revisu.Migrations
{
    /// <inheritdoc />
    public partial class Coluna_Popularidade_Tabela_Obras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "Poluraridade",
                table: "Obras",
                type: "real",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Poluraridade",
                table: "Obras");
        }
    }
}
