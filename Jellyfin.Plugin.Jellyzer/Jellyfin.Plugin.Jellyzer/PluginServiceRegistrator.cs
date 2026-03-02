using Jellyfin.Plugin.Jellyzer.Controllers;
using Jellyfin.Plugin.Jellyzer.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Jellyzer;

/// <summary>
/// Registers all Jellyzer services with the Jellyfin DI container.
/// Add new feature services here as the plugin grows.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // HTTP clients – one per named consumer so timeouts/handlers are independent.
        serviceCollection.AddHttpClient(nameof(OpenAICompatibleTranslationService));
        serviceCollection.AddHttpClient(nameof(JellyzerController));

        // Translation service — swap or add providers here in the future
        // (e.g. DeepL, Google Translate, Azure Cognitive Services…).
        serviceCollection.AddSingleton<ITranslationService, OpenAICompatibleTranslationService>();
    }
}
