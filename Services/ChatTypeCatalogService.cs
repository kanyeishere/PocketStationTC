using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using PocketStation.Domain;

namespace PocketStation.Services;

public sealed class ChatTypeCatalogService
{
    private readonly IDataManager dataManager;

    public ChatTypeCatalogService(IDataManager dataManager)
    {
        this.dataManager = dataManager;
    }

    public IReadOnlyList<ChatTypeOption> GetOptions()
    {
        return ChatFilterDefaults.AllTypes
            .Select(CreateOption)
            .ToList();
    }

    private ChatTypeOption CreateOption(string id)
    {
        if (!Enum.TryParse<XivChatType>(id, out var chatType))
            return new ChatTypeOption(id, id, 0);

        var rowId = (ushort)chatType;
        return new ChatTypeOption(id, id, rowId);
    }
}
