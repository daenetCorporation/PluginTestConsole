using Daenet.LLMPlugin.TestConsole.Entities;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace Daenet.LLMPlugin.TestConsole
{
    /// <summary>
    /// Plugin used for testing the console application. It is automatically instantiated.
    /// </summary>
    internal class TestConsolePlugin
    {

        private Kernel _kernel;

        private ChatHistory _history;

        private TestConsoleConfig _config;

        /// <summary>
        /// Creates the instance of the built-inn plugin.
        /// </summary>
        /// <param name="kernel"></param>
        /// <param name="history"></param>
        public TestConsolePlugin(Kernel kernel, ChatHistory history, TestConsoleConfig config)
        {
            _kernel = kernel;
            _history = history;
            _config = config;
        }

        /// <summary>
        /// On user clear intent, this method will clear the console.
        /// </summary>
        [KernelFunction]
        [Description("Clears the text in the console.")]
        public void ClearConsole()
        {
            Console.Clear();
        }

        /// <summary>
        /// On user clear intent, this method will clear the message history.
        /// </summary>
      
        [KernelFunction]
        [Description("This method deletes all messages in the conversation history. Clears message history in the chat conversation. It does not clear console messages.")]
        public void ClearMessageHistory()
        {
            _history.Clear();
            _history.AddUserMessage("Started the new conversation");
        }

        [KernelFunction]
        [Description("Gets the list of loaded plugins")]
        public string ListPlugins(
            [Description("Provides additional detailed information about the plugin.")] bool details = false)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("List of loaded plugins:");

            foreach (var item in _kernel.Plugins)
            {
                if (!details)
                    sb.AppendLine(item.Name);
                else
                {
                    sb.AppendLine($"Name: {item.Name}: {item.Description}");
                    foreach (var func in item)
                    {
                        sb.AppendLine($"Name: {func.Name}: {func.Description}");
                    }
                }
            }

            return sb.ToString();
        }

        [KernelFunction]
        [Description("Sets the color of the system prompt.")]
        public void SetSystemPromptColor(
          [Description("The color of the system prompt.")] ConsoleColor color)
        {
           _config.PromptColor = color;
        }

        [KernelFunction]
        [Description("Sets the color of the user prompt/input.")]
        public void SetUserPromptColor(
       [Description("The color of the user prompt or user input.")] ConsoleColor color)
        {
            _config.UserInputColor = color;
        }

        [KernelFunction]
        [Description("Sets the color of the assistent message/text.")]
        public void SetAssistentColor(
        [Description("The color of the assistent message.")] ConsoleColor color)
        {
            _config.AssistentMessageColor = color;
        }

        [KernelFunction]
        [Description("Sets the system prompt text.")]
        public void SetSystemPromptText(
       [Description("The text of the system prompt.")] string promptText)
        {
            _config.SystemPrompt = promptText;
        }

        [KernelFunction]
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
