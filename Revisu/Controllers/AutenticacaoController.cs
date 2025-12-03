using Microsoft.AspNetCore.Mvc;
using Revisu.Domain.Dtos;

[ApiController]
[Route("api/usuarios")]
public class AutenticacaoController : ControllerBase
{
    private readonly UsuarioService _service;

    public AutenticacaoController(UsuarioService service)
    {
        _service = service;
    }

    // POST: api/usuarios/cadastrar
    [HttpPost("cadastrar-usuario")]
    public async Task<IActionResult> CadastrarUsuario([FromBody] UsuarioCriarDto dto)
    {
        var usuario = await _service.CadastrarUsuarioAsync(dto);
        return Ok(usuario);
    }

    // POST: api/usuarios/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] UsuarioLoginDto dto)
    {
        try
        {
            var usuario = await _service.LoginAsync(dto);
            return Ok(usuario);
        }
        catch (Exception ex)
        {
            return Unauthorized(new { mensagem = ex.Message });
        }
    }

    // PUT: api/usuarios/editar/{id}
    [HttpPut("editar-usuario/{id}")]
    public async Task<IActionResult> Editar(Guid id, [FromBody] UsuarioEditarDto dto)
    {
        var usuario = await _service.EditarUsuarioAsync(id, dto);
        return Ok(usuario);
    }

    [HttpGet("verificar-email")]
    public async Task<IActionResult> VerificarEmail([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest("Email é obrigatório.");

        bool existe = await _service.VerificarEmailJaCadastradoAsync(email);

        return Ok(new { emailExiste = existe });
    }
}
