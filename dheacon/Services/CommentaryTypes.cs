namespace Dheacon.Services;

public enum CommentaryCategory
{
    ManualTest,
    TerritoryChange,
    Login,
    Idle,
    CombatStart,
    CombatEnd,
    BgmMachinations,
    LevelChange,
    ClassJobChange,
    MountStart,
    MountEnd,
    FlightStart,
    FlightEnd,
    DutyQueueStart,
    DutyQueueEnd,
    DutyStart,
    DutyEnd,
    CraftingStart,
    CraftingEnd,
    GatheringStart,
    GatheringEnd,
    FishingStart,
    FishingEnd,
    CutsceneStart,
    CutsceneEnd,
    CutsceneStartDuty,
    CutsceneEndDuty,
    CutsceneStartNonDuty,
    CutsceneEndNonDuty,
    CutsceneStartTreasureDungeon,
    CutsceneEndTreasureDungeon,
    PerformanceStart,
    PerformanceEnd,
    MinigameStart,
    MinigameEnd,
    SummoningBellStart,
    SummoningBellEnd,
    PartyFinderStart,
    PartyFinderEnd,
    SwimmingStart,
    SwimmingEnd,
    DivingStart,
    DivingEnd,
    Unconscious,
    Recovered,
    PvpEnter,
    PvpLeave,
    NearbyPlayerObservation,
    NearbyCrowdObservation,
}

public sealed record CommentaryContext(
    uint? FromTerritoryId = null,
    uint? ToTerritoryId = null,
    string? FromTerritoryName = null,
    string? ToTerritoryName = null,
    ushort? BgmId = null,
    string? Job = null,
    uint? Level = null,
    string? Event = null,
    string? NearbyPlayerName = null,
    int? NearbyPlayerCount = null,
    string? CutsceneContext = null);

public sealed record CommentaryRequest(
    CommentaryCategory Category,
    string Text,
    string Reason);

public sealed class LinePackEntry
{
    public string Text { get; set; } = string.Empty;
    public int Weight { get; set; } = 1;
}
