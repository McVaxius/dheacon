using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace Dheacon.Services;

public sealed class CommentaryTriggerService
{
    private const ushort MachinationsBgmId = 85;

    private readonly IClientState clientState;
    private readonly ICondition condition;
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly CommentaryLinePackService linePackService;
    private readonly SpeechQueueService speechQueueService;
    private readonly BgmProbeService bgmProbeService;

    private bool playerReadyThisSession;
    private bool wasInCombat;
    private DateTime nextIdleEligibleAtUtc = DateTime.UtcNow.AddMinutes(2);
    private DateTime lastTerritoryCommentaryAtUtc = DateTime.MinValue;
    private DateTime lastCombatCommentaryAtUtc = DateTime.MinValue;
    private DateTime lastBgmCommentaryAtUtc = DateTime.MinValue;

    public CommentaryTriggerService(
        IClientState clientState,
        ICondition condition,
        IDataManager dataManager,
        IPluginLog log,
        Configuration configuration,
        CommentaryLinePackService linePackService,
        SpeechQueueService speechQueueService,
        BgmProbeService bgmProbeService)
    {
        this.clientState = clientState;
        this.condition = condition;
        this.dataManager = dataManager;
        this.log = log;
        this.configuration = configuration;
        this.linePackService = linePackService;
        this.speechQueueService = speechQueueService;
        this.bgmProbeService = bgmProbeService;
    }

    public string LastDecision { get; private set; } = "Reading Roegadyn waiting for player-ready.";

    public void Update()
    {
        if (!IsReadingRoegadynActive())
        {
            wasInCombat = condition[ConditionFlag.InCombat];
            return;
        }

        var localPlayerReady = clientState.IsLoggedIn && Plugin.ObjectTable.LocalPlayer != null;
        if (!localPlayerReady)
        {
            playerReadyThisSession = false;
            wasInCombat = false;
            LastDecision = "Reading Roegadyn waiting for login.";
            return;
        }

        var now = DateTime.UtcNow;
        HandlePlayerReady(now);
        HandleCombat(now);
        HandleIdle(now);
        HandleBgm(now);
    }

    public bool SpeakManual(string? customText = null)
    {
        if (!IsReadingRoegadynActive())
        {
            LastDecision = "Ignored manual speech because Reading Roegadyn mode is not active or the plugin is disabled.";
            return false;
        }

        var text = string.IsNullOrWhiteSpace(customText)
            ? linePackService.GetLine(CommentaryCategory.ManualTest)
            : customText.Trim();

        return Enqueue(CommentaryCategory.ManualTest, text, "manual test");
    }

    public void TriggerTerritoryChange(uint fromTerritory, uint toTerritory)
    {
        if (!IsReadingRoegadynActive())
            return;

        if (!configuration.TerritoryCommentaryEnabled)
        {
            LastDecision = "Ignored territory commentary because it is disabled.";
            return;
        }

        var now = DateTime.UtcNow;
        if (!CooldownElapsed(lastTerritoryCommentaryAtUtc, configuration.TerritoryCommentaryCooldownSeconds, now))
        {
            LastDecision = $"Suppressed territory commentary by cooldown for {fromTerritory} -> {toTerritory}.";
            return;
        }

        var context = new CommentaryContext(
            FromTerritoryId: fromTerritory,
            ToTerritoryId: toTerritory,
            FromTerritoryName: ResolveTerritoryName(fromTerritory),
            ToTerritoryName: ResolveTerritoryName(toTerritory));

        var text = linePackService.GetLine(CommentaryCategory.TerritoryChange, context);
        if (Enqueue(CommentaryCategory.TerritoryChange, text, $"{fromTerritory} -> {toTerritory}"))
            lastTerritoryCommentaryAtUtc = now;
    }

    private void HandlePlayerReady(DateTime now)
    {
        if (playerReadyThisSession)
            return;

        playerReadyThisSession = true;
        nextIdleEligibleAtUtc = now.AddSeconds(Math.Max(30, configuration.IdleCommentaryCooldownSeconds));

        if (!configuration.LoginCommentaryEnabled)
        {
            LastDecision = "Player ready; login commentary disabled.";
            return;
        }

        var text = linePackService.GetLine(CommentaryCategory.Login);
        Enqueue(CommentaryCategory.Login, text, "player ready");
    }

    private void HandleCombat(DateTime now)
    {
        var inCombat = condition[ConditionFlag.InCombat];
        if (inCombat)
            nextIdleEligibleAtUtc = now.AddSeconds(Math.Max(30, configuration.IdleCommentaryCooldownSeconds));

        if (inCombat == wasInCombat)
            return;

        wasInCombat = inCombat;
        if (!configuration.CombatCommentaryEnabled)
        {
            LastDecision = "Combat state changed; combat commentary disabled.";
            return;
        }

        if (!CooldownElapsed(lastCombatCommentaryAtUtc, configuration.CombatCommentaryCooldownSeconds, now))
        {
            LastDecision = "Suppressed combat commentary by cooldown.";
            return;
        }

        var category = inCombat ? CommentaryCategory.CombatStart : CommentaryCategory.CombatEnd;
        var text = linePackService.GetLine(category);
        if (Enqueue(category, text, inCombat ? "combat start" : "combat end"))
            lastCombatCommentaryAtUtc = now;

        nextIdleEligibleAtUtc = now.AddSeconds(Math.Max(30, configuration.IdleCommentaryCooldownSeconds));
    }

    private void HandleIdle(DateTime now)
    {
        if (!configuration.IdleCommentaryEnabled)
            return;

        if (now < nextIdleEligibleAtUtc)
            return;

        if (condition[ConditionFlag.InCombat] || !clientState.IsClientIdle())
        {
            nextIdleEligibleAtUtc = now.AddSeconds(30);
            return;
        }

        var text = linePackService.GetLine(CommentaryCategory.Idle);
        if (Enqueue(CommentaryCategory.Idle, text, "idle cooldown"))
            nextIdleEligibleAtUtc = now.AddSeconds(Math.Max(30, configuration.IdleCommentaryCooldownSeconds));
    }

    private void HandleBgm(DateTime now)
    {
        if (!configuration.BgmMachinationsCommentaryEnabled)
            return;

        var changed = bgmProbeService.Update();
        if (!changed || !bgmProbeService.Available)
            return;

        if (bgmProbeService.CurrentBgmId != MachinationsBgmId)
            return;

        if (!CooldownElapsed(lastBgmCommentaryAtUtc, configuration.BgmCommentaryCooldownSeconds, now))
        {
            LastDecision = "Suppressed Machinations BGM commentary by cooldown.";
            return;
        }

        var context = new CommentaryContext(BgmId: bgmProbeService.CurrentBgmId);
        var text = linePackService.GetLine(CommentaryCategory.BgmMachinations, context);
        if (Enqueue(CommentaryCategory.BgmMachinations, text, $"BGM {bgmProbeService.CurrentBgmId}"))
            lastBgmCommentaryAtUtc = now;
    }

    private bool Enqueue(CommentaryCategory category, string text, string reason)
    {
        var queued = speechQueueService.TryEnqueue(new CommentaryRequest(category, text, reason));
        LastDecision = queued
            ? $"Queued {category}: {reason}."
            : $"Failed to queue {category}: {reason}.";
        return queued;
    }

    private bool IsReadingRoegadynActive()
        => configuration.PluginEnabled && configuration.CommentaryMode == CommentaryMode.ReadingRoegadyn;

    private static bool CooldownElapsed(DateTime lastAtUtc, int cooldownSeconds, DateTime nowUtc)
        => lastAtUtc == DateTime.MinValue || nowUtc - lastAtUtc >= TimeSpan.FromSeconds(Math.Max(0, cooldownSeconds));

    private string ResolveTerritoryName(uint territoryId)
    {
        try
        {
            var sheet = dataManager.GetExcelSheet<TerritoryType>();
            if (sheet.TryGetRow(territoryId, out var row))
            {
                var placeName = row.PlaceName.ValueNullable?.Name.ToString();
                if (!string.IsNullOrWhiteSpace(placeName))
                    return placeName;
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, $"[Dheacon] Failed to resolve territory name for {territoryId}.");
        }

        return $"territory {territoryId}";
    }
}
