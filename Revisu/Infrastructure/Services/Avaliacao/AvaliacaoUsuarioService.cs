using Microsoft.EntityFrameworkCore;
using Revisu.Data;
using Revisu.Domain.Dtos;
using Revisu.Domain.Entities;
using Revisu.Infrastructure.Repositories;

namespace Revisu.Infrastructure.Services.Avaliacao
{
    public class AvaliacaoUsuarioService
    {
        private readonly AppDbContext _context;
        private readonly AvaliacaoUsuarioRepository _repo;

        public AvaliacaoUsuarioService(AppDbContext context, AvaliacaoUsuarioRepository repo)
        {
            _context = context;
            _repo = repo;
        }

        // Buscar avaliação específica do usuário para uma obra
        public async Task<AvaliacaoUsuarioDTO?> BuscarAvaliacaoUsuarioAsync(Guid idUsuario, Guid idObra)
        {
            return await _context.AvaliacaoUsuario
                .Where(a => a.IdUsuario == idUsuario && a.IdObra == idObra)
                .Select(a => new AvaliacaoUsuarioDTO
                {
                    IdAvaliacaoUsuario = a.IdAvaliacaoUsuario,
                    Nota = a.Nota,
                    Comentario = a.Comentario
                })
                .FirstOrDefaultAsync();
        }


        public async Task<ResultadoAvaliacaoDTO> SalvarAvaliacaoObraAsync(
            Guid idUsuario,
            Guid idObra,
            CriarAvaliacaoDto model)
        {
            var resultado = new ResultadoAvaliacaoDTO();

            // Verifica se o usuário existe
            var usuario = await _context.Usuarios.FindAsync(idUsuario);
            if (usuario == null)
            {
                resultado.Sucesso = false;
                resultado.Mensagem = "Usuário não encontrado.";
                return resultado;
            }

            // Verifica se a obra existe
            var obra = await _context.Obras.FindAsync(idObra);
            if (obra == null)
            {
                resultado.Sucesso = false;
                resultado.Mensagem = "Obra não encontrada.";
                return resultado;
            }

            // Verifica se já existe avaliação
            var avaliacaoExistente = await _context.AvaliacaoUsuario
                .FirstOrDefaultAsync(a => a.IdUsuario == idUsuario && a.IdObra == idObra);

            if (avaliacaoExistente != null)
            {
                resultado.Sucesso = false;
                resultado.Mensagem = "O usuário já avaliou esta obra.";
                return resultado;
            }

            // Cria avaliação
            var avaliacao = new AvaliacaoUsuario
            {
                IdAvaliacaoUsuario = Guid.NewGuid(),
                IdUsuario = idUsuario,
                IdObra = idObra,
                Comentario = model.Comentario,
                Nota = model.Nota,
            };

            _context.AvaliacaoUsuario.Add(avaliacao);
            await _context.SaveChangesAsync();

            resultado.Sucesso = true;
            resultado.Avaliacao = avaliacao;
            resultado.Mensagem = "Avaliação salva com sucesso.";

            return resultado;
        }

        public async Task<bool> EditarAvaliacaoAsync(Guid id, EditarAvaliacaoDto dto)
        {
            var atual = await _repo.BuscarPorIdAsync(id);
            if (atual == null)
                return false;

            atual.Comentario = dto.Comentario;
            atual.Nota = dto.Nota;

            await _repo.AtualizarAsync(atual);
            return true;
        }

    }
}
