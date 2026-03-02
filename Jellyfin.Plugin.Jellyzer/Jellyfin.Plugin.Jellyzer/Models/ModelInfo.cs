using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Jellyzer.Models;

/// <summary>
/// Represents an AI model available on the configured endpoint.
/// </summary>
public class ModelInfo
{
    /// <summary>
    /// Gets or sets the model identifier (e.g. "meta-llama-3.1-8b-instruct").
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets a user-friendly display name (falls back to <see cref="Id"/> when absent).
    /// </summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Id) ? "(unknown)" : Id;
}
