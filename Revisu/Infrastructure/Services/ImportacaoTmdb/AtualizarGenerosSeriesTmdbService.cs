using Microsoft.EntityFrameworkCore;
using Revisu.Data;
using Revisu.Domain.Entities;
using Revisu.Infrastructure;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Revisu.Infrastructure.Services.ImportacaoTmdb
{
    public class AtualizarGenerosSeriesTmdbService
    {
        private readonly AppDbContext _db;
        private readonly HttpClient _http;
        private readonly TmdbSettings _settings;
        private readonly IConfiguration _config; // para salvar checkpoint

        public AtualizarGenerosSeriesTmdbService(AppDbContext db, TmdbSettings settings, IConfiguration config)
        {
            _db = db;
            _settings = settings;
            _config = config;
            _http = new HttpClient();
        }

        public async Task<string> AtualizarGenerosAsync(CancellationToken cancellationToken = default)
        {
            int checkpoint = _settings.CheckpointSeriesGenero;

            var series = await _db.Obras
                .Where(o => o.Tipo == "Serie"
                        && o.Sinopse != ""
                        && o.NotaMedia > 0
                        && o.IdTmdb > checkpoint)         
                .OrderBy(o => o.IdTmdb)                  
                .ToListAsync(cancellationToken);

            int total = series.Count;
            int atualizadas = 0;

            foreach (var serie in series)
            {
                try
                {
                    string url =
                        $"https://api.themoviedb.org/3/tv/{serie.IdTmdb}?api_key={_settings.ApiKey}&language=pt-BR";

                    var json = await _http.GetStringAsync(url, cancellationToken);

                    var result = JsonSerializer.Deserialize<TmdbTvDetails>(json);

                    if (result == null || result.genres == null)
                        continue;

                    _db.Entry(serie).Collection(s => s.Generos).Load();

                    serie.Generos.Clear();
                    await _db.SaveChangesAsync(cancellationToken);

                    foreach (var g in result.genres)
                    {
                        var genero = await _db.Generos
                            .FirstOrDefaultAsync(x => x.IdGeneroImdbSerie == g.id || x.IdGeneroImdbMovie == g.id, cancellationToken);

                        if (genero != null && !serie.Generos.Any(x => x.IdGenero == genero.IdGenero))
                            serie.Generos.Add(genero);
                    }

                    atualizadas++;

                    await _db.SaveChangesAsync(cancellationToken);

                    // 🔥 SALVA CHECKPOINT
                    SalvarCheckpoint(serie.IdTmdb);

                    await Task.Delay(_settings.DelayBetweenRequestsMs, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao atualizar série TMDB {serie.IdTmdb}: {ex.Message}");

                    // 🔥 mesmo com erro, salva checkpoint para não repetir esta série na próxima execução
                    SalvarCheckpoint(serie.IdTmdb);

                    continue; // continue em vez de abortar
                }
            }

            return $"Atualização concluída. Séries atualizadas: {atualizadas}/{total}. Último checkpoint: {_settings.CheckpointSeriesGenero}";
        }

        private void SalvarCheckpoint(int idTmdb)
        {
            string filePath = "appsettings.json";

            var json = File.ReadAllText(filePath);
            var configObj = JsonSerializer.Deserialize<JsonNode>(json);

            if (configObj == null)
                return;

            configObj["TMDbSettings"]["CheckpointSeriesGenero"] = idTmdb;

            var newJson = JsonSerializer.Serialize(configObj, new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(filePath, newJson);

            _settings.CheckpointSeriesGenero = idTmdb;
        }


        private class TmdbTvDetails
        {
            public List<TmdbGenre> genres { get; set; } = new();
        }

        private class TmdbGenre
        {
            public int id { get; set; }
            public string name { get; set; }
        }
    }
}
