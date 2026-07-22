using Daenet.LLMPlugin.TestConsole.Entities;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Realtime;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace Daenet.LLMPlugin.TestConsole
{
    /// <summary>
    /// Built-in agent plugin for managing the console session.
    /// Methods decorated with [Description] are automatically registered as AIFunction tools.
    /// </summary>
    internal class TestConsolePlugin
    {
        private readonly IList<AITool> _tools;
        private readonly AgentSession _session;
        private readonly TestConsoleConfig _config;

        public TestConsolePlugin(IList<AITool> tools, AgentSession session, TestConsoleConfig config)
        {
            _tools = tools;
            _session = session;
            _config = config;
        }

        [Description("Clears the text in the console.")]
        public void ClearConsole()
        {
            Console.Clear();
        }

        [Description("This method deletes all messages in the conversation history. Clears message history in the chat conversation. It does not clear console messages.")]
        public void ClearMessageHistory()
        {
            if (_session.TryGetInMemoryChatHistory(out var history))
                history.Clear();
        }


        [Description("Gets the current date and time.")]
        public string GetDateTime(
            [Description("Provides detailed information about the date and time, including month, week, and day details.")] bool details = false)
        {
            var now = DateTime.Now;
            if (!details)
                return now.ToString("yyyy-MM-dd HH:mm:ss");

            var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
            var endOfWeek = startOfWeek.AddDays(6);
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);
            var weekOfYear = ISOWeek.GetWeekOfYear(now);
            var weekOfMonth = ((now.Day - 1) / 7) + 1;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"DateTime: {now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Day: {now.Day} ({now:dddd})");
            sb.AppendLine($"Day of week: {(int)now.DayOfWeek} ({now:dddd})");
            sb.AppendLine($"Day of year: {now.DayOfYear}");
            sb.AppendLine($"Week of month: {weekOfMonth}");
            sb.AppendLine($"ISO week of year: {weekOfYear}");
            sb.AppendLine($"Week range: {startOfWeek:yyyy-MM-dd} to {endOfWeek:yyyy-MM-dd}");
            sb.AppendLine($"Month: {now.Month} ({now:MMMM})");
            sb.AppendLine($"Days in month: {DateTime.DaysInMonth(now.Year, now.Month)}");
            sb.AppendLine($"Month range: {startOfMonth:yyyy-MM-dd} to {endOfMonth:yyyy-MM-dd}");
            sb.AppendLine($"Year: {now.Year}");
            return sb.ToString();
        }

        [Description("Gets the list of loaded agent functions/plugins")]
        public string ListPlugins(
            [Description("Provides additional detailed information about the plugin.")] bool details = false)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("List of loaded agent functions:");

            foreach (var tool in _tools)
            {
                if (tool is AIFunction func)
                {
                    if (!details)
                        sb.AppendLine(func.Name);
                    else
                        sb.AppendLine($"Name: {func.Name}: {func.Description}");
                }
            }

            return sb.ToString();
        }

        [Description("Sets the color of the system prompt.")]
        public void SetSystemPromptColor(
            [Description("The color of the system prompt.")] ConsoleColor color)
        {
            _config.PromptColor = color;
        }

        [Description("Sets the color of the user prompt/input.")]
        public void SetUserPromptColor(
            [Description("The color of the user prompt or user input.")] ConsoleColor color)
        {
            _config.UserInputColor = color;
        }

        [Description("Sets the color of the assistent message/text.")]
        public void SetAssistentColor(
            [Description("The color of the assistent message.")] ConsoleColor color)
        {
            _config.AssistentMessageColor = color;
        }

        [Description("Sets the system prompt text.")]
        public void SetSystemPromptText(
            [Description("The text of the system prompt.")] string promptText)
        {
            _config.SystemPrompt = promptText;
        }

        [Description("Get environment variables.")]
        public string GetEnvironmentVariables()
        {
            StringBuilder sb = new StringBuilder();
            var vars = Environment.GetEnvironmentVariables();
            foreach (var key in vars.Keys)
            {
                sb.AppendLine($"{key} - {vars[key]}");
            }

            return sb.ToString();
        }
    }
}