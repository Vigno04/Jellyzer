namespace Jellyfin.Plugin.Jellyzer.Models;

/// <summary>
/// Translation result for a single metadata field.
/// </summary>
public class FieldTranslationResult
{
    /// <summary>
    /// Gets or sets the field name (e.g. "Title", "Overview", "Tagline").
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the original text before translation.
    /// </summary>
    public string? Original { get; set; }

    /// <summary>
    /// Gets or sets the translated text.
    /// </summary>
    public string? Translated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether translation was actually performed
    /// (false when the field was empty or skipped).
    /// </summary>
    public bool WasTranslated { get; set; }
}
