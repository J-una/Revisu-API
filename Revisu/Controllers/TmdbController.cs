using Microsoft.AspNetCore.Mvc;
using Revisu.Infrastructure.Services;
using Revisu.Infrastructure.Services.ImportacaoTmdb;

namespace Revisu.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TmdbController : ControllerBase
    {
        private readonly TmdbImportService _importMoviesService;
        private readonly TmdImportSeriesService _importSeriesService;
        private readonly RottenTomatoesImportService _rottenTomatoesservice;
        private readonly AdicionarNomeOriginalTmdbService _adicionarNomeOriginalTmdbService;
        private readonly TmdbAtualizarNotasService _atualizarNotasService;
        private readonly AtualizarObrasTmdb _atualizarObrasTmdb;
        private readonly AtualizarPopularidadeTmdbService _popularidadeService;
        private readonly AtualizarGenerosSeriesService _atualizarGenerosSeriesService;
        private readonly AtualizarElencoTmdbService _atualizarElencoTmdbService;
        private readonly AtualizarElencoService _atualizarElencoService;
        private readonly AtualizarGenerosSeriesTmdbService _atualizarGenerosSeriesTmdbService;

        public TmdbController(TmdbImportService importMoviesService,
            TmdImportSeriesService importSeriesService,
            RottenTomatoesImportService rottenTomatoesservice,
            AdicionarNomeOriginalTmdbService adicionarNomeOriginalTmdbService,
            TmdbAtualizarNotasService atualizarNotasService,
            AtualizarObrasTmdb atualizarObrasTmdb,   
            AtualizarPopularidadeTmdbService popularidadeService,
            AtualizarGenerosSeriesService atualizarGenerosSeriesService,
            AtualizarElencoTmdbService atualizarElencoTmdbService,
            AtualizarElencoService atualizarElencoService,
            AtualizarGenerosSeriesTmdbService atualizarGenerosSeriesTmdbService)
        {
            _importMoviesService = importMoviesService;
            _importSeriesService = importSeriesService;
            _rottenTomatoesservice = rottenTomatoesservice;
            _adicionarNomeOriginalTmdbService = adicionarNomeOriginalTmdbService;
            _atualizarNotasService = atualizarNotasService;
            _atualizarObrasTmdb = atualizarObrasTmdb;
            _popularidadeService = popularidadeService;
            _atualizarGenerosSeriesService = atualizarGenerosSeriesService;
            _atualizarElencoTmdbService = atualizarElencoTmdbService;
            _atualizarElencoService = atualizarElencoService;
            _atualizarGenerosSeriesTmdbService = atualizarGenerosSeriesTmdbService;
        }

        /// <summary>
        /// Importa filmes do TMDb para o banco de dados.
        /// </summary>
        /// 
        //Desafasado
        [HttpPost("importar-filmes")]
        public async Task<IActionResult> ImportarFilmes(CancellationToken cancellationToken)
        {
            var result = await _importMoviesService.ImportAllMoviesAsync(cancellationToken);
            return Ok(result);
        }
        //Desafasado
        [HttpPost("import-series")]
        public async Task<IActionResult> ImportSeries(CancellationToken cancellationToken)
        {
            var result = await _importSeriesService.ImportAllSeriesAsync(cancellationToken);
            return Ok(result);
        }

        [HttpPost("importar-Avaliacoes")]
        public async Task<IActionResult> Importar(CancellationToken cancellationToken)
        {
            var resultado = await _rottenTomatoesservice.ImportarAvaliacoesEmLotesAsync(cancellationToken);
            return Ok(resultado);
        }

        [HttpPost("atualizar-notas")]
        public async Task<IActionResult> AtualizarNotas(CancellationToken cancellationToken)
        {
            try
            {
                await _atualizarNotasService.AtualizarNotasAsync(cancellationToken);
                return Ok(new { mensagem = "Notas atualizadas com sucesso!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { erro = $"Erro ao atualizar notas: {ex.Message}" });
            }
        }
        //Desafasado
        [HttpPost("atualizar-nome-obras")]
        public async Task<IActionResult> AtualizarNomeOriginal()
        {
            await _adicionarNomeOriginalTmdbService.AtualizarTitulosOriginaisAsync();
            return Ok("Atualização de títulos originais concluída!");
        }


        //Endpoint para atulizar as obras
        [HttpPost("atualizar-obras")]
        public async Task<IActionResult> AtualizarObras()
        {
            await _atualizarObrasTmdb.SyncRecentChangesAsync();
            return Ok("Atualização de obras concluídas!");
        }

        [HttpPost("atualizar-popularidade")]
        public async Task<IActionResult> AtualizarPopularidade(CancellationToken cancellationToken)
        {
            var result = await _popularidadeService.AtualizarPopularidadeAsync(cancellationToken);
            return Ok(result);
        }

        //[HttpPost("atualizar-generos-series")]
        //public async Task<IActionResult> AtualizarGenerosSeries()
        //{
        //    try
        //    {
        //        var result = await _atualizarGenerosSeriesService.AtualizarGenerosAsync();
        //        return Ok(new { mensagem = result });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new { erro = $"Erro ao atualizar gêneros: {ex.Message}" });
        //    }
        //}

        //[HttpPost("atualizar-elenco")]
        //public async Task<IActionResult> AtualizarElenco()
        //{
        //    try
        //    {
        //        // chama o service correto
        //        await _atualizarElencoTmdbService.ExecutarAsync();

        //        // Se você realmente quiser rodar os gêneros aqui (mas é melhor separar)
        //        //await _atualizarGenerosSeriesService.AtualizarGenerosAsync();

        //        return Ok(new { message = "Processamento iniciado e finalizado com sucesso." });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new { error = ex.Message });
        //    }
        //}

        [HttpPost("atualizar-elenco")]
        public async Task<IActionResult> AtualizarElenco()
        {
            try
            {
                // chama o service correto
                await _atualizarElencoService.AtualizarElencoAsync();

                // Se você realmente quiser rodar os gêneros aqui (mas é melhor separar)
                //await _atualizarGenerosSeriesService.AtualizarGenerosAsync();

                return Ok(new { message = "Processamento iniciado e finalizado com sucesso." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("atualizar-generos-series-tmdb")]
        public async Task<IActionResult> AtualizarGenerosSeriesTmdb(CancellationToken cancellationToken)
        {
            try
            {
                var result = await _atualizarGenerosSeriesTmdbService.AtualizarGenerosAsync(cancellationToken);
                return Ok(new { mensagem = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { erro = $"Erro ao atualizar gêneros das séries: {ex.Message}" });
            }
        }


        [HttpPost("admin/atualizar-elencos-remover-impropios-atualizar-popularidade")]
        public async Task<IActionResult> AtualizarElencos(
            [FromServices] AtualizarElencoService service)
        {
            var result = await service.AtualizarTodosAsync();
            return Ok(result);
        }
    }
}   


