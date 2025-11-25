using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Revisu.Data;
using Revisu.Domain.Entities;
using Revisu.Infrastructure;
using System.Text.Json;

namespace Revisu.Infrastructure.Services
{
    public class AtualizarPopularidadeTmdbService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly TmdbSettings _settings;
        private readonly HttpClient _http;
        private readonly ILogger<AtualizarPopularidadeTmdbService> _logger;

        public AtualizarPopularidadeTmdbService(
            IDbContextFactory<AppDbContext> contextFactory,
            TmdbSettings settings,
            ILogger<AtualizarPopularidadeTmdbService> logger)
        {
            _contextFactory = contextFactory;
            _settings = settings;
            _logger = logger;
            _http = new HttpClient();
        }

        public async Task<string> AtualizarPopularidadeAsync(CancellationToken cancellationToken = default)
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // Carregar apenas obras com sinopse preenchida
            var obras = await db.Obras
                .Where(o => !string.IsNullOrWhiteSpace(o.Sinopse))
                .Select(o => new { o.IdObra, o.IdTmdb, o.Tipo })
                .ToListAsync(cancellationToken);

            int atualizados = 0;

            var semaphore = new SemaphoreSlim(10); // limite de 10 threads
            var tasks = new List<Task>();

            foreach (var obra in obras)
            {
                await semaphore.WaitAsync(cancellationToken);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await AtualizarObra(obra.IdObra, obra.IdTmdb, obra.Tipo, cancellationToken);
                        Interlocked.Increment(ref atualizados);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erro ao atualizar popularidade da obra {id}", obra.IdTmdb);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);

            return $"Popularidade atualizada para {atualizados} obras.";
        }



        private async Task AtualizarObra(Guid idObra, int idTmdb, string tipo, CancellationToken token)
        {
            string tipoUrl = tipo == "Filme" ? "movie" : "tv";
            string url = $"https://api.themoviedb.org/3/{tipoUrl}/{idTmdb}?api_key={_settings.ApiKey}&language=pt-BR";

            var response = await _http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TMDB retornou {status} para {url}", response.StatusCode, url);
                return;
            }

            var json = await response.Content.ReadAsStringAsync(token);
            var model = JsonSerializer.Deserialize<TmdbResponse>(json);

            if (model?.popularity == null)
                return;

            // novo contexto por task — evita deadlocks
            await using var db = await _contextFactory.CreateDbContextAsync(token);

            var obra = await db.Obras.FirstOrDefaultAsync(o => o.IdObra == idObra, token);
            if (obra == null) return;

            obra.Populariedade = model.popularity.Value;

            await db.SaveChangesAsync(token);

            await Task.Delay(_settings.DelayBetweenRequestsMs, token);
        }




        private class TmdbResponse
        {
            public float? popularity { get; set; }
        }
    }
}
