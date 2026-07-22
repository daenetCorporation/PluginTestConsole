using Microsoft.Agents.AI.Workflows;

namespace Daenet.ClawLib
{
    /// <summary>
    /// Custom workflow event emitted when a CLI command executor completes.
    /// </summary>
    internal sealed class CommandCompletedEvent(string output, int stepNumber, int totalSteps, string stepDescription)
        : WorkflowEvent(output)
    {
        public string Output => output;
        public int StepNumber => stepNumber;
        public int TotalSteps => totalSteps;
        public string StepDescription => stepDescription;

        public override string ToString() => $"[Step {stepNumber}/{totalSteps}] {stepDescription}: {Truncate(output, 200)}";

        private static string Truncate(string text, int maxLength)
            => text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
