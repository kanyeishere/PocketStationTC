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
    CharacterState? LocalPlayer,
    CharacterState? Target,
    IReadOnlyList<CharacterState> Party,
    long Timestamp);

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
