namespace Revisu.Domain.Dtos
{
    public class ElencoDTO
    {
        public Guid IdElenco { get; set; }
        public int IdTmdb { get; set; }
        public string Nome { get; set; }
        public string? Foto { get; set; }
        public string Cargo { get; set; }
        public string? Sexo { get; set; }

        // Gêneros das obras em que ele participou
        public List<string> Generos { get; set; } = new();
    }
}

