using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;


namespace Jellyzer.Controllers;

/// <summary>
/// Jellyzer API controller — exposes library items to the configuration page.
/// </summary>
[ApiController]
[Route("jellyzer")]
[Authorize(Policy = "RequiresElevation")]
public sealed class JellyzerApiController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JellyzerApiController> _log;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyzerApiController"/> class.
    /// </summary>
    public JellyzerApiController(ILibraryManager libraryManager, IHttpClientFactory httpClientFactory, ILogger<JellyzerApiController> log)
    {
        _libraryManager = libraryManager;
        _httpClientFactory = httpClientFactory;
        _log = log;
    }

    /// <summary>
    /// Returns a list of all movies and series from the Jellyfin library.
    /// </summary>
    [HttpGet("library-items")]
    public ActionResult<IEnumerable<LibraryItemDto>> GetLibraryItems()
    {
        _log.LogDebug("Jellyzer: fetching library items for configuration page");

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            Recursive = true,
        });

        var result = items
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new LibraryItemDto
            {
                Id = item.Id.ToString(),
                Name = item.Name,
                Type = item.GetType().Name,
                Year = item.ProductionYear,
                Language = item.PreferredMetadataLanguage ?? ""
            });

        return Ok(result);
    }

    /// <summary>
    /// Proxies the /models request to the OpenAPI domain to bypass CORS restrictions.
    /// </summary>
    [HttpGet("openai-models")]
    public async Task<ActionResult> GetOpenAiModels([FromQuery] string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return BadRequest("Domain is required.");
        }

        try
        {
            var url = domain.TrimEnd('/') + "/models";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var response = await client.GetStringAsync(url);
            return Content(response, "application/json");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to fetch models from {Domain}", domain);
            return StatusCode(500, new { error = "Failed to fetch models" });
        }
    }

    /// <summary>
    /// Returns a list of seasons for a given series.
    /// </summary>
    [HttpGet("seasons")]
    public ActionResult<IEnumerable<LibraryItemDto>> GetSeasons([FromQuery] Guid seriesId)
    {
        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            ParentId = seriesId,
            IncludeItemTypes = [BaseItemKind.Season],
        });

        var result = items
            .OrderBy(item => item.IndexNumber ?? 999)
            .Select(item => new LibraryItemDto
            {
                Id = item.Id.ToString(),
                Name = item.IndexNumber.HasValue ? $"Season {item.IndexNumber}" : item.Name,
                Type = "Season",
                Year = item.ProductionYear
            });

        return Ok(result);
    }

    /// <summary>
    /// Returns a list of episodes for a given series or specific season.
    /// </summary>
    [HttpGet("episodes")]
    public ActionResult<IEnumerable<LibraryItemDto>> GetEpisodes([FromQuery] Guid seriesId, [FromQuery] Guid? seasonId)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            Recursive = true
        };

        if (seasonId.HasValue && seasonId.Value != Guid.Empty)
        {
            query.ParentId = seasonId.Value;
        }
        else
        {
            query.AncestorIds = [seriesId];
        }

        var items = _libraryManager.GetItemList(query);

        var result = items
            .OrderBy(item => item.ParentIndexNumber ?? 0)
            .ThenBy(item => item.IndexNumber ?? 999)
            .Select(item => new LibraryItemDto
            {
                Id = item.Id.ToString(),
                Name = $"E{item.IndexNumber} - {item.Name}",
                Type = "Episode",
                Year = item.ProductionYear
            });

        return Ok(result);
    }

    [HttpPost("translate-item")]
    public async Task<ActionResult> TranslateItem([FromBody] TranslateItemRequest request) 
    {
        if (string.IsNullOrWhiteSpace(request.OpenApiDomain) || string.IsNullOrWhiteSpace(request.OpenApiModel))
            return BadRequest("OpenAPI Domain and Model are required.");

        var item = _libraryManager.GetItemById(request.ItemId);
        if (item == null) return NotFound("Item not found.");

        bool changed = false;
        var debugLogs = new List<object>();

        if (request.TranslateTitle)
        {
            if (!string.IsNullOrWhiteSpace(item.Name))
            {
                var res = await CallLlmTranslation(item.Name, request);
                if (request.EnableDebugLogs) debugLogs.Add(new { type = "Title", input = res.prompt, output = res.rawOutput });
                
                if (!string.IsNullOrWhiteSpace(res.text) && res.text != item.Name)
                {
                    item.Name = res.text;
                    changed = true;
                }
            }
            else if (request.EnableDebugLogs)
            {
                debugLogs.Add(new { type = "Title", input = "N/A (Title Empty)", output = "N/A" });
            }
        }

        if (request.TranslateDescription)
        {
            if (!string.IsNullOrWhiteSpace(item.Overview))
            {
                var res = await CallLlmTranslation(item.Overview, request);
                if (request.EnableDebugLogs) debugLogs.Add(new { type = "Description", input = res.prompt, output = res.rawOutput });

                if (!string.IsNullOrWhiteSpace(res.text) && res.text != item.Overview)
                {
                    item.Overview = res.text;
                    changed = true;
                }
            }
            else if (request.EnableDebugLogs)
            {
                debugLogs.Add(new { type = "Description", input = "N/A (Description Empty)", output = "N/A" });
            }
        }

        if (changed)
        {
            await _libraryManager.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, System.Threading.CancellationToken.None);
        }

        return Ok(new { success = true, updated = changed, debugLogs });
    }

    private async Task<(string text, string prompt, string rawOutput)> CallLlmTranslation(string text, TranslateItemRequest request)
    {
        var url = request.OpenApiDomain.TrimEnd('/') + "/chat/completions";
        
        var sysPrompt = request.SystemPrompt ?? "";
        sysPrompt = sysPrompt.Replace("[INPUT_LANGUAGE]", request.InputLanguage ?? "auto-detect")
                             .Replace("[OUTPUT_LANGUAGE]", request.OutputLanguage ?? "auto-detect");

        var payload = new
        {
            model = request.OpenApiModel,
            messages = new[]
            {
                new { role = "system", content = sysPrompt },
                new { role = "user", content = text }
            },
            temperature = 0.3
        };

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
            var responseString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var message = choices[0].GetProperty("message");
                if (message.TryGetProperty("content", out var textEl))
                {
                    var rawOutput = textEl.GetString()?.Trim() ?? "";
                    var resultText = rawOutput;
                    
                    // Automatically filter out <think> ... </think> blocks outputted by reasoning models
                    while (true)
                    {
                        int start = resultText.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
                        if (start == -1) break;
                        
                        int end = resultText.IndexOf("</think>", start, StringComparison.OrdinalIgnoreCase);
                        if (end == -1) break;
                        
                        resultText = resultText.Remove(start, end - start + 8).Trim();
                    }

                    return (resultText, json, rawOutput);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Jellyzer: LLM translation request failed.");
        }

        return ("", json, "ERROR");
    }
}

public sealed class TranslateItemRequest
{
    public Guid ItemId { get; set; }
    public bool TranslateTitle { get; set; }
    public bool TranslateDescription { get; set; }
    public string OpenApiDomain { get; set; } = string.Empty;
    public string OpenApiModel { get; set; } = string.Empty;
    public string InputLanguage { get; set; } = string.Empty;
    public string OutputLanguage { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public bool EnableDebugLogs { get; set; }
}

/// <summary>
/// DTO representing a library item for the configuration dropdown.
/// </summary>
public sealed class LibraryItemDto
{
    /// <summary>Gets or sets the item's Jellyfin ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the item name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the item type ("Movie" or "Series").</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Gets or sets the production year (nullable).</summary>
    public int? Year { get; set; }

    /// <summary>Gets or sets the original language format.</summary>
    public string Language { get; set; } = string.Empty;
}
