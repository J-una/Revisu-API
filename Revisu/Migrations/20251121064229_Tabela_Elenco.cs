using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Revisu.Migrations
{
    /// <inheritdoc />
    public partial class Tabela_Elenco : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Elencos",
                columns: table => new
                {
                    IdElenco = table.Column<Guid>(type: "uuid", nullable: false),
                    IdTmdb = table.Column<int>(type: "integer", nullable: false),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    Foto = table.Column<string>(type: "text", nullable: true),
                    Cargo = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Elencos", x => x.IdElenco);
                });

            migrationBuilder.CreateTable(
                name: "ElencoObras",
                columns: table => new
                {
                    ElencoIdElenco = table.Column<Guid>(type: "uuid", nullable: false),
                    ObrasIdObra = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElencoObras", x => new { x.ElencoIdElenco, x.ObrasIdObra });
                    table.ForeignKey(
                        name: "FK_ElencoObras_Elencos_ElencoIdElenco",
                        column: x => x.ElencoIdElenco,
                        principalTable: "Elencos",
                        principalColumn: "IdElenco",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ElencoObras_Obras_ObrasIdObra",
                        column: x => x.ObrasIdObra,
                        principalTable: "Obras",
                        principalColumn: "IdObra",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElencoObras_ObrasIdObra",
                table: "ElencoObras",
                column: "ObrasIdObra");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElencoObras");

            migrationBuilder.DropTable(
                name: "Elencos");
        }
    }
}
