using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Revisu.Data;
using Revisu.Domain.Entities;
using Revisu.Infrastructure;
using System.Text.Json;

namespace Revisu.Infrastructure.Services
{
    public class AtualizarObrasTmdb
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly HttpClient _http;
        private readonly TmdbSettings _settings;
        private readonly RottenTomatoesImportService _rottenService;
        private readonly ILogger<AtualizarObrasTmdb> _logger;

        public AtualizarObrasTmdb(
            IDbContextFactory<AppDbContext> contextFactory,
            TmdbSettings settings,
            RottenTomatoesImportService rottenService,
            ILogger<AtualizarObrasTmdb> logger)
        {
            _contextFactory = contextFactory;
            _settings = settings;
            _rottenService = rottenService;
            _logger = logger;
            _http = new HttpClient();
        }

        public async Task<string> SyncRecentChangesAsync(DateTime? startDate = null, CancellationToken cancellationToken = default)
        {
            startDate ??= DateTime.UtcNow.AddDays(-7);
            string endDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            string startDateStr = startDate.Value.ToString("yyyy-MM-dd");

            _logger.LogInformation("Sincronizando mudanças recentes ({startDateStr} → {endDate})...", startDateStr, endDate);

            // Busca mudanças no TMDb
            var newMovieIds = await BuscarMudancas("movie", startDateStr, endDate, cancellationToken);
            var newTvIds = await BuscarMudancas("tv", startDateStr, endDate, cancellationToken);

            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var existingMovies = await db.Obras
                .Where(o => o.Tipo == "Filme")
                .Select(o => o.IdTmdb)
                .ToListAsync(cancellationToken);

            var existingSeries = await db.Obras
                .Where(o => o.Tipo == "Serie")
                .Select(o => o.IdTmdb)
                .ToListAsync(cancellationToken);

            var existingMoviesSet = new HashSet<int>(existingMovies);
            var existingSeriesSet = new HashSet<int>(existingSeries);

            _logger.LogInformation("IDs carregados: {filmes} filmes, {series} séries", existingMoviesSet.Count, existingSeriesSet.Count);

            var novosFilmes = newMovieIds.Where(id => !existingMoviesSet.Contains(id)).ToList();
            var novasSeries = newTvIds.Where(id => !existingSeriesSet.Contains(id)).ToList();

            _logger.LogInformation("Novas obras: {filmes} filmes, {series} séries", novosFilmes.Count, novasSeries.Count);

            // Limite de 10 conexões simultâneas para evitar "too many clients"
            var semaphore = new SemaphoreSlim(10);
            var tasks = new List<Task<bool>>();

            // Função auxiliar para processar filmes e séries
            async Task AgendarInsercao(IEnumerable<int> ids, string tipo)
            {
                foreach (var id in ids)
                {
                    await semaphore.WaitAsync(cancellationToken);
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            return await InserirObraSeNova(id, tipo, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Erro ao inserir {tipo} ID {id}", tipo, id);
                            return false;
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, cancellationToken));
                }
            }

            await AgendarInsercao(novosFilmes, "Filme");
            await AgendarInsercao(novasSeries, "Serie");

            var results = await Task.WhenAll(tasks);
            var novos = results.Count(r => r);

            _logger.LogInformation("Sincronização concluída. Novas obras: {novos}", novos);

            // Atualiza avaliações somente se houve novos filmes
            if (novosFilmes.Count > 0)
            {
                _logger.LogInformation("Importando avaliações do RottenTomatoes...");
                await _rottenService.ImportarAvaliacoesEmLotesAsync(cancellationToken);
            }

            return $"Sincronização concluída — {novos} novas obras ({novosFilmes.Count} filmes, {novasSeries.Count} séries).";
        }


        private async Task<HashSet<int>> BuscarMudancas(string tipo, string startDateStr, string endDate, CancellationToken cancellationToken)
        {
            var url = $"https://api.themoviedb.org/3/{tipo}/changes?api_key={_settings.ApiKey}&start_date={startDateStr}&end_date={endDate}&page=1";
            var ids = new HashSet<int>();

            while (!string.IsNullOrEmpty(url))
            {
                var json = await _http.GetStringAsync(url, cancellationToken);
                var result = JsonSerializer.Deserialize<ChangeResponse>(json);

                if (result?.results == null) break;
                foreach (var item in result.results)
                    ids.Add(item.id);

                if (result.page < result.total_pages)
                    url = $"https://api.themoviedb.org/3/{tipo}/changes?api_key={_settings.ApiKey}&start_date={startDateStr}&end_date={endDate}&page={result.page + 1}";
                else break;

                await Task.Delay(_settings.DelayBetweenRequestsMs, cancellationToken);
            }

            return ids;
        }

        private async Task<bool> InserirObraSeNova(int id, string tipo, CancellationToken cancellationToken)
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // Verifica se já existe a obra no banco
            if (await db.Obras.AnyAsync(o => o.IdTmdb == id && o.Tipo == tipo, cancellationToken))
                return false;

            async Task<string?> TryGetJsonAsync(string url)
            {
                try
                {
                    var response = await _http.GetAsync(url, cancellationToken);
                    if (response.IsSuccessStatusCode)
                        return await response.Content.ReadAsStringAsync(cancellationToken);

                    _logger.LogWarning("Erro TMDb {status} para {url}", response.StatusCode, url);
                    return null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao buscar {url}", url);
                    return null;
                }
            }

            string detailsUrlPt = $"https://api.themoviedb.org/3/{(tipo == "Filme" ? "movie" : "tv")}/{id}?api_key={_settings.ApiKey}&language=pt-BR";
            string detailsUrlEn = $"https://api.themoviedb.org/3/{(tipo == "Filme" ? "movie" : "tv")}/{id}?api_key={_settings.ApiKey}&language=en-US";

            var jsonPt = await TryGetJsonAsync(detailsUrlPt);
            if (string.IsNullOrWhiteSpace(jsonPt))
                return false;

            var jsonEn = await TryGetJsonAsync(detailsUrlEn);

            if (tipo == "Filme")
            {
                var moviePt = JsonSerializer.Deserialize<TMDbMovie>(jsonPt);
                var movieEn = string.IsNullOrWhiteSpace(jsonEn) ? null : JsonSerializer.Deserialize<TMDbMovie>(jsonEn);

                // Ignora filmes adultos ou sem sinopse
                if (moviePt == null || moviePt.adult || string.IsNullOrWhiteSpace(moviePt.title) || string.IsNullOrWhiteSpace(moviePt.overview))
                    return false;

                var obra = new Obras
                {
                    IdObra = Guid.NewGuid(),
                    IdTmdb = moviePt.id,
                    Nome = moviePt.title,
                    NomeOriginal = movieEn?.title ?? moviePt.original_title ?? moviePt.title,
                    Sinopse = moviePt.overview,
                    Tipo = tipo,
                    Imagem = moviePt.poster_path ?? "",
                    DataLancamento = moviePt.release_date ?? "",
                    DataCadastro = DateTime.UtcNow,
                    NotaMedia = (float)(moviePt.vote_average ?? 0)
                };

                foreach (var genre in moviePt.genres)
                {
                    var g = await db.Generos.FirstOrDefaultAsync(x => x.IdGeneroImdbMovie == genre.id, cancellationToken);
                    if (g != null)
                        obra.Generos.Add(g);
                }

                await db.Obras.AddAsync(obra, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
            else // Série
            {
                var seriePt = JsonSerializer.Deserialize<TMDbSerie>(jsonPt);
                var serieEn = string.IsNullOrWhiteSpace(jsonEn) ? null : JsonSerializer.Deserialize<TMDbSerie>(jsonEn);

                // Ignora séries adultas ou sem sinopse
                if (seriePt == null || seriePt.adult || string.IsNullOrWhiteSpace(seriePt.name) || string.IsNullOrWhiteSpace(seriePt.overview))
                    return false;

                var obra = new Obras
                {
                    IdObra = Guid.NewGuid(),
                    IdTmdb = seriePt.id,
                    Nome = seriePt.name,
                    NomeOriginal = serieEn?.name ?? seriePt.original_name ?? seriePt.name,
                    Sinopse = seriePt.overview,
                    Tipo = tipo,
                    Imagem = seriePt.poster_path ?? "",
                    DataLancamento = seriePt.first_air_date ?? "",
                    DataCadastro = DateTime.UtcNow,
                    NotaMedia = (float)(seriePt.vote_average ?? 0)
                };

                foreach (var genre in seriePt.genres)
                {
                    var g = await db.Generos.FirstOrDefaultAsync(x => x.IdGeneroImdbMovie == genre.id || x.IdGeneroImdbSerie == genre.id, cancellationToken);
                    if (g != null)
                        obra.Generos.Add(g);
                }

                await db.Obras.AddAsync(obra, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
        }



        // Modelos auxiliares
        private class ChangeResponse
        {
            public int page { get; set; }
            public int total_pages { get; set; }
            public List<ChangeItem> results { get; set; } = new();
        }

        private class ChangeItem { public int id { get; set; } }

        private class TMDbMovie
        {
            public int id { get; set; }
            public string title { get; set; }
            public string original_title { get; set; }
            public string overview { get; set; }
            public string release_date { get; set; }
            public string poster_path { get; set; }
            public double? vote_average { get; set; }
            public bool adult { get; set; } 
            public List<TMDbGenre> genres { get; set; } = new();
        }

        private class TMDbSerie
        {
            public int id { get; set; }
            public string name { get; set; }
            public string original_name { get; set; }
            public string overview { get; set; }
            public string first_air_date { get; set; }
            public string poster_path { get; set; }
            public double? vote_average { get; set; }
            public bool adult { get; set; } 
            public List<TMDbGenre> genres { get; set; } = new();
        }

        private class TMDbGenre
        {
            public int id { get; set; }
            public string name { get; set; }
        }
    }
}
