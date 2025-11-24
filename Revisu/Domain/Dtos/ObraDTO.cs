namespace Revisu.Domain.Dtos
{
    public class ObraDTO
    {
        public Guid IdObra { get; set; }
        public int IdTmdb { get; set; } 
        public string Titulo { get; set; }
        public string Imagem { get; set; }
        public float NotaMedia { get; set; }
        public string Tipo { get; set; }
        public List<string> Generos { get; set; }
    }

}
