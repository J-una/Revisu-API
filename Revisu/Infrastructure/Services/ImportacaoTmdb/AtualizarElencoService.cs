using Microsoft.EntityFrameworkCore;
using Revisu.Data;
using Revisu.Domain.Dtos;
using Revisu.Domain.Entities;
using Revisu.Infrastructure;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Revisu.Infrastructure.Services.ImportacaoTmdb
{
    public class AtualizarElencoService
    {
        private readonly AppDbContext _db;
        private readonly HttpClient _http;
        private readonly TmdbSettings _settings;
        private readonly ILogger<AtualizarElencoService> _logger;

        private const string CheckpointFile = "tmdb_elenco_checkpoint.json";

        public AtualizarElencoService(AppDbContext db, TmdbSettings settings, ILogger<AtualizarElencoService> logger)
        {
            _db = db;
            _settings = settings;
            _logger = logger;
            _http = new HttpClient();
        }

        public async Task<string> AtualizarElencoAsync()
        {
            var obras = await _db.Obras
                .Where(o => o.IdTmdb > 0 && o.NotaMedia > 0 && !string.IsNullOrWhiteSpace(o.Sinopse))
                .OrderBy(o => o.IdObra)
                .ToListAsync();

            int checkpoint = LoadCheckpoint();
            _logger.LogInformation($"Iniciando atualização de elenco a partir do índice {checkpoint}");

            // Cache para evitar consultas repetidas
            var elencoCache = await _db.Elencos.ToDictionaryAsync(e => e.IdTmdb, e => e);

            for (int i = checkpoint; i < obras.Count; i++)
            {
                var obra = obras[i];

                try
                {
                    string tipo = obra.Tipo.ToLower() == "filme" ? "movie" : "tv";
                    string url = $"https://api.themoviedb.org/3/{tipo}/{obra.IdTmdb}/credits?api_key={_settings.ApiKey}";
                    var response = await _http.GetAsync(url);

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        await Task.Delay(3000);
                        response = await _http.GetAsync(url);
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning($"Créditos não encontrados no TMDb para obra {obra.Nome} ({obra.IdTmdb})");
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    var credits = JsonSerializer.Deserialize<CreditsResponse>(json);

                    if (credits == null)
                        continue;

                    var pessoasFiltradas =
                        credits.Cast.Concat(credits.Crew)
                        .Where(p =>
                            p.KnownForDepartment == "Acting" ||
                            p.KnownForDepartment == "Directing")
                        .ToList();

                    var novosElencos = new List<Elenco>();

                    foreach (var p in pessoasFiltradas)
                    {
                        if (!elencoCache.TryGetValue(p.Id, out var existente))
                        {
                            existente = new Elenco
                            {
                                IdElenco = Guid.NewGuid(),
                                IdTmdb = p.Id,
                                Nome = p.Name,
                                Foto = p.ProfilePath,
                                Cargo = p.KnownForDepartment == "Acting" ? "Ator" :
                                        p.KnownForDepartment == "Directing" ? "Diretor" : "Outro",
                                Popularidade = p.Popularity,
                                Sexo = p.Gender switch
                                {
                                    1 => "Feminino",
                                    2 => "Masculino",
                                    _ => null
                                }
                            };

                            novosElencos.Add(existente);
                            elencoCache[p.Id] = existente; // adicionar ao cache
                        }

                        if (!obra.Elenco.Any(e => e.IdElenco == existente.IdElenco))
                        {
                            obra.Elenco.Add(existente);
                        }
                    }

                    // insere todos novos de uma vez
                    if (novosElencos.Any())
                        _db.Elencos.AddRange(novosElencos);

                    // salva apenas uma vez por obra
                    await _db.SaveChangesAsync();

                    SaveCheckpoint(i);
                    _logger.LogInformation($"Elenco atualizado para obra: {obra.Nome} ({obra.IdTmdb})");

                    await Task.Delay(_settings.DelayBetweenRequestsMs);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Erro ao atualizar elenco da obra {obra.Nome} ({obra.IdTmdb})");
                }
            }

            File.Delete(CheckpointFile);
            return "Atualização de elenco concluída!";
        }

        public async Task<string> AtualizarTodosAsync()
        {
            var elencos = await _db.Elencos
                .OrderByDescending(e => e.Popularidade)
                .ToListAsync();

            int total = elencos.Count;

            int batchSize = 500;
            int maxParallel = 5;

            int atualizados = 0;
            int removidos = 0;

            _logger.LogInformation("Iniciando atualização de {total} elencos...", total);

            for (int i = 0; i < total; i += batchSize)
            {
                var batch = elencos.Skip(i).Take(batchSize).ToList();

                _logger.LogInformation("Processando lote {lote} - Registros {inicio} até {fim}",
                    (i / batchSize) + 1,
                    i,
                    i + batch.Count);

                var semaphore = new SemaphoreSlim(maxParallel);

                var tasks = batch.Select(async pessoa =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        _logger.LogInformation("Processando pessoa [{id}] {nome}", pessoa.IdTmdb, pessoa.Nome);

                        await Task.Delay(_settings.DelayBetweenRequestsMs);

                        var url = $"https://api.themoviedb.org/3/person/{pessoa.IdTmdb}?api_key={_settings.ApiKey}&language=pt-BR";

                        _logger.LogDebug("Enviando request para TMDB: {url}", url);

                        TmdbPersonResponse? tmdb = null;

                        try
                        {
                            tmdb = await _http.GetFromJsonAsync<TmdbPersonResponse>(url);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Erro ao chamar o TMDB para pessoa {idTmdb}", pessoa.IdTmdb);
                            return;
                        }

                        if (tmdb == null)
                        {
                            _logger.LogWarning("TMDB retornou NULL para pessoa {idTmdb}", pessoa.IdTmdb);
                            return;
                        }

                        if (tmdb.adult)
                        {
                            _logger.LogWarning("Removendo pessoa {idTmdb} ({nome}) — classificado como adulto", pessoa.IdTmdb, pessoa.Nome);

                            _db.Elencos.Remove(pessoa);
                            Interlocked.Increment(ref removidos);
                            return;
                        }

                        pessoa.Popularidade = tmdb.popularity;

                        _logger.LogInformation("Atualizado → {nome} (ID TMDB: {idTmdb}) Nova popularidade: {pop}",
                            pessoa.Nome, pessoa.IdTmdb, pessoa.Popularidade);

                        Interlocked.Increment(ref atualizados);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erro inesperado ao processar pessoa {idTmdb}", pessoa.IdTmdb);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);

                // Salva por lote
                await _db.SaveChangesAsync();

                _logger.LogInformation("Lote finalizado e salvo no banco. Atualizados até agora: {a}, Removidos: {r}",
                    atualizados, removidos);
            }

            _logger.LogInformation("PROCESSO FINALIZADO! Total atualizados: {a}, Removidos: {r}",
                atualizados, removidos);

            return $"Finalizado! Atualizados: {atualizados}, Removidos: {removidos}";
        }


        private int LoadCheckpoint()
        {
            if (!File.Exists(CheckpointFile)) return 0;
            try
            {
                return JsonSerializer.Deserialize<int>(File.ReadAllText(CheckpointFile));
            }
            catch
            {
                return 0;
            }
        }

        private void SaveCheckpoint(int index)
        {
            File.WriteAllText(CheckpointFile, JsonSerializer.Serialize(index));
        }

        private class CreditsResponse
        {
            [JsonPropertyName("cast")]
            public List<Person> Cast { get; set; } = new();

            [JsonPropertyName("crew")]
            public List<Person> Crew { get; set; } = new();
        }

        public class TmdbPersonResponse
        {
            public bool adult { get; set; }
            public float popularity { get; set; }
            public string name { get; set; }
            public string profile_path { get; set; }
        }

        private class Person
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("profile_path")]
            public string ProfilePath { get; set; }

            [JsonPropertyName("known_for_department")]
            public string KnownForDepartment { get; set; }

            [JsonPropertyName("popularity")]
            public float Popularity { get; set; }

            [JsonPropertyName("gender")]
            public int Gender { get; set; }
        }

    }
}
