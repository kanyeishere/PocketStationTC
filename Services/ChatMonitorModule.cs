using PocketStation.Host;
using PocketStation.Infrastructure.Game;
using PocketStation.Infrastructure.Messaging;
using PocketStation.Domain;

namespace PocketStation.Services;

public sealed class ChatMonitorModule : IGameModule
{
    private readonly Configuration configuration;
    private readonly EventBus eventBus;
    private readonly GameFacade game;
    private readonly object sync = new();
    private readonly Queue<ChatEvent> history = new();

    public string Name => "ChatMonitor";

    public ChatMonitorModule(Configuration configuration, EventBus eventBus, GameFacade game)
    {
        this.configuration = configuration;
        this.eventBus = eventBus;
        this.game = game;
    }

    public void Initialize()
    {
        game.ChatReceived += OnChatReceived;
    }

    public IReadOnlyList<ChatEvent> GetHistory()
    {
        lock (sync)
            return history.ToList();
    }

    public void Dispose()
    {
        game.ChatReceived -= OnChatReceived;
    }

    private void OnChatReceived(ChatEvent evt)
    {
        lock (sync)
        {
            history.Enqueue(evt);
            while (history.Count > configuration.ChatHistoryLimit)
                history.Dequeue();
        }

        eventBus.Publish("event.chat", evt);
    }
}
