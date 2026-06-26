using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using OmenTools.OmenService;
using PocketStation.Protocol;

namespace PocketStation.Game;

public sealed class GameFacade : IDisposable
{
    public event Action<ChatEvent>? ChatReceived;

    private readonly IChatGui chatGui;
    private readonly ICommandManager commandManager;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IPartyList partyList;
    private readonly IFramework framework;

    private long chatSequence;
    private bool disposed;

    public GameFacade(
        IChatGui chatGui,
        ICommandManager commandManager,
        IClientState clientState,
        IObjectTable objectTable,
        IPartyList partyList,
        IFramework framework)
    {
        this.chatGui = chatGui;
        this.commandManager = commandManager;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.partyList = partyList;
        this.framework = framework;
    }

    public void Initialize()
    {
        chatGui.ChatMessage += OnChatMessage;
    }

    public async Task<bool> SendChatOrCommandAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var normalized = content.Trim();
        Plugin.Log.Info("Remote chat command requested: {Command}", normalized);

        return await framework.RunOnFrameworkThread(() =>
        {
            try
            {
                ChatManager.Instance().SendMessage(normalized);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "OmenTools ChatManager send failed, falling back to ICommandManager");
                return commandManager.ProcessCommand(normalized);
            }
        }).ConfigureAwait(false);
    }

    public PlayerSnapshot CaptureSnapshot()
    {
        CharacterState? local = null;
        CharacterState? target = null;

        var localPlayer = objectTable.LocalPlayer;
        if (localPlayer != null)
        {
            local = ToCharacterState(localPlayer);
            target = localPlayer.TargetObject is ICharacter targetCharacter
                ? ToCharacterState(targetCharacter)
                : null;
        }

        var party = new List<CharacterState>();
        for (var i = 0; i < partyList.Length; i++)
        {
            var member = partyList[i];
            if (member?.GameObject is ICharacter character)
                party.Add(ToCharacterState(character));
            else if (member != null)
                party.Add(new CharacterState(
                    member.Name.TextValue,
                    member.EntityId,
                    member.EntityId,
                    member.ClassJob.RowId,
                    member.Level,
                    member.CurrentHP,
                    member.MaxHP,
                    member.CurrentMP,
                    member.MaxMP,
                    member.Position,
                    false,
                    ToStatusEvents(member.Statuses)));
        }

        return new PlayerSnapshot(
            clientState.IsLoggedIn,
            clientState.TerritoryType,
            clientState.MapId,
            local,
            target,
            party,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        chatGui.ChatMessage -= OnChatMessage;
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        var evt = new ChatEvent(
            Interlocked.Increment(ref chatSequence),
            message.LogKind.ToString(),
            message.Sender.TextValue,
            message.Message.TextValue,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        ChatReceived?.Invoke(evt);
    }

    private static CharacterState ToCharacterState(ICharacter character)
    {
        return new CharacterState(
            character.Name.TextValue,
            character.GameObjectId,
            character.EntityId,
            character.ClassJob.RowId,
            character.Level,
            character.CurrentHp,
            character.MaxHp,
            character.CurrentMp,
            character.MaxMp,
            character.Position,
            character.IsDead,
            character is IBattleChara battleChara ? ToStatusEvents(battleChara.StatusList) : []);
    }

    private static IReadOnlyList<StatusEvent> ToStatusEvents(IEnumerable<Dalamud.Game.ClientState.Statuses.IStatus> statuses)
    {
        return statuses
            .Where(status => status.StatusId != 0)
            .Select(status => new StatusEvent(
                status.StatusId,
                status.RemainingTime,
                status.Param,
                status.SourceId))
            .ToList();
    }
}
