using MediaBrowser.Model.Plugins;

namespace Jellyzer.Configuration;

/// <summary>
/// Plugin configuration for Jellyzer.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets whether debug logging is enabled for translation.
    /// </summary>
    public bool EnableDebugLogs { get; set; } = false;

    public bool TranslateTitle { get; set; } = true;
    public bool TranslateDescription { get; set; } = true;
    public bool TranslateSeasonTitle { get; set; } = false;
    public bool TranslateEpisodeTitle { get; set; } = false;

    /// <summary>
    /// Gets or sets the domain for the OpenAI-compatible API.
    /// </summary>
    public string OpenApiDomain { get; set; } = "http://localhost:11434/v1";

    /// <summary>
    /// Gets or sets the optional API key used for OpenAI-compatible APIs.
    /// </summary>
    public string OpenApiKey { get; set; } = string.Empty;

    public string OpenApiModel { get; set; } = string.Empty;

    public string InputLanguage { get; set; } = string.Empty;
    public string OutputLanguage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the max wait time for each LLM request in seconds.
    /// </summary>
    public int LlmTimeoutSeconds { get; set; } = 120;

    public string SystemPrompt { get; set; } = "You are a professional translator. Translate the text from {input-language} to {output-language}. Output ONLY the exact translation with no quotes, explanations, or additional text.";
}
