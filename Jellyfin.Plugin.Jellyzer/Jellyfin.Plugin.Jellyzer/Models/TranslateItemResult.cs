using System;
using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.Jellyzer.Models;

/// <summary>
/// Result returned by <c>POST /Jellyzer/TranslateItem/{itemId}</c>.
/// </summary>
public class TranslateItemResult
{
    /// <summary>
    /// Gets or sets the item ID that was processed.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the item name after translation (or original when not translated).
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the operation succeeded overall.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets an optional error message when <see cref="Success"/> is false.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets the per-field translation results.
    /// </summary>
    public Collection<FieldTranslationResult> Fields { get; } = [];
}
