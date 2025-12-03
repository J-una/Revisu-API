using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Revisu.Domain.Entities
{
    public class AvaliacaoUsuario
    {
        [Key]
        public Guid IdAvaliacaoUsuario { get; set; }

        public Guid IdUsuario { get; set; }
        [ForeignKey("IdUsuario")]
        public Usuario Usuarios { get; set; }

        public Guid? IdObra { get; set; }
        [ForeignKey("IdObra")]
        public Obras Filmes { get; set; }

        public string Comentario { get; set; }
        public float Nota { get; set; }

    }
}
