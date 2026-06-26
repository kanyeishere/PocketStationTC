using OmenTools.OmenService;
using PocketStation.Domain;

namespace PocketStation.Infrastructure.Game;

public static class CurrencyHelper
{
    private static readonly (uint ItemId, string Name, string IconId)[] KnownCurrencies =
    [
        (1,     "金币",         "gil"),
        (28,    "诗学神典石",   "poetics"),
        (48,    "数理神典石",   "math"),
        (49,    "记忆神典石",   "mnemo"),
        (26807, "双色宝石",     "bicolor")
    ];

    public static IReadOnlyList<CurrencyInfo> Capture()
    {
        var result = new List<CurrencyInfo>();
        foreach (var (itemId, name, iconId) in KnownCurrencies)
        {
            var count = LocalPlayerState.GetItemCount(itemId);
            result.Add(new CurrencyInfo(itemId, name, count, iconId));
        }

        return result;
    }
}
