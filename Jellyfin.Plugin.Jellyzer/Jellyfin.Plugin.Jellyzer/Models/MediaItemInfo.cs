using System;

namespace Jellyfin.Plugin.Jellyzer.Models;

/// <summary>
/// Lightweight representation of a translatable media item.
/// </summary>
public class MediaItemInfo
{
    /// <summary>Gets or sets the item ID.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the item name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the item type (Movie, Series, etc.).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Gets or sets the production year (may be null).</summary>
    public int? Year { get; set; }
}
