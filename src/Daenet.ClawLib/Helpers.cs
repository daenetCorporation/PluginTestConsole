using Microsoft.Agents.AI;
using System.Text;

namespace Daenet.ClawLib
{
    internal static class Helpers
    {
        /// <summary>
        /// Reads the four Azure OpenAI connection settings from environment variables.
        /// </summary>
        public static void GetAzureOpenAIConfig(
            out string apiKey,
            out string endpoint,
            out string chatDeployment,
            out string embeddingDeployment)
        {
            apiKey            = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
                                    ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");
            endpoint          = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
                                    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
            chatDeployment      = Environment.GetEnvironmentVariable("AZURE_OPENAI_CHATCOMPLETION_DEPLOYMENT") ?? "gpt-4o";
            embeddingDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DEPLOYMENT") ?? "text-embedding-3-small";
        }

        /// <summary>
        /// Runs an interactive prompt loop that sends each line of user input to
        /// <paramref name="agent"/> and prints the response until the user types
        /// "exit" or closes stdin.
        /// </summary>
        public static async Task RunConversationLoopAsync(AIAgent agent)
        {
            AgentSession session = await agent.CreateSessionAsync();

            Console.OutputEncoding = Encoding.Unicode;
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("CLAW Session started. Type your request (or 'exit' to quit).");
            Console.ResetColor();

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("\nYou > ");
                Console.ResetColor();

                string? userInput = Console.ReadLine();

                if (userInput is null || userInput.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                if (string.IsNullOrWhiteSpace(userInput))
                    continue;

                try
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("Agent thinking...");
                    Console.ResetColor();

                    var response = await agent.RunAsync(userInput, session);

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"\nAgent > {response.Text}");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error: {ex.Message}");
                    Console.ResetColor();
                }
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Session ended.");
            Console.ResetColor();
        }
    }
}
