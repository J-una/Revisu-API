using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Revisu.Migrations
{
    /// <inheritdoc />
    public partial class Tabela_Avaliacao_Usuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AvaliacaoUsuario",
                columns: table => new
                {
                    IdAvaliacaoUsuario = table.Column<Guid>(type: "uuid", nullable: false),
                    IdUsuario = table.Column<Guid>(type: "uuid", nullable: false),
                    IdObra = table.Column<Guid>(type: "uuid", nullable: true),
                    Comentario = table.Column<string>(type: "text", nullable: false),
                    Nota = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvaliacaoUsuario", x => x.IdAvaliacaoUsuario);
                    table.ForeignKey(
                        name: "FK_AvaliacaoUsuario_Obras_IdObra",
                        column: x => x.IdObra,
                        principalTable: "Obras",
                        principalColumn: "IdObra");
                    table.ForeignKey(
                        name: "FK_AvaliacaoUsuario_Usuarios_IdUsuario",
                        column: x => x.IdUsuario,
                        principalTable: "Usuarios",
                        principalColumn: "IdUsuario",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AvaliacaoUsuario_IdObra",
                table: "AvaliacaoUsuario",
                column: "IdObra");

            migrationBuilder.CreateIndex(
                name: "IX_AvaliacaoUsuario_IdUsuario",
                table: "AvaliacaoUsuario",
                column: "IdUsuario");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvaliacaoUsuario");
        }
    }
}
