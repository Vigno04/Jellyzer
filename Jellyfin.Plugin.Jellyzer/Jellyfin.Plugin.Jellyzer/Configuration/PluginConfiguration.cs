using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Jellyzer.Configuration;

/// <summary>
/// Jellyzer plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// Sets sensible defaults: LM Studio running locally, English → Italian.
    /// </summary>
    public PluginConfiguration()
    {
        LmStudioBaseUrl = "http://localhost:1234/v1";
        ModelName = "local-model";
        SourceLanguage = "English";
        TargetLanguage = "Italian";
        TranslateTitle = true;
        TranslateOverview = true;
        TranslateTagline = true;
    }

    // ── AI / Translation provider ─────────────────────────────────────────────

    /// <summary>
    /// Gets or sets the base URL of the OpenAI-compatible API endpoint.
    /// Default: LM Studio local server (http://localhost:1234/v1).
    /// </summary>
    public string LmStudioBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the model identifier sent in every API request.
    /// LM Studio usually ignores this value; other endpoints require it.
    /// </summary>
    public string ModelName { get; set; }

    // ── Language settings ─────────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets the source language of the original metadata.
    /// Jellyfin does not always expose this; English is the safe default.
    /// </summary>
    public string SourceLanguage { get; set; }

    /// <summary>
    /// Gets or sets the target language for the translated metadata.
    /// </summary>
    public string TargetLanguage { get; set; }

    // ── Fields to translate ───────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets a value indicating whether the item title should be translated.
    /// </summary>
    public bool TranslateTitle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item overview/synopsis should be translated.
    /// </summary>
    public bool TranslateOverview { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item tagline should be translated.
    /// </summary>
    public bool TranslateTagline { get; set; }
}
