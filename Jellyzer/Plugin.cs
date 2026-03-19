using Jellyzer.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyzer;

/// <summary>
/// The main Jellyzer plugin.
/// </summary>
public class JellyzerPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JellyzerPlugin"/> class.
    /// </summary>
    public JellyzerPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Jellyzer";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("3F8A2B1C-4D5E-6F70-8192-A3B4C5D6E7F8");

    /// <inheritdoc />
    public override string Description => "Jellyzer — manage and configure your Jellyfin library.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static JellyzerPlugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "jellyzer-config",
                EnableInMainMenu = true,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
            }
        ];
    }
}
