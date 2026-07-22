using System.Text.Json.Serialization;

namespace Daenet.ClawLib.Entities
{
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
}
