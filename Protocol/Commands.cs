namespace PocketStation.Protocol;

public sealed record SendChatCommand(string Content);

public sealed record RequestScreenshotCommand(bool Broadcast = true);

public sealed record StartStreamCommand(int Fps = 30);

public sealed record StopStreamCommand;

public sealed record CommandResult(bool Ok, string Message, object? Data = null);
