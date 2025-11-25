using Microsoft.EntityFrameworkCore;
using Revisu.Data;
using Revisu.Domain.Entities;
using Revisu.Infrastructure;
using System.Text.Json;

namespace Revisu.Infrastructure.Services
{
    public class TmdImportSeriesService
    {
        private readonly AppDbContext _db;
        private readonly HttpClient _http;
        private readonly TmdbSettings _settings;
        private readonly ILogger<TmdbImportService> _logger;

        private const string CheckpointFile = "tmdb_series_checkpoint.json";

        public TmdImportSeriesService(AppDbContext db, TmdbSettings settings, ILogger<TmdbImportService> logger)
        {
            _db = db;
            _settings = settings;
            _logger = logger;
            _http = new HttpClient();
        }

        public async Task<string> ImportAllSeriesAsync(CancellationToken cancellationToken = default)
        {
            int currentYear = DateTime.Now.Year;
            var (startYear, startPage) = LoadCheckpoint();

            var generosCache = await _db.Generos.ToDictionaryAsync(g => g.IdGeneroImdbSerie, g => g, cancellationToken);
            int imported = 0;

            for (int year = startYear; year <= currentYear; year++)
            {
                int page = year == startYear ? startPage : 1;
                bool hasMore = true;

                _logger.LogInformation($"Iniciando importação de séries do ano {year}, a partir da página {page}.");

                while (hasMore && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        string url = $"https://api.themoviedb.org/3/discover/tv?api_key={_settings.ApiKey}&language=pt-BR&sort_by=popularity.desc&page={page}&first_air_date_year={year}";
                        var json = await _http.GetStringAsync(url, cancellationToken);
                        var result = JsonSerializer.Deserialize<TMDbResponse>(json);

                        if (result?.results == null || result.results.Count == 0)
                            break;

                        foreach (var serie in result.results)
                        {
                            if (await _db.Obras.AnyAsync(o => o.IdTmdb == serie.id && o.Tipo == "Serie", cancellationToken))
                                continue;

                            var obra = new Obras
                            {
                                IdObra = Guid.NewGuid(),
                                IdTmdb = serie.id,
                                Nome = serie.name ?? "Sem título",
                                Sinopse = serie.overview ?? "",
                                Tipo = "Serie",
                                Imagem = serie.poster_path ?? "Sem poster",
                                DataLancamento = serie.first_air_date,
                                DataCadastro = DateTime.UtcNow
                            };

                            foreach (var genreId in serie.genre_ids)
                            {
                                if (generosCache.TryGetValue(genreId, out var genero))
                                    obra.Generos.Add(genero);
                            }

                            _db.Obras.Add(obra);
                            imported++;
                        }

                        await _db.SaveChangesAsync(cancellationToken);
                        SaveCheckpoint(year, page);
                        _logger.LogInformation($"Ano {year} - Página {page}/{result.total_pages} salva. Total: {imported}");

                        page++;
                        hasMore = page <= result.total_pages && page <= 500;
                        await Task.Delay(_settings.DelayBetweenRequestsMs, cancellationToken);
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogWarning($"Falha de rede. Tentando novamente em 5 segundos... {ex.Message}");
                        await Task.Delay(5000, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Erro ao importar página {page} do ano {year}: {ex.Message}");
                        await Task.Delay(2000, cancellationToken);
                    }
                }
            }

            _logger.LogInformation($"Importação concluída. Total de séries importadas: {imported}");
            File.Delete(CheckpointFile);
            return $"Importação concluída com sucesso. Total de séries: {imported}";
        }

        private (int Year, int Page) LoadCheckpoint()
        {
            if (!File.Exists(CheckpointFile))
                return (_settings.StartYear, 1);

            try
            {
                var checkpoint = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(CheckpointFile));
                return (checkpoint?.Year ?? _settings.StartYear, checkpoint?.Page ?? 1);
            }
            catch
            {
                return (_settings.StartYear, 1);
            }
        }

        private void SaveCheckpoint(int year, int page)
        {
            var checkpoint = new Checkpoint { Year = year, Page = page };
            File.WriteAllText(CheckpointFile, JsonSerializer.Serialize(checkpoint));
        }

        private class Checkpoint
        {
            public int Year { get; set; }
            public int Page { get; set; }
        }

        private class TMDbResponse
        {
            public List<TMDbSerie> results { get; set; } = new();
            public int total_pages { get; set; }
        }

        private class TMDbSerie
        {
            public int id { get; set; }
            public string name { get; set; }
            public string overview { get; set; }
            public string first_air_date { get; set; }
            public string poster_path { get; set; }
            public List<int> genre_ids { get; set; } = new();
        }
    }
}
