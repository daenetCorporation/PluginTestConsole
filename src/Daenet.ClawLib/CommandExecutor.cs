using Microsoft.Agents.AI.Workflows;
using System.Diagnostics;
using System.Text;

namespace Daenet.ClawLib
{
    /// <summary>
    /// Workflow executor for CLI-only plans. Runs a single CLI command with user approval.
    /// </summary>
    internal sealed class CommandExecutor : Executor<string, string>
    {
        private readonly string _executable;
        private readonly string _arguments;
        private readonly string _description;
        private readonly int _stepNumber;
        private readonly int _totalSteps;
        private static bool _unattendedMode = false;

        public CommandExecutor(string id, string executable, string arguments, string description, int stepNumber, int totalSteps)
            : base(id, ExecutorOptions.Default)
        {
            _executable = executable;
            _arguments = arguments;
            _description = description;
            _stepNumber = stepNumber;
            _totalSteps = totalSteps;
        }

        public override async ValueTask<string> HandleAsync(string previousOutput, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"┌── Step {_stepNumber}/{_totalSteps}: {_description}");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"│   {_executable} {_arguments}");

            if (!string.IsNullOrWhiteSpace(previousOutput) && _stepNumber > 1)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"│   Previous output: {Truncate(previousOutput, 200)}");
            }
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
                    return "ABORTED: The user chose to abort.";
                }
                else if (input == "S")
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("└── Skipped.");
                    Console.ResetColor();
                    string skipResult = $"SKIPPED: Step {_stepNumber} was skipped.";
                    await context.AddEventAsync(new CommandCompletedEvent(skipResult, _stepNumber, _totalSteps, _description), cancellationToken);
                    return skipResult;
                }
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("│   Executing...");
            Console.ResetColor();

            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = _executable,
                    Arguments = _arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process.Start();
                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
                bool exited = process.WaitForExit(60_000);
                string stdout = await stdoutTask;
                string stderr = await stderrTask;

                if (!exited)
                {
                    process.Kill(entireProcessTree: true);
                    string timeoutResult = $"ERROR: Timed out.\nPartial output:\n{Truncate(stdout, 2000)}";
                    await context.AddEventAsync(new CommandCompletedEvent(timeoutResult, _stepNumber, _totalSteps, _description), cancellationToken);
                    return timeoutResult;
                }

                StringBuilder result = new();
                if (!string.IsNullOrWhiteSpace(stdout))
                    result.AppendLine($"STDOUT:\n{Truncate(stdout, 4000)}");
                if (!string.IsNullOrWhiteSpace(stderr))
                    result.AppendLine($"STDERR:\n{Truncate(stderr, 1000)}");
                result.AppendLine($"EXIT CODE: {process.ExitCode}");

                string status = process.ExitCode == 0 ? "✓ Completed" : $"✗ Failed (exit {process.ExitCode})";
                Console.ForegroundColor = process.ExitCode == 0 ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine($"└── {status}");
                Console.ResetColor();

                string finalResult = result.ToString();
                await context.AddEventAsync(new CommandCompletedEvent(finalResult, _stepNumber, _totalSteps, _description), cancellationToken);
                return finalResult;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"└── Error: {ex.Message}");
                Console.ResetColor();
                string errorResult = $"ERROR: {ex.Message}";
                await context.AddEventAsync(new CommandCompletedEvent(errorResult, _stepNumber, _totalSteps, _description), cancellationToken);
                return errorResult;
            }
        }

        private static string Truncate(string text, int maxLength)
            => text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
