using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Revisu.Domain.Entities
{
    public class Elenco
    {
        [Key]
        public Guid IdElenco { get; set; }
        public int IdTmdb { get; set; }
        public string Nome { get; set; }
        public string? Foto { get; set; }
        public string Cargo { get; set; }
        public float Popularidade { get; set; }
        public string? Sexo { get; set; }

        public ICollection<Obras> Obras { get; set; } = new List<Obras>();


    }
}
