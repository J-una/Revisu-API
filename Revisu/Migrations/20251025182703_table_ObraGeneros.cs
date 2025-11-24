using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Revisu.Migrations
{
    /// <inheritdoc />
    public partial class table_ObraGeneros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Genero",
                table: "Obras");

            migrationBuilder.CreateTable(
                name: "GenerosObras",
                columns: table => new
                {
                    GenerosIdGenero = table.Column<Guid>(type: "uuid", nullable: false),
                    ObrasIdObra = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenerosObras", x => new { x.GenerosIdGenero, x.ObrasIdObra });
                    table.ForeignKey(
                        name: "FK_GenerosObras_Generos_GenerosIdGenero",
                        column: x => x.GenerosIdGenero,
                        principalTable: "Generos",
                        principalColumn: "IdGenero",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GenerosObras_Obras_ObrasIdObra",
                        column: x => x.ObrasIdObra,
                        principalTable: "Obras",
                        principalColumn: "IdObra",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GenerosObras_ObrasIdObra",
                table: "GenerosObras",
                column: "ObrasIdObra");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GenerosObras");

            migrationBuilder.AddColumn<string>(
                name: "Genero",
                table: "Obras",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
