using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using Revisu.Data;
using Revisu.Domain.Dtos;
using Revisu.Domain.Entities;


    /// <summary>
    /// Serviço otimizado:
    /// - BuildFeatureCacheAsync(): constrói cache de features por obra (executar OFFLINE periodicamente).
    /// - TrainAndSaveModelAsync(): treina FastTree e salva modelo (OFFLINE).
    /// - RecomendarObrasAsync(): usa cache + modelo salvo em disco (ou heurística) e retorna DTOs.
    /// 
    /// Uso ideal:
    /// 1) Agende BuildFeatureCacheAsync() (diariamente).
    /// 2) Agende TrainAndSaveModelAsync() (após Build).
    /// 3) Em runtime, chame RecomendarObrasAsync() — será muito rápido.
    /// </summary>
    public class RecomendacaoServiceOptimizado
    {
        private readonly AppDbContext _db;
        private readonly string _cachePath = Path.Combine("App_Data", "obra_features_cache.json");
        private readonly string _modelPath = Path.Combine("App_Data", "recommender_model.zip");
        private readonly MLContext _ml;
        private ITransformer? _model;
        private ConcurrentDictionary<Guid, ObraFeatureCached> _cache = new();

        public RecomendacaoServiceOptimizado(AppDbContext db)
        {
            _db = db;
            _ml = new MLContext(seed: 0);
            Directory.CreateDirectory("App_Data");
        }

        #region Build cache (offline)
        /// <summary>
        /// Constrói o cache com features simplificadas para cada obra.
        /// Deve ser rodado offline (console job / admin endpoint).
        /// </summary>
        public async Task BuildFeatureCacheAsync(CancellationToken cancellationToken = default)
        {
            // Carrega apenas colunas necessárias (projeção) - evita includes caros.
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

            var cache = new ConcurrentDictionary<Guid, ObraFeatureCached>();

            // Pre-compile regex and tokenization util
            Regex tokenRegex = new Regex(@"[^\w\s]", RegexOptions.Compiled);
            Func<string, HashSet<string>> toTokenSet = (s) =>
            {
                if (string.IsNullOrWhiteSpace(s)) return new HashSet<string>();
                var cleaned = tokenRegex.Replace(s.ToLowerInvariant(), " ");
                var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                    .Where(t => t.Length > 2)   // small words removed
                                    .Select(t => t.Trim());
                return new HashSet<string>(tokens);
            };

            // Parallel processing to speed up (utiliza todos os CPUs)
            var bag = new ConcurrentBag<ObraFeatureCached>();
            await Task.Run(() =>
            {
                Parallel.ForEach(obras, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, o =>
                {
                    var sinopseTokens = toTokenSet(o.Sinopse.Length > 1000 ? o.Sinopse[..1000] : o.Sinopse); // trim
                    var feat = new ObraFeatureCached
                    {
                        IdObra = o.IdObra,
                        IdTmdb = o.IdTmdb,
                        Nome = o.Nome,
                        Imagem = o.Imagem,
                        NotaMedia = o.NotaMedia,
                        Popularidade = o.Popularidade ?? 0f,
                        Tipo = o.Tipo,
                        Generos = o.Generos.ToArray(),
                        ElencoIds = o.Elenco.ToArray(),
                        SinopseTokens = sinopseTokens.ToArray()
                    };
                    bag.Add(feat);
                });
            }, cancellationToken);

            foreach (var item in bag)
                cache[item.IdObra] = item;

            // persiste em disco (JSON)
            using (var fs = File.Create(_cachePath))
            {
                await JsonSerializer.SerializeAsync(fs, cache.Values.ToList(), new JsonSerializerOptions { WriteIndented = false }, cancellationToken);
            }

            // atualiza cache em memória
            _cache = new ConcurrentDictionary<Guid, ObraFeatureCached>(cache);
        }
        #endregion

        #region Train model (offline)
        /// <summary>
        /// Treina um modelo FastTree (ou outro) usando as features geradas no cache
        /// e salva em disco. Deve ser executado OFFLINE.
        /// </summary>
        public async Task TrainAndSaveModelAsync(CancellationToken cancellationToken = default)
        {
            // carrega cache (exigido)
            if (!File.Exists(_cachePath))
                throw new InvalidOperationException("Cache não encontrado. Rode BuildFeatureCacheAsync primeiro.");

            var cachedList = JsonSerializer.Deserialize<List<ObraFeatureCached>>(await File.ReadAllTextAsync(_cachePath, cancellationToken))
                             ?? new List<ObraFeatureCached>();

            // Monta dataset simples: label = 1 se obra tem NotaMedia >= 7 (exemplo)
            // Melhor: use biblioteca real do usuário para gerar labels reais; aqui usamos heurística/placeholder.
            var training = cachedList
                .Select(c => new ObraFeature
                {
                    GeneroSimilarity = 0f,
                    ElencoSimilarity = 0f,
                    SinopseSimilarity = 0f,
                    NotaMedia = c.NotaMedia,
                    Popularidade = c.Popularidade,
                    Tipo = c.Tipo,
                    Label = c.NotaMedia >= 7f // placeholder label: "boa obra"
                })
                .ToList();

            var data = _ml.Data.LoadFromEnumerable(training);

            var pipeline = _ml.Transforms.Categorical.OneHotEncoding("Tipo")
                .Append(_ml.Transforms.Concatenate("Features",
                    nameof(ObraFeature.GeneroSimilarity),
                    nameof(ObraFeature.ElencoSimilarity),
                    nameof(ObraFeature.SinopseSimilarity),
                    nameof(ObraFeature.NotaMedia),
                    nameof(ObraFeature.Popularidade),
                    "Tipo"))
                .Append(_ml.BinaryClassification.Trainers.FastTree(numberOfLeaves: 50, numberOfTrees: 200));

            var model = pipeline.Fit(data);

            // salva
            using var fs = File.Create(_modelPath);
            _ml.Model.Save(model, data.Schema, fs);

            // mantém em memória
            _model = model;
        }
        #endregion

        #region Recomendar (rápido)
        /// <summary>
        /// Recomenda obras para um usuário usando:
        /// - modelo carregado em disco (rápido) OR
        /// - heurística ponderada se não houver modelo
        /// </summary>
        public async Task<List<ObraDTO>> RecomendarObrasAsync(Guid idUsuario, int quantidade = 10, CancellationToken cancellationToken = default)
        {
            // Carrega cache em memória se necessário
            if (_cache == null || !_cache.Any())
                await LoadCacheIntoMemoryAsync(cancellationToken);

            // Carrega modelo se existir
            if (_model == null && File.Exists(_modelPath))
            {
                using var fs = File.OpenRead(_modelPath);
                _model = _ml.Model.Load(fs, out _);
            }

            // Carrega biblioteca do usuário (somente ids)
            var biblioteca = await _db.Biblioteca
                .Where(b => b.IdUsuario == idUsuario && !b.Excluido)
                .Select(b => new { b.IdObra, b.IdElenco })
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var obrasAssistidasIds = new HashSet<Guid>(biblioteca.Where(x => x.IdObra.HasValue).Select(x => x.IdObra!.Value));
            var elencoUsuario = biblioteca.Where(b => b.IdElenco.HasValue).Select(b => b.IdElenco!.Value).ToList();

            // perfil do usuário: contagem de generos e elenco
            var obrasAssistidasCached = _cache.Values.Where(c => obrasAssistidasIds.Contains(c.IdObra)).ToList();

            var generosFavCounts = obrasAssistidasCached.SelectMany(x => x.Generos)
                .GroupBy(g => g)
                .ToDictionary(g => g.Key, g => g.Count());

            var elencoFavCounts = obrasAssistidasCached.SelectMany(x => x.ElencoIds)
                .GroupBy(e => e)
                .ToDictionary(g => g.Key, g => g.Count());

            // prepara predição (se modelo existe)
            PredictionEngine<ObraFeature, ObraPrediction>? engine = null;
            if (_model != null)
                engine = _ml.Model.CreatePredictionEngine<ObraFeature, ObraPrediction>(_model);

            // scoring paralelo sobre cache (muitos itens, mas só memória)
            var scored = new ConcurrentBag<(Guid IdObra, float Score)>();

            // preparar local copies for speed
            var cacheValues = _cache.Values.ToArray();
            var userGenreKeys = new HashSet<Guid>(generosFavCounts.Keys);
            var userElencoKeys = new HashSet<Guid>(elencoFavCounts.Keys);

            Parallel.ForEach(cacheValues, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, obraFeat =>
            {
                if (cancellationToken.IsCancellationRequested) return;

                // skip if user already watched
                if (obrasAssistidasIds.Contains(obraFeat.IdObra)) return;

                // compute features fast using sets and arrays prepared in cache
                float generoSim = 0f;
                if (userGenreKeys.Count > 0 && obraFeat.Generos != null && obraFeat.Generos.Length > 0)
                {
                    var inter = obraFeat.Generos.Count(g => userGenreKeys.Contains(g));
                    var union = obraFeat.Generos.Length + userGenreKeys.Count;
                    generoSim = union == 0 ? 0f : (float)inter / union;
                }

                float elencoSim = 0f;
                if (userElencoKeys.Count > 0 && obraFeat.ElencoIds != null && obraFeat.ElencoIds.Length > 0)
                {
                    var inter = obraFeat.ElencoIds.Count(e => userElencoKeys.Contains(e));
                    elencoSim = userElencoKeys.Count == 0 ? 0f : (float)inter / userElencoKeys.Count;
                }

                // sinopse similarity using jaccard tokens (fast array ops)
                float sinopseSim = 0f;
                if (obraFeat.SinopseTokens?.Length > 0 && obrasAssistidasCached.Count > 0)
                {
                    // build user token set once using assistidas cache (could be cached per request if many)
                    var userTokens = new HashSet<string>(
                        obrasAssistidasCached.SelectMany(a => a.SinopseTokens).Distinct()
                    );
                    var inter = obraFeat.SinopseTokens.Count(t => userTokens.Contains(t));
                    var union = userTokens.Count + obraFeat.SinopseTokens.Length;
                    sinopseSim = union == 0 ? 0f : (float)inter / union;
                }

                var featureInstance = new ObraFeature
                {
                    GeneroSimilarity = generoSim,
                    ElencoSimilarity = elencoSim,
                    SinopseSimilarity = sinopseSim,
                    NotaMedia = obraFeat.NotaMedia,
                    Popularidade = obraFeat.Popularidade,
                    Tipo = obraFeat.Tipo
                };

                float score = 0f;

                if (engine != null)
                {
                    // model prediction
                    try
                    {
                        var pred = engine.Predict(featureInstance);
                        score = pred.Score;
                    }
                    catch
                    {
                        // fallback heuristic if prediction fails
                        score = HeuristicScore(featureInstance);
                    }
                }
                else
                {
                    score = HeuristicScore(featureInstance);
                }

                scored.Add((obraFeat.IdObra, score));
            });

            // take top N
            var top = scored
                .OrderByDescending(t => t.Score)
                .Take(quantidade)
                .Select(t => t.IdObra)
                .ToList();

            // prepare DTO results by fetching minimal metadata for chosen ids
            var topMeta = await _db.Obras
                .Where(o => top.Contains(o.IdObra))
                .Select(o => new ObraDTO
                {
                    IdObra = o.IdObra,
                    IdTmdb = o.IdTmdb,
                    Titulo = o.Nome,
                    Imagem = o.Imagem,
                    NotaMedia = o.NotaMedia,
                    Tipo = o.Tipo,
                    Generos = o.Generos.Select(g => g.Nome).ToList()
                })
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // preserve order (top list ordering)
            var ordered = top.Select(id => topMeta.First(m => m.IdObra == id)).ToList();

            return ordered;
        }

        private float HeuristicScore(ObraFeature f)
        {
            // fall back simple weighted score
            // weights adjustable: give genre & elenco higher weight
            return f.GeneroSimilarity * 2.5f
                 + f.ElencoSimilarity * 3.0f
                 + f.SinopseSimilarity * 1.5f
                 + (f.NotaMedia / 10f) * 1.2f
                 + (f.Popularidade / 100f) * 0.5f;
        }

        private async Task LoadCacheIntoMemoryAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_cachePath)) throw new InvalidOperationException("Cache not built. Run BuildFeatureCacheAsync first.");

            var list = JsonSerializer.Deserialize<List<ObraFeatureCached>>(await File.ReadAllTextAsync(_cachePath, cancellationToken))
                       ?? new List<ObraFeatureCached>();

            _cache = new ConcurrentDictionary<Guid, ObraFeatureCached>(list.ToDictionary(x => x.IdObra, x => x));
        }
        #endregion

        #region Helper DTOs/Classes
        private class ObraFeature
        {
            public float GeneroSimilarity { get; set; }
            public float ElencoSimilarity { get; set; }
            public float SinopseSimilarity { get; set; }
            public float NotaMedia { get; set; }
            public float Popularidade { get; set; }
            public string Tipo { get; set; } = "";
            public bool Label { get; set; }
        }

        private class ObraPrediction
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
        #endregion
    }
