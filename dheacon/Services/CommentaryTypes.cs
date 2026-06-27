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
}

public sealed record CommentaryContext(
    uint? FromTerritoryId = null,
    uint? ToTerritoryId = null,
    string? FromTerritoryName = null,
    string? ToTerritoryName = null,
    ushort? BgmId = null);

public sealed record CommentaryRequest(
    CommentaryCategory Category,
    string Text,
    string Reason);
