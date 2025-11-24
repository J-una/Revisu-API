using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace Revisu.Domain.Entities
{
    public class Biblioteca
    {
        [Key]
        public Guid IdBiblioteca { get; set; }
        public Guid? IdUsuario { get; set; }
        [ForeignKey("IdUsuario")]
        public Usuario Usuario { get; set; }
        public Guid? IdObra { get; set; }
        [ForeignKey("IdObra")]
        public Obras Filmes { get; set; } 
        public Guid? IdElenco { get; set; }
        [ForeignKey("IdElenco")]
        public Elenco Elenco { get; set; }

        public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
        public bool Excluido { get; set; } = false;

    }
}
