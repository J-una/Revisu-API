using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Revisu.Domain.Entities
{
    public class Avaliacoes
    {
        [Key]
        public Guid IdAvaliacao { get; set; }
        public Guid IdObra { get; set; }
        [ForeignKey("IdObra")]
        public Obras Filmes { get; set; }
        public float Nota { get; set; }
        public DateTime DataCadastro { get; set; } = DateTime.UtcNow; 
    }
}
