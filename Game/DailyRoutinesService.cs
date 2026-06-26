using System.Text.Json;
using PocketStation.Protocol;

namespace PocketStation.Game;

public sealed class DailyRoutinesService
{
    private readonly string configPath;
    private readonly string langPath;
    private Dictionary<string, string>? _langCache;

    public DailyRoutinesService(string pluginConfigDirectory)
    {
        var parent = Path.GetDirectoryName(pluginConfigDirectory)
                     ?? pluginConfigDirectory;
        configPath = Path.Combine(parent, "DailyRoutines.json");

        // Localization file path
        langPath = Path.Combine(parent, "DailyRoutines", "Dev", "Assets", "Langs", "ChineseSimplified.json");

        Plugin.Log.Info("DailyRoutines config path resolved: {Path} (exists={Exists})",
            configPath, File.Exists(configPath));
    }

    public DailyRoutinesSnapshot CaptureSnapshot()
    {
        var modules = new List<DailyRoutinesModule>();
        var resolvedPath = configPath;
        var lang = LoadLang();

        // Fallback: try default XIVLauncherCN path if primary path not found
        if (!File.Exists(resolvedPath))
        {
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XIVLauncherCN", "pluginConfigs", "DailyRoutines.json");
            if (File.Exists(fallback))
                resolvedPath = fallback;
        }

        try
        {
            if (!File.Exists(resolvedPath))
            {
                Plugin.Log.Error("DailyRoutines config not found at {Path}", resolvedPath);
                return new DailyRoutinesSnapshot(modules);
            }

            var json = File.ReadAllText(resolvedPath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("ModuleEnabled", out var moduleEnabled))
            {
                Plugin.Log.Error("DailyRoutines config has no ModuleEnabled property");
                return new DailyRoutinesSnapshot(modules);
            }

            foreach (var prop in moduleEnabled.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.True && prop.Value.ValueKind != JsonValueKind.False)
                    continue;

                var name = prop.Name;
                var displayName = GetDisplayName(name, lang);

                // Skip internal modules without localization
                if (displayName == name)
                    continue;

                modules.Add(new DailyRoutinesModule(
                    name,
                    prop.Value.GetBoolean(),
                    displayName));
            }

            Plugin.Log.Info("DailyRoutines: loaded {Count} modules", modules.Count);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to read DailyRoutines config from {Path}", resolvedPath);
        }

        return new DailyRoutinesSnapshot(modules);
    }

    private static string GetDisplayName(string moduleName, Dictionary<string, string>? lang)
    {
        if (lang == null || lang.Count == 0) return moduleName;

        // 1. Exact match
        if (lang.TryGetValue(moduleName, out var display)) return display;
        // 2. ModuleName + "Title" (exact)
        var titleKey = moduleName + "Title";
        if (lang.TryGetValue(titleKey, out display)) return display;
        // 3. Case-insensitive (+Title) — e.g. "AutoCutSceneSkip" vs "AutoCutsceneSkipTitle"
        var match = lang.Keys.FirstOrDefault(k => k.Equals(titleKey, StringComparison.OrdinalIgnoreCase));
        if (match != null) return lang[match];

        return moduleName;
    }

    private Dictionary<string, string>? LoadLang()
    {
        if (_langCache != null) return _langCache;

        try
        {
            var path = langPath;
            if (!File.Exists(path))
            {
                path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "XIVLauncherCN", "pluginConfigs", "DailyRoutines", "Dev", "Assets", "Langs", "ChineseSimplified.json");
                if (!File.Exists(path))
                {
                    Plugin.Log.Warning("DailyRoutines lang file not found");
                    _langCache = new Dictionary<string, string>();
                    return _langCache;
                }
            }

            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            _langCache = dict ?? new Dictionary<string, string>();
            Plugin.Log.Info("DailyRoutines lang loaded: {Count} entries from {Path}",
                _langCache.Count, path);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to load DailyRoutines lang file");
            _langCache = new Dictionary<string, string>();
        }

        return _langCache;
    }
}
