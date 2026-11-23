using System.Text.Json.Serialization;

namespace BayControl.Host.Models;

public sealed class SimProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("processNames")]
    public List<string> ProcessNames { get; set; } = [];

    [JsonPropertyName("windowTitleContains")]
    public List<string> WindowTitleContains { get; set; } = [];

    [JsonPropertyName("actions")]
    public Dictionary<string, ActionMapping> Actions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ActionMapping
{
    [JsonPropertyName("keys")]
    public List<string> Keys { get; set; } = [];

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("group")]
    public string? Group { get; set; }

    [JsonPropertyName("holdMs")]
    public int HoldMs { get; set; }
}
