using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Revisu.Recommendation;
using Revisu.Data;

namespace Revisu.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RecommendationController : ControllerBase
    {
        private readonly RecomendacaoHybridService _service;

        public RecommendationController(RecomendacaoHybridService service)
        {
            _service = service;
        }

        // POST api/recommendation/build-cache
        // Run once periodically (admin/job)
        [HttpPost("build-cache")]
        public async Task<IActionResult> BuildCache(CancellationToken ct)
        {
            try
            {
                await _service.BuildFeatureCacheAsync(ct);
                return Ok(new { message = "Feature cache built" });
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        // POST api/recommendation/train-cf
        // Train collaborative using all users
        [HttpPost("train-cf")]
        public async Task<IActionResult> TrainCollaborative(CancellationToken ct)
        {
            try
            {
                await _service.TrainCollaborativeModelAsync(ct);
                return Ok(new { message = "Collaborative model trained" });
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        // GET api/recommendation/recommend/{userId}?top=10
        [HttpGet("recommend/{userId:guid}")]
        public async Task<IActionResult> Recommend(Guid userId, [FromQuery] int top = 100, CancellationToken ct = default)
        {
            try
            {
                var list = await _service.RecommendForUserAsync(userId, top, ct);
                return Ok(list);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        [HttpGet("similar-obras/{obraId}/{idUsuario}")]
        public async Task<IActionResult> GetSimilarObra(Guid obraId, Guid idUsuario, int top = 100)
        {
            var result = await _service.RecommendObrasSimilarToAsync(obraId, idUsuario, top);
            return Ok(result);
        }

        [HttpGet("similar-elenco/{elencoId}/{idUsuario}")]
        public async Task<IActionResult> GetSimilarElenco(Guid elencoId,Guid idUsuario, int top = 50)
        {
            var result = await _service.RecommendByElencoAsync(elencoId, idUsuario, top);
            return Ok(result);
        }

    }
}
