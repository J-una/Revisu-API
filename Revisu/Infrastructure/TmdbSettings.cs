namespace Revisu.Infrastructure
{
    public class TmdbSettings
    {
        public string ApiKey { get; set; }
        public int StartYear { get; set; } = 1950;
        public int DelayBetweenRequestsMs { get; set; } = 500;
        public int CheckpointSeriesGenero { get; set; }
    }
}
