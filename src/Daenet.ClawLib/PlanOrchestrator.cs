using Daenet.ClawLib.Entities;
using Microsoft.Agents.AI;
using System.ComponentModel;
using System.Text;

namespace Daenet.ClawLib
{
    /// <summary>
    /// Orchestrates plan execution: iterates through steps, invoking the Task Agent
    /// for each one and passing prior results as context. Used as a tool by the Plan Agent.
    /// </summary>
    internal sealed class PlanOrchestrator(AIAgent taskAgent)
    {
        private static bool _unattendedMode = false;

        [Description("""
            Execute all steps in the plan sequentially. Each step is executed by a Task Agent
            that receives step-specific instructions and context from prior steps.
            """)]
        public async Task<string> ExecutePlanAsync(
            [Description("The list of steps to execute. Each has 'instructions', 'description', and 'type'.")]
            PlanStep[] steps)
        {
            if (steps is null || steps.Length == 0)
                return "ERROR: No steps provided.";

            StringBuilder allResults = new();

            for (int i = 0; i < steps.Length; i++)
            {
                var step = steps[i];

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"┌── Step {i + 1}/{steps.Length}: {step.Description}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"│   Type: {step.Type ?? "general"}");
                Console.ResetColor();

                if (_unattendedMode)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("│   [Unattended Mode: Auto-executing]");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write("│   Execute? [Y]es / [S]kip / [A]bort / [U]nattended > ");
                    Console.ResetColor();

                    string? input = Console.ReadLine()?.Trim().ToUpperInvariant();

                    if (input == "U")
                    {
                        _unattendedMode = true;
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine("│   [Unattended mode enabled - all remaining steps will auto-execute]");
                        Console.ResetColor();
                    }
                    else if (input == "A")
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("└── Aborted by user.");
                        Console.ResetColor();
                        allResults.AppendLine($"--- Step {i + 1}: ABORTED ---");
                        break;
                    }
                    else if (input == "S")
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("└── Skipped.");
                        Console.ResetColor();
                        allResults.AppendLine($"--- Step {i + 1}: SKIPPED ---");
                        continue;
                    }
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("│   Executing via Task Agent...");
                Console.ResetColor();

                try
                {
                    string taskPrompt =
                         $"""
                           Task {i + 1} of {steps.Length}: {step.Description}

                           Instructions:
                           {step.Instructions}
                           """;

                    AgentSession taskAgentSession = await taskAgent.CreateSessionAsync();
                    var response = await taskAgent.RunAsync(taskPrompt, taskAgentSession);
                    string result = response.Text ?? "No output.";

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"│   Result: {Truncate(result, 300)}");
                    Console.WriteLine("└── ✓ Completed");
                    Console.ResetColor();

                    allResults.AppendLine($"--- Step {i + 1}/{steps.Length}: {step.Description} ---");
                    allResults.AppendLine(result);
                    allResults.AppendLine();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"└── Error: {ex.Message}");
                    Console.ResetColor();

                    allResults.AppendLine($"--- Step {i + 1}: ERROR: {ex.Message} ---");
                }
            }

            return allResults.ToString();
        }

        private static string Truncate(string text, int maxLength)
            => text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
