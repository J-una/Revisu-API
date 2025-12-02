using Microsoft.EntityFrameworkCore;
using Revisu.Data;
using Revisu.Domain.Dtos;

public class PesquisarService
{
    private readonly AppDbContext _db;

    public PesquisarService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PesquisaResultadoDto> PesquisarAsync(string termo)
    {
        termo = termo.ToLower().Trim();

        var resultado = new PesquisaResultadoDto();

        // 🔹 Pesquisar Obras
        resultado.Obras = await _db.Obras
            .Where(o =>
                !string.IsNullOrWhiteSpace(o.Sinopse) &&
                o.NotaMedia > 0 &&
                o.Nome.ToLower().Contains(termo))
            .Select(o => new PesquisaItemDto
            {
                Id = o.IdObra,
                Nome = o.Nome,
                Imagem = o.Imagem
            })
            .Take(5)
            .ToListAsync();

        // 🔹 Pesquisar Atores
        resultado.Atores = await _db.Elencos
            .Where(e =>
                e.Cargo == "Ator" &&
                e.Nome.ToLower().Contains(termo))
            .Select(e => new PesquisaItemDto
            {
                Id = e.IdElenco,
                Nome = e.Nome,
                Imagem = e.Foto
            })
            .Take(5)
            .ToListAsync();

        //  Pesquisar Diretores
        resultado.Diretores = await _db.Elencos
            .Where(e =>
                e.Cargo == "Diretor" &&
                e.Nome.ToLower().Contains(termo))
            .Select(e => new PesquisaItemDto
            {
                Id = e.IdElenco,
                Nome = e.Nome,
                Imagem = e.Foto
            })
            .Take(5)
            .ToListAsync();

        return resultado;
    }
}
