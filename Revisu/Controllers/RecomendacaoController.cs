using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Revisu.Data;
using Revisu.Domain.Dtos;
using Revisu.Domain.Entities;
using Revisu.Infrastructure.Services.Biblioteca;
using Revisu.Infrastructure.Services.Quiz;


namespace Revisu.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RecomendacaoController : ControllerBase
    {
        //private readonly RecomendacaoHibridaService _recomendacaoService;
        private readonly QuizService _quizService;
        private readonly BibliotecaService _bibliotecaService;
        private readonly AppDbContext _db;
        private readonly DetalhesService _detalhesService;
        private readonly PesquisarService _pesquisarService;

        public RecomendacaoController(
            //RecomendacaoHibridaService recomendacaoService,
            QuizService listarQuizService,
            BibliotecaService bibliotecaService,
            AppDbContext db,
            DetalhesService sobrePessoasService,
            PesquisarService pesquisarService)
        {
            //_recomendacaoService = recomendacaoService;
            _quizService = listarQuizService;
            _bibliotecaService = bibliotecaService;
            _db = db;
            _detalhesService = sobrePessoasService;
            _pesquisarService = pesquisarService;
        }


        //[HttpGet("{idUsuario:guid}")]
        //public async Task<IActionResult> ObterRecomendacoes(Guid idUsuario)
        //{
        //    try
        //    {
        //        var recomendacoes = await _recomendacaoService.RecomendarAsync(idUsuario);


        //        if (recomendacoes == null || !recomendacoes.Any())
        //            return NotFound(new { mensagem = "Nenhuma recomendação encontrada para este usuário." });

        //        var dtoList = recomendacoes.Select(o => new ObraDTO
        //        {
        //            IdObra = o.IdObra,
        //            Nome = o.Nome,
        //            NomeOriginal = o.NomeOriginal,
        //            Sinopse = o.Sinopse,
        //            Tipo = o.Tipo,
        //            Imagem = o.Imagem,
        //            NotaMedia = o.NotaMedia,
        //            DataLancamento = o.DataLancamento,
        //            Generos = o.Generos.Select(g => g.Nome).ToList()
        //        }).ToList();

        //        return Ok(dtoList);
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new { erro = $"Erro ao gerar recomendações: {ex.Message}" });
        //    }
        //}

        //Lista filmes aleatorios para gerar o quiz
        [HttpGet("filmes-quiz")]
        public async Task<IActionResult> QuizFilmes()
        {
            var filmes = await _quizService.ListarFilmesRandomizadosAsync();
            return Ok(filmes);
        }

        //Lista series aleatorias para gerar o quiz
        [HttpGet("series-quiz")]
        public async Task<IActionResult> ListarSeries()
        {
            var series = await _quizService.ListarSeriesRandomizadasAsync();
            return Ok(series);
        }

        //Salvar item na biblioteca
        [HttpPost("Salvar-Biblioteca")]
        public async Task<IActionResult> SalvarBiblioteca([FromBody] BibliotecaDTO dto)
        {
            await _bibliotecaService.SalvarAsync(dto.IdUsuario, dto.IdObra, dto.IdElenco);

            return Ok("Item salvo na biblioteca!");
        }

        //Remover item da biblioteca
        [HttpPost("Remover-Biblioteca")]
        public async Task<IActionResult> RemoverBiblioteca([FromBody] BibliotecaDTO dto)
        {
            await _bibliotecaService.RemoverAsync(dto.IdUsuario, dto.IdObra, dto.IdElenco);

            return Ok("Item removido da biblioteca!");
        }

        [HttpGet("recomendar-lenta")]
        public async Task<IActionResult> Recomendar(Guid idUsuario)
        {
            var service = new RecomendacaoLentaService(_db);
            var recomendacoes = await service.RecomendarObrasAsync(idUsuario);

            return Ok(recomendacoes);
        }

        [HttpGet("recomendar-optimizado")]
        public async Task<IActionResult> RecomendarOptimizado(Guid idUsuario, [FromServices] RecomendacaoServiceOptimizado service)
        {
            var recomendacoes = await service.RecomendarObrasAsync(idUsuario);
            return Ok(recomendacoes);
        }


        [HttpGet("recomendar-factorization-machine{idUsuario:guid}")]
        public async Task<IActionResult> RecomendarHibrida(
            Guid idUsuario,
            [FromServices] RecomendacaoHibridaService svc,
            [FromQuery] int top = 10)
        {
            var result = await svc.RecommendHybridAsync(idUsuario, top);
            return Ok(result);
        }


        [HttpGet("100-obras")]
        public async Task<ActionResult<IEnumerable<ObraDTO>>> Get50Obras()
        {
            var obras = await _db.Obras
                .Where(o => !string.IsNullOrWhiteSpace(o.Sinopse) && o.NotaMedia > 0)
                .OrderByDescending(o => o.Populariedade)
                .Take(100)
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
                .ToListAsync();

            return Ok(obras);
        }


        // ------------------------- 2) 100 ATORES -------------------------
        [HttpGet("100-atores")]
        public async Task<ActionResult<IEnumerable<AtorDTO>>> Get50Atores()
        {
            var atores = await _db.Elencos
                .Where(e => e.Cargo == "Ator")
                .OrderBy(e => Guid.NewGuid())
                .Take(100)
                .Select(e => new AtorDTO
                {
                    IdElenco = e.IdElenco,
                    IdTmdb = e.IdTmdb,
                    Nome = e.Nome,
                    Foto = e.Foto,
                    Cargo = e.Cargo,
                    Sexo = e.Sexo,
                    Generos = e.Obras
                        .SelectMany(o => o.Generos)
                        .Select(g => g.Nome)
                        .Distinct()
                        .ToList()
                })
                .ToListAsync();

            return Ok(atores);
        }


        // ------------------------- 3) 100 DIRETORES -------------------------
        [HttpGet("100-diretores")]
        public async Task<ActionResult<IEnumerable<DiretorDTO>>> Get50Diretores()
        {
            var diretores = await _db.Elencos
                .Where(e => e.Cargo == "Diretor")
                .OrderBy(e => Guid.NewGuid())
                .Take(100)
                .Select(e => new DiretorDTO
                {
                    IdElenco = e.IdElenco,
                    IdTmdb = e.IdTmdb,
                    Nome = e.Nome,
                    Foto = e.Foto,
                    Cargo = e.Cargo,
                    Sexo = e.Sexo,
                    Obras = e.Obras
                        .Select(o => o.Nome)
                        .ToList()
                })
                .ToListAsync();

            return Ok(diretores);
        }

        [HttpGet("detalhes-obras/{idObra:guid}/{idUsuario:guid}")]
        public async Task<ActionResult<DetalhesObraDTO>> GetObraById(Guid idObra, Guid idUsuario)
        {
            var obra = await _detalhesService.ObterDetalhesObraAsync(idObra, idUsuario);

            if (obra == null)
                return NotFound(new { message = "Obra não encontrada." });

            return Ok(obra);
        }


        [HttpGet("pessoa/{idElenco:guid}/{idUsuario:guid}")]
        public async Task<IActionResult> ObterPessoa(Guid idElenco, Guid idUsuario)
        {
            var dados = await _detalhesService.ObterDetalhesPessoaAsync(idElenco, idUsuario);

            if (dados == null)
                return NotFound("Pessoa não encontrada no TMDB ou no banco");

            return Ok(dados);
        }

        [HttpGet("pesquisar")]
        public async Task<IActionResult> Pesquisar([FromQuery] string termo)
        {
            if (string.IsNullOrWhiteSpace(termo))
                return BadRequest("Informe um termo para pesquisa.");

            var resultado = await _pesquisarService.PesquisarAsync(termo);

            return Ok(resultado);
        }

    }
}
