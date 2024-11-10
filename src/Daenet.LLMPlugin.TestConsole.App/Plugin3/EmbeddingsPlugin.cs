using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Embeddings;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Daenet.LLMPlugin.TestConsole.App.Plugin3
{
    public class EmbeddingsPlugin
    {
        private EmbeddingsPluginConfig _config;
        private Kernel _kernel;

        public EmbeddingsPlugin(EmbeddingsPluginConfig cfg, Kernel kernel)
        {
            _config = cfg;
            _kernel = kernel;
        }

#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        [KernelFunction()]
        [Description("Calculates the cosine similarity between two sentences.")]
        public async Task<double> CalculateSimilarity(
                 [Description("First sentence.")] string sentence1,
                 [Description("Second sentence.")] string sentence2)
        {
            var embeddingSvc = _kernel.GetRequiredService<ITextEmbeddingGenerationService>();
            var e1 = await embeddingSvc.GenerateEmbeddingAsync(sentence1);
            var e2 = await embeddingSvc.GenerateEmbeddingAsync(sentence2);

            return CalculateSimilarity(e1.ToArray(), e2.ToArray());
        }
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.


        /// <summary>
        /// Calculates the cosine similarity.
        /// </summary>
        /// <param name="embedding1"></param>
        /// <param name="embedding2"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public double CalculateSimilarity(float[] embedding1, float[] embedding2)
        {
            if (embedding1.Length != embedding2.Length)
            {
                return 0;
                //throw new ArgumentException("embedding must have the same length.");
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
