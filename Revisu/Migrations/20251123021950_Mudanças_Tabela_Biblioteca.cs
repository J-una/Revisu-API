using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Revisu.Migrations
{
    /// <inheritdoc />
    public partial class Mudanças_Tabela_Biblioteca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Biblioteca_Elencos_IdElenco",
                table: "Biblioteca");

            migrationBuilder.DropForeignKey(
                name: "FK_Biblioteca_Obras_IdObra",
                table: "Biblioteca");

            migrationBuilder.DropForeignKey(
                name: "FK_Biblioteca_Usuarios_IdUsuario",
                table: "Biblioteca");

            migrationBuilder.AlterColumn<Guid>(
                name: "IdUsuario",
                table: "Biblioteca",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "IdObra",
                table: "Biblioteca",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "IdElenco",
                table: "Biblioteca",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_Biblioteca_Elencos_IdElenco",
                table: "Biblioteca",
                column: "IdElenco",
                principalTable: "Elencos",
                principalColumn: "IdElenco");

            migrationBuilder.AddForeignKey(
                name: "FK_Biblioteca_Obras_IdObra",
                table: "Biblioteca",
                column: "IdObra",
                principalTable: "Obras",
                principalColumn: "IdObra");

            migrationBuilder.AddForeignKey(
                name: "FK_Biblioteca_Usuarios_IdUsuario",
                table: "Biblioteca",
                column: "IdUsuario",
                principalTable: "Usuarios",
                principalColumn: "IdUsuario");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Biblioteca_Elencos_IdElenco",
                table: "Biblioteca");

            migrationBuilder.DropForeignKey(
                name: "FK_Biblioteca_Obras_IdObra",
                table: "Biblioteca");

            migrationBuilder.DropForeignKey(
                name: "FK_Biblioteca_Usuarios_IdUsuario",
                table: "Biblioteca");

            migrationBuilder.AlterColumn<Guid>(
                name: "IdUsuario",
                table: "Biblioteca",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "IdObra",
                table: "Biblioteca",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "IdElenco",
                table: "Biblioteca",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Biblioteca_Elencos_IdElenco",
                table: "Biblioteca",
                column: "IdElenco",
                principalTable: "Elencos",
                principalColumn: "IdElenco",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Biblioteca_Obras_IdObra",
                table: "Biblioteca",
                column: "IdObra",
                principalTable: "Obras",
                principalColumn: "IdObra",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Biblioteca_Usuarios_IdUsuario",
                table: "Biblioteca",
                column: "IdUsuario",
                principalTable: "Usuarios",
                principalColumn: "IdUsuario",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
