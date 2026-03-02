using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Jellyzer.Services;

/// <summary>
/// Translation provider that uses any OpenAI-compatible chat-completion API
/// (LM Studio, Ollama with OpenAI adapter, OpenAI itself, etc.).
/// </summary>
public class OpenAICompatibleTranslationService : ITranslationService
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenAICompatibleTranslationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAICompatibleTranslationService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public OpenAICompatibleTranslationService(
        IHttpClientFactory httpClientFactory,
        ILogger<OpenAICompatibleTranslationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProviderName => "OpenAI-Compatible (LM Studio)";

    /// <inheritdoc />
    public async Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var config = Plugin.Instance?.Configuration
            ?? throw new InvalidOperationException("Jellyzer plugin configuration is not available.");

        var baseUrl = config.LmStudioBaseUrl.TrimEnd('/');
        var requestUrl = $"{baseUrl}/chat/completions";

        var payload = new ChatCompletionRequest
        {
            Model = config.ModelName,
            Temperature = 0.3f,
            Messages =
            [
                new ChatMessage
                {
                    Role = "system",
                    Content = BuildSystemPrompt(sourceLanguage, targetLanguage)
                },
                new ChatMessage
                {
                    Role = "user",
                    Content = text
                }
            ]
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogDebug(
            "Jellyzer: calling {Provider} at {Url} (model={Model}, {Src}→{Tgt})",
            ProviderName,
            requestUrl,
            config.ModelName,
            sourceLanguage,
            targetLanguage);

        try
        {
            using var client = _httpClientFactory.CreateClient(nameof(OpenAICompatibleTranslationService));
            client.Timeout = TimeSpan.FromMinutes(2);

            using var response = await client
                .PostAsync(new Uri(requestUrl), content, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody, _jsonOptions);

            var translated = completion?.Choices?[0]?.Message?.Content?.Trim();

            if (string.IsNullOrWhiteSpace(translated))
            {
                _logger.LogWarning("Jellyzer: received empty translation response, keeping original text.");
                return text;
            }

            return translated;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "Jellyzer: translation request failed – original text will be kept.");
            return text;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string BuildSystemPrompt(string sourceLanguage, string targetLanguage)
        => $"You are a professional translator specializing in movie and TV show metadata. "
         + $"Translate the following text from {sourceLanguage} to {targetLanguage}. "
         + "Preserve proper nouns, technical terms, and any formatting. "
         + "Return only the translated text – no explanations, no quotes, no extra commentary.";

    // ── OpenAI request / response DTOs (internal to this service) ────────────

    private sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public ChatMessage[] Messages { get; set; } = [];

        [JsonPropertyName("temperature")]
        public float Temperature { get; set; } = 0.3f;
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public ChatChoice[]? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }
}
