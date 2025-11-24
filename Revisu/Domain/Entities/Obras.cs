using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Revisu.Domain.Entities
{
    public class Obras
    {
        [Key]
        public Guid IdObra { get; set; }
        public int IdTmdb { get; set; }
        public string? IdRottenTomatoes { get; set; }
        public string Nome { get; set; }
        public string? NomeOriginal { get; set; }
        public string Sinopse { get; set; }
        public string Tipo { get; set; }
        public string Imagem { get; set; }
        public float NotaMedia { get; set; }
        public string DataLancamento { get; set; }
        public DateTime DataCadastro { get; set; }
        public float? Populariedade { get; set; }
        // Relacionamento N:N com Generos
        public ICollection<Generos> Generos { get; set; } = new List<Generos>();
        public ICollection<Elenco> Elenco { get; set; } = new List<Elenco>();
    }
}
