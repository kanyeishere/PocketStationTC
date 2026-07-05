using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace PocketStation.Infrastructure.Game;

internal static class ChatSender
{
    public static unsafe void Send(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        using var utf8String = new Utf8String();
        utf8String.SetString(message);
        UIModule.Instance()->ProcessChatBoxEntry(&utf8String, nint.Zero, false);
    }
}
