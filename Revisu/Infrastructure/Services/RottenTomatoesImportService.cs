using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Revisu.Data;
using Revisu.Domain.Entities;
using Revisu.Infrastructure;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Revisu.Infrastructure.Services
{
    public class RottenTomatoesImportService
    {
        private readonly AppDbContext _db;
        private readonly IHttpClientFactory _httpFactory;
        private readonly RottenSettings _settings;
        private readonly ILogger<RottenTomatoesImportService> _logger;
        private const string CheckpointFile = "checkpoint_rotten_batch.json";

        public RottenTomatoesImportService(
            AppDbContext db,
            IHttpClientFactory httpFactory,
            RottenSettings settings,
            ILogger<RottenTomatoesImportService> logger)
        {
            _db = db;
            _httpFactory = httpFactory;
            _settings = settings;
            _logger = logger;
        }

        public async Task<string> ImportarAvaliacoesEmLotesAsync(CancellationToken cancellationToken = default)
        {
            var total = await _db.Obras
                .Where(o => o.IdRottenTomatoes == null && o.Tipo == "Filme")
                .CountAsync(cancellationToken);

            int batchSize = Math.Max(1, _settings.BatchSize);
            int startBatchIndex = LoadCheckpoint();
            int totalBatches = (int)Math.Ceiling(total / (double)batchSize);

            _logger.LogInformation("Iniciando importação RottenTomatoes — {total} obras. Batch size: {batchSize}. Retomando batch {startBatchIndex}.",
                total, batchSize, startBatchIndex);

            for (int batchIndex = startBatchIndex; batchIndex < totalBatches; batchIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int skip = batchIndex * batchSize;

                var obrasBatch = await _db.Obras
                    .Where(o => o.IdRottenTomatoes == null && o.Tipo == "Filme" && o.Sinopse != "")
                    .OrderByDescending(o => o.DataLancamento)
                    .Skip(skip)
                    .Take(batchSize)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                if (obrasBatch.Count == 0)
                {
                    SaveCheckpoint(batchIndex + 1);
                    continue;
                }

                _logger.LogInformation("Processando batch {batchIndex}/{totalBatches} — {count} obras", batchIndex + 1, totalBatches, obrasBatch.Count);

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, _settings.MaxDegreeOfParallelism),
                    CancellationToken = cancellationToken
                };

                var obrasToUpdate = new ConcurrentBag<Obras>();
                var avaliacoesToInsert = new ConcurrentBag<Avaliacoes>();

                await Parallel.ForEachAsync(obrasBatch, parallelOptions, async (obra, ct) =>
                {
                    try
                    {
                        await Task.Delay(_settings.DelayBetweenRequestsMs, ct);

                        var emsId = await BuscarEmsIdInteligenteAsync(obra.Nome, obra.DataLancamento, ct);
                        if (string.IsNullOrEmpty(emsId))
                        {
                            _logger.LogDebug("Nenhum emsId encontrado para '{nome}'", obra.Nome);
                            return;
                        }

                        obra.IdRottenTomatoes = emsId;
                        obrasToUpdate.Add(obra);

                        var avals = await BuscarAvaliacoesRawAsync(emsId, obra.IdObra, ct);
                        foreach (var a in avals)
                            avaliacoesToInsert.Add(a);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Erro processando obra {nome}", obra.Nome);
                    }
                });

                using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    var updateList = obrasToUpdate.ToList();
                    if (updateList.Count > 0)
                    {
                        foreach (var o in updateList)
                        {
                            _db.Obras.Attach(o);
                            _db.Entry(o).State = EntityState.Modified;
                        }

                        await _db.SaveChangesAsync(cancellationToken);
                    }

                    var insertList = avaliacoesToInsert.ToList();
                    if (insertList.Count > 0)
                    {
                        await _db.Avaliacoes.AddRangeAsync(insertList, cancellationToken);
                        await _db.SaveChangesAsync(cancellationToken);
                    }

                    await tx.CommitAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(cancellationToken);
                    _logger.LogError(ex, "Falha ao gravar batch {batchIndex}", batchIndex);
                    throw;
                }


                SaveCheckpoint(batchIndex + 1);
                _logger.LogInformation("Batch {batchIndex} concluído — {u} obras, {a} avaliações",
                    batchIndex + 1, obrasToUpdate.Count, avaliacoesToInsert.Count);
            }

            TryDeleteCheckpoint();
            return $"Importação concluída — {total} obras processadas em {totalBatches} batches.";
        }

        private async Task<string?> BuscarEmsIdInteligenteAsync(string titulo, string dataLancamento, CancellationToken cancellationToken)
        {
            var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("x-algolia-api-key", _settings.AlgoliaApiKey);
            client.DefaultRequestHeaders.Add("x-algolia-application-id", _settings.AlgoliaAppId);

            var payload = new
            {
                requests = new[]
                {
                    new
                    {
                        indexName = "content_rt",
                        @params = $"analyticsTags=[\"header_search\"]&clickAnalytics=true&filters=isEmsSearchable=1&hitsPerPage={_settings.HitsPerPage}&query={Uri.EscapeDataString(titulo)}"
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await client.PostAsync(_settings.AlgoliaUrl, content, cancellationToken);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
            var hits = doc.RootElement.GetProperty("results")[0].GetProperty("hits").EnumerateArray();

            var normalizedQuery = NormalizarTexto(titulo);
            int? anoObra = null;
            if (!string.IsNullOrWhiteSpace(dataLancamento) && DateTime.TryParse(dataLancamento, out var parsed))
                anoObra = parsed.Year;

            foreach (var hit in hits)
            {
                if (!hit.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "movie")
                    continue;

                var title = hit.GetProperty("title").GetString() ?? "";
                var aka = hit.TryGetProperty("aka", out var akaArray)
                    ? akaArray.EnumerateArray().Select(a => a.GetString() ?? "").ToList()
                    : new List<string>();

                bool match = NormalizarTexto(title).Contains(normalizedQuery) ||
                             aka.Any(a => NormalizarTexto(a).Contains(normalizedQuery));

                if (!match) continue;

                if (anoObra.HasValue &&
                    hit.TryGetProperty("releaseYear", out var ry) &&
                    ry.ValueKind == JsonValueKind.Number)
                {
                    var anoHit = ry.GetInt32();
                    if (Math.Abs(anoHit - anoObra.Value) <= 1)
                        return hit.GetProperty("emsId").GetString();
                }

                return hit.GetProperty("emsId").GetString();
            }

            return null;
        }

        private async Task<List<Avaliacoes>> BuscarAvaliacoesRawAsync(string emsId, Guid idObra, CancellationToken cancellationToken)
        {
            var client = _httpFactory.CreateClient();
            var url = $"https://www.rottentomatoes.com/cnapi/movie/{emsId}/reviews/user";
            using var resp = await client.GetAsync(url, cancellationToken);
            if (!resp.IsSuccessStatusCode) return new List<Avaliacoes>();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
            if (!doc.RootElement.TryGetProperty("reviews", out var arr))
                return new List<Avaliacoes>();

            var list = new List<Avaliacoes>();
            foreach (var r in arr.EnumerateArray())
            {
                float nota = 0;
                if (r.TryGetProperty("rating", out var rating) && rating.ValueKind == JsonValueKind.Number)
                    nota = (float)rating.GetDouble();

                list.Add(new Avaliacoes
                {
                    IdAvaliacao = Guid.NewGuid(),
                    IdObra = idObra,
                    Nota = nota,
                    DataCadastro = DateTime.UtcNow
                });
            }

            return list;
        }

        private static string NormalizarTexto(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            input = input.ToLowerInvariant();
            input = input.Normalize(NormalizationForm.FormD);
            var chars = input.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray();
            var s = new string(chars);
            s = Regex.Replace(s, @"[^\w\s]", "");
            return s.Trim();
        }

        private void SaveCheckpoint(int nextBatchIndex)
        {
            var obj = new { nextBatchIndex };
            File.WriteAllText(CheckpointFile, JsonSerializer.Serialize(obj));
        }

        private int LoadCheckpoint()
        {
            if (!File.Exists(CheckpointFile)) return 0;
            try
            {
                var doc = JsonDocument.Parse(File.ReadAllText(CheckpointFile));
                return doc.RootElement.GetProperty("nextBatchIndex").GetInt32();
            }
            catch
            {
                return 0;
            }
        }

        private void TryDeleteCheckpoint()
        {
            try { if (File.Exists(CheckpointFile)) File.Delete(CheckpointFile); } catch { }
        }

        public void ResetCheckpoint()
        {
            TryDeleteCheckpoint();
            _logger.LogWarning("Checkpoint resetado — importação começará do início.");
        }
    }
}
