using System;
using System.Collections.Concurrent;
using Microsoft.ML;

namespace Revisu.Recommendation
{
    public class GlobalRecommendationCache
    {
        // Conteúdo vetorial (sinopses → vetores)
        public ConcurrentDictionary<Guid, float[]> ContentVectors { get; set; }
            = new ConcurrentDictionary<Guid, float[]>();

        // Modelos ML carregados 1x e reaproveitados
        public ITransformer? CollaborativeModel { get; set; }
        public DataViewSchema? CollaborativeSchema { get; set; }

        public ITransformer? RerankerModel { get; set; }
        public DataViewSchema? RerankerSchema { get; set; }

        // Prediction Engines cacheados para não recriar sempre
        public PredictionEngine<InteractionRecord, CfPrediction>? CfEngine { get; set; }
        public PredictionEngine<RerankRecord, RerankerPrediction>? RerankEngine { get; set; }

        public MLContext ML { get; } = new MLContext(seed: 0);
    }
}
