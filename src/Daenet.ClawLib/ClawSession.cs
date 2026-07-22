using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daenet.ClawLib
{
    /*
     * Three-Agent Architecture:
     * 
    User Prompt
     │
     ▼
┌─────────────┐  PlanStep[]   ┌─────────────-┐  per step    ┌─────────────┐
│ Intent Agent│──────────────▶│  Plan Agent  │─────────────▶│  Task Agent │
│ (decompose) │               │ (orchestrate)│◀─────────────│  (execute)  │
└─────────────┘               └─────────────-┘   result +   └─────────────┘
                                                 context     has: CLI tool
                                                             has: Playwright
     *
     *  CLI-only plans can optionally use RunCommandLineAsync
     *  to build an Agent Framework Workflow with one Executor per step.
     */

    /// <summary>
    /// Demonstrates a CLAW (Command Line Agent Workflow) session using a three-agent architecture:
    /// Intent Agent → Plan Agent → Task Agent.
    /// </summary>
    public class ClawSession
    {
        private static bool _unattendedMode = false;

        private readonly string _apiKey;
        private readonly string _deploymentChatModel;
        private readonly string _deploymentEmbeddingModel;
        private readonly string _endpoint;
        private readonly List<AITool> _aiTools;
        private AgentSession _session;


        public AgentSession AgentSession
        {
            get
            {

                return _session;
            }
        }

        /// <summary>
        /// Creates a new CLAW session.
        /// </summary>
        /// <param name="apiKey">Azure OpenAI API key.</param>
        /// <param name="deploymentChatModel">Chat model deployment name (e.g. "gpt-4o").</param>
        /// <param name="deploymentEmbeddingModel">Embedding model deployment name (e.g. "text-embedding-3-small").</param>
        /// <param name="endpoint">Azure OpenAI endpoint URI (e.g. "https://&lt;resource&gt;.openai.azure.com/").</param>
        public ClawSession(string apiKey, string deploymentChatModel, string deploymentEmbeddingModel, string endpoint, List<AITool> tools)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _deploymentChatModel = deploymentChatModel ?? throw new ArgumentNullException(nameof(deploymentChatModel));
            _deploymentEmbeddingModel = deploymentEmbeddingModel ?? throw new ArgumentNullException(nameof(deploymentEmbeddingModel));
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _aiTools = tools;
        }

        /// <summary>
        /// Creates a <see cref="ClawSession"/> by reading connection settings from environment variables:
        /// <list type="bullet">
        ///   <item>AZURE_OPENAI_API_KEY</item>
        ///   <item>AZURE_OPENAI_ENDPOINT</item>
        ///   <item>AZURE_OPENAI_CHATCOMPLETION_DEPLOYMENT  (default: gpt-4o)</item>
        ///   <item>AZURE_OPENAI_EMBEDDING_DEPLOYMENT       (default: text-embedding-3-small)</item>
        /// </list>
        /// </summary>
        public static ClawSession FromEnvironment(List<AITool> tools)
            => new(
                apiKey: Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
                                             ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set."),
                deploymentChatModel: Environment.GetEnvironmentVariable("AZURE_OPENAI_CHATCOMPLETION_DEPLOYMENT") ?? "gpt-4o",
                deploymentEmbeddingModel: Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DEPLOYMENT") ?? "text-embedding-3-small",
                endpoint: Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
                                             ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set."),
                tools: tools
            );

        public async Task<AIAgent> InitializeAsync()
        {
            var endpoint = _endpoint;
            var deploymentName = _deploymentChatModel;

            //var linkedInTools = await playwrightMcpClient.ListToolsAsync();
            Console.OutputEncoding = Encoding.Unicode;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"{_aiTools.Count()} tool(s) available");
            Console.ResetColor();

            // Create a shared IChatClient for all three agents.
            IChatClient chatClient = new AzureOpenAIClient(
                new Uri(endpoint),
                new Azure.AzureKeyCredential(_apiKey))
                .GetChatClient(deploymentName)
                .AsIChatClient();

            // Tools available to the Task Agent (CLI execution + Playwright + MS Learn tools + any other tools).
            AITool[] taskTools = [
                AIFunctionFactory.Create(ExecuteCliCommandAsync),
                .. _aiTools,
                AIFunctionFactory.Create(RememberKeyAsync),
                AIFunctionFactory.Create(PrintRememberKeyValues)
            ];

            var taskToolList = string.Join(Environment.NewLine, taskTools.Select(tool =>
                $"- Name: {tool.Name}{(string.IsNullOrWhiteSpace(tool.Description) ? string.Empty : $"{Environment.NewLine}  Description: {tool.Description}")}"));

            // Agent 3: Task Agent — executes individual tasks.
            // Reused for every step; each invocation gets step-specific instructions via the prompt.
            AIAgent taskAgent = chatClient.AsAIAgent(
                instructions: """
                    You are a task execution agent running on Windows. You receive a specific task to execute
                    along with context from previous steps. Execute the task using the best of available tools.
                    Always return a clear, concise result describing what was done and the output.
                    If execution fails, explain the error and suggest alternatives.
                    """,
                //instructions: """
                //   You are a task execution agent running on Windows. You receive a specific task to execute
                //   along with context from previous steps. Execute the task using the available tools:
                //   - Use ExecuteCliCommandAsync for CLI/PowerShell commands.
                //   - Use Playwright tools for browser automation tasks.
                //   - Use MS Learn tools for accessing Microsoft documentation and learning resources.
                //   - For reasoning or analysis tasks, perform them directly.

                //   Always return a clear, concise result describing what was done and the output.
                //   If execution fails, explain the error and suggest alternatives.
                //   """,
                name: "TaskAgent",
                tools: taskTools);

            // Agent 2: Plan Agent — orchestrates execution of the plan step by step.
            // It calls ExecutePlanAsync which iterates through steps and invokes the Task Agent.
            var planOrchestrator = new PlanOrchestrator(taskAgent);

            AIAgent planAgent = chatClient.AsAIAgent(
                instructions: $"""
                    You are a plan orchestration agent. You receive a plan (a list of steps) and execute
                    them sequentially by calling the {nameof(planOrchestrator.ExecutePlanAsync)} tool ONCE with the complete plan.
                    Each step will be executed by a Task Agent that has access to the following tools:
                    {taskToolList}
                    After execution, summarize the results of all steps to the user.

                    For CLI-only plans where all steps are simple CLI commands, you may alternatively
                    call {nameof(RunCommandLineAsync)} to build an Agent Framework Workflow with one Executor per step.

                    When executing CLI execute them via CMD with '/c <command>' or PowerShell with '-NoProfile -Command <cmd>'. You cannot directly call CLI tools like 'git' or 'dotnet' - they must be invoked through cmd or pwsh to ensure proper execution and output capture.
                    """,
                name: "PlanAgent",
                tools: [
                    AIFunctionFactory.Create(planOrchestrator.ExecutePlanAsync),
                    AIFunctionFactory.Create(RunCommandLineAsync)
                ]);

            // Agent 1: Intent Agent — takes user prompt and creates a plan.
            // The plan is then handed to the Plan Agent for execution.
            var intentOrchestrator = new IntentOrchestrator(planAgent);

            AIAgent intentAgent = chatClient.AsAIAgent(
                instructions: $"""
                    You are an intent analysis agent running on Windows. When the user describes a task:
                    
                    1. Analyze the user's intent carefully.
                    2. Decompose it into a sequential plan of concrete steps.
                    3. Call the {nameof(intentOrchestrator.CreateAndExecutePlanAsync)} tool ONCE with the list of steps.
                       Each step must have:
                       - 'instructions': detailed instructions for executing this specific step,
                         including the exact command, tool, or action to perform.
                       - 'description': a brief human-readable summary of what this step does.
                       - 'type': either "cli", "browser", or "reasoning" to classify the step.
                    4. After the plan executes, summarize the results to the user.
                    
                    Be thorough in your decomposition. Each step should be atomic and self-contained.
                    Include any installation or prerequisite steps if needed.
                    
                    IMPORTANT: Never plan destructive commands (rm -rf, format, del /s, etc.) without 
                    making it clear what will be deleted.
                    """,
                name: "IntentAgent",
                tools: [AIFunctionFactory.Create(intentOrchestrator.CreateAndExecutePlanAsync)]);

            Session = await intentAgent.CreateSessionAsync();

            return intentAgent;
        }

        private static Dictionary<string, string> _memoryStore = new();

        public AgentSession Session { get => Session1; set => Session1 = value; }
        public AgentSession Session1 { get => _session; set => _session = value; }

        /// <summary>
        /// Tool function for the Task Agent to execute a CLI command.
        /// Prompts the user for approval before execution (interceptor pattern).
        /// </summary>
        [Description("Remembers information as a key-value pair for later use.")]
        private static async Task<string> RememberKeyAsync(
            [Description("The key to remember the information under.")] string key,
            [Description("The information to remember.")] string value)
        {
            if (_memoryStore.ContainsKey(key))
            {
                _memoryStore[key] = value;
            }
            else
            {
                _memoryStore.Add(key, value);
            }

            return $"Remembered information under key '{key}'.";
        }


        [Description("Prints all remembered key-value pairs.")]
        private static async void PrintRememberKeyValues()
        {
            foreach (var kvp in _memoryStore)
            {
                Console.WriteLine($"Key: {kvp.Key}, Value: {kvp.Value}");
            }
        }


        /// <summary>
        /// Tool function for the Task Agent to execute a CLI command.
        /// Prompts the user for approval before execution (interceptor pattern).
        /// </summary>
        [Description("Execute a CLI or PowerShell command. Use 'cmd' with '/c <command>' or 'pwsh' with '-NoProfile -Command <cmd>'.")]
        static async Task<string> ExecuteCliCommandAsync(
             [Description("The executable to run (e.g. 'cmd', 'pwsh', 'git', 'dotnet').")] string executable,
             [Description("The arguments to pass to the executable.")] string arguments)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"│   CLI: {executable} {arguments}");
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
                    Console.WriteLine("│   [Unattended mode enabled - all remaining tasks will auto-execute]");
                    Console.ResetColor();
                }
                else if (input == "A")
                    return "ABORTED: The user chose to abort.";
                else if (input == "S")
                    return "SKIPPED: Command was skipped by the user.";
            }

            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process.Start();

                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();

                bool exited = process.WaitForExit(60_000);
                string stdout = await stdoutTask;
                string stderr = await stderrTask;

                if (!exited)
                {
                    process.Kill(entireProcessTree: true);
                    return $"ERROR: Command timed out after 60 seconds.\nPartial output:\n{Truncate(stdout, 2000)}";
                }

                StringBuilder result = new();
                if (!string.IsNullOrWhiteSpace(stdout))
                    result.AppendLine($"STDOUT:\n{Truncate(stdout, 4000)}");
                if (!string.IsNullOrWhiteSpace(stderr))
                    result.AppendLine($"STDERR:\n{Truncate(stderr, 1000)}");
                result.AppendLine($"EXIT CODE: {process.ExitCode}");

                string status = process.ExitCode == 0 ? "✓" : $"✗ (exit {process.ExitCode})";
                Console.ForegroundColor = process.ExitCode == 0 ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine($"│   {status}");
                Console.ResetColor();

                return result.ToString();
            }
            catch (Exception ex)
            {
                return $"ERROR: {ex.Message}";
            }
        }

        /// <summary>
        /// CLI-only workflow path: builds an Agent Framework Workflow with one Executor per step.
        /// Used by the Plan Agent when all steps are CLI commands.
        /// </summary>
        [Description("""
            Execute a plan of CLI-only commands as an Agent Framework Workflow.
            Use this only when ALL steps are CLI commands. Each step becomes an Executor.
            """)]
        static async Task<string> RunCommandLineAsync(
            [Description("CLI tasks to execute. Each has 'executable', 'arguments', and 'description'.")] ClawTask[] tasks)
        {
            if (tasks is null || tasks.Length == 0)
                return "ERROR: No tasks provided.";

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"┌── CLI Workflow: {tasks.Length} task(s)");
            for (int i = 0; i < tasks.Length; i++)
            {
                string prefix = i == tasks.Length - 1 ? "└──" : "├──";
                Console.WriteLine($"{prefix} [{i + 1}] {tasks[i].Description}");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"    {tasks[i].Executable} {tasks[i].Arguments}");
                Console.ForegroundColor = ConsoleColor.Yellow;
            }
            Console.ResetColor();
            Console.WriteLine();

            var executors = tasks.Select((task, index) =>
                new CommandExecutor($"Step{index + 1}", task.Executable, task.Arguments, task.Description, index + 1, tasks.Length))
                .ToArray();

            var builder = new WorkflowBuilder(executors[0]);
            for (int i = 0; i < executors.Length - 1; i++)
                builder.AddEdge(executors[i], executors[i + 1]);
            builder.WithOutputFrom(executors[^1]);
            var workflow = builder.Build();

            StringBuilder results = new();
            await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, input: "");
            await foreach (WorkflowEvent evt in run.WatchStreamAsync())
            {
                if (evt is CommandCompletedEvent cmdEvt)
                {
                    results.AppendLine($"--- Step {cmdEvt.StepNumber}/{cmdEvt.TotalSteps}: {cmdEvt.StepDescription} ---");
                    results.AppendLine(cmdEvt.Output);
                    results.AppendLine();
                }
                else if (evt is WorkflowErrorEvent errorEvt)
                {
                    results.AppendLine($"WORKFLOW ERROR: {errorEvt.Exception?.Message}");
                }
            }

            return results.ToString();
        }

        private static string Truncate(string text, int maxLength)
            => text.Length <= maxLength ? text : text[..maxLength] + $"\n... (truncated, {text.Length - maxLength} chars omitted)";
    }

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
            [Description("The list of plan steps. Each has 'instructions', 'description', and 'type' (cli/browser/reasoning).")] PlanStep[] steps)
        {
            if (steps is null || steps.Length == 0)
                return "ERROR: No steps provided.";

            // Display the plan to the user.
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

            // Serialize the plan and send it to the Plan Agent for execution.
            string planJson = JsonSerializer.Serialize(steps, new JsonSerializerOptions { WriteIndented = true });
            string planPrompt = $"Execute the following plan step by step:\n{planJson}";

            _planSession ??= await planAgent.CreateSessionAsync();
            var response = await planAgent.RunAsync(planPrompt, _planSession);
            return response.Text ?? "Plan execution completed with no output.";
        }
    }

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
            [Description("The list of steps to execute. Each has 'instructions', 'description', and 'type'.")] PlanStep[] steps)
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

                // Interceptor: prompt user for approval or auto-execute in unattended mode.
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
                    // Build the task-specific prompt with instructions and prior context.
                    string taskPrompt = 
                         $"""
                           Task {i + 1} of {steps.Length}: {step.Description}
                           
                           Instructions:
                           {step.Instructions}
                           """;

                    // Each task gets a fresh session so it executes independently
                    // but receives prior context explicitly via the prompt.
                    AgentSession taskSession = await taskAgent.CreateSessionAsync();
                    var response = await taskAgent.RunAsync(taskPrompt, taskSession);
                    string result = response.Text ?? "No output.";

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"│   Result: {Truncate(result, 300)}");
                    Console.WriteLine("└── ✓ Completed");
                    Console.ResetColor();

                    // Accumulate results as context for subsequent steps.
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

    /// <summary>
    /// Represents a single step in the execution plan, created by the Intent Agent.
    /// </summary>
    public sealed class PlanStep
    {
        [JsonPropertyName("instructions")]
        public required string Instructions { get; set; }

        [JsonPropertyName("description")]
        public required string Description { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    /// <summary>
    /// Represents a CLI-only task for the Agent Framework Workflow path.
    /// </summary>
    public sealed class ClawTask
    {
        [JsonPropertyName("executable")]
        public required string Executable { get; set; }

        [JsonPropertyName("arguments")]
        public string Arguments { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public required string Description { get; set; }
    }

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

