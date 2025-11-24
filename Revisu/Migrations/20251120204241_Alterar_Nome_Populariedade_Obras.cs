using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Revisu.Migrations
{
    public partial class Alterar_Nome_Populariedade_Obras : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Poluraridade",      // nome antigo
                table: "Obras",
                newName: "Populariedade"  // nome novo
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Populariedade",    // nome novo
                table: "Obras",
                newName: "Poluraridade"   // volta ao nome antigo
            );
        }
    }
}
