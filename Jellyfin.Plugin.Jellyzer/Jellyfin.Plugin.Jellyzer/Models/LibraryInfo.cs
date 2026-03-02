using System;

namespace Jellyfin.Plugin.Jellyzer.Models;

/// <summary>
/// Lightweight representation of a Jellyfin virtual library for the config UI.
/// </summary>
public class LibraryInfo
{
    /// <summary>Gets or sets the library item ID.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the library display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the library collection type (e.g. "movies", "tvshows").</summary>
    public string? CollectionType { get; set; }
}
