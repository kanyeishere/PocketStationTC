using System.Numerics;

namespace PocketStation.Protocol;

public sealed record ChatEvent(
    long Sequence,
    string Channel,
    string Sender,
    string Message,
    long Timestamp);

public sealed record StatusEvent(
    uint StatusId,
    float RemainingTime,
    ushort Param,
    uint SourceId);

public sealed record CharacterState(
    string Name,
    ulong ObjectId,
    uint EntityId,
    uint ClassJobId,
    string ClassJobName,
    byte Level,
    uint CurrentHp,
    uint MaxHp,
    uint CurrentMp,
    uint MaxMp,
    Vector3 Position,
    bool IsDead,
    IReadOnlyList<StatusEvent> Statuses);

public sealed record PlayerSnapshot(
    bool IsLoggedIn,
    uint TerritoryType,
    uint MapId,
    string TerritoryName,
    string WorldName,
    string DataCenterName,
    CharacterState? LocalPlayer,
    CharacterState? Target,
    IReadOnlyList<CharacterState> Party,
    long Timestamp,
    IReadOnlyList<CurrencyInfo>? Currencies = null);

public sealed record CurrencyInfo(
    uint ItemId,
    string Name,
    uint Count,
    string IconId);

public sealed record ScreenshotReadyEvent(
    string Url,
    int Width,
    int Height,
    long CapturedAt,
    string ContentType);

public sealed record SystemEvent(
    string Level,
    string Message,
    object? Data = null);

public sealed record DalamudPluginInfo(
    string InternalName,
    string Name,
    string Version,
    bool IsLoaded);
