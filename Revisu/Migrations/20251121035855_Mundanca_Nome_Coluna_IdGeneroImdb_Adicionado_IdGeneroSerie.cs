using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Revisu.Migrations
{
    public partial class Mundanca_Nome_Coluna_IdGeneroImdb_Adicionado_IdGeneroSerie : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Renomeia a coluna existente
            migrationBuilder.RenameColumn(
                name: "IdGeneroImdb",
                table: "Generos",
                newName: "IdGeneroImdbMovie"
            );

            // Altera para permitir null
            migrationBuilder.AlterColumn<int>(
                name: "IdGeneroImdbMovie",
                table: "Generos",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer"
            );

            // Nova coluna
            migrationBuilder.AddColumn<int>(
                name: "IdGeneroImdbSerie",
                table: "Generos",
                type: "integer",
                nullable: true
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove a coluna nova
            migrationBuilder.DropColumn(
                name: "IdGeneroImdbSerie",
                table: "Generos"
            );

            // Volta a ser NOT NULL
            migrationBuilder.AlterColumn<int>(
                name: "IdGeneroImdbMovie",
                table: "Generos",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true
            );

            // Renomeia de volta ao nome original
            migrationBuilder.RenameColumn(
                name: "IdGeneroImdbMovie",
                table: "Generos",
                newName: "IdGeneroImdb"
            );
        }
    }
}
