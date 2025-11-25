using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using Microsoft.ML.Data;
using Revisu.Data;
using Revisu.Domain.Dtos;
using Revisu.Domain.Entities;

public class RecomendacaoHibridaService
{
    private readonly AppDbContext _db;
    private readonly string _contentIndexPath = Path.Combine("App_Data","content_index.json");
    private readonly string _cfModelPath = Path.Combine("App_Data","cf_model.zip");
    private readonly string _rerankerModelPath = Path.Combine("App_Data","reranker_model.zip");
    private readonly MLContext _ml;

    // caches in memory
    private ConcurrentDictionary<Guid, float[]> _contentVectors = new();
    private ITransformer? _cfModel;
    private ITransformer? _rerankerModel;
    private DataViewSchema _cfSchema;
    private DataViewSchema _rerankerSchema;

    public RecomendacaoHibridaService(AppDbContext db)
    {
        _db = db;
        _ml = new MLContext(seed: 0);
        Directory.CreateDirectory("App_Data");
    }

    // -------------------
    // 1) Build content index (offline)
    // -------------------
    public async Task BuildContentIndexAsync(CancellationToken cancellationToken = default)
    {
        // Carrega obras com sinopse válida
        var obras = await _db.Obras
            .Where(o => o.NotaMedia > 0 && !string.IsNullOrWhiteSpace(o.Sinopse))
            .Select(o => new { o.IdObra, o.Sinopse })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Normaliza documentos
        var docs = obras.Select(o => new TextDoc
        {
            Id = o.IdObra.ToString(),
            Text = o.Sinopse.Length > 2000 ? o.Sinopse.Substring(0, 2000) : o.Sinopse
        }).ToList();

        var data = _ml.Data.LoadFromEnumerable(docs);

        var pipeline =
            _ml.Transforms.Text.NormalizeText("NormText", nameof(TextDoc.Text))
            .Append(_ml.Transforms.Text.TokenizeIntoWords("Tokens", "NormText"))
            .Append(_ml.Transforms.Text.RemoveDefaultStopWords(
                outputColumnName: "TokensNoStop",
                inputColumnName: "Tokens"))
            // CONVERSÃO NECESSÁRIA
            .Append(_ml.Transforms.Conversion.MapValueToKey(
                outputColumnName: "TokensNoStopKey",
                inputColumnName: "TokensNoStop"))
            // AGORA PODE FAZER NGRAMS
            .Append(_ml.Transforms.Text.ProduceHashedNgrams(
                outputColumnName: "Features",
                inputColumnName: "TokensNoStopKey",
                numberOfBits: 10
            ));




        var model = pipeline.Fit(data);
        var transformed = model.Transform(data);

        // Extrai vetores
        var features = transformed.GetColumn<float[]>("Features").ToArray();
        var ids = transformed.GetColumn<string>("Id").ToArray();

        var dict = new Dictionary<Guid, float[]>();
        for (int i = 0; i < ids.Length; i++)
            dict[Guid.Parse(ids[i])] = features[i];

        // Salva JSON
        using var fs = File.Create(_contentIndexPath);
        await JsonSerializer.SerializeAsync(fs, dict, new JsonSerializerOptions { WriteIndented = false });

        // Carrega em memória
        _contentVectors = new ConcurrentDictionary<Guid, float[]>(dict);
    }

    // -------------------
    // 2) Train collaborative filtering (Matrix Factorization) (offline)
    // -------------------
    public async Task TrainCfModelAsync(CancellationToken cancellationToken = default)
    {
        // Load interactions: positive = in Biblioteca (not Excluido)
        var positives = await _db.Biblioteca
            .Where(b => b.IdUsuario != null && b.IdObra != null && !b.Excluido)
            .Select(b => new InteractionRecord { UserId = b.IdUsuario!.ToString(), ItemId = b.IdObra!.ToString(), Label = 1f })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Negative sampling: for each user sample some random items not in their library
        var allObraIds = await _db.Obras.AsNoTracking().Select(o => o.IdObra).ToListAsync(cancellationToken);
        var rnd = new Random(0);
        var negatives = new List<InteractionRecord>();
        var userGroups = positives.GroupBy(p => p.UserId);
        foreach (var g in userGroups)
        {
            var positiveIds = new HashSet<string>(g.Select(x => x.ItemId));
            int negativesPerUser = Math.Min(positiveIds.Count * 2, 100); // hyperparam
            var candidates = allObraIds.Where(id => !positiveIds.Contains(id.ToString())).ToList();
            for (int i = 0; i < negativesPerUser && candidates.Count>0; i++)
            {
                var pick = candidates[rnd.Next(candidates.Count)];
                candidates.Remove(pick);
                negatives.Add(new InteractionRecord { UserId = g.Key, ItemId = pick.ToString(), Label = 0f });
            }
        }

        var training = positives.Concat(negatives).ToList();
        var data = _ml.Data.LoadFromEnumerable(training);

        // Map keys for matrix factorization
        var pipeline =
            _ml.Transforms.Conversion.MapValueToKey("UserIdEncoded", nameof(InteractionRecord.UserId))
            .Append(_ml.Transforms.Conversion.MapValueToKey("ItemIdEncoded", nameof(InteractionRecord.ItemId)))
            .Append(_ml.Recommendation().Trainers.MatrixFactorization(
                labelColumnName: nameof(InteractionRecord.Label),
                matrixColumnIndexColumnName: "UserIdEncoded",
                matrixRowIndexColumnName: "ItemIdEncoded",
                numberOfIterations: 40,
                approximationRank: 32
            ));

        var model = pipeline.Fit(data);

        using (var fs = File.Create(_cfModelPath))
            _ml.Model.Save(model, data.Schema, fs);

        _cfModel = model;
    }

    // -------------------
    // 3) Train reranker (offline) — combina CF + conteúdo + features (heavier)
    // -------------------
    public async Task TrainRerankerAsync(int candidatesPerUser = 200, CancellationToken cancellationToken = default)
    {
        // Ensure content index & cf model exist
        if (!File.Exists(_contentIndexPath)) throw new InvalidOperationException("Content index missing. Run BuildContentIndexAsync first.");
        if (!File.Exists(_cfModelPath)) throw new InvalidOperationException("CF model missing. Run TrainCfModelAsync first.");

        // Load caches
        await LoadContentIndexToMemoryAsync();
        using (var fs = File.OpenRead(_cfModelPath))
            _cfModel = _ml.Model.Load(fs, out _cfSchema);

        var cfEngine = _ml.Model.CreatePredictionEngine<InteractionRecord, CfPrediction>(_cfModel);

        // Prepare users list (those with historial)
        var users = await _db.Biblioteca.Where(b => b.IdUsuario != null && b.IdObra != null && !b.Excluido)
            .AsNoTracking()
            .Select(b => b.IdUsuario)
            .Distinct()
            .ToListAsync(cancellationToken);

        var allObraIds = (await _db.Obras.AsNoTracking().Select(o => o.IdObra).ToListAsync(cancellationToken)).ToArray();

        var rnd = new Random(0);
        var trainingRows = new List<RerankRecord>();

        foreach (var userGuid in users)
        {
            var uid = userGuid!.ToString();
            // user's positives
            var positives = await _db.Biblioteca
                .Where(b => b.IdUsuario == userGuid && b.IdObra != null && !b.Excluido)
                .AsNoTracking()
                .Select(b => b.IdObra!.Value)
                .ToListAsync(cancellationToken);

            if (!positives.Any()) continue;

            // CF candidates: score all items? expensive. Instead sample a subset and compute CF score
            var candidates = new HashSet<Guid>();

            // CF top: score random sample and pick top (optimized)
            var sample = allObraIds.OrderBy(_ => rnd.Next()).Take(200).ToArray();
            var scored = new List<(Guid id, float score)>();
            foreach (var oid in sample)
            {
                var pred = cfEngine.Predict(new InteractionRecord { UserId = uid, ItemId = oid.ToString() });
                scored.Add((oid, pred.Score));
            }
            foreach (var t in scored.OrderByDescending(s => s.score).Take(candidatesPerUser/2))
                candidates.Add(t.id);

            // content neighbors: for each positive pick top content-similar
            foreach (var pos in positives)
            {
                if (!_contentVectors.TryGetValue(pos, out var vec)) continue;
                // compute cosine with small random subset for speed
                var sample2 = allObraIds.OrderBy(_ => rnd.Next()).Take(1000).ToArray();
                var simList = new List<(Guid id, float sim)>();
                foreach (var oid in sample2)
                {
                    if (!_contentVectors.TryGetValue(oid, out var v2)) continue;
                    var sim = Cosine(vec, v2);
                    simList.Add((oid, sim));
                }
                foreach (var t in simList.OrderByDescending(x=>x.sim).Take(10))
                    candidates.Add(t.id);
            }

            // now we have candidates (union). For training we need labeled pairs
            var negatives = candidates.Where(c => !positives.Contains(c)).ToList();
            var positivesIntersect = candidates.Where(c => positives.Contains(c)).ToList();

            // add positive rows
            foreach (var p in positivesIntersect)
            {
                var row = await BuildRerankRow(uid, p, 1f, cfEngine);
                trainingRows.Add(row);
            }

            // add negative rows (sample some)
            var negSample = negatives.OrderBy(_=>rnd.Next()).Take(Math.Min(positivesIntersect.Count*3, negatives.Count)).ToList();
            foreach (var n in negSample)
            {
                var row = await BuildRerankRow(uid, n, 0f, cfEngine);
                trainingRows.Add(row);
            }
        }

        // Train reranker using FastTree (binary classification)
        var data = _ml.Data.LoadFromEnumerable(trainingRows);
        var pipeline = _ml.Transforms.Concatenate("Features",
                nameof(RerankRecord.CfScore),
                nameof(RerankRecord.ContentSim),
                nameof(RerankRecord.GenreJaccard),
                nameof(RerankRecord.ElencoJaccard),
                nameof(RerankRecord.NotaMedia),
                nameof(RerankRecord.Popularidade))
            .Append(_ml.BinaryClassification.Trainers.FastTree(numberOfLeaves:50, numberOfTrees:200));

        var model = pipeline.Fit(data);
        using var fs2 = File.Create(_rerankerModelPath);
        _ml.Model.Save(model, data.Schema, fs2);

        _rerankerModel = model;
    }

    // -------------------
    // 4) Runtime: Recommend hybrid
    // -------------------
    public async Task<List<ObraDTO>> RecommendHybridAsync(Guid idUsuario, int topN = 10, CancellationToken cancellationToken = default)
    {
        // load caches/models if needed
        await LoadContentIndexToMemoryAsync();
        if (_cfModel == null && File.Exists(_cfModelPath)) { using var f = File.OpenRead(_cfModelPath); _cfModel = _ml.Model.Load(f, out _cfSchema); }
        if (_rerankerModel == null && File.Exists(_rerankerModelPath)) { using var f = File.OpenRead(_rerankerModelPath); _rerankerModel = _ml.Model.Load(f, out _rerankerSchema); }

        var cfEngine = _cfModel != null ? _ml.Model.CreatePredictionEngine<InteractionRecord, CfPrediction>(_cfModel) : null;
        var rerankerEngine = _rerankerModel != null ? _ml.Model.CreatePredictionEngine<RerankRecord, RerankerPrediction>(_rerankerModel) : null;

        // user history & profile
        var biblioteca = await _db.Biblioteca
            .Where(b => b.IdUsuario == idUsuario && !b.Excluido)
            .Select(b => new { b.IdObra, b.IdElenco })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var watched = new HashSet<Guid>(biblioteca.Where(x => x.IdObra.HasValue).Select(x => x.IdObra!.Value));
        var watchedCached = _contentVectors.Where(kv => watched.Contains(kv.Key)).Select(kv => kv.Key).ToList();

        // candidate generation: CF top-K (score on all items) + content top-K around watched
        var allObraIds = (await _db.Obras.AsNoTracking().Select(o => o.IdObra).ToListAsync(cancellationToken)).ToArray();
        var candidates = new HashSet<Guid>();
        var rnd = new Random();

        // CF candidates - score a random subset or all depending on size
        if (cfEngine != null)
        {
            var sample = allObraIds.Length > 20000 ? allObraIds.OrderBy(_=>rnd.Next()).Take(20000).ToArray() : allObraIds;
            var cfScores = new ConcurrentBag<(Guid id, float score)>();
            Parallel.ForEach(sample, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount-1) }, oid =>
            {
                if (watched.Contains(oid)) return;
                var pred = cfEngine.Predict(new InteractionRecord { UserId = idUsuario.ToString(), ItemId = oid.ToString() });
                cfScores.Add((oid, pred.Score));
            });
            foreach (var t in cfScores.OrderByDescending(s=>s.score).Take(200))
                candidates.Add(t.id);
        }

        // Content neighbors
        foreach (var w in watchedCached)
        {
            if (!_contentVectors.TryGetValue(w, out var vec)) continue;
            // compare to subset
            var sample = allObraIds.OrderBy(_=>rnd.Next()).Take(5000).ToArray();
            var sims = new List<(Guid id, float sim)>();
            foreach (var oid in sample)
            {
                if (watched.Contains(oid)) continue;
                if (!_contentVectors.TryGetValue(oid, out var v2)) continue;
                sims.Add((oid, Cosine(vec, v2)));
            }
            foreach (var t in sims.OrderByDescending(x=>x.sim).Take(50))
                candidates.Add(t.id);
        }

        // For each candidate compute features and score with reranker
        var scoredList = new ConcurrentBag<(Guid id, float score)>();
        Parallel.ForEach(candidates, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount-1) }, oid =>
        {
            var rowTask = BuildRerankRow(idUsuario.ToString(), oid, -1f, _cfModel != null ? _ml.Model.CreatePredictionEngine<InteractionRecord, CfPrediction>(_cfModel) : null); // label not used
            rowTask.Wait();
            var row = rowTask.Result;

            float score = 0f;
            if (rerankerEngine != null)
            {
                var pred = rerankerEngine.Predict(row);
                score = pred.Probability; // binary classification probability
            }
            else if (_cfModel != null)
            {
                var pred = cfEngine.Predict(new InteractionRecord { UserId = idUsuario.ToString(), ItemId = oid.ToString() });
                score = pred.Score;
            }
            else
            {
                score = HeuristicHybridScore(row);
            }

            scoredList.Add((oid, score));
        });

        var top = scoredList.OrderByDescending(x=>x.score).Take(topN).Select(x=>x.id).ToArray();

        // fetch metadata
        var metas = await _db.Obras.Where(o=>top.Contains(o.IdObra))
            .Select(o => new ObraDTO {
                IdObra = o.IdObra,
                IdTmdb = o.IdTmdb,
                Titulo = o.Nome,
                Imagem = o.Imagem,
                NotaMedia = o.NotaMedia,
                Tipo = o.Tipo,
                Generos = o.Generos.Select(g=>g.Nome).ToList()
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // preserve order
        var ordered = top.Select(id => metas.First(m=>m.IdObra==id)).ToList();
        return ordered;
    }

    // -------------------
    // Helper: build training/rerank row
    // -------------------
    private async Task<RerankRecord> BuildRerankRow(string userIdStr, Guid obraId, float label, PredictionEngine<InteractionRecord, CfPrediction>? cfEngine)
    {
        // CF score
        float cfScore = 0f;
        if (cfEngine != null)
            cfScore = cfEngine.Predict(new InteractionRecord { UserId = userIdStr, ItemId = obraId.ToString() }).Score;

        // content sim: average similarity to watched items
        float contentSim = 0f;
        var userWatchedIds = await _db.Biblioteca.Where(b => b.IdUsuario != null && b.IdUsuario!.ToString() == userIdStr && b.IdObra != null && !b.Excluido)
            .AsNoTracking().Select(b => b.IdObra!.Value).ToListAsync();

        var watchedVecs = userWatchedIds.Where(id => _contentVectors.ContainsKey(id)).Select(id=>_contentVectors[id]).ToList();
        if (watchedVecs.Count>0 && _contentVectors.TryGetValue(obraId, out var vec))
        {
            float sum=0f;
            foreach(var wv in watchedVecs) sum += Cosine(vec, wv);
            contentSim = sum / watchedVecs.Count;
        }

        // genre jaccard & elenco jaccard
        var obraGenres = await _db.Obras.Where(o=>o.IdObra==obraId).SelectMany(o=>o.Generos.Select(g=>g.IdGenero)).ToListAsync();
        var userGenres = await _db.Biblioteca.Where(b=>b.IdUsuario!=null && b.IdUsuario!.ToString()==userIdStr && b.IdObra!=null && !b.Excluido)
            .SelectMany(b=>b.IdObra!=null ? _db.Obras.Where(o=>o.IdObra==b.IdObra).SelectMany(o=>o.Generos.Select(g=>g.IdGenero)) : Enumerable.Empty<Guid>())
            .ToListAsync();

        var genreJaccard = Jaccard(obraGenres, userGenres);

        // elenco
        var obraElenco = await _db.Obras.Where(o=>o.IdObra==obraId).SelectMany(o=>o.Elenco.Select(e=>e.IdElenco)).ToListAsync();
        var userElenco = await _db.Biblioteca.Where(b=>b.IdUsuario!=null && b.IdUsuario!.ToString()==userIdStr && b.IdObra!=null && !b.Excluido)
            .SelectMany(b=>_db.Obras.Where(o=>o.IdObra==b.IdObra).SelectMany(o=>o.Elenco.Select(e=>e.IdElenco))).ToListAsync();
        var elencoJaccard = Jaccard(obraElenco, userElenco);

        // nota & popularidade
        var meta = await _db.Obras.Where(o=>o.IdObra==obraId).Select(o=>new { o.NotaMedia, Popularidade = o.Populariedade ?? 0f }).AsNoTracking().FirstOrDefaultAsync();
        float nota = meta?.NotaMedia ?? 0f;
        float pop = meta?.Popularidade ?? 0f;

        return new RerankRecord
        {
            UserId = userIdStr,
            ItemId = obraId.ToString(),
            Label = label,
            CfScore = cfScore,
            ContentSim = contentSim,
            GenreJaccard = genreJaccard,
            ElencoJaccard = elencoJaccard,
            NotaMedia = nota,
            Popularidade = pop
        };
    }

    // -------------------
    // UTIL: load content index
    // -------------------
    private async Task LoadContentIndexToMemoryAsync()
    {
        if (_contentVectors.Any()) return;
        if (!File.Exists(_contentIndexPath)) throw new InvalidOperationException("Content index not found");
        var dict = JsonSerializer.Deserialize<Dictionary<Guid,float[]>>(await File.ReadAllTextAsync(_contentIndexPath)) ?? new Dictionary<Guid,float[]>();
        _contentVectors = new ConcurrentDictionary<Guid,float[]>(dict);
    }

    // -------------------
    // small helpers
    // -------------------
    private static float Cosine(float[] a, float[] b)
    {
        double dot = 0; double na=0; double nb=0;
        for(int i=0;i<Math.Min(a.Length,b.Length);i++){ dot += a[i]*b[i]; na += a[i]*a[i]; nb += b[i]*b[i]; }
        if (na==0 || nb==0) return 0f;
        return (float)(dot / (Math.Sqrt(na)*Math.Sqrt(nb)));
    }

    private static float Jaccard(IEnumerable<Guid> a, IEnumerable<Guid> b)
    {
        var s1 = new HashSet<Guid>(a);
        var s2 = new HashSet<Guid>(b);
        if (s1.Count==0 && s2.Count==0) return 0f;
        var inter = s1.Intersect(s2).Count();
        var uni = s1.Union(s2).Count();
        return uni==0?0f: (float)inter/uni;
    }

    private float HeuristicHybridScore(RerankRecord r)
    {
        return r.CfScore*1.0f + r.ContentSim*2.0f + r.GenreJaccard*3.0f + r.ElencoJaccard*4.0f + (r.NotaMedia/10f)*0.5f + (r.Popularidade/100f)*0.2f;
    }

    // -------------------
    // small DTOs
    // -------------------
    private class TextDoc { public string Id { get; set; } = ""; public string Text { get; set; } = ""; }
    private class InteractionRecord { public string UserId { get; set; } = ""; public string ItemId { get; set; } = ""; public float Label { get; set; } }
    private class CfPrediction { public float Score { get; set; } }
    private class RerankRecord {
        public string UserId { get; set; } = "";
        public string ItemId { get; set; } = "";
        public float Label { get; set; }
        public float CfScore { get; set; }
        public float ContentSim { get; set; }
        public float GenreJaccard { get; set; }
        public float ElencoJaccard { get; set; }
        public float NotaMedia { get; set; }
        public float Popularidade { get; set; }
    }
    private class RerankerPrediction { public bool PredictedLabel { get; set; } public float Score { get; set; } public float Probability { get; set; } }
}
