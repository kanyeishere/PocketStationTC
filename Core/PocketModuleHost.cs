namespace PocketStation.Core;

public sealed class PocketModuleHost : IDisposable
{
    private readonly List<IGameModule> modules = [];
    private bool initialized;
    private bool disposed;

    public void Add(IGameModule module)
    {
        if (initialized)
            throw new InvalidOperationException("Modules cannot be added after initialization.");

        modules.Add(module);
    }

    public void Initialize()
    {
        if (initialized)
            return;

        initialized = true;

        foreach (var module in modules)
        {
            module.Initialize();
            Plugin.Log.Info("Module initialized: {Name}", module.Name);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        for (var i = modules.Count - 1; i >= 0; i--)
        {
            try
            {
                modules[i].Dispose();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Failed to dispose module {Name}", modules[i].Name);
            }
        }

        modules.Clear();
    }
}
