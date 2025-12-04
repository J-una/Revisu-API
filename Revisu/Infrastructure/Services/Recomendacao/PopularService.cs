using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Revisu.Data;
using Revisu.Domain.Dtos;
using Revisu.Domain.Entities;
using Revisu.Infrastructure;

public class PopularService
{
    private readonly HttpClient _http;
    private readonly AppDbContext _db;
    private readonly string _apiKey;

    public PopularService(
        HttpClient httpClient,
        IOptions<TmdbSettings> tmdbSettings,
        IDbContextFactory<AppDbContext> factory)
    {
        _http = httpClient;
        _apiKey = tmdbSettings.Value.ApiKey;
        _db = factory.CreateDbContext();
    }

    public async Task<PopularesResultadoDTO> ObterPopularesAsync(Guid? idUsuario)
    {
        // Buscar vários IDs do TMDB 
        var filmes = await ObterTmdbIds("movie", 3);
        var series = await ObterTmdbIds("tv", 3);

        var ids = filmes.Concat(series).Distinct().ToList();

        // Buscar Obras filtradas e limitar a 50
        var obrasNoBanco = await _db.Obras
            .Include(o => o.Generos)
            .Where(o =>
                ids.Contains(o.IdTmdb) &&
                o.NotaMedia > 0 &&
                !string.IsNullOrWhiteSpace(o.Sinopse)
            )
            .OrderByDescending(o => o.Populariedade)
            .Take(50) 
            .ToListAsync();

        //  Obras e elenco marcados pelo usuário
        List<Guid> obrasMarcadas = new();
        List<Guid> elencoMarcados = new();

        if (idUsuario.HasValue && idUsuario.Value != Guid.Empty)
        {
            obrasMarcadas = await _db.Biblioteca
                .Where(b => b.IdUsuario == idUsuario.Value && b.IdObra != null && b.Excluido == false)
                .Select(b => b.IdObra!.Value)
                .ToListAsync();

            elencoMarcados = await _db.Biblioteca
                .Where(b => b.IdUsuario == idUsuario.Value && b.IdElenco != null && b.Excluido == false)
                .Select(b => b.IdElenco!.Value)
                .ToListAsync();
        }

        // Atores e Diretores
        var elencoQuery = _db.Elencos
            .Include(e => e.Obras)
                .ThenInclude(o => o.Generos)
            .OrderByDescending(e => e.Popularidade);

        var atores = await elencoQuery
            .Where(e => e.Cargo == "Ator")
            .Take(50)
            .ToListAsync();

        var diretores = await elencoQuery
            .Where(e => e.Cargo == "Diretor")
            .Take(50)
            .ToListAsync();

        // DTO Final
        return new PopularesResultadoDTO
        {
            Obras = obrasNoBanco.Select(o => new ObraDTO
            {
                IdObra = o.IdObra,
                IdTmdb = o.IdTmdb,
                Titulo = o.Nome,
                Imagem = o.Imagem,
                NotaMedia = o.NotaMedia,
                Tipo = o.Tipo,
                Marcado = obrasMarcadas.Contains(o.IdObra),
                Generos = o.Generos.Select(g => g.Nome).ToList()
            }).ToList(),

            Atores = atores.Select(a => new AtorDTO
            {
                IdElenco = a.IdElenco,
                IdTmdb = a.IdTmdb,
                Nome = a.Nome,
                Foto = a.Foto,
                Cargo = a.Cargo,
                Sexo = a.Sexo,
                Marcado = elencoMarcados.Contains(a.IdElenco),
                Generos = a.Obras
                    .SelectMany(o => o.Generos.Select(g => g.Nome))
                    .Distinct()
                    .ToList()

            }).ToList(),

            Diretores = diretores.Select(d => new DiretorDTO
            {
                IdElenco = d.IdElenco,
                IdTmdb = d.IdTmdb,
                Nome = d.Nome,
                Foto = d.Foto,
                Cargo = d.Cargo,
                Sexo = d.Sexo,
                Marcado = elencoMarcados.Contains(d.IdElenco),
                Obras = d.Obras.Select(o => o.Nome).ToList()

            }).ToList()
        };
    }






    private async Task<List<int>> ObterTmdbIds(string tipo, int paginas = 1)
    {
        var ids = new List<int>();

        for (int i = 1; i <= paginas; i++)
        {
            string url = $"https://api.themoviedb.org/3/{tipo}/popular?api_key={_apiKey}&language=pt-BR&page={i}";

            var response = await _http.GetFromJsonAsync<TmdbPopularResult>(url);

            if (response?.results != null)
            {
                ids.AddRange(
                    response.results
                        .Select(r => r.id)
                );
            }
        }

        return ids.Distinct().ToList();
    }

}

public class TmdbPopularResult
    {
        public List<TmdbPopularItem> results { get; set; }
    }

    public class TmdbPopularItem
    {
        public int id { get; set; }
    }

    public class PopularesResultadoDTO
    {
        public List<ObraDTO> Obras { get; set; } = new();
        public List<AtorDTO> Atores { get; set; } = new();
        public List<DiretorDTO> Diretores { get; set; } = new();
    }