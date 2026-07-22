using System.Text.Json.Serialization;

namespace Daenet.ClawLib.Entities
{
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
}
