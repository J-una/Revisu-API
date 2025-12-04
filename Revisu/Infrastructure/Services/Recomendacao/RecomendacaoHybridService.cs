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
        private Dictionary<Guid, string> _cacheElencoCargo = new();

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

            if((_cacheElencoCargo == null || _cacheElencoCargo.Count == 0))
            {
                _cacheElencoCargo = _db.Elencos
                    .ToDictionary(e => e.IdElenco, e => e.Cargo);
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
        //public async Task<List<ObraDTO>> RecommendForUserAsync(
        //    Guid userId,
        //    int top = 10,
        //    CancellationToken cancellationToken = default)
        //{
        //    await EnsureLoadedAsync();

        //    // ---------------------------
        //    // 1. Load user library
        //    // ---------------------------
        //    var userLibrary = await _db.Biblioteca
        //        .Where(b => b.IdUsuario == userId && !b.Excluido)
        //        .Select(b => new { b.IdObra, b.IdElenco })
        //        .AsNoTracking()
        //        .ToListAsync(cancellationToken);

        //    var watchedIds = new HashSet<Guid>(userLibrary.Where(x => x.IdObra.HasValue)
        //        .Select(x => x.IdObra!.Value));

        //    var watchedElenco = new HashSet<Guid>(userLibrary.Where(x => x.IdElenco.HasValue)
        //        .Select(x => x.IdElenco!.Value));

        //    var watchedCached = _cache.Values.Where(c => watchedIds.Contains(c.IdObra)).ToList();

        //    // genres + plot tokens
        //    var userGenres = new HashSet<Guid>(watchedCached.SelectMany(c => c.Generos));
        //    var userTokens = new HashSet<string>(watchedCached.SelectMany(c => c.SinopseTokens));

        //    // quantidade de obras vistas → influência global
        //    int nWatched = watchedIds.Count;
        //    float viewingFactor = nWatched switch
        //    {
        //        0 => 0.3f,
        //        <= 3 => 0.55f,
        //        <= 8 => 0.75f,
        //        <= 15 => 0.9f,
        //        _ => 1f
        //    };

        //    // ---------------------------
        //    // 2. Candidate pool
        //    // ---------------------------
        //    var candidates = _cache.Values.Where(c => !watchedIds.Contains(c.IdObra)).ToArray();
        //    var scoredBag = new ConcurrentBag<(Guid id, float score)>();

        //    // Thread‑local CF engine
        //    ThreadLocal<PredictionEngine<InteractionRecord, CfPrediction>?> threadCfEngine =
        //        new(() =>
        //        {
        //            lock (_modelLock)
        //            {
        //                if (_cfModel != null)
        //                    return _ml.Model.CreatePredictionEngine<InteractionRecord, CfPrediction>(_cfModel);
        //                return null;
        //            }
        //        });

        //    // hybrid weights
        //    const float W_CF = 0.55f;      // reforçado CF
        //    const float W_CONTENT = 0.40f; // reforçado conteúdo
        //    const float W_META = 0.05f;    // meta menos influente

        //    // ---------------------------
        //    // 3. Scoring loop (parallel)
        //    // ---------------------------
        //    Parallel.ForEach(
        //        candidates,
        //        new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
        //        c =>
        //        {
        //            if (cancellationToken.IsCancellationRequested) return;

        //            // -----------------------------------------------------
        //            // CONTENT — Gênero agora com PESO QUADRÁTICO + viewingFactor
        //            // -----------------------------------------------------
        //            float genreSim = 0f;
        //            if (userGenres.Count > 0 && c.Generos.Length > 0)
        //            {
        //                var intersection = c.Generos.Count(g => userGenres.Contains(g));
        //                var union = userGenres.Count + c.Generos.Length;

        //                float jaccard = union == 0 ? 0f : (float)intersection / union;

        //                // Quadratic boost — se bater 4 ou 5 gêneros = explode
        //                genreSim = jaccard * jaccard;

        //                // Dependendo de quantos filmes o usuário viu, aumente ainda mais
        //                genreSim *= (0.5f + 0.5f * viewingFactor);
        //            }

        //            // ator/atriz/diretor
        //            float elencoSim = 0f;
        //            if (watchedElenco.Count > 0 && c.ElencoIds.Length > 0)
        //            {
        //                var inter = c.ElencoIds.Count(e => watchedElenco.Contains(e));
        //                elencoSim = watchedElenco.Count == 0 ? 0f : (float)inter / watchedElenco.Count;
        //            }

        //            // tokens da sinopse
        //            float sinopseSim = 0f;
        //            if (userTokens.Count > 0 && c.SinopseTokens.Length > 0)
        //            {
        //                var inter = c.SinopseTokens.Count(t => userTokens.Contains(t));
        //                var union = userTokens.Count + c.SinopseTokens.Length;
        //                sinopseSim = union == 0 ? 0f : (float)inter / union;
        //            }

        //            // Score de conteúdo normalizado
        //            float contentScore =
        //                0.70f * genreSim +
        //                0.20f * elencoSim +
        //                0.10f * sinopseSim;

        //            // -----------------------------------------------------
        //            // META
        //            // -----------------------------------------------------
        //            float metaScore =
        //                (c.NotaMedia / 10f) * 0.7f +
        //                (c.Popularidade / 100f) * 0.3f;

        //            // -----------------------------------------------------
        //            // CF (collaborative)
        //            // -----------------------------------------------------
        //            float cfScoreRaw = 0f;
        //            var cfEngine = threadCfEngine.Value;
        //            if (cfEngine != null)
        //            {
        //                try
        //                {
        //                    cfScoreRaw = cfEngine.Predict(new InteractionRecord
        //                    {
        //                        UserId = userId.ToString(),
        //                        ItemId = c.IdObra.ToString()
        //                    }).Score;
        //                }
        //                catch
        //                {
        //                    cfScoreRaw = 0f;
        //                }
        //            }

        //            // sigmoid
        //            float cfScore = 1f / (1 + (float)Math.Exp(-cfScoreRaw));

        //            // -----------------------------------------------------
        //            // FINAL HYBRID SCORE
        //            // -----------------------------------------------------
        //            float finalScore =
        //                W_CF * cfScore +
        //                W_CONTENT * contentScore +
        //                W_META * metaScore;

        //            scoredBag.Add((c.IdObra, finalScore));
        //        });

        //    // ---------------------------
        //    // 4. Top results
        //    // ---------------------------
        //    var topIds = scoredBag
        //        .OrderByDescending(x => x.score)
        //        .Take(top)
        //        .Select(x => x.id)
        //        .ToList();

        //    // fetch metadata
        //    var topMeta = await _db.Obras
        //        .Where(o => topIds.Contains(o.IdObra))
        //        .Include(o => o.Generos)
        //        .AsNoTracking()
        //        .ToListAsync(cancellationToken);

        //    var ordered = topIds.Select(id => topMeta.First(o => o.IdObra == id)).ToList();

        //    threadCfEngine.Dispose();

        //    return ordered.Select(o => new ObraDTO
        //    {
        //        IdObra = o.IdObra,
        //        IdTmdb = o.IdTmdb,
        //        Titulo = o.Nome,
        //        Imagem = o.Imagem,
        //        NotaMedia = o.NotaMedia,
        //        Tipo = o.Tipo,
        //        Generos = o.Generos.Select(g => g.Nome).ToList()
        //    }).ToList();
        //}

        public async Task<RecommendationBundleDTO> RecommendForUserAsync(
            Guid userId,
            int top,
            CancellationToken cancellationToken = default)
        {
            await EnsureLoadedAsync();

            // ----------------------------------------------------
            // 1. Biblioteca do usuário
            // ----------------------------------------------------
            var userLibrary = await _db.Biblioteca
                .Where(b => b.IdUsuario == userId && !b.Excluido)
                .Select(b => new { b.IdObra, b.IdElenco })
                .AsNoTracking()
                .ToListAsync(cancellationToken);


            var watchedIds = new HashSet<Guid>(userLibrary
                .Where(x => x.IdObra.HasValue)
                .Select(x => x.IdObra!.Value));

            var watchedElenco = new HashSet<Guid>(userLibrary
                .Where(x => x.IdElenco.HasValue)
                .Select(x => x.IdElenco!.Value));

            var watchedCached = _cache.Values.Where(c => watchedIds.Contains(c.IdObra)).ToList();

            var userGenres = new HashSet<Guid>(watchedCached.SelectMany(c => c.Generos));
            var userTokens = new HashSet<string>(watchedCached.SelectMany(c => c.SinopseTokens));

            int nWatched = watchedIds.Count;
            float viewingFactor = nWatched switch
            {
                0 => 0.3f,
                <= 3 => 0.55f,
                <= 8 => 0.75f,
                <= 15 => 0.9f,
                _ => 1f
            };

            // ----------------------------------------------------
            // 2. Obras candidatas
            // ----------------------------------------------------
            var candidates = _cache.Values.Where(c => !watchedIds.Contains(c.IdObra)).ToArray();

            var scoredBag = new ConcurrentBag<(Guid id, float score)>();

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

            Parallel.ForEach(
                candidates,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
                c =>
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    float genreSim = 0f;
                    if (userGenres.Count > 0 && c.Generos.Length > 0)
                    {
                        var intersection = c.Generos.Count(g => userGenres.Contains(g));
                        var union = userGenres.Count + c.Generos.Length;
                        genreSim = union == 0 ? 0f : (float)intersection / union;
                        genreSim *= (0.5f + 0.5f * viewingFactor);
                    }

                    float elencoSim = 0f;
                    if (watchedElenco.Count > 0 && c.ElencoIds.Length > 0)
                    {
                        var inter = c.ElencoIds.Count(e => watchedElenco.Contains(e));
                        elencoSim = watchedElenco.Count == 0 ? 0f : (float)inter / watchedElenco.Count;
                    }

                    float sinopseSim = 0f;
                    if (userTokens.Count > 0 && c.SinopseTokens.Length > 0)
                    {
                        var inter = c.SinopseTokens.Count(t => userTokens.Contains(t));
                        var union = userTokens.Count + c.SinopseTokens.Length;
                        sinopseSim = union == 0 ? 0f : (float)inter / union;
                    }

                    float contentScore =
                        0.70f * genreSim +
                        0.20f * elencoSim +
                        0.10f * sinopseSim;

                    float metaScore =
                        (c.NotaMedia / 10f) * 0.7f +
                        (c.Popularidade / 100f) * 0.3f;

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
                        catch { }
                    }

                    float cfScore = 1f / (1 + (float)Math.Exp(-cfScoreRaw));

                    float finalScore =
                        0.55f * cfScore +
                        0.40f * contentScore +
                        0.05f * metaScore;

                    scoredBag.Add((c.IdObra, finalScore));
                });

            // ----------------------------------------------------
            // 3. Top obras recomendadas
            // ----------------------------------------------------
            var topIds = scoredBag
                .OrderByDescending(x => x.score)
                .Take(top)
                .Select(x => x.id)
                .ToList();

            var topMeta = await _db.Obras
                .Where(o => topIds.Contains(o.IdObra))
                .Include(o => o.Generos)
                .Include(o => o.Elenco)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var obrasDTO = topMeta.Select(o => new ObraDTO
            {
                IdObra = o.IdObra,
                IdTmdb = o.IdTmdb,
                Titulo = o.Nome,
                Imagem = o.Imagem,
                NotaMedia = o.NotaMedia,
                Tipo = o.Tipo,
                Generos = o.Generos.Select(g => g.Nome).ToList(),
                Marcado = watchedIds.Contains(o.IdObra)
            }).ToList();

            // =================================================================
            // 4. RECOMENDAÇÃO DE ATORES E DIRETORES (Garantir não vazios)
            // =================================================================

            bool userHasActors = watchedElenco.Any();

            var baseElenco = userHasActors
                ? watchedElenco
                : watchedCached.SelectMany(o => o.ElencoIds).ToHashSet();

            // Frequência
            var freq = new Dictionary<Guid, int>();
            foreach (var obra in watchedCached)
            {
                foreach (var membro in obra.ElencoIds)
                {
                    if (!freq.ContainsKey(membro))
                        freq[membro] = 0;

                    freq[membro] += baseElenco.Contains(membro) ? 1 : 3;
                }
            }

            var freqKeys = freq.Keys.ToHashSet();

            var elencoCandidatos = await _db.Elencos
                .Where(e => freqKeys.Contains(e.IdElenco))
                .Include(e => e.Obras)
                    .ThenInclude(o => o.Generos)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var scoredElenco = elencoCandidatos
                .Select(e =>
                {
                    float cooc = freq[e.IdElenco];

                    var generosElenco = e.Obras
                        .SelectMany(o => o.Generos.Select(g => g.IdGenero))
                        .ToHashSet();

                    float genreSim = 0;
                    if (generosElenco.Any())
                    {
                        var inter = generosElenco.Count(g => userGenres.Contains(g));
                        var union = generosElenco.Count + userGenres.Count;
                        genreSim = union == 0 ? 0 : (float)inter / union;
                    }

                    float score = 0.6f * cooc + 0.3f * genreSim + 0.1f * e.Popularidade;

                    return (e, score);
                })
                .OrderByDescending(x => x.score)
                .Take(top)
                .ToList();

            // ----------------------------------------------------
            // 5. Montagem DTO
            // ----------------------------------------------------
            var atoresDTO = scoredElenco
                .Where(x => x.e.Cargo == "Ator")
                .Select(x => new AtorDTO
                {
                    IdElenco = x.e.IdElenco,
                    IdTmdb = x.e.IdTmdb,
                    Nome = x.e.Nome,
                    Foto = x.e.Foto,
                    Cargo = x.e.Cargo,
                    Sexo = x.e.Sexo,
                    Generos = x.e.Obras
                        .SelectMany(o => o.Generos.Select(g => g.Nome))
                        .Distinct()
                        .ToList(),
                    Marcado = watchedElenco.Contains(x.e.IdElenco)
                })
                .ToList();

            var diretoresDTO = scoredElenco
                .Where(x => x.e.Cargo == "Diretor")
                .Select(x => new DiretorDTO
                {
                    IdElenco = x.e.IdElenco,
                    IdTmdb = x.e.IdTmdb,
                    Nome = x.e.Nome,
                    Foto = x.e.Foto,
                    Cargo = x.e.Cargo,
                    Sexo = x.e.Sexo,
                    Obras = x.e.Obras.Select(o => o.Nome).Distinct().ToList(),
                    Marcado = watchedElenco.Contains(x.e.IdElenco)
                })
                .ToList();

            // ----------------------------------------------------
            // 6. GARANTIR QUE NÃO VENHAM LISTAS VAZIAS
            // ----------------------------------------------------
            if (atoresDTO.Count == 0)
            {
                atoresDTO = await _db.Elencos
                    .Where(e => e.Cargo == "Ator")
                    .OrderByDescending(e => e.Popularidade)
                    .Take(top)
                    .Select(e => new AtorDTO
                    {
                        IdElenco = e.IdElenco,
                        IdTmdb = e.IdTmdb,
                        Nome = e.Nome,
                        Foto = e.Foto,
                        Cargo = e.Cargo,
                        Sexo = e.Sexo,
                        Generos = e.Obras.SelectMany(o => o.Generos.Select(g => g.Nome)).Distinct().ToList()
                    })
                    .ToListAsync(cancellationToken);
            }

            if (diretoresDTO.Count == 0)
            {
                diretoresDTO = await _db.Elencos
                    .Where(e => e.Cargo == "Diretor")
                    .OrderByDescending(e => e.Popularidade)
                    .Take(top)
                    .Select(e => new DiretorDTO
                    {
                        IdElenco = e.IdElenco,
                        IdTmdb = e.IdTmdb,
                        Nome = e.Nome,
                        Foto = e.Foto,
                        Cargo = e.Cargo,
                        Sexo = e.Sexo,
                        Obras = e.Obras.Select(o => o.Nome).Distinct().ToList()
                    })
                    .ToListAsync(cancellationToken);
            }

            // ----------------------------------------------------
            // 7. RETORNO FINAL
            // ----------------------------------------------------
            return new RecommendationBundleDTO
            {
                Obras = obrasDTO,
                Atores = atoresDTO,
                Diretores = diretoresDTO
            };
        }



        public async Task<List<ObraDTO>> RecommendObrasSimilarToAsync(
            Guid obraId,
            Guid idUsuario,
            int top = 10,
            CancellationToken cancellationToken = default)
        {
            await EnsureLoadedAsync();

            // Adicionado: carregar biblioteca (necessário para marcar obras)
            var userLibrary = await _db.Biblioteca
                .Where(b => !b.Excluido && b.IdUsuario == idUsuario)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var watchedIds = userLibrary
                .Where(x => x.IdObra.HasValue)
                .Select(x => x.IdObra!.Value)
                .ToHashSet();


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
                    // Gêneros (peso forte)
                    float genreSim = 0;
                    if (c.Generos.Length > 0 && targetGenres.Count > 0)
                    {
                        var inter = c.Generos.Count(g => targetGenres.Contains(g));
                        var union = c.Generos.Length + targetGenres.Count;
                        float jaccard = union == 0 ? 0f : (float)inter / union;
                        genreSim = jaccard * jaccard;
                    }

                    // Sinopse
                    float sinopseSim = 0;
                    if (c.SinopseTokens.Length > 0 && targetTokens.Count > 0)
                    {
                        var inter = c.SinopseTokens.Count(t => targetTokens.Contains(t));
                        var union = c.SinopseTokens.Length + targetTokens.Count;
                        sinopseSim = union == 0 ? 0f : (float)inter / union;
                    }

                    // Elenco (peso menor)
                    float elencoSim = 0;
                    if (c.ElencoIds.Length > 0 && targetElenco.Count > 0)
                    {
                        var inter = c.ElencoIds.Count(e => targetElenco.Contains(e));
                        elencoSim = (float)inter / Math.Max(1, targetElenco.Count);
                    }

                    // Meta score
                    float metaScore =
                        (c.NotaMedia / 10f) * 0.7f +
                        (c.Popularidade / 100f) * 0.3f;

                    float finalScore =
                        0.70f * genreSim +
                        0.20f * sinopseSim +
                        0.05f * elencoSim +
                        0.05f * metaScore;

                    bag.Add((c.IdObra, finalScore));
                });

            var topIds = bag
                .OrderByDescending(x => x.score)
                .Take(top)
                .Select(x => x.id)
                .ToList();

            var obras = await _db.Obras
                .Where(o => topIds.Contains(o.IdObra))
                .Include(o => o.Generos)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var ordered = topIds.Select(id => obras.First(o => o.IdObra == id));

            return ordered.Select(o => new ObraDTO
            {
                IdObra = o.IdObra,
                IdTmdb = o.IdTmdb,
                Titulo = o.Nome,
                Imagem = o.Imagem,
                NotaMedia = o.NotaMedia,
                Tipo = o.Tipo,
                Generos = o.Generos.Select(g => g.Nome).ToList(),
                Marcado = watchedIds.Contains(o.IdObra) // 🔥 Corrigido
            }).ToList();
        }


        public async Task<RecomendacaoElencoDTO> RecommendByElencoAsync(
            Guid idElenco,
            Guid idUsuario,
            int top = 10,
            CancellationToken cancellationToken = default)
        {
            await EnsureLoadedAsync();

            var userLibrary = await _db.Biblioteca
                .Where(b => !b.Excluido && b.IdUsuario == idUsuario)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var watchedIds = userLibrary
                .Where(x => x.IdObra.HasValue)
                .Select(x => x.IdObra!.Value)
                .ToHashSet();

            var watchedElenco = userLibrary
                .Where(x => x.IdElenco.HasValue)
                .Select(x => x.IdElenco!.Value)
                .ToHashSet();


            // 1. Buscar membro do elenco
            var elencoBase = await _db.Elencos
                .Where(e => e.IdElenco == idElenco)
                .Select(e => new { e.IdElenco, e.Nome, e.Cargo })
                .FirstOrDefaultAsync(cancellationToken);

            if (elencoBase == null)
                throw new InvalidOperationException("Elenco não encontrado.");

            string cargoBase = elencoBase.Cargo;

            // 2. Obras do elenco
            var obrasDoElenco = _cache.Values
                .Where(o => o.ElencoIds.Contains(idElenco))
                .Select(o => o.IdObra)
                .ToHashSet();

            // 3. Frequência de coocorrência
            var freq = new Dictionary<Guid, int>();

            foreach (var obraId in obrasDoElenco)
            {
                var obra = _cache[obraId];

                foreach (var membro in obra.ElencoIds)
                {
                    if (membro == idElenco) continue;

                    var membroCargo = _cacheElencoCargo[membro];

                    if (membroCargo != cargoBase) continue;

                    if (!freq.ContainsKey(membro))
                        freq[membro] = 0;

                    freq[membro]++;
                }
            }

            var similaresIds = freq
                .OrderByDescending(x => x.Value)
                .Take(top)
                .Select(x => x.Key)
                .ToList();

            List<AtorDTO> atoresDTO = new();
            List<DiretorDTO> diretoresDTO = new();

            if (cargoBase == "Ator")
            {
                atoresDTO = await _db.Elencos
                    .Where(e => similaresIds.Contains(e.IdElenco) && e.Cargo == "Ator")
                    .Select(e => new AtorDTO
                    {
                        IdElenco = e.IdElenco,
                        IdTmdb = e.IdTmdb,
                        Nome = e.Nome,
                        Foto = e.Foto,
                        Cargo = e.Cargo,
                        Sexo = e.Sexo,
                        Generos = e.Obras.SelectMany(o => o.Generos.Select(g => g.Nome)).Distinct().ToList(),
                        Marcado = watchedElenco.Contains(e.IdElenco) 
                    })
                    .ToListAsync(cancellationToken);
            }
            else
            {
                diretoresDTO = await _db.Elencos
                    .Where(e => similaresIds.Contains(e.IdElenco) && e.Cargo == "Diretor")
                    .Select(e => new DiretorDTO
                    {
                        IdElenco = e.IdElenco,
                        IdTmdb = e.IdTmdb,
                        Nome = e.Nome,
                        Foto = e.Foto,
                        Cargo = e.Cargo,
                        Sexo = e.Sexo,
                        Obras = e.Obras.Select(o => o.Nome).Distinct().ToList(),
                        Marcado = watchedElenco.Contains(e.IdElenco) 
                    })
                    .ToListAsync(cancellationToken);
            }

            // 6. Obras relacionadas
            var obrasRelacionadasIds = _cache.Values
                .Where(o => o.ElencoIds.Any(a => similaresIds.Contains(a)))
                .OrderByDescending(o => o.NotaMedia)
                .Take(top)
                .Select(o => o.IdObra)
                .ToList();

            var obrasRelacionadas = await _db.Obras
                .Where(o => obrasRelacionadasIds.Contains(o.IdObra))
                .Include(o => o.Generos)
                .Select(o => new ObraDTO
                {
                    IdObra = o.IdObra,
                    IdTmdb = o.IdTmdb,
                    Titulo = o.Nome,
                    Imagem = o.Imagem,
                    NotaMedia = o.NotaMedia,
                    Tipo = o.Tipo,
                    Generos = o.Generos.Select(g => g.Nome).ToList(),
                    Marcado = watchedIds.Contains(o.IdObra) 
                })
                .ToListAsync(cancellationToken);

            return new RecomendacaoElencoDTO
            {
                IdElenco = idElenco,
                NomeElenco = elencoBase.Nome,
                AtoresParecidos = atoresDTO,
                DiretoresParecidos = diretoresDTO,
                ObrasRelacionadas = obrasRelacionadas
            };
        }




        public class RecomendacaoElencoDTO
        {
            public Guid IdElenco { get; set; }
            public string NomeElenco { get; set; } = "";
            public List<AtorDTO> AtoresParecidos { get; set; } = new();
            public List<DiretorDTO> DiretoresParecidos { get; set; } = new();
            public List<ObraDTO> ObrasRelacionadas { get; set; } = new();
        }

        public class RecommendationBundleDTO
        {
            public List<ObraDTO> Obras { get; set; } = new();
            public List<AtorDTO> Atores { get; set; } = new();
            public List<DiretorDTO> Diretores { get; set; } = new();
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
