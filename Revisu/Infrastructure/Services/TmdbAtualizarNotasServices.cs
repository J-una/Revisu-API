using Microsoft.EntityFrameworkCore;
using Revisu.Data;
using Revisu.Domain.Entities;
using Revisu.Infrastructure;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Revisu.Infrastructure.Services
{
    public class TmdbAtualizarNotasService
    {
        private readonly AppDbContext _db;
        private readonly IHttpClientFactory _httpFactory;
        private readonly TmdbSettings _settings;
        private readonly ILogger<TmdbAtualizarNotasService> _logger;

        public TmdbAtualizarNotasService(AppDbContext db, IHttpClientFactory httpFactory, TmdbSettings settings, ILogger<TmdbAtualizarNotasService> logger)
        {
            _db = db;
            _httpFactory = httpFactory;
            _settings = settings;
            _logger = logger;
        }

        public async Task AtualizarNotasAsync(CancellationToken cancellationToken = default)
        {
            var obras = await _db.Obras
                .Where(o => o.IdTmdb != 0 && o.NotaMedia == 0 && o.Sinopse != "")
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var http = _httpFactory.CreateClient();
            var atualizadas = new ConcurrentBag<Obras>();
            var total = obras.Count;

            _logger.LogInformation($"Iniciando atualização de {total} obras...");

            // Controla o número máximo de requisições simultâneas
            var paralelismo = 20;

            await Parallel.ForEachAsync(obras, new ParallelOptions
            {
                MaxDegreeOfParallelism = paralelismo,
                CancellationToken = cancellationToken
            }, async (obra, ct) =>
            {
                try
                {
                    string tipoPath = obra.Tipo.ToLower() == "serie" ? "tv" : "movie";
                    string url = $"https://api.themoviedb.org/3/{tipoPath}/{obra.IdTmdb}?api_key={_settings.ApiKey}&language=pt-BR";

                    var json = await http.GetStringAsync(url, ct);
                    var tmdbData = JsonSerializer.Deserialize<TmdbDetalheResponse>(json);

                    if (tmdbData != null)
                    {
                        obra.NotaMedia = (float)Math.Round(tmdbData.vote_average, 1);
                        atualizadas.Add(obra);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Falha ao atualizar {obra.Nome}: {ex.Message}");
                }
            });

            // Atualiza tudo em batchs de 500
            _logger.LogInformation($"Salvando {atualizadas.Count} registros atualizados...");
            var obrasAtualizadas = atualizadas.ToList();
            for (int i = 0; i < obrasAtualizadas.Count; i += 500)
            {
                var batch = obrasAtualizadas.Skip(i).Take(500).ToList();
                _db.Obras.UpdateRange(batch);
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation($"Batch {i / 500 + 1} salvo ({batch.Count} obras)");
            }

            _logger.LogInformation($"Processo concluído. Total de notas atualizadas: {atualizadas.Count}");
        }

        private class TmdbDetalheResponse
        {
            public double vote_average { get; set; }
        }
    }
}
