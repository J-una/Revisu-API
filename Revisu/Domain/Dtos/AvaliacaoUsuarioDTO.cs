using Revisu.Domain.Entities;

namespace Revisu.Domain.Dtos
{
    public class AvaliacaoUsuarioDTO
    {
        public Guid IdAvaliacaoUsuario { get; set; }
        public float Nota { get; set; }
        public string Comentario { get; set; }
    }

    public class ResultadoAvaliacaoDTO
    {
        public bool Sucesso { get; set; }
        public string Mensagem { get; set; }
        public AvaliacaoUsuario Avaliacao { get; set; }
    }

    public class CriarAvaliacaoDto
    {
        public string Comentario { get; set; }
        public float Nota { get; set; }
    }

    public class EditarAvaliacaoDto
    {
        public string Comentario { get; set; }
        public float Nota { get; set; }
    }



}
