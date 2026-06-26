using Dalamud.Plugin.Services;
using PocketStation.Host;
using PocketStation.Infrastructure.Game;
using PocketStation.Infrastructure.Messaging;
using PocketStation.Domain;

namespace PocketStation.Services;

public sealed class PlayerStateModule : IGameModule
{
    private readonly Configuration configuration;
    private readonly EventBus eventBus;
    private readonly GameFacade game;
    private readonly IFramework framework;

    private long lastPublishTicks;
    private PlayerSnapshot? latest;

    public string Name => "PlayerState";

    public PlayerStateModule(
        Configuration configuration,
        EventBus eventBus,
        GameFacade game,
        IFramework framework)
    {
        this.configuration = configuration;
        this.eventBus = eventBus;
        this.game = game;
        this.framework = framework;
    }

    public void Initialize()
    {
        framework.Update += OnFrameworkUpdate;
        PublishSnapshot();
    }

    public PlayerSnapshot GetLatest()
    {
        return latest ?? game.CaptureSnapshot();
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref lastPublishTicks) < configuration.PlayerStateIntervalMs)
            return;

        Interlocked.Exchange(ref lastPublishTicks, now);
        PublishSnapshot();
    }

    private void PublishSnapshot()
    {
        try
        {
            latest = game.CaptureSnapshot();
            eventBus.Publish("event.player.snapshot", latest);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Failed to capture player snapshot");
        }
    }
}
