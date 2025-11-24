using Microsoft.EntityFrameworkCore;
using Revisu.Data;
using Revisu.Domain.Entities;
using Revisu.Infrastructure;
using System.Text.Json;

namespace Revisu.Services
{
    public class TmdbImportService
    {
        private readonly AppDbContext _db;
        private readonly HttpClient _http;
        private readonly TmdbSettings _settings;

        public TmdbImportService(AppDbContext db, TmdbSettings settings)
        {
            _db = db;
            _settings = settings;
            _http = new HttpClient();
        }

        public async Task<string> ImportAllMoviesAsync(CancellationToken cancellationToken = default)
        {
            int currentYear = DateTime.Now.Year;
            int imported = 0;

            for (int year = _settings.StartYear; year <= currentYear; year++)
            {
                int page = 1;
                bool hasMore = true;

                Console.WriteLine($"Importando filmes do ano {year}...");

                while (hasMore && !cancellationToken.IsCancellationRequested)
                {
                    string url = $"https://api.themoviedb.org/3/discover/movie?api_key={_settings.ApiKey}&language=pt-BR&sort_by=popularity.desc&page={page}&primary_release_year={year}";
                    var json = await _http.GetStringAsync(url, cancellationToken);
                    var result = JsonSerializer.Deserialize<TMDbResponse>(json);

                    if (result?.results == null || result.results.Count == 0)
                        break;

                    foreach (var movie in result.results)
                    {
                        if (await _db.Obras.AnyAsync(o => o.IdTmdb == movie.id, cancellationToken))
                            continue;

                        var obra = new Obras
                        {
                            IdObra = Guid.NewGuid(),
                            IdTmdb = movie.id,
                            Nome = movie.title ?? "Sem título",
                            Sinopse = movie.overview ?? "",
                            Tipo = "Filme",
                            Imagem = movie.poster_path ?? "Sem poster",
                            DataLancamento = movie.release_date,
                            DataCadastro = DateTime.UtcNow
                        };

                        foreach (var genreId in movie.genre_ids)
                        {
                            var genero = await _db.Generos.FirstOrDefaultAsync(g => g.IdGeneroImdbMovie == genreId, cancellationToken);
                            if (genero != null)
                                obra.Generos.Add(genero);
                        }

                        _db.Obras.Add(obra);
                        imported++;
                    }

                    await _db.SaveChangesAsync(cancellationToken);
                    Console.WriteLine($"Página {page}/{result.total_pages} do ano {year} salva. Total até agora: {imported}");

                    page++;
                    hasMore = page <= result.total_pages && page <= 500;

                    await Task.Delay(_settings.DelayBetweenRequestsMs, cancellationToken);
                }
            }

            return $"Importação concluída. Total de filmes importados: {imported}";
        }

        private class TMDbResponse
        {
            public List<TMDbMovie> results { get; set; } = new();
            public int total_pages { get; set; }
        }

        private class TMDbMovie
        {
            public int id { get; set; }
            public string title { get; set; }
            public string overview { get; set; }
            public string release_date { get; set; }
            public string poster_path { get; set; }
            public List<int> genre_ids { get; set; } = new();
        }
    }
}
