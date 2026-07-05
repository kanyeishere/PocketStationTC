using FFXIVClientStructs.FFXIV.Client.Game;
using PocketStation.Domain;

namespace PocketStation.Infrastructure.Game;

public static class CurrencyHelper
{
    private static readonly (uint ItemId, string Name, string IconId, bool IsWeeklyLimited)[] KnownCurrencies =
    [
        (1,     "金币",         "gil",      false),
        (28,    "诗学神典石",   "poetics",  false),
        (48,    "数理神典石",   "math",     false),
        (49,    "记忆神典石",   "mnemo",    true),
        (26807, "双色宝石",     "bicolor",  false)
    ];

    public static IReadOnlyList<CurrencyInfo> Capture()
    {
        var result = new List<CurrencyInfo>();
        var limitedProgress = CaptureLimitedTomestoneProgress();

        foreach (var (itemId, name, iconId, isWeeklyLimited) in KnownCurrencies)
        {
            var count = GetItemCount(itemId);
            result.Add(new CurrencyInfo(
                itemId,
                name,
                count,
                iconId,
                isWeeklyLimited ? limitedProgress.WeeklyAcquired : null,
                isWeeklyLimited ? limitedProgress.WeeklyLimit : null));
        }

        return result;
    }

    private unsafe static (uint? WeeklyAcquired, uint? WeeklyLimit) CaptureLimitedTomestoneProgress()
    {
        try
        {
            var inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return (null, null);

            var acquired = Math.Max(0, inventoryManager->GetWeeklyAcquiredTomestoneCount());
            var limit = Math.Max(0, InventoryManager.GetLimitedTomestoneWeeklyLimit());
            return ((uint)acquired, (uint)limit);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Failed to capture limited tomestone weekly progress");
            return (null, null);
        }
    }

    private unsafe static uint GetItemCount(uint itemId)
    {
        try
        {
            var currencyManager = FFXIVClientStructs.FFXIV.Client.Game.CurrencyManager.Instance();
            if (currencyManager != null && currencyManager->HasItem(itemId))
                return currencyManager->GetItemCount(itemId);

            var inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return 0;

            var normal = inventoryManager->GetInventoryItemCount(itemId);
            var highQuality = inventoryManager->GetInventoryItemCount(itemId, true);
            return (uint)Math.Max(0, normal + highQuality);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Failed to capture item count for {ItemId}", itemId);
            return 0;
        }
    }
}
