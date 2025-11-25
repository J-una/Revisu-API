using Microsoft.EntityFrameworkCore;
using Revisu.Data;
using Revisu.Domain.Entities;
using Revisu.Infrastructure;
using System;
using System.Net;
using System.Runtime;
using System.Text.Json;

public class AtualizarElencoTmdbService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly HttpClient _http;
    private readonly ILogger<AtualizarElencoTmdbService> _logger;
    private readonly TmdbSettings _settings;

    private const string CheckpointFile = "checkpoint_elenco.json";
    private const int BatchSize = 500;
    private const int MaxParallel = 2;

    public AtualizarElencoTmdbService(
        IDbContextFactory<AppDbContext> contextFactory,
        IHttpClientFactory httpFactory,
        ILogger<AtualizarElencoTmdbService> logger,
        TmdbSettings settings)
    {
        _contextFactory = contextFactory;
        _http = httpFactory.CreateClient("tmdb");
        _logger = logger;
        _settings = settings;
    }

    public async Task ExecutarAsync()
    {
        var obras = await CarregarObrasAsync();

        int total = obras.Count;
        int checkpoint = CarregarCheckpoint();

        _logger.LogWarning($"Retomando no lote {checkpoint}/{total / BatchSize}");

        var batches = obras
            .Select((x, index) => new { x, index })
            .GroupBy(g => g.index / BatchSize)
            .Where(g => g.Key >= checkpoint)
            .ToList();

        foreach (var batch in batches)
        {
            int batchIndex = batch.Key;
            var lista = batch.Select(x => x.x).ToList();

            _logger.LogWarning($"Processando lote {batchIndex} com {lista.Count} registros...");

            await Parallel.ForEachAsync(
                lista,
                new ParallelOptions { MaxDegreeOfParallelism = MaxParallel },
                async (item, ct) =>
                {
                    try
                    {
                        await ProcessarObraAsync(item);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Erro ao processar obra {item.IdObra}");
                    }
                });

            SalvarCheckpoint(batchIndex);

            _logger.LogWarning($"Lote {batchIndex} concluído!");
        }

        _logger.LogWarning("PROCESSO FINALIZADO COM SUCESSO!");
    }


    private async Task<List<ObraProcessar>> CarregarObrasAsync()
    {
        using var db = _contextFactory.CreateDbContext();

        return await db.Obras
            .Where(o => o.NotaMedia > 0 && !string.IsNullOrWhiteSpace(o.Sinopse))
            .OrderBy(o => o.IdObra)
            .Select(o => new ObraProcessar
            {
                IdObra = o.IdObra,
                IdTmdb = o.IdTmdb,
                Tipo = o.Tipo
            })
            .ToListAsync();
    }

    private async Task ProcessarObraAsync(ObraProcessar obraInfo)
    {
        using var db = _contextFactory.CreateDbContext();

        string tipo = obraInfo.Tipo.ToLower() == "filme" ? "movie" : "tv";

        var response = await _http.GetAsync($"/3/{tipo}/{obraInfo.IdTmdb}/credits?api_key={_settings.ApiKey}");


        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            await Task.Delay(3000);
            response = await _http.GetAsync($"/3/{tipo}/{obraInfo.IdTmdb}/credits?api_key={_settings.ApiKey}");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning($"Não encontrado no TMDb: {obraInfo.IdTmdb}");
            return;
        }

        var json = await response.Content.ReadAsStringAsync();
        var credits = JsonSerializer.Deserialize<CreditsResponse>(json);

        var obra = await db.Obras
            .Include(o => o.Elenco)
            .FirstOrDefaultAsync(o => o.IdObra == obraInfo.IdObra);
         
        if (obra == null) return;

        foreach (var pessoa in credits.Cast.Concat(credits.Crew))
        {
            // Procura elenco existente
            var existente = await db.Elencos
                .FirstOrDefaultAsync(e => e.IdTmdb == pessoa.Id);

            if (existente == null)
            {
                existente = new Elenco
                {
                    IdElenco = Guid.NewGuid(),
                    IdTmdb = pessoa.Id,
                    Nome = pessoa.Name,
                    Foto = pessoa.ProfilePath,
                    Cargo = pessoa.KnownForDepartment
                };
                db.Elencos.Add(existente);
                await db.SaveChangesAsync(); // salvar aqui garante que o Id seja trackeado
            }

            // Verifica se já está vinculado
            if (!obra.Elenco.Any(e => e.IdElenco == existente.IdElenco))
            {
                obra.Elenco.Add(existente);
            }
        }

        // Salva o relacionamento
        await db.SaveChangesAsync();


        // 👉 RESPEITA O DELAY DEFINIDO NO SETTINGS
        await Task.Delay(_settings.DelayBetweenRequestsMs);
    }


    private int CarregarCheckpoint()
    {
        if (!File.Exists(CheckpointFile))
        {
            File.WriteAllText(CheckpointFile, JsonSerializer.Serialize(0));
            return 0;
        }

        return JsonSerializer.Deserialize<int>(File.ReadAllText(CheckpointFile));
    }

    private void SalvarCheckpoint(int batch)
    {
        File.WriteAllText(CheckpointFile, JsonSerializer.Serialize(batch, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private class ObraProcessar
    {
        public Guid IdObra { get; set; }
        public int IdTmdb { get; set; }
        public string Tipo { get; set; }
    }

    private class CreditsResponse
    {
        public List<Person> Cast { get; set; } = new();
        public List<Person> Crew { get; set; } = new();
    }

    private class Person
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ProfilePath { get; set; }
        public string KnownForDepartment { get; set; }
    }
}
