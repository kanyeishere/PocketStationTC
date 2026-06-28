using PocketStation.Domain;
using PocketStation.Host;

namespace PocketStation;

public static class CommandShortcutDefaults
{
    /// <summary>
    /// Increment every time new built-in shortcuts are added so that
    /// <see cref="EnsureDefaults"/> merges them into existing configs.
    /// </summary>
    public const int CurrentVersion = 2;

    public static readonly IReadOnlyList<CommandShortcut> BuiltIn =
    [
        new("ad-run-points", "自动刷满周常点数", "/ad run Support 1314 9"),
        new("ad-stop", "停止自动刷本", "/ad stop"),
        new("submarine", "自动收取潜水艇", "/pdr submarine"),
        new("pdr-inn", "自动前往旅馆", "/pdr inn"),
        new("ad-goto-home", "前往个人房屋", "/ad goto home"),
        new("ad-goto-fc", "前往部队房屋", "/ad goto fc"),
        new("ad-go-apartment", "前往公寓", "/ad go apartment"),
        new("ad-exitduty", "退出当前副本", "/ad exitduty"),
        new("gbr-auto-on", "开启自动采集", "/gbr auto on"),
        new("gbr-auto-off", "关闭自动采集", "/gbr auto off"),
    ];

    public static void EnsureDefaults(Configuration configuration)
    {
        // First-run: seed everything.
        if (configuration.CommandShortcuts.Count == 0)
        {
            configuration.CommandShortcuts = [.. BuiltIn];
            configuration.ShortcutDefaultsVersion = CurrentVersion;
            return;
        }

        // Merge new built-in shortcuts that the user doesn't already have.
        if (configuration.ShortcutDefaultsVersion < CurrentVersion)
        {
            var existingIds = new HashSet<string>(configuration.CommandShortcuts.Select(s => s.Id));
            foreach (var builtIn in BuiltIn)
            {
                if (!existingIds.Contains(builtIn.Id))
                    configuration.CommandShortcuts.Add(builtIn);
            }

            configuration.ShortcutDefaultsVersion = CurrentVersion;
        }
    }
}
