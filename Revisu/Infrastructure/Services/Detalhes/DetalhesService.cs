using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Revisu.Domain.Dtos;
using Revisu.Infrastructure;
using Revisu.Domain.Entities;
using System.Net.Http.Json;
using Revisu.Data;

public class DetalhesService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly AppDbContext _context;

    public DetalhesService(
        HttpClient httpClient,
        IOptions<TmdbSettings> tmdbSettings,
        AppDbContext context)
    {
        _httpClient = httpClient;
        _apiKey = tmdbSettings.Value.ApiKey;
        _context = context;
    }

    public async Task<SobrePessoaDTO?> ObterDetalhesPessoaAsync(Guid idElenco, Guid idUsuario)
    {
        //  Pega o registro no banco
        var elenco = await _context.Elencos
            .FirstOrDefaultAsync(e => e.IdElenco == idElenco);

        var marcado = await _context.Biblioteca
            .AnyAsync(e => e.IdElenco == idElenco && e.IdUsuario == idUsuario && !e.Excluido);

        if (elenco == null)
            return null;

        int tmdbId = elenco.IdTmdb;

        var url = $"https://api.themoviedb.org/3/person/{tmdbId}?api_key={_apiKey}&language=pt-BR";

        var resposta = await _httpClient.GetFromJsonAsync<TmdbPersonResponse>(url);

        if (resposta == null)
            return null;

        return new SobrePessoaDTO
        {
            IdElenco = elenco.IdElenco,
            Nome = resposta.name,
            Biografia = resposta.biography,
            DataNascimento = resposta.birthday,
            Cargo= elenco.Cargo,
            DataMorte = resposta.deathday,
            Sexo = resposta.gender switch
            {
                1 => "Feminino",
                2 => "Masculino",
                _ => "Não informado"
            },
            Foto = resposta.profile_path,
            Marcado = marcado
        };
    }


    public async Task<DetalhesObraDTO?> ObterDetalhesObraAsync(Guid idObra, Guid idUsuario)
    {
        // Dados completos da obra
        var obra = await _context.Obras
            .Where(o => o.IdObra == idObra)
            .Select(o => new DetalhesObraDTO
            {
                IdObra = o.IdObra,
                IdTmdb = o.IdTmdb,
                Titulo = o.Nome,
                Sinopse = o.Sinopse,
                Imagem = o.Imagem,
                NotaMedia = o.NotaMedia,
                Tipo = o.Tipo,
                Generos = o.Generos.Select(g => g.Nome).ToList(),

                Atores = o.Elenco.Select(e => new AtorDTO
                {
                    IdElenco = e.IdElenco,
                    IdTmdb = e.IdTmdb,
                    Nome = e.Nome,
                    Foto = e.Foto,
                    Cargo = e.Cargo,
                    Sexo = e.Sexo,

                    Generos = e.Obras
                        .SelectMany(ob => ob.Generos)
                        .Select(g => g.Nome)
                        .Distinct()
                        .ToList()

                }).ToList()
            })
            .FirstOrDefaultAsync();

        if (obra == null)
            return null;

        // Verificar se essa obra está marcada na biblioteca do usuário
        obra.Marcado = await _context.Biblioteca
            .AnyAsync(b => b.IdObra == idObra && b.IdUsuario == idUsuario && !b.Excluido);

        return obra;
    }

}
