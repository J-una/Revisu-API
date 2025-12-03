using Revisu.Domain.Dtos;
using Revisu.Domain.Entities;

public class UsuarioService
{
    private readonly UsuarioRepository _repo;

    public UsuarioService(UsuarioRepository repo)
    {
        _repo = repo;
    }

    public async Task<UsuarioResponseDto> CadastrarUsuarioAsync(UsuarioCriarDto dto)
    {
        var existente = await _repo.ObterPorEmailAsync(dto.Email);
        if (existente != null)
            throw new Exception("Email já cadastrado.");

        var usuario = new Usuario
        {
            IdUsuario = Guid.NewGuid(),
            Nome = dto.Nome,
            Email = dto.Email,
            DataNascimento = dto.DataNascimento.ToUniversalTime(),
            Quiz = false,
            Ativo = true,
            dataCadastro = DateTime.UtcNow,
            Senha = BCrypt.Net.BCrypt.HashPassword(dto.Senha)
        };

        await _repo.CadastrarAsync(usuario);

        return new UsuarioResponseDto
        {
            IdUsuario = usuario.IdUsuario,
            Nome = usuario.Nome,
            Email = usuario.Email,
            DataNascimento = usuario.DataNascimento,
            Quiz = usuario.Quiz
        };
    }

    public async Task<UsuarioResponseDto> LoginAsync(UsuarioLoginDto dto)
    {
        var usuario = await _repo.ObterPorEmailAsync(dto.Email);
        string senhaOriginal = "senha123";
        string senhaCriptografada = BCrypt.Net.BCrypt.HashPassword(senhaOriginal);

        Console.WriteLine(senhaCriptografada);
        if (usuario == null)
            throw new Exception("Usuário não encontrado.");

        if (!BCrypt.Net.BCrypt.Verify(dto.Senha, usuario.Senha))
            throw new Exception("Senha incorreta.");

        return new UsuarioResponseDto
        {
            IdUsuario = usuario.IdUsuario,
            Nome = usuario.Nome,
            Email = usuario.Email,
            DataNascimento = usuario.DataNascimento,
            Quiz = usuario.Quiz
        };
    }

    public async Task<UsuarioResponseDto> EditarUsuarioAsync(Guid id, UsuarioEditarDto dto)
    {
        var usuario = await _repo.ObterPorIdAsync(id);
        if (usuario == null)
            throw new Exception("Usuário não encontrado.");

        // Atualizações simples
        usuario.Nome = dto.Nome;
        usuario.Email = dto.Email;

        // Data de nascimento (envie sempre no formato yyyy-MM-ddTHH:mm:ss)
        usuario.DataNascimento = DateTime.SpecifyKind(dto.DataNascimento, DateTimeKind.Utc);

        // Atualizar senha somente se enviada
        if (!string.IsNullOrWhiteSpace(dto.Senha))
            usuario.Senha = BCrypt.Net.BCrypt.HashPassword(dto.Senha);

        await _repo.AtualizarAsync(usuario);

        return new UsuarioResponseDto
        {
            IdUsuario = usuario.IdUsuario,
            Nome = usuario.Nome,
            Email = usuario.Email,
            DataNascimento = usuario.DataNascimento,
            Quiz = usuario.Quiz
        };
    }

    public async Task<bool> VerificarEmailJaCadastradoAsync(string email)
    {
        var existente = await _repo.ObterPorEmailAsync(email);
        return existente != null;
    }

}
