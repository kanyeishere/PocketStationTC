namespace PocketStation.Protocol;

public sealed record SendChatCommand(string Content);

public sealed record RequestScreenshotCommand(bool Broadcast = true);

public sealed record CommandResult(bool Ok, string Message, object? Data = null);
