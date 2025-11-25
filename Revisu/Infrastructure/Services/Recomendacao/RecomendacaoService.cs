using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using Microsoft.ML.Data;
using Revisu.Data;
using Revisu.Domain.Entities;
using System.Collections.Concurrent;

namespace Revisu.Recommendation
{
    public class RecomendacaoService
    {
        private readonly AppDbContext _db;
        private readonly GlobalRecommendationCache _cache;

        private readonly string ContentIndexPath = Path.Combine("App_Data", "content_index.json");
        private readonly string CfModelPath = Path.Combine("App_Data", "cf_model.zip");
        private readonly String RerankerPath = Path.Combine("App_Data", "reranker.zip");

        public RecomendacaoService(AppDbContext db, GlobalRecommendationCache cache)
        {
            _db = db;
            _cache = cache;

            Directory.CreateDirectory("App_Data");
        }

        // -------------------------------------------------------------------
        // 1) Construir vetores de sinopse (conteúdo)
        // -------------------------------------------------------------------
        public async Task BuildContentIndexAsync()
        {
            var obras = await _db.Obras
                .AsNoTracking()
                .Where(o => !string.IsNullOrWhiteSpace(o.Sinopse) && o.NotaMedia > 0)
                .Select(o => new { o.IdObra, o.Sinopse })
                .ToListAsync();

            var docs = obras.Select(o => new TextDoc
            {
                Id = o.IdObra.ToString(),
                Text = o.Sinopse
            });

            var ml = _cache.ML;
            var data = ml.Data.LoadFromEnumerable(docs);

            var pipeline =
                ml.Transforms.Text.NormalizeText("Norm", nameof(TextDoc.Text))
                .Append(ml.Transforms.Text.TokenizeIntoWords("Tok", "Norm"))
                .Append(ml.Transforms.Text.RemoveDefaultStopWords("Tok2", "Tok"))
                .Append(ml.Transforms.Conversion.MapValueToKey("TokKeys", "Tok2"))
                .Append(ml.Transforms.Text.ProduceHashedNgrams("Features", "TokKeys", numberOfBits: 12));

            var model = pipeline.Fit(data);
            var transformed = model.Transform(data);

            var vectors = transformed.GetColumn<float[]>("Features").ToArray();
            var ids = transformed.GetColumn<string>("Id").ToArray();

            var dict = new ConcurrentDictionary<Guid, float[]>();
            for (int i = 0; i < ids.Length; i++)
                dict.TryAdd(Guid.Parse(ids[i]), vectors[i]);

            await File.WriteAllTextAsync(ContentIndexPath,
                System.Text.Json.JsonSerializer.Serialize(dict));

            _cache.ContentVectors = dict;
        }


        // -------------------------------------------------------------------
        // 2) Treinar modelo de filtro colaborativo (Matrix Factorization)
        // -------------------------------------------------------------------
        public async Task TrainCfModelAsync()
        {
            var ml = _cache.ML;

            var interactions = await _db.Biblioteca
                .Where(x => x.IdUsuario != null && x.IdObra != null && !x.Excluido)
                .Select(x => new InteractionRecord
                {
                    UserId = x.IdUsuario!.ToString(),
                    ItemId = x.IdObra!.ToString(),
                    Label = 1f
                })
                .ToListAsync();

            var data = ml.Data.LoadFromEnumerable(interactions);

            var pipeline =
                ml.Transforms.Conversion.MapValueToKey("UserIdEncoded", nameof(InteractionRecord.UserId))
                .Append(ml.Transforms.Conversion.MapValueToKey("ItemIdEncoded", nameof(InteractionRecord.ItemId)))
                .Append(ml.Recommendation().Trainers.MatrixFactorization(
                    labelColumnName: "Label",
                    matrixRowIndexColumnName: "UserIdEncoded",
                    matrixColumnIndexColumnName: "ItemIdEncoded",
                    numberOfIterations: 40,
                    approximationRank: 32
                ));

            var model = pipeline.Fit(data);

            ml.Model.Save(model, data.Schema, File.Create(CfModelPath));

            _cache.CollaborativeModel = model;
            _cache.CollaborativeSchema = data.Schema;

            _cache.CfEngine = ml.Model.CreatePredictionEngine<InteractionRecord, CfPrediction>(model);
        }

        // -------------------------------------------------------------------
        // 3) Treinar reranker (combina: CF + conteúdo + gênero + elenco)
        // -------------------------------------------------------------------
        public async Task TrainRerankerAsync()
        {
            var ml = _cache.ML;
            await EnsureCacheLoadedAsync();

            var users = await _db.Biblioteca
                .Where(x => !x.Excluido)
                .Select(x => x.IdUsuario)
                .Distinct()
                .ToListAsync();

            var allObras = await _db.Obras.AsNoTracking().ToListAsync();
            var training = new List<RerankRecord>();

            foreach (var user in users)
            {
                if (user == null) continue;

                // Obras assistidas (positivas)
                var pos = await _db.Biblioteca
                    .Where(x => x.IdUsuario == user && x.IdObra != null)
                    .Select(x => x.IdObra!.Value)
                    .ToListAsync();

                // Exemplo positivo
                foreach (var obra in pos)
                {
                    var row = await BuildRow(user.Value.ToString(), obra, true);
                    training.Add(row);
                }

                // Criando negativos (obras não vistas)
                var negCandidates = allObras
                    .Where(o => !pos.Contains(o.IdObra))
                    .OrderBy(x => Guid.NewGuid())
                    .Take(pos.Count * 2)          // 2 negativos para cada positivo
                    .ToList();

                foreach (var obra in negCandidates)
                {
                    var row = await BuildRow(user.Value.ToString(), obra.IdObra, false);
                    training.Add(row);
                }
            }

            // Carregar no ML.NET
            var data = ml.Data.LoadFromEnumerable(training);

            var pipeline =
                ml.Transforms.Concatenate("Features",
                    nameof(RerankRecord.CfScore),
                    nameof(RerankRecord.SinopseSim),
                    nameof(RerankRecord.GenresSim),
                    nameof(RerankRecord.ElencoSim),
                    nameof(RerankRecord.Nota),
                    nameof(RerankRecord.Pop)
                )
                .Append(ml.BinaryClassification.Trainers.FastTree());

            var model = pipeline.Fit(data);

            ml.Model.Save(model, data.Schema, File.Create(RerankerPath));

            _cache.RerankerModel = model;
            _cache.RerankerSchema = data.Schema;
            _cache.RerankEngine = ml.Model.CreatePredictionEngine<RerankRecord, RerankerPrediction>(model);
        }


        // -------------------------------------------------------------------
        // 4) Recomendar para um usuário
        // -------------------------------------------------------------------
        //public async Task<List<Obras>> RecomendarAsync(Guid idUsuario, int top = 10)
        //{
        //    await EnsureCacheLoadedAsync();

        //    var cf = _cache.CfEngine;
        //    var rerank = _cache.RerankEngine;

        //    var watched = await _db.Biblioteca
        //        .Where(x => x.IdUsuario == idUsuario && !x.Excluido && x.IdObra != null)
        //        .Select(x => x.IdObra!.Value)
        //        .ToListAsync();

        //    var all = await _db.Obras.AsNoTracking().ToListAsync();

        //    var candidates = all
        //        .Where(o => !watched.Contains(o.IdObra))
        //        .ToList();

        //    var scored = new ConcurrentBag<(Obras obra, float score)>();

        //    Parallel.ForEach(candidates, c =>
        //    {
        //        var row = BuildRow(idUsuario.ToString(), c.IdObra, 0).Result;

        //        float score =
        //            rerank != null
        //            ? rerank.Predict(row).Probability
        //            : cf?.Predict(new InteractionRecord
        //            {
        //                UserId = idUsuario.ToString(),
        //                ItemId = c.IdObra.ToString()
        //            }).Score ?? 0;

        //        scored.Add((c, score));
        //    });

        //    return scored
        //        .OrderByDescending(x => x.score)
        //        .Take(top)
        //        .Select(x => x.obra)
        //        .ToList();
        //}

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------
        private async Task EnsureCacheLoadedAsync()
        {
            if (_cache.ContentVectors.Count == 0 && File.Exists(ContentIndexPath))
            {
                var dict = System.Text.Json.JsonSerializer
                    .Deserialize<ConcurrentDictionary<Guid, float[]>>(await File.ReadAllTextAsync(ContentIndexPath));

                if (dict != null)
                    _cache.ContentVectors = dict;
            }

            if (_cache.CollaborativeModel == null && File.Exists(CfModelPath))
            {
                using var fs = File.OpenRead(CfModelPath);
                _cache.CollaborativeModel = _cache.ML.Model.Load(fs, out var schema);
                _cache.CollaborativeSchema = schema;
                _cache.CfEngine = _cache.ML.Model.CreatePredictionEngine<InteractionRecord, CfPrediction>(_cache.CollaborativeModel);
            }

            if (_cache.RerankerModel == null && File.Exists(RerankerPath))
            {
                using var fs = File.OpenRead(RerankerPath);
                _cache.RerankerModel = _cache.ML.Model.Load(fs, out var schema);
                _cache.RerankerSchema = schema;
                _cache.RerankEngine = _cache.ML.Model.CreatePredictionEngine<RerankRecord, RerankerPrediction>(_cache.RerankerModel);
            }
        }

        private async Task<RerankRecord> BuildRow(string user, Guid obraId, bool label)
        {
            var obra = await _db.Obras
                .Include(o => o.Generos)
                .Include(o => o.Elenco)
                .FirstAsync(o => o.IdObra == obraId);

            var userGenres = await _db.Biblioteca
                .Where(x => x.IdUsuario!.ToString() == user && x.IdObra != null)
                .SelectMany(x => x.Filmes.Generos.Select(g => g.IdGenero))
                .ToListAsync();

            var userElenco = await _db.Biblioteca
                .Where(x => x.IdUsuario!.ToString() == user && x.IdObra != null)
                .SelectMany(x => x.Filmes.Elenco.Select(e => e.IdElenco))
                .ToListAsync();

            float cfScore = _cache.CfEngine?.Predict(new InteractionRecord
            {
                UserId = user,
                ItemId = obraId.ToString()
            }).Score ?? 0f;

            float sinopseSim = 0f;
            if (_cache.ContentVectors.TryGetValue(obraId, out var vec))
            {
                var watched = await _db.Biblioteca
                    .Where(x => x.IdUsuario!.ToString() == user && x.IdObra != null)
                    .Select(x => x.IdObra!.Value)
                    .ToListAsync();

                float sum = 0;
                int count = 0;
                foreach (var w in watched)
                {
                    if (_cache.ContentVectors.TryGetValue(w, out var other))
                    {
                        sum += Cosine(vec, other);
                        count++;
                    }
                }
                if (count > 0)
                    sinopseSim = sum / count;
            }

            if (float.IsNaN(sinopseSim))
                sinopseSim = 0;

            return new RerankRecord
            {
                User = user,
                Item = obraId.ToString(),
                Label = label,    // Agora bool
                CfScore = cfScore,
                SinopseSim = sinopseSim,
                GenresSim = Jaccard(obra.Generos.Select(g => g.IdGenero), userGenres),
                ElencoSim = Jaccard(obra.Elenco.Select(e => e.IdElenco), userElenco),
                Nota = obra.NotaMedia,
                Pop = obra.Populariedade ?? 0
            };
        }


        private static float Jaccard(IEnumerable<Guid> a, IEnumerable<Guid> b)
        {
            var s1 = new HashSet<Guid>(a);
            var s2 = new HashSet<Guid>(b);
            if (s1.Count == 0 || s2.Count == 0) return 0f;
            return (float)s1.Intersect(s2).Count() / s1.Union(s2).Count();
        }

        private static float Cosine(float[] a, float[] b)
        {
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }
            return na == 0 || nb == 0 ? 0 : (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb)));
        }

        private class TextDoc { public string Id { get; set; } = ""; public string Text { get; set; } = ""; }
    }

    public class InteractionRecord { public string UserId { get; set; } = ""; public string ItemId { get; set; } = ""; public float Label { get; set; } }
    public class CfPrediction { public float Score { get; set; } }
    public class RerankRecord
    {
        public string User { get; set; } = "";
        public string Item { get; set; } = "";
        public bool Label { get; set; }
        public float CfScore { get; set; }
        public float SinopseSim { get; set; }
        public float GenresSim { get; set; }
        public float ElencoSim { get; set; }
        public float Nota { get; set; }
        public float Pop { get; set; }
    }


    public class RerankerPrediction { public float Probability { get; set; } }
}
