using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Jellyzer.Services;

/// <summary>
/// Abstraction for a text translation provider.
/// Implement this interface to add new translation backends
/// (e.g. DeepL, Google Translate, Azure Cognitive Services).
/// </summary>
public interface ITranslationService
{
    /// <summary>
    /// Gets a human-readable name for the provider (for logging / UI).
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Translates a plain-text string from <paramref name="sourceLanguage"/> to
    /// <paramref name="targetLanguage"/>.
    /// </summary>
    /// <param name="text">The text to translate. Must not be null or empty.</param>
    /// <param name="sourceLanguage">Natural-language name of the source language (e.g. "English").</param>
    /// <param name="targetLanguage">Natural-language name of the target language (e.g. "Italian").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The translated text, or the original text on failure.</returns>
    Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default);
}
