using Microsoft.EntityFrameworkCore;
using Revisu.Data;
using Revisu.Domain.Dtos;
using Revisu.Domain.Entities;

namespace Revisu.Services.Biblioteca
{
    public class BibliotecaService
    {
        private readonly AppDbContext _context;

        public BibliotecaService(AppDbContext context)
        {
            _context = context;
        }

        public async Task SalvarAsync(Guid idUsuario, Guid? idObra, Guid? idElenco)
        {
            if (idObra == null && idElenco == null)
                throw new Exception("É necessário informar IdObra ou IdElenco");


            var existente = await _context.Biblioteca
                .FirstOrDefaultAsync(x =>
                    x.IdUsuario == idUsuario &&
                    x.IdObra == idObra &&
                    x.IdElenco == idElenco
                );

            if (existente != null && !existente.Excluido)
                return;


            if (existente != null && existente.Excluido)
            {
                existente.Excluido = false;
                existente.DataCadastro = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return;
            }

            // Criar novo registro
            var biblioteca = new Domain.Entities.Biblioteca
            {
                IdBiblioteca = Guid.NewGuid(),
                IdUsuario = idUsuario,
                IdObra = idObra,
                IdElenco = idElenco,
                DataCadastro = DateTime.UtcNow,
                Excluido = false
            };

            _context.Biblioteca.Add(biblioteca);
            await _context.SaveChangesAsync();
        }


        public async Task RemoverAsync(Guid idUsuario, Guid? idObra, Guid? idElenco)
        {
            var item = await _context.Biblioteca
                .FirstOrDefaultAsync(x =>
                    x.IdUsuario == idUsuario &&
                    x.IdObra == idObra &&
                    x.IdElenco == idElenco &&
                    !x.Excluido
                );

            if (item == null)
                return;

            item.Excluido = true;

            await _context.SaveChangesAsync();
        }
    }
}
