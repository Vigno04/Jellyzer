using Jellyzer.Controllers;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyzer.ScheduledTasks;

/// <summary>
/// Mirrors Jellyzer translation runs in Jellyfin scheduled tasks so users can monitor execution in the control panel.
/// </summary>
public sealed class JellyzerTranslationMonitorTask(ILogger<JellyzerTranslationMonitorTask> log) : IScheduledTask
{
    public string Name => "Jellyzer Translation";

    public string Key => "JellyzerTranslationMonitor";

    public string Description => "Tracks the active Jellyzer translation run and exposes progress in the server task panel.";

    public string Category => "Jellyzer";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Wait briefly for a run to become active in case this task starts just before the worker updates its state.
        var startDeadline = DateTime.UtcNow.AddSeconds(8);
        var status = JellyzerApiController.GetTranslationStatusSnapshot();

        while (!status.IsRunning && DateTime.UtcNow < startDeadline && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            status = JellyzerApiController.GetTranslationStatusSnapshot();
        }

        if (!status.IsRunning)
        {
            log.LogDebug("Jellyzer monitor task started with no active translation run.");
            progress.Report(100);
            return;
        }

        log.LogInformation("Jellyzer translation monitor task attached to running translation job.");

        while (!cancellationToken.IsCancellationRequested)
        {
            status = JellyzerApiController.GetTranslationStatusSnapshot();
            progress.Report(Math.Clamp(status.ProgressPercent, 0, 100));

            if (!status.IsRunning)
            {
                break;
            }

            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }

        progress.Report(100);
    }
}
