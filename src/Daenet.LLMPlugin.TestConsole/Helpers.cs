using System;
using System.Collections.Generic;
using System.Text;

namespace Daenet.LLMPlugin.TestConsole
{
    internal class Helpers
    {
        public static void GetAzureEndpointAndModelDeployment(out string apiKey, out string chatModelDeploymentName)
        {
            apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
            chatModelDeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-5.4-mini";
        }

    }
}
