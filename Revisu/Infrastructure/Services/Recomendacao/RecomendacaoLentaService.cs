using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using Microsoft.ML.Transforms;
using Revisu.Data;
using Revisu.Domain.Dtos;
using Revisu.Domain.Entities;
using Revisu.Infrastructure.MachineLearn;
using System.Text.RegularExpressions;

public class RecomendacaoLentaService
{
    private readonly AppDbContext _db;

    public RecomendacaoLentaService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<ObraDTO>> RecomendarObrasAsync(Guid idUsuario, int quantidade = 10)
    {
        var bibliotecaUser = await _db.Biblioteca
            .Include(b => b.Filmes).ThenInclude(f => f.Generos)
            .Include(b => b.Filmes).ThenInclude(f => f.Elenco)
            .Where(b => b.IdUsuario == idUsuario && !b.Excluido)
            .ToListAsync();

        var obrasAssistidas = bibliotecaUser
            .Select(b => b.Filmes)
            .Distinct()
            .ToList();

        var todasObras = await _db.Obras
            .Where(o => o.NotaMedia > 0 && !string.IsNullOrWhiteSpace(o.Sinopse))
            .Include(o => o.Generos)
            .Include(o => o.Elenco)
            .ToListAsync();


        var obrasNaoAssistidas = todasObras
            .Where(o => !obrasAssistidas.Any(a => a.IdObra == o.IdObra))
            .ToList();

        // coletar preferências do usuário
        var generosFav = obrasAssistidas
            .SelectMany(o => o.Generos)
            .GroupBy(g => g.IdGenero)
            .ToDictionary(g => g.Key, g => g.Count());

        var atoresFav = obrasAssistidas
            .SelectMany(o => o.Elenco)
            .GroupBy(a => a.IdElenco)
            .ToDictionary(a => a.Key, a => a.Count());

        // montar dataset
        var dataset = new List<ObraFeature>();

        foreach (var obra in todasObras)
        {
            dataset.Add(new ObraFeature
            {
                GeneroSimilarity = CalcGeneroSimilarity(generosFav, obra),
                ElencoSimilarity = CalcElencoSimilarity(atoresFav, obra),
                SinopseSimilarity = CalcSinopseSimilarity(obrasAssistidas, obra),
                NotaMedia = obra.NotaMedia,
                Popularidade = obra.Populariedade ?? 0,
                Tipo = obra.Tipo,
                Label = obrasAssistidas.Any(o => o.IdObra == obra.IdObra)
            });
        }

        // --- ML.NET ---
        var ml = new MLContext();

        var data = ml.Data.LoadFromEnumerable(dataset);

        var pipeline = ml.Transforms.Categorical.OneHotEncoding("Tipo")
            .Append(ml.Transforms.Concatenate("Features",
                "GeneroSimilarity",
                "ElencoSimilarity",
                "SinopseSimilarity",
                "NotaMedia",
                "Popularidade",
                "Tipo"))
            .Append(ml.BinaryClassification.Trainers.FastTree());

        var model = pipeline.Fit(data);

        // aplica modelo nas obras não assistidas
        var engine = ml.Model.CreatePredictionEngine<ObraFeature, ObraPrediction>(model);

        var recomendadas = obrasNaoAssistidas
            .Select(o => new
            {
                Obra = o,
                Score = engine.Predict(new ObraFeature
                {
                    GeneroSimilarity = CalcGeneroSimilarity(generosFav, o),
                    ElencoSimilarity = CalcElencoSimilarity(atoresFav, o),
                    SinopseSimilarity = CalcSinopseSimilarity(obrasAssistidas, o),
                    NotaMedia = o.NotaMedia,
                    Popularidade = o.Populariedade ?? 0,
                    Tipo = o.Tipo
                }).Score
            })
            .OrderByDescending(x => x.Score)
            .Take(quantidade)
            .Select(x => x.Obra)
            .ToList();

        return recomendadas.Select(o => new ObraDTO
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

    // --------------------- MÉTRICAS -------------------------
    private float CalcGeneroSimilarity(Dictionary<Guid, int> generosFav, Obras obra)
    {
        if (!generosFav.Any()) return 0;

        var generosObra = obra.Generos.Select(g => g.IdGenero);
        var intersect = generosObra.Count(id => generosFav.ContainsKey(id));
        var union = generosObra.Count() + generosFav.Count;

        return (float)intersect / union;
    }

    private float CalcElencoSimilarity(Dictionary<Guid, int> atoresFav, Obras obra)
    {
        if (!atoresFav.Any()) return 0;

        var atoresObra = obra.Elenco.Select(a => a.IdElenco);
        var intersect = atoresObra.Count(id => atoresFav.ContainsKey(id));

        return (float)intersect / atoresFav.Count;
    }

    private float CalcSinopseSimilarity(List<Obras> assistidas, Obras obra)
    {
        if (!assistidas.Any()) return 0;

        string limpar(string s) => Regex.Replace(s.ToLower(), @"[^\w\s]", "");

        var sinopseUser = string.Join(" ", assistidas.Select(o => limpar(o.Sinopse)));
        var sinopseObra = limpar(obra.Sinopse);

        if (string.IsNullOrWhiteSpace(sinopseUser) || string.IsNullOrWhiteSpace(sinopseObra))
            return 0;

        var palavrasUser = sinopseUser.Split(' ').Distinct().ToList();
        var palavrasObra = sinopseObra.Split(' ').Distinct().ToList();

        var intersect = palavrasUser.Intersect(palavrasObra).Count();
        var union = palavrasUser.Union(palavrasObra).Count();

        return (float)intersect / union;
    }
}

public class ObraPrediction
{
    [ColumnName("Score")]
    public float Score { get; set; }
}
