using Microsoft.EntityFrameworkCore;
using Revisu.Data;
using Revisu.Domain.Entities;
using Revisu.Infrastructure;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

public class AtualizarGenerosSeriesService
{
    private readonly AppDbContext _db;
    private readonly HttpClient _http;
    private readonly TmdbSettings _settings;

    private const string CheckpointFile = "series_generos_checkpoint.json";
    private const int BatchSize = 100;
    private const int MaxParallel = 3; // seguro dentro do rate-limit do TMDB

    public AtualizarGenerosSeriesService(AppDbContext db, TmdbSettings settings)
    {
        _db = db;
        _settings = settings;
        _http = new HttpClient();
    }

    public async Task<string> AtualizarGenerosAsync()
    {
        var checkpoint = LoadCheckpoint();

        var generosSerie = await _db.Generos
            .Where(g => g.IdGeneroImdbSerie != null)
            .ToDictionaryAsync(g => g.IdGeneroImdbSerie!.Value, g => g);

        var series = await _db.Obras
            .Where(o =>
                o.Tipo == "Serie" &&
                o.Sinopse != "" &&
                o.NotaMedia > 0)
            .OrderBy(o => o.IdObra)
            .ToListAsync();

        int startIndex = checkpoint;
        int total = series.Count;
        int processed = 0;

        Console.WriteLine($"Iniciando atualização de gêneros para {total} séries...");
        Console.WriteLine($"Retomando na posição {startIndex}/{total}");

        var block = new ActionBlock<Obras>(
            async serie =>
            {
                try
                {
                    await ProcessarSerieAsync(serie, generosSerie);
                }
                catch
                {
                    // apenas log — mantém o fluxo
                }
            },
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = MaxParallel
            }
        );

        for (int i = startIndex; i < total; i++)
        {
            block.Post(series[i]);
            processed++;

            if (processed % BatchSize == 0)
            {
                await _db.SaveChangesAsync();
                SaveCheckpoint(i);
                Console.WriteLine($"Batch salvo. Progresso: {i}/{total}");
            }
        }

        block.Complete();
        await block.Completion;

        await _db.SaveChangesAsync();
        File.Delete(CheckpointFile);

        return $"Atualização concluída. Total processado: {processed}";
    }

    private async Task ProcessarSerieAsync(Obras serie, Dictionary<int, Generos> generosSerie)
    {
        string url = $"https://api.themoviedb.org/3/tv/{serie.IdTmdb}?api_key={_settings.ApiKey}&language=pt-BR";
        string json = await _http.GetStringAsync(url);

        var response = JsonSerializer.Deserialize<TmdbDetalhesSerie>(json);

        if (response?.genres == null)
            return;

        serie.Generos.Clear();

        foreach (var g in response.genres)
        {
            if (generosSerie.TryGetValue(g.id, out var genero))
                serie.Generos.Add(genero);
        }
    }

    private int LoadCheckpoint()
    {
        if (!File.Exists(CheckpointFile))
            return 0;

        try
        {
            return int.Parse(File.ReadAllText(CheckpointFile));
        }
        catch
        {
            return 0;
        }
    }

    private void SaveCheckpoint(int index)
    {
        File.WriteAllText(CheckpointFile, index.ToString());
    }

    public class TmdbDetalhesSerie
    {
        public List<TmdbGenero> genres { get; set; } = new();
    }

    public class TmdbGenero
    {
        public int id { get; set; }
        public string name { get; set; }
    }
}
