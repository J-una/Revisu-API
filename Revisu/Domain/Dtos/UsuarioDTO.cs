namespace Revisu.Domain.Dtos
{
    public class UsuarioCriarDto
    {
        public string Nome { get; set; }
        public string Email { get; set; }
        public string Senha { get; set; }
        public DateTime DataNascimento { get; set; }
    }

    public class UsuarioLoginDto
    {
        public string Email { get; set; }
        public string Senha { get; set; }
    }

    public class UsuarioEditarDto
    {
        public string Nome { get; set; }
        public DateTime DataNascimento { get; set; }
        public string Senha { get; set; }

        public string Email { get; set; }
    }

    public class UsuarioResponseDto
    {
        public Guid IdUsuario { get; set; }
        public string Nome { get; set; }
        public string Email { get; set; }
        public DateTime DataNascimento { get; set; }
        public bool Quiz { get; set; }
    }

}
