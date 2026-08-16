using Anthropic;
using Azure.AI.OpenAI;
using Daenet.ClawLib.Entities;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

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
            set
            {
                _session = value;
            }
        }

        private static Dictionary<string, string> _memoryStore = new();
        
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
        /// Creates a <see cref="ClawSession"/> from explicit configuration values.
        /// </summary>
        public static ClawSession Create(string apiKey, string deploymentChatModel, string deploymentEmbeddingModel, string endpoint, List<AITool> tools)
            => new(apiKey, deploymentChatModel, deploymentEmbeddingModel, endpoint, tools);

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
                deploymentChatModel: Environment.GetEnvironmentVariable("AZURE_OPENAI_CHATCOMPLETION_DEPLOYMENT") ?? "gpt-5.4", //""claude-sonnet-5",
                deploymentEmbeddingModel: Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DEPLOYMENT") ?? "text-embedding-3-small",
                endpoint: Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
                                             ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set."),
                tools: tools
            );

        public IChatClient CreateClient()
        {
            if (IsAnthropicDeployment(_deploymentChatModel))
            {
                return CreateAnthropicClient();
            }

            return new AzureOpenAIClient(
                new Uri(_endpoint),
                new Azure.AzureKeyCredential(_apiKey))
                .GetChatClient(_deploymentChatModel)
                .AsIChatClient();
        }

        private IChatClient CreateAnthropicClient()
        {
            var anthropicClient = new AnthropicClient
            {
                ApiKey = _apiKey
            };

            return anthropicClient.AsIChatClient(_deploymentChatModel);
        }

        private static bool IsAnthropicDeployment(string deploymentName)
            => deploymentName.Contains("claude", StringComparison.OrdinalIgnoreCase)
            || deploymentName.Contains("anthropic", StringComparison.OrdinalIgnoreCase);

        public async Task<AIAgent> InitializeAsync(ILoggerFactory? loggerFactory)
        {

            Console.OutputEncoding = Encoding.Unicode;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"{_aiTools.Count()} tool(s) available");
            Console.ResetColor();

            IChatClient chatClient = CreateClient();

            AITool[] taskTools = [
                AIFunctionFactory.Create(ExecuteCliCommandAsync),
                .. _aiTools,
                AIFunctionFactory.Create(RememberKeyAsync),
                AIFunctionFactory.Create(PrintRememberKeyValues)
            ];

            var taskToolList = string.Join(Environment.NewLine, taskTools.Select(tool =>
                $"- Name: {tool.Name}{(string.IsNullOrWhiteSpace(tool.Description) ? string.Empty : $"{Environment.NewLine}  Description: {tool.Description}")}"));

            var opts = new AgentSkillsProviderOptions()
            {
               DisableRunSkillScriptApproval = true,
                 IncludeDetailedErrors = true,
            };

            var fileOptions = new AgentFileSkillsSourceOptions
            {
                AllowedResourceExtensions = [".md", ".txt", ".py", ".jpg", ".json"],
            };


            // --- Skills Provider ---
            // Discovers skills from the 'skills' directory containing SKILL.md files.
            // The script runner runs file-based scripts (e.g. Python) as local subprocesses.
            var skillsProvider = new AgentSkillsProvider(
                Path.Combine(AppContext.BaseDirectory, "Skills"),
                SubprocessScriptRunner.RunAsync,
                loggerFactory: loggerFactory,
                options: opts,
                fileOptions: fileOptions);

      
            var taskAgentOptions = new ChatClientAgentOptions
            {
                Name = "TaskAgent",
                Description = "Task execution agent running on Windows.",
                ChatOptions = new ChatOptions
                {
                    Instructions = """
                        You are a task execution agent running on Windows. You receive a specific task to execute
                        along with context from previous steps. Execute the task using the best of available tools and skills.
                        Always return a clear, concise result describing what was done and the output.
                        If execution fails, explain the error and suggest alternatives.
                        Always provide the complete error message if available.
                        """,
                    Tools = taskTools,
                },
                //AIContextProviders = [skillsProvider],
            };

            AIAgent taskAgent = chatClient.AsAIAgent(
                options: taskAgentOptions,
                loggerFactory: loggerFactory);

            var planOrchestrator = new PlanOrchestrator(taskAgent);

            var planAgentOptions = new ChatClientAgentOptions
            {
                Name = "PlanAgent",
                Description = "Plan orchestration agent.",
                ChatOptions = new ChatOptions
                {
                    Instructions = $"""
                        You are a plan orchestration agent. You receive a plan (a list of steps) and execute
                        them sequentially by calling the {nameof(planOrchestrator.ExecutePlanAsync)} tool ONCE with the complete plan.
                        Each step will be executed by a Task Agent that has access to the following tools:
                        {taskToolList} and any available skills. Create execution steps that uses tasks, which can be executed by available tools and skills and yoir knowledge.
                        After execution, summarize the results of all steps to the user.
                        """,
                    Tools = [
                        AIFunctionFactory.Create(planOrchestrator.ExecutePlanAsync),
                    ],
                },
                //AIContextProviders = [skillsProvider],
            };

            AIAgent planAgent = chatClient.AsAIAgent(
                options: planAgentOptions,
                loggerFactory: loggerFactory);

            var intentOrchestrator = new IntentOrchestrator(planAgent);

            var intentAgentOptions = new ChatClientAgentOptions
            {
                Name = "IntentAgent",
                Description = "Intent analysis and planning agent.",
                ChatOptions = new ChatOptions
                {
                    Instructions = $"""
                        You are an intent analysis agent running on Windows. When the user describes a task:

                        1. Analyze the user's intent carefully.
                        2. Decompose it into a sequential plan of concrete steps.
                        3. If the user intent is simple and does not requieres a plan or some CLI and you have tools available which can execute it, the use the tool to execute the task.
                        4. Use also skills if available to create required tasks.If skill is used, it should be executed atomicaly without plan.
                        5. If the plan involves one or more skills, execution of every skill is atomic task. Do not try to plan tasks owned by the skill.
                        6. Call the {nameof(intentOrchestrator.CreateAndExecutePlanAsync)} tool ONCE with the list of steps.
                           Each step must have:
                           - 'instructions': detailed instructions for executing this specific step,
                             including the exact command, tool, or action to perform.
                           - 'description': a brief human-readable summary of what this step does.
                           - 'type': either "cli", "browser", or "reasoning" to classify the step.
                        7. After the plan executes, summarize the results to the user.

                        Be thorough in your decomposition. Each step should be atomic and self-contained.
                        Include any installation or prerequisite steps if needed.

                        IMPORTANT: Never plan destructive commands (rm -rf, format, del /s, etc.) without 
                        making it clear what will be deleted.
                        List of available tools:
                        {taskToolList}
                        """,
                    Tools = [AIFunctionFactory.Create(intentOrchestrator.CreateAndExecutePlanAsync)],
                },
                AIContextProviders = [skillsProvider],
            };

            AIAgent intentAgent = chatClient.AsAIAgent(
                options: intentAgentOptions,
                loggerFactory: loggerFactory);

            AgentSession = await intentAgent.CreateSessionAsync();

            return intentAgent;
        }

      
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
        [Description("Execute any CLI command or PowerShell command. Use 'cmd' with '/c <command>' or 'pwsh' with '-NoProfile -Command <cmd>'.")]
        static async Task<string> ExecuteCliCommandAsync(
             [Description("A short description of the task being executed.")] string shortDescriptionOfTask,
             [Description("The executable to run (e.g. 'cmd', 'pwsh', 'git', 'dotnet').")] string executable,
             [Description("The arguments to pass to the executable.")] string arguments,
             [Description("The timeout in milliseconds for the command to complete.")] int timeoutMs = 60000)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"│   Task: {shortDescriptionOfTask}");
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
                    return $"ERROR: Command timed out after {timeoutMs / 1000} seconds.\nPartial output:\n{Truncate(stdout, 2000)}";
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
}
