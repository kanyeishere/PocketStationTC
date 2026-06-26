namespace PocketStation.Protocol;

public sealed class ChatFilterMode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Custom";
    public bool IsBuiltIn { get; set; }
    public List<string> EnabledTypes { get; set; } = [];
    public List<string> IncludeKeywords { get; set; } = [];
    public List<string> ExcludeKeywords { get; set; } = [];
}

public sealed class ChatFilterSettings
{
    public string CurrentModeId { get; set; } = ChatFilterDefaults.AllId;
    public List<ChatFilterMode> Modes { get; set; } = [];
    public IReadOnlyList<string> AllTypes { get; set; } = ChatFilterDefaults.AllTypes;
}

public static class ChatFilterDefaults
{
    public const string AllId = "all";

    public static IReadOnlyList<string> AllTypes { get; } =
    [
        "None",
        "Debug",
        "Urgent",
        "Notice",
        "Say",
        "Shout",
        "TellOutgoing",
        "TellIncoming",
        "Party",
        "Alliance",
        "Ls1",
        "Ls2",
        "Ls3",
        "Ls4",
        "Ls5",
        "Ls6",
        "Ls7",
        "Ls8",
        "FreeCompany",
        "NoviceNetwork",
        "CustomEmote",
        "StandardEmote",
        "Yell",
        "CrossParty",
        "PvPTeam",
        "CrossLinkShell1",
        "Damage",
        "Miss",
        "Action",
        "Item",
        "Healing",
        "GainBuff",
        "GainDebuff",
        "LoseBuff",
        "LoseDebuff",
        "GlamourNotifications",
        "Alarm",
        "Echo",
        "SystemMessage",
        "SystemError",
        "GatheringSystemMessage",
        "ErrorMessage",
        "NPCDialogue",
        "LootNotice",
        "Progress",
        "LootRoll",
        "Crafting",
        "Gathering",
        "NPCDialogueAnnouncements",
        "FreeCompanyAnnouncement",
        "FreeCompanyLoginLogout",
        "RetainerSale",
        "PeriodicRecruitmentNotification",
        "Sign",
        "RandomNumber",
        "NoviceNetworkSystem",
        "Orchestrion",
        "PvpTeamAnnouncement",
        "PvpTeamLoginLogout",
        "MessageBook",
        "GmTell",
        "GmSay",
        "GmShout",
        "GmYell",
        "GmParty",
        "GmFreeCompany",
        "GmLinkshell1",
        "GmLinkshell2",
        "GmLinkshell3",
        "GmLinkshell4",
        "GmLinkshell5",
        "GmLinkshell6",
        "GmLinkshell7",
        "GmLinkshell8",
        "GmNoviceNetwork",
        "CrossLinkShell2",
        "CrossLinkShell3",
        "CrossLinkShell4",
        "CrossLinkShell5",
        "CrossLinkShell6",
        "CrossLinkShell7",
        "CrossLinkShell8",
    ];

    public static List<ChatFilterMode> CreateBuiltInModes() =>
    [
        BuiltIn(AllId, "全部消息"),
        BuiltIn("chat", "聊天频道",
            "Say", "Shout", "Yell", "TellIncoming", "TellOutgoing", "Party", "CrossParty", "Alliance",
            "FreeCompany", "NoviceNetwork", "PvPTeam",
            "Ls1", "Ls2", "Ls3", "Ls4", "Ls5", "Ls6", "Ls7", "Ls8",
            "CrossLinkShell1", "CrossLinkShell2", "CrossLinkShell3", "CrossLinkShell4",
            "CrossLinkShell5", "CrossLinkShell6", "CrossLinkShell7", "CrossLinkShell8"),
        BuiltIn("tells", "私聊",
            "TellIncoming", "TellOutgoing", "GmTell"),
        BuiltIn("party", "小队/团队",
            "Party", "CrossParty", "Alliance", "GmParty"),
        BuiltIn("social", "社交频道",
            "FreeCompany", "FreeCompanyAnnouncement", "FreeCompanyLoginLogout", "NoviceNetwork",
            "NoviceNetworkSystem", "PvPTeam", "PvpTeamAnnouncement", "PvpTeamLoginLogout",
            "Ls1", "Ls2", "Ls3", "Ls4", "Ls5", "Ls6", "Ls7", "Ls8",
            "CrossLinkShell1", "CrossLinkShell2", "CrossLinkShell3", "CrossLinkShell4",
            "CrossLinkShell5", "CrossLinkShell6", "CrossLinkShell7", "CrossLinkShell8"),
        BuiltIn("combat", "战斗状态",
            "Damage", "Miss", "Action", "Item", "Healing", "GainBuff", "GainDebuff", "LoseBuff", "LoseDebuff"),
        BuiltIn("system", "系统提示",
            "Debug", "Urgent", "Notice", "Alarm", "Echo", "SystemMessage", "SystemError",
            "GatheringSystemMessage", "ErrorMessage", "Progress", "Sign", "RandomNumber",
            "GlamourNotifications", "Orchestrion", "MessageBook"),
        BuiltIn("loot-craft", "掉落/制作",
            "LootNotice", "LootRoll", "Crafting", "Gathering", "RetainerSale"),
        BuiltIn("npc", "NPC 对话",
            "NPCDialogue", "NPCDialogueAnnouncements"),
        BuiltIn("gm", "GM 消息",
            "GmTell", "GmSay", "GmShout", "GmYell", "GmParty", "GmFreeCompany",
            "GmLinkshell1", "GmLinkshell2", "GmLinkshell3", "GmLinkshell4",
            "GmLinkshell5", "GmLinkshell6", "GmLinkshell7", "GmLinkshell8", "GmNoviceNetwork"),
    ];

    public static ChatFilterSettings CreateSettings(Configuration configuration)
    {
        EnsureDefaults(configuration);
        return new ChatFilterSettings
        {
            CurrentModeId = configuration.SelectedChatModeId,
            Modes = configuration.ChatFilterModes.Select(Clone).ToList(),
            AllTypes = AllTypes
        };
    }

    public static void ApplySettings(Configuration configuration, ChatFilterSettings settings)
    {
        configuration.ChatFilterModes = settings.Modes.Select(Sanitize).ToList();
        configuration.SelectedChatModeId = settings.CurrentModeId;
        EnsureDefaults(configuration);
    }

    public static void EnsureDefaults(Configuration configuration)
    {
        var normalized = new List<ChatFilterMode>();
        var byId = new Dictionary<string, ChatFilterMode>(StringComparer.OrdinalIgnoreCase);

        foreach (var mode in CreateBuiltInModes())
        {
            normalized.Add(mode);
            byId[mode.Id] = mode;
        }

        foreach (var mode in configuration.ChatFilterModes.Select(Sanitize))
        {
            if (string.IsNullOrWhiteSpace(mode.Id))
                continue;

            if (byId.ContainsKey(mode.Id) && mode.IsBuiltIn)
                continue;

            if (byId.ContainsKey(mode.Id))
                mode.Id = $"custom-{Guid.NewGuid():N}";

            mode.IsBuiltIn = false;
            normalized.Add(mode);
            byId[mode.Id] = mode;
        }

        configuration.ChatFilterModes = normalized;
        if (!byId.ContainsKey(configuration.SelectedChatModeId))
            configuration.SelectedChatModeId = AllId;
    }

    private static ChatFilterMode BuiltIn(string id, string name, params string[] enabledTypes) => new()
    {
        Id = id,
        Name = name,
        IsBuiltIn = true,
        EnabledTypes = enabledTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
    };

    private static ChatFilterMode Sanitize(ChatFilterMode mode)
    {
        return new ChatFilterMode
        {
            Id = string.IsNullOrWhiteSpace(mode.Id) ? Guid.NewGuid().ToString("N") : mode.Id.Trim(),
            Name = string.IsNullOrWhiteSpace(mode.Name) ? "未命名模式" : mode.Name.Trim(),
            IsBuiltIn = mode.IsBuiltIn,
            EnabledTypes = NormalizeList(mode.EnabledTypes),
            IncludeKeywords = NormalizeList(mode.IncludeKeywords),
            ExcludeKeywords = NormalizeList(mode.ExcludeKeywords)
        };
    }

    private static ChatFilterMode Clone(ChatFilterMode mode) => Sanitize(mode);

    private static List<string> NormalizeList(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
