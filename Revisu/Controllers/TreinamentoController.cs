using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Revisu.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TreinamentoController : ControllerBase
    {

        //Rodar esse primeiro
        [HttpPost("admin/build-cache")]
        public async Task<IActionResult> BuildCache([FromServices] RecomendacaoServiceOptimizado service)
        {
            await service.BuildFeatureCacheAsync();
            return Ok("CACHE gerado com sucesso!");
        }
        //Rodar esse depois de gerar o cache
        [HttpPost("admin/train-model")]
        public async Task<IActionResult> TrainModel([FromServices] RecomendacaoServiceOptimizado service)
        {
            await service.TrainAndSaveModelAsync();
            return Ok("MODELO treinado e salvo!");
        }



        //Deve ser rodado uma vez, ou quando atualizar muitas sinopses.
        [HttpPost("admin/build-index")]
        public async Task<IActionResult> BuildIndex([FromServices] RecomendacaoHibridaService svc)
        {
            await svc.BuildContentIndexAsync();
            return Ok("Index de conteúdo criado!");
        }
        //Treina parte de Collaborative Filtering CF
        [HttpPost("admin/train-cf")]
        public async Task<IActionResult> TrainCF([FromServices] RecomendacaoHibridaService svc)
        {
            await svc.TrainCfModelAsync();
            return Ok("Modelo CF treinado!");
        }
        //Treina o modelo de re-ranking (FastTree)
        [HttpPost("admin/train-reranker")]
        public async Task<IActionResult> TrainReranker([FromServices] RecomendacaoHibridaService svc)
        {
            await svc.TrainRerankerAsync();
            return Ok("Reranker treinado!");
        }
    }
}
