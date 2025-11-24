namespace Revisu.Infrastructure
{
    public class RottenSettings
    {
        public string AlgoliaUrl { get; set; }
        public string AlgoliaApiKey { get; set; }
        public string AlgoliaAppId { get; set; }
        public int HitsPerPage { get; set; } = 5;
        public int DelayBetweenRequestsMs { get; set; } = 200;
        public int BatchSize { get; set; } = 1000;
        public int MaxDegreeOfParallelism { get; set; } = 8;
    }
}
