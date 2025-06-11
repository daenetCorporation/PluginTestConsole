using System;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Daenet.LLMPlugin.TestConsole.Entities
{
    /// <summary>
    /// Defines the configuration of MCP tools, which will be imported in the test console.
    /// </summary>
    public class McpToolsConfig
    {
        /// <summary>
        /// The list of MCP servers to be used as MCP imported tools.
        /// </summary>
        public List<McpServer>? McpServers { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class McpServer
    {
        /// <summary>
        /// The name of the server. The name can contain only ASCII letters, digits, and underscores.
        /// </summary>
        public string?  Name { get; set; } 
        /// <summary>
        /// The URL of the MCP server connected via SSE.
        /// </summary>
        public Uri? Url { get; set; }

        /// <summary>
        /// The comand for MCP servers connected via STDIO.
        /// </summary>
        public string? Command { get; set; }

        /// <summary>
        /// Arguments of MCP servers connected via STDIO.
        /// Examples: "['-y', '@modelcontextprotocol/server-everything'']"
        /// </summary>
        public string? Arguments { get; set; }

        /// <summary>
        /// The port of the MCP server.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Optoinal arguments for the MCP server. If specified the header 'ImpersonatingUser' will be appended to the request with the value of this property.
        /// </summary>
        public string? ImpersonatingUser { get; set; }

    }
}
