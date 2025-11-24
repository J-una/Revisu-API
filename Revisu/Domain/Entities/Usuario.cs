using System.ComponentModel.DataAnnotations;

namespace Revisu.Domain.Entities
{
    public class Usuario
    {
        [Key]
        public Guid IdUsuario { get; set; } 
        public string Nome { get; set; }
        public string Email { get; set; }
        public string Senha { get; set; }
        public DateTime DataNascimento { get; set; }
        public bool Quiz { get; set; }
        public bool Ativo { get; set; } = true;
        public DateTime dataCadastro { get; set; }
    }
}
