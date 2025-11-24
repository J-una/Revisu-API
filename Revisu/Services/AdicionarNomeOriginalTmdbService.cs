using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Revisu.Data;
using Revisu.Infrastructure;
using System.Text.Json;

namespace Revisu.Services
{
    public class AdicionarNomeOriginalTmdbService
    {
        private readonly AppDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly TmdbSettings _tmdbSettings;

        private const string MovieBaseUrl = "https://api.themoviedb.org/3/movie/";
        private const string TvBaseUrl = "https://api.themoviedb.org/3/tv/";

        public AdicionarNomeOriginalTmdbService(AppDbContext context, IOptions<TmdbSettings> tmdbOptions)
        {
            _context = context;
            _tmdbSettings = tmdbOptions.Value;
            _httpClient = new HttpClient();
        }

        public async Task AtualizarTitulosOriginaisAsync()
        {
            // Busca filmes e séries sem NomeOriginal e sem IdRottenTomatoes
            var obras = await _context.Obras
                .Where(o => o.NomeOriginal == null && o.IdTmdb > 0 && o.Sinopse != "" && o.IdRottenTomatoes == null)
                .ToListAsync();

            Console.WriteLine($"{obras.Count} obras encontradas para atualização...");

            foreach (var obra in obras)
            {
                try
                {
                    // Escolhe a URL com base no tipo
                    var urlBase = obra.Tipo?.ToLower() == "serie" ? TvBaseUrl : MovieBaseUrl;
                    var url = $"{urlBase}{obra.IdTmdb}?api_key={_tmdbSettings.ApiKey}&language=en-US";

                    var response = await _httpClient.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Erro {response.StatusCode} para ID {obra.IdTmdb} ({obra.Nome})");
                        continue;
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);

                    // Filmes usam "title", series usam "name"
                    string? nomeOriginal = null;

                    if (obra.Tipo?.ToLower() == "serie")
                    {
                        if (doc.RootElement.TryGetProperty("name", out var nameElement))
                            nomeOriginal = nameElement.GetString();
                    }
                    else // Filme
                    {
                        if (doc.RootElement.TryGetProperty("title", out var titleElement))
                            nomeOriginal = titleElement.GetString();
                    }

                    if (!string.IsNullOrEmpty(nomeOriginal))
                    {
                        obra.NomeOriginal = nomeOriginal;
                        Console.WriteLine($"Atualizado: {obra.Nome} → {obra.NomeOriginal}");
                    }
                    else
                    {
                        Console.WriteLine($"Nenhum nome encontrado para {obra.Nome} ({obra.Tipo})");
                    }

                    await _context.SaveChangesAsync();
                    await Task.Delay(_tmdbSettings.DelayBetweenRequestsMs);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao atualizar ID {obra.IdTmdb}: {ex.Message}");
                }
            }

            Console.WriteLine("Atualização concluída!");
        }
    }
}
