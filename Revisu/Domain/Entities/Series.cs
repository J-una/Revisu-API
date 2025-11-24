using System.ComponentModel.DataAnnotations;

namespace Revisu.Domain.Entities
{
    public class Series
    {
        [Key]
        public Guid IdObra { get; set; }
        public int IdTmdb { get; set; }
        public Guid? IdRottenTomatoes { get; set; }
        public string Nome { get; set; }
        public string Sinopse { get; set; }
        public string Tipo { get; set; }
        public string Imagem { get; set; }
        public string DataLancamento { get; set; }
        public DateTime DataCadastro { get; set; }

        // Relacionamento N:N com Generos
        public ICollection<Generos> Generos { get; set; } = new List<Generos>();
    }
}
