using System.Collections;
using System.Reflection;
using Dalamud.Plugin;
using OmenTools;
using OmenTools.Dalamud.Helpers;

namespace PocketStation.Helpers;

/// <summary>
///     扩展 <see cref="DalamudReflector" />，补充插件开关能力。
///     OmenTools 是外部 submodule，不便直接修改，故在此包装。
/// </summary>
public static class DalamudReflectorEx
{
    private const BindingFlags AllFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    // 首次调用时反射解析，之后零开销
    private static MethodInfo? _cachedLoadAsync;
    private static MethodInfo? _cachedUnloadAsync;
    private static object? _cachedDisposalMode;
    private static bool _resolved;

    /// <summary>
    ///     启用或禁用一个已安装的插件。
    /// </summary>
    /// <returns>null 表示成功，否则返回错误信息</returns>
    public static async Task<string?> SetPluginStateAsync(string internalName, bool enable)
    {
        try
        {
            var pm = DalamudReflector.GetPluginManager();
            if (pm == null) return "PluginManager not available.";

            var installedPlugins = (IList)pm.GetType()
                .GetProperty("InstalledPlugins")?.GetValue(pm);
            if (installedPlugins == null) return "InstalledPlugins not available.";

            foreach (var plugin in installedPlugins)
            {
                var type = plugin.GetType().Name == "LocalDevPlugin"
                    ? plugin.GetType().BaseType
                    : plugin.GetType();
                if (type == null) continue;

                var name = (string?)type.GetProperty("InternalName")?.GetValue(plugin);
                if (!string.Equals(name, internalName, StringComparison.OrdinalIgnoreCase)) continue;

                var isLoaded = (bool?)type.GetProperty("IsLoaded")?.GetValue(plugin) ?? false;

                if (enable && isLoaded) return null;
                if (!enable && !isLoaded) return null;

                EnsureResolved(type);

                if (enable)
                {
                    if (_cachedLoadAsync == null)
                        return "LoadAsync not found on LocalPlugin.";
                    // LoadAsync(PluginLoadReason, bool reloading, CancellationToken)
                    var task = (Task)_cachedLoadAsync.Invoke(plugin,
                        [PluginLoadReason.Installer, false, CancellationToken.None]);
                    await task.ConfigureAwait(false);
                }
                else
                {
                    if (_cachedUnloadAsync == null)
                        return "UnloadAsync not found on LocalPlugin.";
                    // UnloadAsync(PluginLoaderDisposalMode) 或 UnloadAsync()
                    var hasParam = _cachedUnloadAsync.GetParameters().Length > 0;
                    var args = hasParam && _cachedDisposalMode != null
                        ? new[] { _cachedDisposalMode }
                        : null;
                    var task = (Task)_cachedUnloadAsync.Invoke(plugin, args);
                    await task.ConfigureAwait(false);
                }

                return null;
            }

            return $"Plugin '{internalName}' not found.";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static void EnsureResolved(Type localPluginType)
    {
        if (_resolved) return;
        _resolved = true;

        _cachedLoadAsync = localPluginType.GetMethod("LoadAsync", AllFlags);
        _cachedUnloadAsync = localPluginType.GetMethod("UnloadAsync", AllFlags, [])
                          ?? localPluginType.GetMethod("UnloadAsync", AllFlags);

        try
        {
            var disposalType = DService.Instance().PI.GetType().Assembly
                .GetType("Dalamud.Plugin.Internal.Types.PluginLoaderDisposalMode");
            if (disposalType != null)
                _cachedDisposalMode = Enum.ToObject(disposalType, 1); // WaitBeforeDispose
        }
        catch
        {
            // ignored
        }
    }
}
