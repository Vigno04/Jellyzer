using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Jellyzer.Models;
using Jellyfin.Plugin.Jellyzer.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Jellyzer.Controllers;

/// <summary>
/// Jellyzer API controller – exposes metadata translation endpoints.
/// All routes are under /Jellyzer to avoid conflicts with core Jellyfin routes.
/// </summary>
[Authorize(Policy = Policies.RequiresElevation)]
[ApiController]
[Route("Jellyzer")]
[Produces(MediaTypeNames.Application.Json)]
public class JellyzerController : ControllerBase
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILibraryManager _libraryManager;
    private readonly ITranslationService _translationService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JellyzerController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyzerController"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="translationService">Active translation service provider.</param>
    /// <param name="httpClientFactory">HTTP client factory for direct API calls.</param>
    /// <param name="logger">Logger.</param>
    public JellyzerController(
        ILibraryManager libraryManager,
        ITranslationService translationService,
        IHttpClientFactory httpClientFactory,
        ILogger<JellyzerController> logger)
    {
        _libraryManager = libraryManager;
        _translationService = translationService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // ── Model discovery ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the list of models available at the given OpenAI-compatible endpoint.
    /// </summary>
    /// <param name="baseUrl">
    /// Optional base URL override (e.g. <c>http://localhost:1234/v1</c>).
    /// When omitted the plugin configuration value is used.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">List of available model identifiers.</response>
    /// <response code="502">Could not reach the endpoint.</response>
    /// <returns>An array of <see cref="ModelInfo"/> objects.</returns>
    [HttpGet("Models")]
    [ProducesResponseType(typeof(IReadOnlyList<ModelInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<ModelInfo>>> GetModelsAsync(
        [FromQuery] string? baseUrl = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveBase = (baseUrl ?? Plugin.Instance?.Configuration?.LmStudioBaseUrl ?? "http://localhost:1234/v1")
            .TrimEnd('/');

        var url = $"{effectiveBase}/models";

        _logger.LogDebug("Jellyzer: fetching model list from {Url}", url);

        try
        {
            using var client = _httpClientFactory.CreateClient(nameof(JellyzerController));
            client.Timeout = TimeSpan.FromSeconds(10);

            using var response = await client
                .GetAsync(new Uri(url), cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var models = new List<ModelInfo>();
            using (var jsonDoc = JsonDocument.Parse(body))
            {
                if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    models = jsonDoc.RootElement.Deserialize<List<ModelInfo>>(_jsonOptions) ?? [];
                }
                else if (jsonDoc.RootElement.TryGetProperty("data", out var dataProp))
                {
                    models = dataProp.Deserialize<List<ModelInfo>>(_jsonOptions) ?? [];
                }
                else if (jsonDoc.RootElement.TryGetProperty("models", out var modelsProp))
                {
                    models = modelsProp.Deserialize<List<ModelInfo>>(_jsonOptions) ?? [];
                }
            }

            _logger.LogInformation(
                "Jellyzer: found {Count} model(s) at {Url}",
                models.Count,
                url);

            return Ok(models);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Jellyzer: could not reach models endpoint at {Url}", url);
            return StatusCode(StatusCodes.Status502BadGateway, $"Could not reach {url}: {ex.Message}");
        }
    }

    // ── Libraries and media items ───────────────────────────────────────────

    /// <summary>
    /// Returns movie and TV virtual libraries available on the server.
    /// </summary>
    /// <response code="200">List of compatible libraries.</response>
    /// <returns>Library list for the config UI.</returns>
    [HttpGet("Libraries")]
    [ProducesResponseType(typeof(IReadOnlyList<LibraryInfo>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<LibraryInfo>> GetLibraries()
    {
        var libraries = _libraryManager
            .GetVirtualFolders()
            .Where(folder => Guid.TryParse(folder.ItemId, out _))
            .Where(folder =>
            {
                var collectionType = folder.CollectionType?.ToString();
                return string.Equals(collectionType, "movies", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(collectionType, "tvshows", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(collectionType, "mixed", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(collectionType);
            })
            .Select(folder => new LibraryInfo
            {
                Id = Guid.Parse(folder.ItemId),
                Name = folder.Name ?? string.Empty,
                CollectionType = folder.CollectionType?.ToString()
            })
            .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(libraries);
    }

    /// <summary>
    /// Returns movie and series items, optionally filtered by virtual library.
    /// </summary>
    /// <param name="libraryId">Optional virtual library GUID.</param>
    /// <response code="200">List of media items.</response>
    /// <returns>Media items for selection in the config UI.</returns>
    [HttpGet("MediaItems")]
    [ProducesResponseType(typeof(IReadOnlyList<MediaItemInfo>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<MediaItemInfo>> GetMediaItems([FromQuery] Guid? libraryId = null)
    {
        var query = new InternalItemsQuery
        {
            Recursive = true,
            IsVirtualItem = false
        };

        if (libraryId.HasValue)
        {
            query.ParentId = libraryId.Value;
        }

        var items = _libraryManager
            .GetItemList(query, false)
            .Where(item => item is Movie or Series)
            .Select(item => new MediaItemInfo
            {
                Id = item.Id,
                Name = item.Name ?? string.Empty,
                Type = item is Series ? "Series" : "Movie",
                Year = item.ProductionYear,
                Overview = item.Overview ?? string.Empty
            })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(items);
    }

    // ── Translation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Translates the metadata of the specified library item using the configured AI provider.
    /// Translated fields are written back to the Jellyfin database immediately.
    /// </summary>
    /// <param name="itemId">The Jellyfin item ID (GUID) to translate.</param>
    /// <param name="request">
    /// Optional per-request overrides (which fields to translate, language pair).
    /// When null or a property is null, the plugin configuration defaults are used.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Translation completed (inspect <see cref="TranslateItemResult.Success"/> for field-level details).</response>
    /// <response code="404">No item found with the given ID.</response>
    /// <returns>A detailed translation result.</returns>
    [HttpPost("TranslateItem/{itemId}")]
    [ProducesResponseType(typeof(TranslateItemResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TranslateItemResult>> TranslateItemAsync(
        [FromRoute] Guid itemId,
        [FromBody] TranslateItemRequest? request,
        CancellationToken cancellationToken = default)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound($"Item {itemId} not found.");
        }

        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            return StatusCode(500, "Plugin configuration unavailable.");
        }

        // Resolve effective settings (request override → config default)
        var sourceLang = request?.SourceLanguage ?? config.SourceLanguage;
        var targetLang = request?.TargetLanguage ?? config.TargetLanguage;
        var translateTitle = request?.TranslateTitle ?? config.TranslateTitle;
        var translateOverview = request?.TranslateOverview ?? config.TranslateOverview;
        var translateTagline = request?.TranslateTagline ?? config.TranslateTagline;

        _logger.LogInformation(
            "Jellyzer: starting translation of '{Name}' ({Id}) [{Src}→{Tgt}] via {Provider}",
            item.Name,
            itemId,
            sourceLang,
            targetLang,
            _translationService.ProviderName);

        var result = new TranslateItemResult
        {
            ItemId = itemId,
            ItemName = item.Name ?? string.Empty,
            Success = true
        };

        var itemChanged = false;

        // ── Title ──────────────────────────────────────────────────────────────
        if (translateTitle && !string.IsNullOrWhiteSpace(item.Name))
        {
            var originalName = item.Name;
            var translatedName = await _translationService
                .TranslateAsync(originalName!, sourceLang, targetLang, cancellationToken)
                .ConfigureAwait(false);

            result.Fields.Add(new FieldTranslationResult
            {
                Field = "Title",
                Original = originalName,
                Translated = translatedName,
                WasTranslated = translatedName != originalName
            });

            if (translatedName != originalName)
            {
                item.Name = translatedName;
                itemChanged = true;
            }
        }

        // ── Overview ───────────────────────────────────────────────────────────
        if (translateOverview && !string.IsNullOrWhiteSpace(item.Overview))
        {
            var originalOverview = item.Overview;
            var translatedOverview = await _translationService
                .TranslateAsync(originalOverview!, sourceLang, targetLang, cancellationToken)
                .ConfigureAwait(false);

            result.Fields.Add(new FieldTranslationResult
            {
                Field = "Overview",
                Original = originalOverview,
                Translated = translatedOverview,
                WasTranslated = translatedOverview != originalOverview
            });

            if (translatedOverview != originalOverview)
            {
                item.Overview = translatedOverview;
                itemChanged = true;
            }
        }

        // ── Tagline ────────────────────────────────────────────────────────────
        if (translateTagline && !string.IsNullOrWhiteSpace(item.Tagline))
        {
            var originalTagline = item.Tagline;
            var translatedTagline = await _translationService
                .TranslateAsync(originalTagline!, sourceLang, targetLang, cancellationToken)
                .ConfigureAwait(false);

            result.Fields.Add(new FieldTranslationResult
            {
                Field = "Tagline",
                Original = originalTagline,
                Translated = translatedTagline,
                WasTranslated = translatedTagline != originalTagline
            });

            if (translatedTagline != originalTagline)
            {
                item.Tagline = translatedTagline;
                itemChanged = true;
            }
        }

        // ── Persist ────────────────────────────────────────────────────────────
        if (itemChanged)
        {
            try
            {
                var parent = item.GetParent();
                await _libraryManager
                    .UpdateItemAsync(item, parent, ItemUpdateType.MetadataEdit, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "Jellyzer: successfully saved metadata for '{Name}' ({Id})",
                    item.Name,
                    itemId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Jellyzer: failed to persist metadata changes for {Id}", itemId);
                result.Success = false;
                result.ErrorMessage = $"Translation completed but saving failed: {ex.Message}";
            }
        }
        else
        {
            _logger.LogInformation("Jellyzer: no changes to save for '{Name}' ({Id})", item.Name, itemId);
        }

        return Ok(result);
    }

    // ── Plugin info ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns basic info about the active plugin configuration (useful for connection tests).
    /// </summary>
    /// <response code="200">Configuration summary.</response>
    /// <returns>A configuration summary object.</returns>
    [HttpGet("Info")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetInfo()
    {
        var config = Plugin.Instance?.Configuration;
        return Ok(new
        {
            ProviderName = _translationService.ProviderName,
            LmStudioBaseUrl = config?.LmStudioBaseUrl,
            ModelName = config?.ModelName,
            SourceLanguage = config?.SourceLanguage,
            TargetLanguage = config?.TargetLanguage
        });
    }
}
