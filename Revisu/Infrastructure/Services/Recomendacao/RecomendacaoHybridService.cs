using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using Microsoft.ML.Data;
using Revisu.Data;
using Revisu.Domain.Dtos;
using Revisu.Domain.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Revisu.Recommendation
{
    public class RecomendacaoHybridService
    {
        private readonly AppDbContext _db;
        private readonly MLContext _ml;
        private readonly string _cachePath = Path.Combine("App_Data", "obra_features_cache.json");
        private readonly string _cfModelPath = Path.Combine("App_Data", "cf_model.zip");

        // in-memory runtime
        private ConcurrentDictionary<Guid, ObraFeatureCached> _cache = new();
        private ITransformer? _cfModel;
        private object _modelLock = new();

        public RecomendacaoHybridService(AppDbContext db)
        {
            _db = db;
            _ml = new MLContext(seed: 0);
            Directory.CreateDirectory("App_Data");
        }

        // ---------------------------
        // Build offline cache (run periodically)
        // ---------------------------
        public async Task BuildFeatureCacheAsync(CancellationToken cancellationToken = default)
        {
            var obras = await _db.Obras
                .Where(o => o.NotaMedia > 0 && !string.IsNullOrWhiteSpace(o.Sinopse))
                .Select(o => new
                {
                    o.IdObra,
                    o.IdTmdb,
                    o.Nome,
                    o.Imagem,
                    o.NotaMedia,
                    Popularidade = o.Populariedade,
                    o.Tipo,
                    o.Sinopse,
                    Generos = o.Generos.Select(g => g.IdGenero).ToList(),
                    Elenco = o.Elenco.Select(e => e.IdElenco).ToList()
                })
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            Regex tokenRegex = new Regex(@"[^\w\s]", RegexOptions.Compiled);
            Func<string, string[]> tokenize = s =>
            {
                if (string.IsNullOrWhiteSpace(s)) return Array.Empty<string>();
                var cleaned = tokenRegex.Replace(s.ToLowerInvariant(), " ");
                var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                    .Where(t => t.Length > 2)
                                    .Select(t => t.Trim())
                                    .Distinct()
                                    .ToArray();
                return tokens;
            };

            var dict = new ConcurrentDictionary<Guid, ObraFeatureCached>();
            // Parallel tokenization (no DB access inside)
            var bag = new ConcurrentBag<ObraFeatureCached>();
            await Task.Run(() =>
            {
                Parallel.ForEach(obras, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, o =>
                {
                    var tokens = tokenize(o.Sinopse.Length > 2000 ? o.Sinopse[..2000] : o.Sinopse);
                    var feat = new ObraFeatureCached
                    {
                        IdObra = o.IdObra,
                        IdTmdb = o.IdTmdb,
                        Nome = o.Nome,
                        Imagem = o.Imagem,
                        NotaMedia = o.NotaMedia,
                        Popularidade = o.Popularidade ?? 0f,
                        Tipo = o.Tipo ?? string.Empty,
                        Generos = o.Generos?.ToArray() ?? Array.Empty<Guid>(),
                        ElencoIds = o.Elenco?.ToArray() ?? Array.Empty<Guid>(),
                        SinopseTokens = tokens
                    };
                    bag.Add(feat);
                });
            }, cancellationToken);

            foreach (var f in bag) dict[f.IdObra] = f;

            // persist to disk
            using (var fs = File.Create(_cachePath))
            {
                await JsonSerializer.SerializeAsync(fs, dict.Values.ToList(), new JsonSerializerOptions { WriteIndented = false }, cancellationToken);
            }

            _cache = new ConcurrentDictionary<Guid, ObraFeatureCached>(dict);
        }

        // ---------------------------
        // Train collaborative model using all users (MatrixFactorization)
        // ---------------------------
        public async Task TrainCollaborativeModelAsync(CancellationToken cancellationToken = default)
        {
            // load interactions from Biblioteca (user-item pairs)
            var interactions = await _db.Biblioteca
                .Where(b => b.IdUsuario != null && b.IdObra != null && !b.Excluido)
                .Select(b => new InteractionRecord
                {
                    UserId = b.IdUsuario!.ToString(),
                    ItemId = b.IdObra!.ToString(),
                    Label = 1f
                })
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (interactions.Count == 0)
                throw new InvalidOperationException("No interaction data available to train collaborative model.");

            var data = _ml.Data.LoadFromEnumerable(interactions);

            var pipeline = _ml.Transforms.Conversion.MapValueToKey("UserIdEncoded", nameof(InteractionRecord.UserId))
                .Append(_ml.Transforms.Conversion.MapValueToKey("ItemIdEncoded", nameof(InteractionRecord.ItemId)))
                .Append(_ml.Recommendation().Trainers.MatrixFactorization(
                    labelColumnName: nameof(InteractionRecord.Label),
                    matrixRowIndexColumnName: "UserIdEncoded",
                    matrixColumnIndexColumnName: "ItemIdEncoded",
                    numberOfIterations: 40,
                    approximationRank: 32
                ));

            var model = pipeline.Fit(data);

            // save to disk (atomic)
            var tmp = _cfModelPath + ".tmp";
            using (var fs = File.Create(tmp))
            {
                _ml.Model.Save(model, data.Schema, fs);
            }
            File.Move(tmp, _cfModelPath, overwrite: true);

            lock (_modelLock)
            {
                _cfModel = model;
            }
        }

        // ensure disk model/cache loaded into memory
        public async Task EnsureLoadedAsync()
        {
            if ((_cache == null || _cache.Count == 0) && File.Exists(_cachePath))
            {
                var list = JsonSerializer.Deserialize<List<ObraFeatureCached>>(await File.ReadAllTextAsync(_cachePath))
                           ?? new List<ObraFeatureCached>();
                _cache = new ConcurrentDictionary<Guid, ObraFeatureCached>(list.ToDictionary(x => x.IdObra, x => x));
            }

            if (_cfModel == null && File.Exists(_cfModelPath))
            {
                lock (_modelLock)
                {
                    using var fs = File.OpenRead(_cfModelPath);
                    _cfModel = _ml.Model.Load(fs, out _);
                }
            }
        }

        // ---------------------------
        // Recommend: combine CF + content + meta
        // ---------------------------
        //public async Task<List<ObraDTO>> RecommendForUserAsync(Guid userId, int top = 10, CancellationToken cancellationToken = default)
        //{
        //    await EnsureLoadedAsync();

        //    // load user's watched ids and watched elenco (one DB roundtrip)
        //    var userLibrary = await _db.Biblioteca
        //        .Where(b => b.IdUsuario == userId && !b.Excluido)
        //        .Select(b => new { b.IdObra, b.IdElenco })
        //        .AsNoTracking()
        //        .ToListAsync(cancellationToken);

        //    var watchedIds = new HashSet<Guid>(userLibrary.Where(x => x.IdObra.HasValue).Select(x => x.IdObra!.Value));
        //    var watchedElenco = new HashSet<Guid>(userLibrary.Where(x => x.IdElenco.HasValue).Select(x => x.IdElenco!.Value));

        //    // create user profile (genres from watched obras)
        //    var watchedCached = _cache.Values.Where(c => watchedIds.Contains(c.IdObra)).ToList();
        //    var userGenres = new HashSet<Guid>(watchedCached.SelectMany(c => c.Generos));
        //    var userTokens = new HashSet<string>(watchedCached.SelectMany(c => c.SinopseTokens));

        //    // candidate list = cached obras - watched
        //    var candidates = _cache.Values.Where(c => !watchedIds.Contains(c.IdObra)).ToArray();

        //    var scoredBag = new ConcurrentBag<(Guid id, float score)>();

        //    // thread-local prediction engine for CF (if present)
        //    ThreadLocal<PredictionEngine<InteractionRecord, CfPrediction>?> threadCfEngine = new ThreadLocal<PredictionEngine<InteractionRecord, CfPrediction>?>(() =>
        //    {
        //        lock (_modelLock)
        //        {
        //            if (_cfModel != null) return _ml.Model.CreatePredictionEngine<InteractionRecord, CfPrediction>(_cfModel);
        //            return null;
        //        }
        //    });

        //    // weights (tuneable)
        //    const float W_CF = 0.4f;
        //    const float W_CONTENT = 0.55f;
        //    const float W_META = 0.05f;

        //    Parallel.ForEach(candidates, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, c =>
        //    {
        //        if (cancellationToken.IsCancellationRequested) return;

        //        // content similarities
        //        float genreSim = 0f;
        //        if (userGenres.Count > 0 && c.Generos.Length > 0)
        //        {
        //            var inter = c.Generos.Count(g => userGenres.Contains(g));
        //            var union = c.Generos.Length + userGenres.Count;
        //            genreSim = union == 0 ? 0f : (float)inter / union;
        //        }

        //        float elencoSim = 0f;
        //        if (watchedElenco.Count > 0 && c.ElencoIds.Length > 0)
        //        {
        //            var inter = c.ElencoIds.Count(e => watchedElenco.Contains(e));
        //            elencoSim = watchedElenco.Count == 0 ? 0f : (float)inter / watchedElenco.Count;
        //        }

        //        float sinopseSim = 0f;
        //        if (userTokens.Count > 0 && c.SinopseTokens.Length > 0)
        //        {
        //            var inter = c.SinopseTokens.Count(t => userTokens.Contains(t));
        //            var union = userTokens.Count + c.SinopseTokens.Length;
        //            sinopseSim = union == 0 ? 0f : (float)inter / union;
        //        }

        //        float contentScore = 0.85f * genreSim + 0.12f * elencoSim + 0.03f * sinopseSim; // relative weights inside content

        //        // meta score (nota + popularidade)
        //        float metaScore = (c.NotaMedia / 10f) * 0.7f + (c.Popularidade / 100f) * 0.3f;

        //        // collaborative score (if model exists)
        //        float cfScoreRaw = 0f;
        //        var cfEngine = threadCfEngine.Value;
        //        if (cfEngine != null)
        //        {
        //            try
        //            {
        //                var pred = cfEngine.Predict(new InteractionRecord { UserId = userId.ToString(), ItemId = c.IdObra.ToString() });
        //                cfScoreRaw = pred.Score; // raw MF score (can be negative/positive)
        //            }
        //            catch
        //            {
        //                cfScoreRaw = 0f;
        //            }
        //        }

        //        // normalize CF raw using sigmoid to bring to [0,1]
        //        float cfScore = 1f / (1f + (float)Math.Exp(-cfScoreRaw)); // maps to (0,1)

        //        // final weighted combination
        //        float final = W_CF * cfScore + W_CONTENT * contentScore + W_META * metaScore;

        //        scoredBag.Add((c.IdObra, final));
        //    });

        //    // pick top candidates
        //    var topIds = scoredBag.OrderByDescending(x => x.score)
        //                          .Take(top)
        //                          .Select(x => x.id)
        //                          .ToList();

        //    // fetch metadata for top ids
        //    var topMeta = await _db.Obras
        //        .Where(o => topIds.Contains(o.IdObra))
        //        .Include(o => o.Generos)   // <-- isso já traz o Nome
        //        .AsNoTracking()
        //        .ToListAsync(cancellationToken);


        //    // preserve ordering of topIds
        //    var ordered = topIds.Select(id => topMeta.First(m => m.IdObra == id)).ToList();

        //    // cleanup threadlocal engines
        //    //foreach (var e in threadCfEngine.Values)
        //    //{
        //    //    if (e is IDisposable d) d.Dispose();
        //    //}
        //    threadCfEngine.Dispose();

        //    var result = ordered.Select(o => new ObraDTO
        //    {
        //        IdObra = o.IdObra,
        //        IdTmdb = o.IdTmdb,
        //        Titulo = o.Nome,
        //        Imagem = o.Imagem,
        //        NotaMedia = o.NotaMedia,
        //        Tipo = o.Tipo,
        //        Generos = o.Generos.Select(g => g.Nome).ToList()
        //    }).ToList();

        //    return result;
        //}

        //Versão com foco no genero
        public async Task<List<ObraDTO>> RecommendForUserAsync(
            Guid userId,
            int top = 10,
            CancellationToken cancellationToken = default)
        {
            await EnsureLoadedAsync();

            // ---------------------------
            // 1. Load user library
            // ---------------------------
            var userLibrary = await _db.Biblioteca
                .Where(b => b.IdUsuario == userId && !b.Excluido)
                .Select(b => new { b.IdObra, b.IdElenco })
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var watchedIds = new HashSet<Guid>(userLibrary.Where(x => x.IdObra.HasValue)
                .Select(x => x.IdObra!.Value));

            var watchedElenco = new HashSet<Guid>(userLibrary.Where(x => x.IdElenco.HasValue)
                .Select(x => x.IdElenco!.Value));

            var watchedCached = _cache.Values.Where(c => watchedIds.Contains(c.IdObra)).ToList();

            // genres + plot tokens
            var userGenres = new HashSet<Guid>(watchedCached.SelectMany(c => c.Generos));
            var userTokens = new HashSet<string>(watchedCached.SelectMany(c => c.SinopseTokens));

            // quantidade de obras vistas → influência global
            int nWatched = watchedIds.Count;
            float viewingFactor = nWatched switch
            {
                0 => 0.3f,
                <= 3 => 0.55f,
                <= 8 => 0.75f,
                <= 15 => 0.9f,
                _ => 1f
            };

            // ---------------------------
            // 2. Candidate pool
            // ---------------------------
            var candidates = _cache.Values.Where(c => !watchedIds.Contains(c.IdObra)).ToArray();
            var scoredBag = new ConcurrentBag<(Guid id, float score)>();

            // Thread‑local CF engine
            ThreadLocal<PredictionEngine<InteractionRecord, CfPrediction>?> threadCfEngine =
                new(() =>
                {
                    lock (_modelLock)
                    {
                        if (_cfModel != null)
                            return _ml.Model.CreatePredictionEngine<InteractionRecord, CfPrediction>(_cfModel);
                        return null;
                    }
                });

            // hybrid weights
            const float W_CF = 0.55f;      // reforçado CF
            const float W_CONTENT = 0.40f; // reforçado conteúdo
            const float W_META = 0.05f;    // meta menos influente

            // ---------------------------
            // 3. Scoring loop (parallel)
            // ---------------------------
            Parallel.ForEach(
                candidates,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
                c =>
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    // -----------------------------------------------------
                    // CONTENT — Gênero agora com PESO QUADRÁTICO + viewingFactor
                    // -----------------------------------------------------
                    float genreSim = 0f;
                    if (userGenres.Count > 0 && c.Generos.Length > 0)
                    {
                        var intersection = c.Generos.Count(g => userGenres.Contains(g));
                        var union = userGenres.Count + c.Generos.Length;

                        float jaccard = union == 0 ? 0f : (float)intersection / union;

                        // Quadratic boost — se bater 4 ou 5 gêneros = explode
                        genreSim = jaccard * jaccard;

                        // Dependendo de quantos filmes o usuário viu, aumente ainda mais
                        genreSim *= (0.5f + 0.5f * viewingFactor);
                    }

                    // ator/atriz/diretor
                    float elencoSim = 0f;
                    if (watchedElenco.Count > 0 && c.ElencoIds.Length > 0)
                    {
                        var inter = c.ElencoIds.Count(e => watchedElenco.Contains(e));
                        elencoSim = watchedElenco.Count == 0 ? 0f : (float)inter / watchedElenco.Count;
                    }

                    // tokens da sinopse
                    float sinopseSim = 0f;
                    if (userTokens.Count > 0 && c.SinopseTokens.Length > 0)
                    {
                        var inter = c.SinopseTokens.Count(t => userTokens.Contains(t));
                        var union = userTokens.Count + c.SinopseTokens.Length;
                        sinopseSim = union == 0 ? 0f : (float)inter / union;
                    }

                    // Score de conteúdo normalizado
                    float contentScore =
                        0.70f * genreSim +
                        0.20f * elencoSim +
                        0.10f * sinopseSim;

                    // -----------------------------------------------------
                    // META
                    // -----------------------------------------------------
                    float metaScore =
                        (c.NotaMedia / 10f) * 0.7f +
                        (c.Popularidade / 100f) * 0.3f;

                    // -----------------------------------------------------
                    // CF (collaborative)
                    // -----------------------------------------------------
                    float cfScoreRaw = 0f;
                    var cfEngine = threadCfEngine.Value;
                    if (cfEngine != null)
                    {
                        try
                        {
                            cfScoreRaw = cfEngine.Predict(new InteractionRecord
                            {
                                UserId = userId.ToString(),
                                ItemId = c.IdObra.ToString()
                            }).Score;
                        }
                        catch
                        {
                            cfScoreRaw = 0f;
                        }
                    }

                    // sigmoid
                    float cfScore = 1f / (1 + (float)Math.Exp(-cfScoreRaw));

                    // -----------------------------------------------------
                    // FINAL HYBRID SCORE
                    // -----------------------------------------------------
                    float finalScore =
                        W_CF * cfScore +
                        W_CONTENT * contentScore +
                        W_META * metaScore;

                    scoredBag.Add((c.IdObra, finalScore));
                });

            // ---------------------------
            // 4. Top results
            // ---------------------------
            var topIds = scoredBag
                .OrderByDescending(x => x.score)
                .Take(top)
                .Select(x => x.id)
                .ToList();

            // fetch metadata
            var topMeta = await _db.Obras
                .Where(o => topIds.Contains(o.IdObra))
                .Include(o => o.Generos)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var ordered = topIds.Select(id => topMeta.First(o => o.IdObra == id)).ToList();

            threadCfEngine.Dispose();

            return ordered.Select(o => new ObraDTO
            {
                IdObra = o.IdObra,
                IdTmdb = o.IdTmdb,
                Titulo = o.Nome,
                Imagem = o.Imagem,
                NotaMedia = o.NotaMedia,
                Tipo = o.Tipo,
                Generos = o.Generos.Select(g => g.Nome).ToList()
            }).ToList();
        }

        public async Task<List<ObraDTO>> RecommendSimilarToAsync(
            Guid obraId,
            int top = 10,
            CancellationToken cancellationToken = default)
        {
            await EnsureLoadedAsync();

            // 1. Recuperar obra base
            if (!_cache.TryGetValue(obraId, out var target))
                throw new InvalidOperationException("Obra não encontrada no cache.");

            var targetGenres = new HashSet<Guid>(target.Generos);
            var targetTokens = new HashSet<string>(target.SinopseTokens);
            var targetElenco = new HashSet<Guid>(target.ElencoIds);

            // 2. Lista de candidatos (exceto o próprio filme)
            var candidates = _cache.Values
                .Where(o => o.IdObra != obraId)
                .ToArray();

            var bag = new ConcurrentBag<(Guid id, float score)>();

            Parallel.ForEach(
                candidates,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
                c =>
                {
                    // ------------------------
                    // GÊNEROS — maior peso
                    // ------------------------
                    float genreSim = 0;
                    if (c.Generos.Length > 0 && targetGenres.Count > 0)
                    {
                        var inter = c.Generos.Count(g => targetGenres.Contains(g));
                        var union = c.Generos.Length + targetGenres.Count;
                        float jaccard = union == 0 ? 0f : (float)inter / union;

                        // Quadratic boost – muito importante para parecidos
                        genreSim = jaccard * jaccard;
                    }

                    // ------------------------
                    // SINOPSE — tokens parecidos
                    // ------------------------
                    float sinopseSim = 0;
                    if (c.SinopseTokens.Length > 0 && targetTokens.Count > 0)
                    {
                        var inter = c.SinopseTokens.Count(t => targetTokens.Contains(t));
                        var union = c.SinopseTokens.Length + targetTokens.Count;
                        sinopseSim = union == 0 ? 0f : (float)inter / union;
                    }

                    // ------------------------
                    // ELENCO — opcional, peso pequeno
                    // ------------------------
                    float elencoSim = 0;
                    if (c.ElencoIds.Length > 0 && targetElenco.Count > 0)
                    {
                        var inter = c.ElencoIds.Count(e => targetElenco.Contains(e));
                        elencoSim = (float)inter / Math.Max(1, targetElenco.Count);
                    }

                    // ------------------------
                    // META — ajuda a empurrar filmes relevantes
                    // ------------------------
                    float metaScore =
                        (c.NotaMedia / 10f) * 0.7f +
                        (c.Popularidade / 100f) * 0.3f;

                    // ------------------------
                    // PONDERAÇÃO FINAL – focada em filmes parecidos
                    // ------------------------
                    float finalScore =
                        0.70f * genreSim +      // MUITO forte
                        0.20f * sinopseSim +    // importante
                        0.05f * elencoSim +     // fraco
                        0.05f * metaScore;      // bem fraco

                    bag.Add((c.IdObra, finalScore));
                });

            // 3. Pegar top resultados
            var topIds = bag
                .OrderByDescending(x => x.score)
                .Take(top)
                .Select(x => x.id)
                .ToList();

            // 4. Buscar detalhes no banco
            var obras = await _db.Obras
                .Where(o => topIds.Contains(o.IdObra))
                .Include(o => o.Generos)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var ordered = topIds.Select(id => obras.First(o => o.IdObra == id));

            // 5. DTO
            return ordered.Select(o => new ObraDTO
            {
                IdObra = o.IdObra,
                IdTmdb = o.IdTmdb,
                Titulo = o.Nome,
                Imagem = o.Imagem,
                NotaMedia = o.NotaMedia,
                Tipo = o.Tipo,
                Generos = o.Generos.Select(g => g.Nome).ToList()
            }).ToList();
        }



        // ---------------------------
        // small helper classes
        // ---------------------------
        private class InteractionRecord
        {
            public string UserId { get; set; } = "";
            public string ItemId { get; set; } = "";
            public float Label { get; set; }
        }
        private class CfPrediction
        {
            public float Score { get; set; }
        }
        private class ObraFeatureCached
        {
            public Guid IdObra { get; set; }
            public int IdTmdb { get; set; }
            public string Nome { get; set; } = "";
            public string? Imagem { get; set; }
            public float NotaMedia { get; set; }
            public float Popularidade { get; set; }
            public string Tipo { get; set; } = "";
            public Guid[] Generos { get; set; } = Array.Empty<Guid>();
            public Guid[] ElencoIds { get; set; } = Array.Empty<Guid>();
            public string[] SinopseTokens { get; set; } = Array.Empty<string>();
        }
    }
}
