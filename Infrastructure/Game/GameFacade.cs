using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Statuses;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using PocketStation.Domain;

namespace PocketStation.Infrastructure.Game;

public sealed class GameFacade : IDisposable
{
    public event Action<ChatEvent>? ChatReceived;

    private readonly IChatGui chatGui;
    private readonly ICommandManager commandManager;
    private readonly IClientState clientState;
    private readonly IDataManager dataManager;
    private readonly IObjectTable objectTable;
    private readonly IPartyList partyList;
    private readonly IFramework framework;

    private long chatSequence;
    private bool disposed;

    public GameFacade(
        IChatGui chatGui,
        ICommandManager commandManager,
        IClientState clientState,
        IDataManager dataManager,
        IObjectTable objectTable,
        IPartyList partyList,
        IFramework framework)
    {
        this.chatGui = chatGui;
        this.commandManager = commandManager;
        this.clientState = clientState;
        this.dataManager = dataManager;
        this.objectTable = objectTable;
        this.partyList = partyList;
        this.framework = framework;
    }

    public void PrintChat(string message)
    {
        chatGui.Print(message);
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
                ChatSender.Send(normalized);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Direct chat send failed, falling back to ICommandManager");
                return commandManager.ProcessCommand(normalized);
            }
        }).ConfigureAwait(false);
    }

    public PlayerSnapshot CaptureSnapshot()
    {
        CharacterState? local = null;
        CharacterState? target = null;

        var localPlayer = clientState.LocalPlayer;
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
                    member.ObjectId,
                    member.ObjectId,
                    member.ClassJob.RowId,
                    GetJobName(member.ClassJob.RowId),
                    member.Level,
                    member.CurrentHP,
                    member.MaxHP,
                    member.CurrentMP,
                    member.MaxMP,
                    member.Position,
                    false,
                    ToStatusEvents(member.Statuses)));
        }

        var currencies = CurrencyHelper.Capture();

        var currentWorld = GetCurrentWorldId();
        var territoryName = GetZonePlaceName(clientState.TerritoryType);
        var worldName = GetWorldName(currentWorld);
        var dataCenterName = GetWorldDataCenterName(currentWorld);

        return new PlayerSnapshot(
            clientState.IsLoggedIn,
            clientState.TerritoryType,
            clientState.MapId,
            territoryName,
            worldName,
            dataCenterName,
            local,
            target,
            party,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            currencies);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        chatGui.ChatMessage -= OnChatMessage;
    }

    private void OnChatMessage(
        XivChatType type,
        int timestamp,
        ref SeString sender,
        ref SeString message,
        ref bool isHandled)
    {
        var evt = new ChatEvent(
            Interlocked.Increment(ref chatSequence),
            type.ToString(),
            sender.TextValue,
            message.TextValue,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        ChatReceived?.Invoke(evt);
    }

    private CharacterState ToCharacterState(ICharacter character)
    {
        return new CharacterState(
            character.Name.TextValue,
            character.GameObjectId,
            character.EntityId,
            character.ClassJob.RowId,
            GetJobName(character.ClassJob.RowId),
            character.Level,
            character.CurrentHp,
            character.MaxHp,
            character.CurrentMp,
            character.MaxMp,
            character.Position,
            character.IsDead,
            character is IBattleChara battleChara ? ToStatusEvents(battleChara.StatusList) : []);
    }

    private static IReadOnlyList<StatusEvent> ToStatusEvents(IEnumerable<Dalamud.Game.ClientState.Statuses.Status> statuses)
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

    private string GetJobName(uint rowId)
    {
        var row = dataManager.GetExcelSheet<ClassJob>().GetRowOrDefault(rowId);
        return row?.Name.ToString() ?? string.Empty;
    }

    private string GetZonePlaceName(uint territoryType)
    {
        var row = dataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryType);
        return row?.PlaceName.ValueNullable?.Name.ToString() ?? string.Empty;
    }

    private string GetWorldName(uint rowId)
    {
        var row = dataManager.GetExcelSheet<World>().GetRowOrDefault(rowId);
        return row?.Name.ToString() ?? string.Empty;
    }

    private string GetWorldDataCenterName(uint rowId)
    {
        var row = dataManager.GetExcelSheet<World>().GetRowOrDefault(rowId);
        return row?.DataCenter.ValueNullable?.Name.ToString() ?? string.Empty;
    }

    private static unsafe uint GetCurrentWorldId()
    {
        var agent = AgentLobby.Instance();
        return agent != null ? (uint)agent->LobbyData.CurrentWorldId : 0;
    }
}
