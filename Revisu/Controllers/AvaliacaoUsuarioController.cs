using Microsoft.AspNetCore.Mvc;
using Revisu.Domain.Dtos;
using Revisu.Domain.Entities;
using Revisu.Infrastructure.Repositories;
using Revisu.Infrastructure.Services.Avaliacao;

namespace Revisu.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AvaliacaoUsuarioController : ControllerBase
    {
        private readonly AvaliacaoUsuarioRepository _repo;
        private readonly AvaliacaoUsuarioService _service;

        public AvaliacaoUsuarioController(AvaliacaoUsuarioRepository repo,
            AvaliacaoUsuarioService service)
        {
            _repo = repo;
            _service = service;
        }

        [HttpGet("listar-avaliacao-obra")]
        public async Task<IActionResult> ListarAvaliacaoObra(Guid idUsuario, Guid idObra)
        {
            var avaliacao = await _service.BuscarAvaliacaoUsuarioAsync(idUsuario, idObra);

            if (avaliacao == null)
                return Ok(null);

            return Ok(avaliacao);
        }

        // POST: api/AvaliacaoUsuario
        [HttpPost("salvar-avaliacao-obra")]
        public async Task<IActionResult> SalvarAvaliacao(
            Guid idUsuario,
            Guid idObra,
            [FromBody] CriarAvaliacaoDto model)
        {
            var resultado = await _service.SalvarAvaliacaoObraAsync(idUsuario, idObra, model);

            if (!resultado.Sucesso)
                return BadRequest(resultado.Mensagem);

            return Ok("Cadastrado com sucesso");
        }


        // PUT: api/AvaliacaoUsuario/{id}
        [HttpPut("editar-avaliacao-obra/{id}")]
        public async Task<IActionResult> Atualizar(
            Guid id,
            [FromBody] EditarAvaliacaoDto model)
        {
            var ok = await _service.EditarAvaliacaoAsync(id, model);

            if (!ok)
                return NotFound("Avaliação não encontrada.");

            return Ok("Avaliação atualizada com sucesso.");
        }


        // DELETE: api/AvaliacaoUsuario/{id}
        [HttpDelete("deletar-avalicao-obra/{id}")]
        public async Task<IActionResult> Deletar(Guid id)
        {
            var ok = await _repo.DeletarAsync(id);
            if (!ok) return NotFound();

            return NoContent();
        }
    }
}
