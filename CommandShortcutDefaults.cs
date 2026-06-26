using PocketStation.Protocol;

namespace PocketStation;

public static class CommandShortcutDefaults
{
    public static readonly IReadOnlyList<CommandShortcut> BuiltIn =
    [
        new("ad-run-points", "自动刷点数四人本", "/ad run Support 1314 1"),
        new("ad-stop", "停止自动刷本", "/ad stop"),
        new("submarine", "自动收取潜水艇", "/pdr submarine"),
    ];

    public static void EnsureDefaults(Configuration configuration)
    {
        if (configuration.CommandShortcuts.Count == 0)
        {
            configuration.CommandShortcuts = [.. BuiltIn];
        }
    }
}
