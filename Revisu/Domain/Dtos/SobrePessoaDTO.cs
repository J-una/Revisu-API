using System.Collections.Specialized;

namespace Revisu.Domain.Dtos
{
    public class SobrePessoaDTO
    {
        public string Biografia { get; set; }
        public string DataNascimento { get; set; }
        public string DataMorte { get; set; }
        public string Cargo { get;set; }
        public string Sexo { get; set; }
        public string Nome { get; set; }
        public string Foto { get; set; }
        public bool Marcado { get; set; }
    }

    public class TmdbPersonResponse
    {
        public string biography { get; set; }
        public string birthday { get; set; }
        public string deathday { get; set; }
        public int gender { get; set; }
        public string name { get; set; }
        public string profile_path { get; set; }
    }


    public class PesquisaItemDto
    {
        public Guid Id { get; set; }
        public string Nome { get; set; }
        public string? Imagem { get; set; }
    }

    public class PesquisaResultadoDto
    {
        public List<PesquisaItemDto> Obras { get; set; } = new();
        public List<PesquisaItemDto> Atores { get; set; } = new();
        public List<PesquisaItemDto> Diretores { get; set; } = new();
    }

}
