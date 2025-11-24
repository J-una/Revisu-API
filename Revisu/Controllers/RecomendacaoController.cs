using Microsoft.AspNetCore.Mvc;
using Revisu.Data;
using Revisu.Domain.Dtos;
using Revisu.Services;
using Revisu.Services.Biblioteca;
using Revisu.Services.Quiz;

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

        public RecomendacaoController(
            //RecomendacaoHibridaService recomendacaoService,
            QuizService listarQuizService,
            BibliotecaService bibliotecaService,
            AppDbContext db)
        {
            //_recomendacaoService = recomendacaoService;
            _quizService = listarQuizService;
            _bibliotecaService = bibliotecaService;
            _db = db;
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

        [HttpGet("recomendar")]
        public async Task<IActionResult> Recomendar(Guid idUsuario)
        {
            var service = new RecomendacaoService(_db);
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

    }
}
