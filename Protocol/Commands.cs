namespace PocketStation.Protocol;

public sealed record SendChatCommand(string Content);

public sealed record RequestScreenshotCommand(bool Broadcast = true);

public sealed record StartStreamCommand(int Fps = 30);

public sealed record StopStreamCommand;

public sealed record CommandShortcut(string Id, string Label, string Command);

public sealed record CommandResult(bool Ok, string Message, object? Data = null);

public sealed record TogglePluginCommand(string InternalName);

public sealed record DailyRoutinesModule(string Name, bool Enabled, string DisplayName = "");

public sealed record DailyRoutinesSnapshot(IReadOnlyList<DailyRoutinesModule> Modules);

public sealed record ToggleDailyRoutineCommand(string ModuleName, bool Enable);
