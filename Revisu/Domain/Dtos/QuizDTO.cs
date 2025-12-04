using System.Globalization;
using System.Security.Cryptography.X509Certificates;

namespace Revisu.Domain.Dtos
{
    public class QuizDTO
    {
        public Guid IdObra { get; set; }
        public string Nome { get; set; }
        public string Imagem { get; set; }
        public float Nota { get; set; }
        public string Tipo { get; set; }
        public List<string> Generos { get; set; }

        public QuizDTO(Guid idObra, string nome, string imagem, float nota, string tipo,List<string> generos)
        {
            IdObra = idObra;
            Nome = nome;
            Imagem = imagem;
            Tipo = tipo;
            Nota = nota;
            Generos = generos;
        }
    }

    public class SalvarQuizDTO
    {
        public Guid IdUsuario { get; set; }
        public List<Guid> IdObras { get; set; } = new();
    }
}
