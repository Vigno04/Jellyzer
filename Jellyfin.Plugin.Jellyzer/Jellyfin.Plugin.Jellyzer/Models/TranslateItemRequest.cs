namespace Jellyfin.Plugin.Jellyzer.Models;

/// <summary>
/// Request body for <c>POST /Jellyzer/TranslateItem/{itemId}</c>.
/// Callers may override which fields to translate per-request;
/// when null the plugin configuration defaults are used.
/// </summary>
public class TranslateItemRequest
{
    /// <summary>
    /// Gets or sets a value indicating whether the item title should be translated.
    /// Null = use plugin configuration default.
    /// </summary>
    public bool? TranslateTitle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item overview should be translated.
    /// Null = use plugin configuration default.
    /// </summary>
    public bool? TranslateOverview { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item tagline should be translated.
    /// Null = use plugin configuration default.
    /// </summary>
    public bool? TranslateTagline { get; set; }

    /// <summary>
    /// Gets or sets the source language override for this request.
    /// Null = use plugin configuration default.
    /// </summary>
    public string? SourceLanguage { get; set; }

    /// <summary>
    /// Gets or sets the target language override for this request.
    /// Null = use plugin configuration default.
    /// </summary>
    public string? TargetLanguage { get; set; }
}
