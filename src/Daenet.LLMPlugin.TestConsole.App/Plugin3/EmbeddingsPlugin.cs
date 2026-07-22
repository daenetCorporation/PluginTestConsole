using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace Daenet.LLMPlugin.TestConsole.App.Plugin3
{
    public class EmbeddingsPlugin
    {
        private readonly EmbeddingsPluginConfig _config;
        private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

        public EmbeddingsPlugin(EmbeddingsPluginConfig cfg, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
        {
            _config = cfg;
            _embeddingGenerator = embeddingGenerator;
        }

        [Description("Calculates the cosine similarity between two sentences.")]
        public async Task<double> CalculateSimilarity(
            [Description("First sentence.")] string sentence1,
            [Description("Second sentence.")] string sentence2)
        {
            var e1 = await _embeddingGenerator.GenerateAsync(sentence1);
            var e2 = await _embeddingGenerator.GenerateAsync(sentence2);

            return CalculateSimilarity(e1.Vector.ToArray(), e2.Vector.ToArray());
        }

        /// <summary>
        /// Calculates the cosine similarity.
        /// </summary>
        public double CalculateSimilarity(float[] embedding1, float[] embedding2)
        {
            if (embedding1.Length != embedding2.Length)
            {
                return 0;
            }

            double dotProduct = 0.0;
            double magnitude1 = 0.0;
            double magnitude2 = 0.0;

            for (int i = 0; i < embedding1.Length; i++)
            {
                dotProduct += embedding1[i] * embedding2[i];
                magnitude1 += Math.Pow(embedding1[i], 2);
                magnitude2 += Math.Pow(embedding2[i], 2);
            }

            magnitude1 = Math.Sqrt(magnitude1);
            magnitude2 = Math.Sqrt(magnitude2);

            if (magnitude1 == 0.0 || magnitude2 == 0.0)
            {
                throw new ArgumentException("embedding must not have zero magnitude.");
            }

            double cosineSimilarity = dotProduct / (magnitude1 * magnitude2);

            return cosineSimilarity;
        }
    }
}
