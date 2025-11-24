using System.ComponentModel.DataAnnotations;

namespace Revisu.Domain.Entities
{
    public class Generos
    {
        [Key]
        public Guid IdGenero { get; set; }
        public int? IdGeneroImdbMovie { get; set; }
        public int? IdGeneroImdbSerie { get; set; }
        public string Nome { get; set; }

        //Relacionamento N:N com Generos
        public ICollection<Obras> Obras { get; set; } = new List<Obras>();
    }
}
