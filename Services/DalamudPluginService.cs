using Dalamud.Plugin;
using PocketStation.Domain;

namespace PocketStation.Services;

public sealed class DalamudPluginService
{
    private readonly IDalamudPluginInterface pluginInterface;

    public DalamudPluginService(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    public IReadOnlyList<DalamudPluginInfo> GetInstalledPlugins()
    {
        return pluginInterface.InstalledPlugins
            .Select(plugin => new DalamudPluginInfo(
                plugin.InternalName,
                plugin.Name,
                plugin.Version?.ToString() ?? "?",
                plugin.IsLoaded))
            .ToList();
    }
}
