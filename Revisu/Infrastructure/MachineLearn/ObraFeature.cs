namespace Revisu.Infrastructure.MachineLearn
{
    public class ObraFeature
    {
        public float GeneroSimilarity { get; set; }
        public float ElencoSimilarity { get; set; }
        public float SinopseSimilarity { get; set; }
        public float NotaMedia { get; set; }
        public float Popularidade { get; set; }
        public string Tipo { get; set; }

        public bool Label { get; set; } // 1 = usuário gosta, 0 = não gosta
    }
}
