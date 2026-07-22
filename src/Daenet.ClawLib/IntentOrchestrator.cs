using Daenet.ClawLib.Entities;
using Microsoft.Agents.AI;
using System.ComponentModel;
using System.Text.Json;

namespace Daenet.ClawLib
{
    /// <summary>
    /// Orchestrates intent decomposition: creates a plan and hands it to the Plan Agent.
    /// Used as a tool by the Intent Agent.
    /// </summary>
    internal sealed class IntentOrchestrator(AIAgent planAgent)
    {
        private AgentSession? _planSession;

        [Description("""
            Create a plan from the decomposed steps and execute it via the Plan Agent.
            Provide ALL steps at once. Each step has instructions, a description, and a type.
            """)]
        public async Task<string> CreateAndExecutePlanAsync(
            [Description("The list of plan steps. Each has 'instructions', 'description', and 'type' (cli/browser/reasoning).")]
            PlanStep[] steps)
        {
            if (steps is null || steps.Length == 0)
                return "ERROR: No steps provided.";

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"┌── Plan: {steps.Length} step(s)");
            for (int i = 0; i < steps.Length; i++)
            {
                string prefix = i == steps.Length - 1 ? "└──" : "├──";
                string typeTag = steps[i].Type?.ToUpperInvariant() switch
                {
                    "CLI" => "🖥️",
                    "BROWSER" => "🌐",
                    "REASONING" => "🧠",
                    _ => "📋"
                };
                Console.WriteLine($"{prefix} [{i + 1}] {typeTag} {steps[i].Description}");
            }
            Console.ResetColor();
            Console.WriteLine();

            string planJson = JsonSerializer.Serialize(steps, new JsonSerializerOptions { WriteIndented = true });
            string planPrompt = $"Execute the following plan step by step:\n{planJson}";

            _planSession ??= await planAgent.CreateSessionAsync();
            var response = await planAgent.RunAsync(planPrompt, _planSession);
            return response.Text ?? "Plan execution completed with no output.";
        }
    }
}
