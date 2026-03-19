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
using System.Threading;
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
    private static readonly object TranslationStateLock = new();
    private static TranslationJobState _translationState = TranslationJobState.CreateIdle();

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

    /// <summary>
    /// Starts a full translation run in the background.
    /// </summary>
    [HttpPost("translation/start")]
    public ActionResult StartTranslation([FromBody] StartTranslationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OpenApiDomain) || string.IsNullOrWhiteSpace(request.OpenApiModel))
        {
            return BadRequest("OpenAPI Domain and Model are required.");
        }

        if (request.ItemId == Guid.Empty)
        {
            return BadRequest("A target item is required.");
        }

        lock (TranslationStateLock)
        {
            if (_translationState.IsRunning)
            {
                return Conflict(new { message = "A translation job is already in progress." });
            }

            _translationState = TranslationJobState.CreateRunning();
        }

        _ = Task.Run(() => RunTranslationJobAsync(request), CancellationToken.None);
        return Accepted();
    }

    /// <summary>
    /// Returns current background translation status.
    /// </summary>
    [HttpGet("translation/status")]
    public ActionResult<TranslationStatusResponse> GetTranslationStatus()
    {
        return Ok(GetTranslationStatusSnapshot());
    }

    /// <summary>
    /// Requests a graceful stop after the current item finishes processing.
    /// </summary>
    [HttpPost("translation/stop")]
    public ActionResult StopTranslation()
    {
        lock (TranslationStateLock)
        {
            if (!_translationState.IsRunning)
            {
                return Conflict(new { message = "No translation job is currently running." });
            }

            if (!_translationState.StopRequested)
            {
                _translationState.StopRequested = true;
                _translationState.AppendLog("Stop requested. Waiting for current item to finish...");
            }
        }

        return Accepted();
    }

    internal static TranslationStatusResponse GetTranslationStatusSnapshot()
    {
        lock (TranslationStateLock)
        {
            return _translationState.ToResponse();
        }
    }

    [HttpPost("translate-item")]
    public async Task<ActionResult> TranslateItem([FromBody] TranslateItemRequest request) 
    {
        if (string.IsNullOrWhiteSpace(request.OpenApiDomain) || string.IsNullOrWhiteSpace(request.OpenApiModel))
            return BadRequest("OpenAPI Domain and Model are required.");

        var item = _libraryManager.GetItemById(request.ItemId);
        if (item == null) return NotFound("Item not found.");

        var result = await TranslateItemInternal(item, request.TranslateTitle, request.TranslateDescription, request).ConfigureAwait(false);
        return Ok(new { success = true, updated = result.Updated, debugLogs = result.DebugLogs });
    }

    private async Task RunTranslationJobAsync(StartTranslationRequest request)
    {
        try
        {
            var targets = BuildTargets(request);
            UpdateTranslationState(state =>
            {
                state.TotalItems = targets.Count;
                state.ProcessedItems = 0;
                state.ProgressPercent = targets.Count == 0 ? 100 : 0;
                state.CurrentItem = string.Empty;
                state.AppendLog("=== Translation started ===");
                if (targets.Count == 0)
                {
                    state.AppendLog("No eligible items were found based on the selected options.");
                }
            });

            if (targets.Count == 0)
            {
                UpdateTranslationState(state =>
                {
                    state.AppendLog("=== Translation Finished ===");
                    state.IsRunning = false;
                    state.FinishedUtc = DateTime.UtcNow;
                });

                return;
            }

            var translatedRequest = new TranslateItemRequest
            {
                OpenApiDomain = request.OpenApiDomain,
                OpenApiModel = request.OpenApiModel,
                InputLanguage = request.InputLanguage,
                OutputLanguage = request.OutputLanguage,
                SystemPrompt = request.SystemPrompt,
                EnableDebugLogs = request.EnableDebugLogs
            };

            foreach (var target in targets)
            {
                var shouldStop = false;
                UpdateTranslationState(state => shouldStop = state.StopRequested);
                if (shouldStop)
                {
                    UpdateTranslationState(state => state.AppendLog("=== Translation stopped by user ==="));
                    break;
                }

                UpdateTranslationState(state =>
                {
                    state.CurrentItem = target.Name;
                    state.AppendLog($"[Pending] {target.Name}...");
                });

                var item = _libraryManager.GetItemById(target.ItemId);
                if (item == null)
                {
                    UpdateTranslationAfterItem($"[Skipped] Item no longer exists: {target.Name}");
                    continue;
                }

                var result = await TranslateItemInternal(item, target.TranslateTitle, target.TranslateDescription, translatedRequest).ConfigureAwait(false);
                if (result.Updated)
                {
                    UpdateTranslationState(state => state.AppendLog($"[Done] Translated {target.Name}"));
                }
                else
                {
                    UpdateTranslationState(state => state.AppendLog($"[Skipped] No changes needed for {target.Name}"));
                }

                foreach (var logEntry in result.DebugLogs)
                {
                    UpdateTranslationState(state =>
                    {
                        state.AppendLog($"----- DEBUG INFO ({logEntry.Type}) -----");
                        state.AppendLog($"INPUT JSON: {logEntry.Input}");
                        state.AppendLog($"RAW OUTPUT: {logEntry.Output}");
                        state.AppendLog("----------------------------------");
                    });
                }

                UpdateTranslationAfterItem(null);
            }

            UpdateTranslationState(state =>
            {
                if (!state.StopRequested)
                {
                    state.AppendLog("=== Translation Finished ===");
                }

                state.CurrentItem = string.Empty;
                if (!state.StopRequested)
                {
                    state.ProgressPercent = 100;
                }

                state.IsRunning = false;
                state.StopRequested = false;
                state.FinishedUtc = DateTime.UtcNow;
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Jellyzer: background translation job failed.");
            UpdateTranslationState(state =>
            {
                state.AppendLog("=== Translation aborted with error ===");
                state.CurrentItem = string.Empty;
                state.IsRunning = false;
                state.StopRequested = false;
                state.FinishedUtc = DateTime.UtcNow;
            });
        }
    }

    private void UpdateTranslationAfterItem(string? explicitLog)
    {
        UpdateTranslationState(state =>
        {
            if (!string.IsNullOrWhiteSpace(explicitLog))
            {
                state.AppendLog(explicitLog);
            }

            state.ProcessedItems++;
            state.ProgressPercent = state.TotalItems == 0
                ? 100
                : Math.Min(100, (double)state.ProcessedItems / state.TotalItems * 100);
        });
    }

    private void UpdateTranslationState(Action<TranslationJobState> update)
    {
        lock (TranslationStateLock)
        {
            update(_translationState);
        }
    }

    private List<TranslationWorkItem> BuildTargets(StartTranslationRequest request)
    {
        var list = new List<TranslationWorkItem>();
        var seen = new HashSet<Guid>();

        var rootItem = _libraryManager.GetItemById(request.ItemId);
        if (rootItem == null)
        {
            return list;
        }

        if (request.TranslateTitle || request.TranslateDescription)
        {
            AddWorkItem(list, seen, new TranslationWorkItem(rootItem.Id, rootItem.Name, request.TranslateTitle, request.TranslateDescription));
        }

        if (!string.Equals(rootItem.GetType().Name, "Series", StringComparison.Ordinal))
        {
            return list;
        }

        var seasonIds = new List<Guid>();
        if (request.SeasonId.HasValue && request.SeasonId.Value != Guid.Empty)
        {
            seasonIds.Add(request.SeasonId.Value);
        }
        else
        {
            seasonIds.AddRange(GetSeasonIds(rootItem.Id));
        }

        foreach (var seasonId in seasonIds)
        {
            var season = _libraryManager.GetItemById(seasonId);
            if (season == null)
            {
                continue;
            }

            if (request.TranslateSeasonTitle)
            {
                AddWorkItem(list, seen, new TranslationWorkItem(season.Id, season.Name, true, false));
            }

            if (!request.TranslateEpisodeTitle && !request.TranslateDescription)
            {
                continue;
            }

            var episodeIds = GetEpisodeIds(seasonId);
            foreach (var episodeId in episodeIds)
            {
                if (request.EpisodeId.HasValue && request.EpisodeId.Value != Guid.Empty && episodeId != request.EpisodeId.Value)
                {
                    continue;
                }

                var episode = _libraryManager.GetItemById(episodeId);
                if (episode == null)
                {
                    continue;
                }

                AddWorkItem(
                    list,
                    seen,
                    new TranslationWorkItem(
                        episode.Id,
                        episode.Name,
                        request.TranslateEpisodeTitle,
                        request.TranslateDescription));
            }
        }

        return list;
    }

    private void AddWorkItem(List<TranslationWorkItem> list, HashSet<Guid> seen, TranslationWorkItem workItem)
    {
        if (workItem.ItemId == Guid.Empty || (!workItem.TranslateTitle && !workItem.TranslateDescription))
        {
            return;
        }

        if (seen.Add(workItem.ItemId))
        {
            list.Add(workItem);
        }
    }

    private IEnumerable<Guid> GetSeasonIds(Guid seriesId)
    {
        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            ParentId = seriesId,
            IncludeItemTypes = [BaseItemKind.Season],
        });

        return items.OrderBy(item => item.IndexNumber ?? 999).Select(item => item.Id);
    }

    private IEnumerable<Guid> GetEpisodeIds(Guid seasonId)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            Recursive = true,
            ParentId = seasonId,
        };

        var items = _libraryManager.GetItemList(query);
        return items
            .OrderBy(item => item.ParentIndexNumber ?? 0)
            .ThenBy(item => item.IndexNumber ?? 999)
            .Select(item => item.Id);
    }

    private async Task<TranslateItemExecutionResult> TranslateItemInternal(BaseItem item, bool translateTitle, bool translateDescription, TranslateItemRequest request)
    {
        bool changed = false;
        var debugLogs = new List<DebugLogEntry>();

        if (translateTitle)
        {
            if (!string.IsNullOrWhiteSpace(item.Name))
            {
                var res = await CallLlmTranslation(item.Name, request).ConfigureAwait(false);
                if (request.EnableDebugLogs)
                {
                    debugLogs.Add(new DebugLogEntry("Title", res.prompt, res.rawOutput));
                }

                if (!string.IsNullOrWhiteSpace(res.text) && res.text != item.Name)
                {
                    item.Name = res.text;
                    changed = true;
                }
            }
            else if (request.EnableDebugLogs)
            {
                debugLogs.Add(new DebugLogEntry("Title", "N/A (Title Empty)", "N/A"));
            }
        }

        if (translateDescription)
        {
            if (!string.IsNullOrWhiteSpace(item.Overview))
            {
                var res = await CallLlmTranslation(item.Overview, request).ConfigureAwait(false);
                if (request.EnableDebugLogs)
                {
                    debugLogs.Add(new DebugLogEntry("Description", res.prompt, res.rawOutput));
                }

                if (!string.IsNullOrWhiteSpace(res.text) && res.text != item.Overview)
                {
                    item.Overview = res.text;
                    changed = true;
                }
            }
            else if (request.EnableDebugLogs)
            {
                debugLogs.Add(new DebugLogEntry("Description", "N/A (Description Empty)", "N/A"));
            }
        }

        if (changed)
        {
            await _libraryManager.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
        }

        return new TranslateItemExecutionResult(changed, debugLogs);
    }

    private async Task<(string text, string prompt, string rawOutput)> CallLlmTranslation(string text, TranslateItemRequest request)
    {
        var url = request.OpenApiDomain.TrimEnd('/') + "/chat/completions";
        
        var sysPrompt = request.SystemPrompt ?? "";
        sysPrompt = sysPrompt.Replace("[INPUT_LANGUAGE]", request.InputLanguage ?? "auto-detect")
                             .Replace("[OUTPUT_LANGUAGE]", request.OutputLanguage ?? "auto-detect")
                             .Replace("{input-language}", request.InputLanguage ?? "auto-detect")
                             .Replace("{output-language}", request.OutputLanguage ?? "auto-detect");

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

public sealed class StartTranslationRequest
{
    public Guid ItemId { get; set; }
    public Guid? SeasonId { get; set; }
    public Guid? EpisodeId { get; set; }
    public bool TranslateTitle { get; set; }
    public bool TranslateDescription { get; set; }
    public bool TranslateSeasonTitle { get; set; }
    public bool TranslateEpisodeTitle { get; set; }
    public string OpenApiDomain { get; set; } = string.Empty;
    public string OpenApiModel { get; set; } = string.Empty;
    public string InputLanguage { get; set; } = string.Empty;
    public string OutputLanguage { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public bool EnableDebugLogs { get; set; }
}

public sealed class TranslationStatusResponse
{
    public bool IsRunning { get; set; }
    public bool StopRequested { get; set; }
    public double ProgressPercent { get; set; }
    public int ProcessedItems { get; set; }
    public int TotalItems { get; set; }
    public string CurrentItem { get; set; } = string.Empty;
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public List<string> Logs { get; set; } = [];
}

public sealed class TranslationJobState
{
    public bool IsRunning { get; set; }
    public bool StopRequested { get; set; }
    public double ProgressPercent { get; set; }
    public int ProcessedItems { get; set; }
    public int TotalItems { get; set; }
    public string CurrentItem { get; set; } = string.Empty;
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public List<string> Logs { get; set; } = [];

    public static TranslationJobState CreateIdle() => new()
    {
        IsRunning = false,
        StopRequested = false,
        ProgressPercent = 0,
        ProcessedItems = 0,
        TotalItems = 0,
        CurrentItem = string.Empty,
        StartedUtc = null,
        FinishedUtc = null,
        Logs = [],
    };

    public static TranslationJobState CreateRunning() => new()
    {
        IsRunning = true,
        StopRequested = false,
        ProgressPercent = 0,
        ProcessedItems = 0,
        TotalItems = 0,
        CurrentItem = string.Empty,
        StartedUtc = DateTime.UtcNow,
        FinishedUtc = null,
        Logs = [],
    };

    public void AppendLog(string message)
    {
        Logs.Add($"{DateTime.UtcNow:HH:mm:ss} {message}");
        if (Logs.Count > 500)
        {
            Logs.RemoveAt(0);
        }
    }

    public TranslationStatusResponse ToResponse() => new()
    {
        IsRunning = IsRunning,
        StopRequested = StopRequested,
        ProgressPercent = ProgressPercent,
        ProcessedItems = ProcessedItems,
        TotalItems = TotalItems,
        CurrentItem = CurrentItem,
        StartedUtc = StartedUtc,
        FinishedUtc = FinishedUtc,
        Logs = [.. Logs],
    };
}

public sealed record TranslationWorkItem(Guid ItemId, string Name, bool TranslateTitle, bool TranslateDescription);

public sealed record DebugLogEntry(string Type, string Input, string Output);

public sealed record TranslateItemExecutionResult(bool Updated, List<DebugLogEntry> DebugLogs);

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
