using Microsoft.EntityFrameworkCore;
using Revisu.Data;
using Revisu.Domain.Dtos;
using Revisu.Domain.Entities;

namespace Revisu.Services.Quiz
{
    public class QuizService
    {
        private readonly AppDbContext _context;

        public QuizService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<QuizDTO>> ListarFilmesRandomizadosAsync()
        {
            return await _context.Obras
                .Where(o =>
                    o.Tipo == "Filme" &&
                    o.Populariedade != null &&
                    o.Populariedade > 10 &&         
                    o.NotaMedia >= 5 &&
                    !string.IsNullOrWhiteSpace(o.Sinopse)
                )
                .OrderBy(o => Guid.NewGuid()) 
                .Take(4)
                .Select(o => new QuizDTO(
                    o.IdObra,
                    o.Nome,
                    o.Imagem
                ))
                .ToListAsync();
        }

        public async Task<List<QuizDTO>> ListarSeriesRandomizadasAsync()
        {
            return await _context.Obras
                .Where(o =>
                    o.Tipo == "Serie" &&
                    o.Populariedade != null &&
                    o.Populariedade > 10 &&         
                    o.NotaMedia >= 5 &&
                    !string.IsNullOrWhiteSpace(o.Sinopse)
                )
                .OrderBy(o => Guid.NewGuid()) 
                .Take(4)
                .Select(o => new QuizDTO(
                    o.IdObra,
                    o.Nome,
                    o.Imagem
                ))
                .ToListAsync();
        }

        public async Task SalvarObraNaBibliotecaAsync(Guid idUsuario, List<Guid> idObras)
        {
            foreach (var idObra in idObras)
            {
                // Verifica se a obra existe
                var obraExiste = await _context.Obras.AnyAsync(o => o.IdObra == idObra);

                if (!obraExiste)
                    throw new Exception($"Obra não encontrada: {idObra}");

                // Verifica se já está salva
                var jaExiste = await _context.Biblioteca
                    .AnyAsync(b => b.IdUsuario == idUsuario && b.IdObra == idObra && b.Excluido == false);

                if (jaExiste)
                    continue;

                // Cria o registro
                var biblioteca = new Domain.Entities.Biblioteca
                {
                    IdBiblioteca = Guid.NewGuid(),
                    IdUsuario = idUsuario,
                    IdObra = idObra,
                    IdElenco = null,
                    DataCadastro = DateTime.UtcNow
                };

                _context.Biblioteca.Add(biblioteca);
            }

            // Faz apenas um save no final (Melhor performance)
            await _context.SaveChangesAsync();
        }


    }
}
