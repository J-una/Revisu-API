using Microsoft.EntityFrameworkCore;
using Revisu.Data;
using Revisu.Domain.Entities;

namespace Revisu.Infrastructure.Repositories
{
    public class AvaliacaoUsuarioRepository
    {
        private readonly AppDbContext _context;

        public AvaliacaoUsuarioRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<AvaliacaoUsuario>> ListarAsync()
        {
            return await _context.AvaliacaoUsuario
                .Include(a => a.Usuarios)
                .Include(a => a.Filmes)
                .ToListAsync();
        }

        public async Task<AvaliacaoUsuario?> BuscarPorIdAsync(Guid id)
        {
            return await _context.AvaliacaoUsuario
                .Include(a => a.Usuarios)
                .Include(a => a.Filmes)
                .FirstOrDefaultAsync(a => a.IdAvaliacaoUsuario == id);
        }

        public async Task<AvaliacaoUsuario> CriarAsync(AvaliacaoUsuario entity)
        {
            entity.IdAvaliacaoUsuario = Guid.NewGuid();
            _context.AvaliacaoUsuario.Add(entity);
            await _context.SaveChangesAsync();
            return entity;
        }

        public async Task<bool> AtualizarAsync(AvaliacaoUsuario entity)
        {
            var atual = await _context.AvaliacaoUsuario.FindAsync(entity.IdAvaliacaoUsuario);
            if (atual == null) return false;

            atual.IdUsuario = entity.IdUsuario;
            atual.IdObra = entity.IdObra;
            atual.Comentario = entity.Comentario;
            atual.Nota = entity.Nota;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeletarAsync(Guid id)
        {
            var atual = await _context.AvaliacaoUsuario.FindAsync(id);
            if (atual == null) return false;

            _context.AvaliacaoUsuario.Remove(atual);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<AvaliacaoUsuario?> BuscarPorParametrosAsync(Guid idUsuario, Guid idObra)
        {
            return await _context.AvaliacaoUsuario
                .FirstOrDefaultAsync(a =>
                    a.IdUsuario == idUsuario &&
                    a.IdObra == idObra
                );
        }
    }
}
