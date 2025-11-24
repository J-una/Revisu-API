using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Revisu.Migrations
{
    /// <inheritdoc />
    public partial class Tabela_Biblioteca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Biblioteca",
                columns: table => new
                {
                    IdBiblioteca = table.Column<Guid>(nullable: false),
                    IdUsuario = table.Column<Guid>(nullable: false),
                    IdObra = table.Column<Guid>(nullable: false),
                    IdElenco = table.Column<Guid>(nullable: false),
                    DataCadastro = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Biblioteca", x => x.IdBiblioteca);

                    table.ForeignKey(
                        name: "FK_Biblioteca_Usuarios_IdUsuario",
                        column: x => x.IdUsuario,
                        principalTable: "Usuarios",
                        principalColumn: "IdUsuario",
                        onDelete: ReferentialAction.Cascade);

                    table.ForeignKey(
                        name: "FK_Biblioteca_Obras_IdObra",
                        column: x => x.IdObra,
                        principalTable: "Obras",
                        principalColumn: "IdObra",
                        onDelete: ReferentialAction.Cascade);

                    table.ForeignKey(
                        name: "FK_Biblioteca_Elencos_IdElenco",
                        column: x => x.IdElenco,
                        principalTable: "Elencos",
                        principalColumn: "IdElenco",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Biblioteca_IdUsuario",
                table: "Biblioteca",
                column: "IdUsuario");

            migrationBuilder.CreateIndex(
                name: "IX_Biblioteca_IdObra",
                table: "Biblioteca",
                column: "IdObra");

            migrationBuilder.CreateIndex(
                name: "IX_Biblioteca_IdElenco",
                table: "Biblioteca",
                column: "IdElenco");
        }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Biblioteca");
        }
    }
}
