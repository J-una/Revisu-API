using Microsoft.EntityFrameworkCore;
using Revisu.Data;
using Revisu.Domain.Dtos;
using Revisu.Domain.Entities;
using Revisu.Infrastructure.Services.Biblioteca;

namespace Revisu.Infrastructure.Services.Quiz
{
    public class QuizService
    {
        private readonly AppDbContext _context;
        private readonly BibliotecaService _bibliotecaService;

        public QuizService(AppDbContext context, BibliotecaService bibliotecaService)
        {
            _context = context;
            _bibliotecaService = bibliotecaService;
        }

        public async Task<List<QuizDTO>> ListarObrasRandomizadosAsync()
        {
            var query = _context.Obras
                .Where(o =>
                    o.Populariedade != null &&
                    o.Populariedade > 10 &&
                    o.NotaMedia >= 5 &&
                    !string.IsNullOrWhiteSpace(o.Sinopse)
                    && o.Imagem != "Sem Poster"
                ).Include(o => o.Generos); 

            return await query
                .OrderBy(o => Guid.NewGuid())
                .Take(400)
                .Select(o => new QuizDTO(
                    o.IdObra,
                    o.Nome,
                    o.Imagem,
                    o.NotaMedia,
                    o.Tipo,
                    o.Generos.Select(g => g.Nome).ToList()
                ))
                .ToListAsync();
        }





        public async Task SalvarObrasDoQuizAsync(Guid idUsuario, List<Guid> idObras)
        {
            if (idObras == null || idObras.Count == 0)
                throw new Exception("Nenhuma obra foi enviada.");

            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == idUsuario);

            if (usuario == null)
                throw new Exception("Usuário não encontrado.");

            // Reutiliza a função padrão de salvar itens na biblioteca
            foreach (var idObra in idObras)
            {
                await _bibliotecaService.SalvarAsync(idUsuario, idObra, null);
            }

            // Marca como quiz concluído
            usuario.Quiz = true;
            _context.Usuarios.Update(usuario);

            await _context.SaveChangesAsync();
        }


    }
}
