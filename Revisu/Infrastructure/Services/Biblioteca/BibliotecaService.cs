using Microsoft.EntityFrameworkCore;
using Revisu.Data;
using Revisu.Domain.Dtos;
using Revisu.Domain.Entities;

namespace Revisu.Infrastructure.Services.Biblioteca
{
    public class BibliotecaService
    {
        private readonly AppDbContext _context;

        public BibliotecaService(AppDbContext context)
        {
            _context = context;
        }

        public async Task SalvarAsync(Guid idUsuario, Guid? idObra, Guid? idElenco)
        {
            if (idObra == null && idElenco == null)
                throw new Exception("É necessário informar IdObra ou IdElenco");


            var existente = await _context.Biblioteca
                .FirstOrDefaultAsync(x =>
                    x.IdUsuario == idUsuario &&
                    x.IdObra == idObra &&
                    x.IdElenco == idElenco
                );

            if (existente != null && !existente.Excluido)
                return;


            if (existente != null && existente.Excluido)
            {
                existente.Excluido = false;
                existente.DataCadastro = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return;
            }

            // Criar novo registro
            var biblioteca = new Domain.Entities.Biblioteca
            {
                IdBiblioteca = Guid.NewGuid(),
                IdUsuario = idUsuario,
                IdObra = idObra,
                IdElenco = idElenco,
                DataCadastro = DateTime.UtcNow,
                Excluido = false
            };

            _context.Biblioteca.Add(biblioteca);
            await _context.SaveChangesAsync();
        }


        public async Task RemoverAsync(Guid idUsuario, Guid? idObra, Guid? idElenco)
        {
            var item = await _context.Biblioteca
                .FirstOrDefaultAsync(x =>
                    x.IdUsuario == idUsuario &&
                    x.IdObra == idObra &&
                    x.IdElenco == idElenco &&
                    !x.Excluido
                );

            if (item == null)
                return;

            item.Excluido = true;

            await _context.SaveChangesAsync();
        }

        public async Task<PopularesResultadoDTO> ListarBibliotecaDoUsuarioAsync(Guid idUsuario)
        {
            var dados = await _context.Biblioteca
                .Where(b => b.IdUsuario == idUsuario && b.Excluido == false)
                .Include(b => b.Filmes)
                    .ThenInclude(o => o.Generos)
                .Include(b => b.Elenco)
                    .ThenInclude(e => e.Obras)
                        .ThenInclude(o => o.Generos)
                .ToListAsync();

            // Separando IDs marcados
            var obrasMarcadas = dados
                .Where(b => b.IdObra != null)
                .Select(b => b.IdObra.Value)
                .ToHashSet();

            var elencoMarcados = dados
                .Where(b => b.IdElenco != null)
                .Select(b => b.IdElenco.Value)
                .ToHashSet();

            // -------------------------------
            // 1) OBRAS
            // -------------------------------

            var obras = dados
                .Where(b => b.Filmes != null)
                .Select(b => b.Filmes)
                .Distinct()
                .ToList();

            var obrasDto = obras.Select(o => new ObraDTO
            {
                IdObra = o.IdObra,
                IdTmdb = o.IdTmdb,
                Titulo = o.Nome,
                Imagem = o.Imagem,
                NotaMedia = o.NotaMedia,
                Tipo = o.Tipo,
                Marcado = obrasMarcadas.Contains(o.IdObra),
                Generos = o.Generos.Select(g => g.Nome).ToList()

            }).ToList();


            // -------------------------------
            // 2) ATORES
            // -------------------------------

            var atores = dados
                .Where(b => b.Elenco != null && b.Elenco.Cargo.ToLower() == "ator")
                .Select(b => b.Elenco)
                .Distinct()
                .ToList();

            var atoresDto = atores.Select(a => new AtorDTO
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

            }).ToList();


            // -------------------------------
            // 3) DIRETORES
            // -------------------------------

            var diretores = dados
                .Where(b => b.Elenco != null && b.Elenco.Cargo.ToLower() == "diretor")
                .Select(b => b.Elenco)
                .Distinct()
                .ToList();

            var diretoresDto = diretores.Select(d => new DiretorDTO
            {
                IdElenco = d.IdElenco,
                IdTmdb = d.IdTmdb,
                Nome = d.Nome,
                Foto = d.Foto,
                Cargo = d.Cargo,
                Sexo = d.Sexo,
                Marcado = elencoMarcados.Contains(d.IdElenco),
                Obras = d.Obras.Select(o => o.Nome).ToList()

            }).ToList();


            // -------------------------------
            // RESULTADO FINAL
            // -------------------------------

            return new PopularesResultadoDTO
            {
                Obras = obrasDto,
                Atores = atoresDto,
                Diretores = diretoresDto
            };
        }


    }
}
