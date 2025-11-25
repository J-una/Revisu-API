//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Logging;
//using Revisu.Data;
//using Revisu.Domain.Entities;

//namespace Revisu.Services
//{
//    public class RecomendacaoHibridaService
//    {
//        private readonly AppDbContext _db;
//        private readonly ILogger<RecomendacaoHibridaService> _logger;

//        public RecomendacaoHibridaService(AppDbContext db, ILogger<RecomendacaoHibridaService> logger)
//        {
//            _db = db;
//            _logger = logger;
//        }

//        public async Task<List<Obras>> RecomendarAsync(Guid idUsuario)
//        {
//            try
//            {
//                // 🔹 Passo 1: busca histórico do usuário
//                var biblioteca = await _db.Biblioteca
//                    .Include(b => b.Obras)
//                        .ThenInclude(o => o.Generos)
//                    .Where(b => b.IdUsuario == idUsuario)
//                    .ToListAsync();

//                // Se o usuário não assistiu nada ainda, recomenda obras com alta nota média
//                if (!biblioteca.Any())
//                {
//                    _logger.LogInformation("Usuário sem histórico — retornando obras mais bem avaliadas.");

//                    return await _db.Obras
//                        .Include(o => o.Generos)
//                        .Where(o => o.Sinopse != "")
//                        .OrderByDescending(o => o.NotaMedia)
//                        .Take(20)
//                        .ToListAsync();
//                }

//                // 🔹 Passo 2: identifica os gêneros mais assistidos
//                var generosPreferidos = biblioteca
//                    .SelectMany(b => b.Obras.Generos)
//                    .GroupBy(g => g.Nome)
//                    .OrderByDescending(g => g.Count())
//                    .Take(3)
//                    .Select(g => g.Key)
//                    .ToList();

//                _logger.LogInformation("Gêneros preferidos detectados: {generos}", string.Join(", ", generosPreferidos));

//                // 🔹 Passo 3: busca as obras não assistidas
//                var obrasNaoVistas = await _db.Obras
//                    .Include(o => o.Generos)
//                    .Where(o => !_db.Biblioteca.Any(b => b.IdUsuario == idUsuario && b.IdObra == o.IdObra))
//                    .ToListAsync();

//                // 🔹 Passo 4: monta dataset simplificado (gêneros + notas médias)
//                var dataset = obrasNaoVistas.Select(o => new
//                {
//                    Obra = o,
//                    Generos = o.Generos.Select(g => g.Nome).ToList(),
//                    Nota = o.NotaMedia
//                }).ToList();

//                // 🔹 Passo 5: cria "árvore de decisão" simples baseada em gênero e nota
//                var recomendadas = dataset
//                    .Where(d =>
//                        d.Generos.Any(g => generosPreferidos.Contains(g)) && // gênero preferido
//                        d.Nota >= 6.5) // filtro de qualidade mínima
//                    .OrderByDescending(d => d.Nota)
//                    .Take(20)
//                    .Select(d => d.Obra)
//                    .ToList();

//                // 🔹 Se não encontrou nada dentro dos gêneros preferidos, recomenda as mais populares
//                if (!recomendadas.Any())
//                {
//                    recomendadas = await _db.Obras
//                        .Include(o => o.Generos)
//                        .OrderByDescending(o => o.NotaMedia)
//                        .Take(10)
//                        .ToListAsync();
//                }

//                return recomendadas;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Erro ao gerar recomendações para o usuário {idUsuario}", idUsuario);
//                return new List<Obras>();
//            }
//        }
//    }
//}
