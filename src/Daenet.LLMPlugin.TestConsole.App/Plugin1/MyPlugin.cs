using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Daenet.LLMPlugin.TestConsole.App.Plugin1
{
    public class MyPlugin
    {
        private readonly MyPluginConfig _cfg;

        public MyPlugin(MyPluginConfig cfg)
        {
            _cfg = cfg;
        }


        [KernelFunction]
        [Description("Provides the list of names of processes")]
        public string GetProcessInfo([Description("If set in true, it provides the detaile process information.")] bool provideDetailedInfo = false)
        {
            StringBuilder sb = new StringBuilder();

            foreach (Process proc in Process.GetProcesses().ToList())
            {
                sb.AppendLine($"Proce name: {proc.ProcessName}");
                if (provideDetailedInfo)
                {
                    sb.AppendLine($"Proces ID: {proc.Id}"); ;
                    sb.AppendLine($"Proces working set: {proc.WorkingSet64}");
                   //sb.Append($"Working directory or local path: {proc.StartInfo.WorkingDirectory}");
                    sb.AppendLine("---------------------------------------------");
                }
            }

            return sb.ToString();
        }

        [KernelFunction]
        [Description("Provides the count of running processes.")]
        public int GetProcessCount()
        {
            return Process.GetProcesses().ToList().Count;
        }

        [KernelFunction]
        [Description("Kills the process with the given name or process id.")]
        public string KillProcess(
            [Description("The name of the process to be killed.")] string? processName,
            [Description("The ID of the process to be killed.")] int? processId)
        {
            try
            {
                var processes = Process.GetProcesses().ToList();

                if (processId.HasValue && processId > 0)
                {
                    var targetProcess = processes.FirstOrDefault(p => p.Id == processId);
                    if (targetProcess != null)
                    {
                        targetProcess.Kill();
                    }
                }
                else if (processName != null)
                {
                    var targetProcess = processes.FirstOrDefault(p => p.ProcessName == processName);
                    if (targetProcess != null)
                        targetProcess.Kill();
                }
            }
            catch (Exception ex)
            {
                return $"The process cannot be terminated. Error: {ex.Message}";
            }

            return "Process has been killed!";
        }

        [KernelFunction]
        [Description("Gets the local IP addres of the machine.")]

        public string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return "No network adapters with an IPv4 address in the system!";
        }

        [KernelFunction]
        [Description("Gets the name of the local machine.")]
        public string GetMachineName()
        {
            return Environment.MachineName;
        }
    }
}
