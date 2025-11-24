namespace Revisu.Domain.Dtos
{
    public class QuizDTO
    {
        public Guid IdObra { get; set; }
        public string Nome { get; set; }
        public string Imagem { get; set; }

        public QuizDTO(Guid idObra, string nome, string imagem)
        {
            IdObra = idObra;
            Nome = nome;
            Imagem = imagem;
        }
    }

    public class SalvarQuizDTO
    {
        public Guid IdUsuario { get; set; }
        public List<Guid> IdObras { get; set; } = new();
    }
}
