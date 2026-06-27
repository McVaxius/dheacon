using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace Dheacon.Services;

public sealed class CommentaryTriggerService : IDisposable
{
    private const ushort MachinationsBgmId = 85;

    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly ICondition condition;
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly CommentaryLinePackService linePackService;
    private readonly SpeechQueueService speechQueueService;
    private readonly BgmProbeService bgmProbeService;
    private readonly IReadOnlyList<ConditionTransition> expandedTransitions;
    private readonly Dictionary<string, bool> expandedConditionStates = new(StringComparer.OrdinalIgnoreCase);

    private bool playerReadyThisSession;
    private bool wasInCombat;
    private bool wasInCutscene;
    private bool expandedConditionStatesInitialized;
    private DateTime nextIdleEligibleAtUtc = DateTime.UtcNow.AddMinutes(2);
    private DateTime lastTerritoryCommentaryAtUtc = DateTime.MinValue;
    private DateTime lastCombatCommentaryAtUtc = DateTime.MinValue;
    private DateTime lastBgmCommentaryAtUtc = DateTime.MinValue;
    private DateTime lastExpandedCommentaryAtUtc = DateTime.MinValue;
    private DateTime lastNearbyObservationAtUtc = DateTime.MinValue;

    public CommentaryTriggerService(
        IClientState clientState,
        IPlayerState playerState,
        ICondition condition,
        IDataManager dataManager,
        IPluginLog log,
        Configuration configuration,
        CommentaryLinePackService linePackService,
        SpeechQueueService speechQueueService,
        BgmProbeService bgmProbeService)
    {
        this.clientState = clientState;
        this.playerState = playerState;
        this.condition = condition;
        this.dataManager = dataManager;
        this.log = log;
        this.configuration = configuration;
        this.linePackService = linePackService;
        this.speechQueueService = speechQueueService;
        this.bgmProbeService = bgmProbeService;
        expandedTransitions = CreateExpandedTransitions();

        clientState.ClassJobChanged += OnClassJobChanged;
        clientState.LevelChanged += OnLevelChanged;
        clientState.EnterPvP += OnEnterPvp;
        clientState.LeavePvP += OnLeavePvp;
    }

    public string LastDecision { get; private set; } = "Reading Roegadyn waiting for player-ready.";

    public void Dispose()
    {
        clientState.ClassJobChanged -= OnClassJobChanged;
        clientState.LevelChanged -= OnLevelChanged;
        clientState.EnterPvP -= OnEnterPvp;
        clientState.LeavePvP -= OnLeavePvp;
    }

    public void Update()
    {
        if (!IsReadingRoegadynActive())
        {
            wasInCombat = condition[ConditionFlag.InCombat];
            wasInCutscene = IsCutsceneActive();
            SyncExpandedConditionStates();
            return;
        }

        var localPlayerReady = clientState.IsLoggedIn && Plugin.ObjectTable.LocalPlayer != null;
        if (!localPlayerReady)
        {
            playerReadyThisSession = false;
            wasInCombat = false;
            wasInCutscene = false;
            expandedConditionStatesInitialized = false;
            expandedConditionStates.Clear();
            LastDecision = "Reading Roegadyn waiting for login.";
            return;
        }

        var now = DateTime.UtcNow;
        if (!expandedConditionStatesInitialized)
            SyncExpandedConditionStates();

        HandlePlayerReady(now);
        HandleCombat(now);
        HandleIdle(now);
        HandleBgm(now);
        HandleCutsceneContext(now);
        HandleNearbyObservation(now);
        HandleExpandedConditionEvents(now);
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
            ToTerritoryName: ResolveTerritoryName(toTerritory),
            Event: "territory change");

        var text = linePackService.GetLine(CommentaryCategory.TerritoryChange, context);
        if (TryEnqueueAutomatic(CommentaryCategory.TerritoryChange, text, $"{fromTerritory} -> {toTerritory}"))
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

        var text = linePackService.GetLine(CommentaryCategory.Login, new CommentaryContext(Event: "login"));
        TryEnqueueAutomatic(CommentaryCategory.Login, text, "player ready");
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
        var text = linePackService.GetLine(category, new CommentaryContext(Event: inCombat ? "combat start" : "combat end"));
        if (TryEnqueueAutomatic(category, text, inCombat ? "combat start" : "combat end"))
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

        var text = linePackService.GetLine(CommentaryCategory.Idle, new CommentaryContext(Event: "idle"));
        TryEnqueueAutomatic(CommentaryCategory.Idle, text, "idle cooldown");
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

        var context = new CommentaryContext(BgmId: bgmProbeService.CurrentBgmId, Event: "Machinations BGM");
        var text = linePackService.GetLine(CommentaryCategory.BgmMachinations, context);
        if (TryEnqueueAutomatic(CommentaryCategory.BgmMachinations, text, $"BGM {bgmProbeService.CurrentBgmId}"))
            lastBgmCommentaryAtUtc = now;
    }

    private void HandleCutsceneContext(DateTime now)
    {
        var inCutscene = IsCutsceneActive();
        if (inCutscene == wasInCutscene)
            return;

        wasInCutscene = inCutscene;
        if (!configuration.ExpandedEventCommentaryEnabled)
        {
            LastDecision = "Cutscene state changed; expanded commentary disabled.";
            return;
        }

        var cutsceneContext = ResolveCutsceneContext();
        var category = (inCutscene, cutsceneContext) switch
        {
            (true, "treasure dungeon") => CommentaryCategory.CutsceneStartTreasureDungeon,
            (false, "treasure dungeon") => CommentaryCategory.CutsceneEndTreasureDungeon,
            (true, "duty") => CommentaryCategory.CutsceneStartDuty,
            (false, "duty") => CommentaryCategory.CutsceneEndDuty,
            (true, _) => CommentaryCategory.CutsceneStartNonDuty,
            _ => CommentaryCategory.CutsceneEndNonDuty,
        };

        var context = new CommentaryContext(Event: "cutscene", CutsceneContext: cutsceneContext);
        TryTriggerExpandedEvent(category, context, $"{cutsceneContext} cutscene {(inCutscene ? "start" : "end")}", now);
    }

    private void HandleNearbyObservation(DateTime now)
    {
        if (!configuration.NearbyObservationCommentaryEnabled)
            return;

        if (!CooldownElapsed(lastNearbyObservationAtUtc, configuration.NearbyObservationCooldownSeconds, now))
            return;

        if (condition[ConditionFlag.InCombat] || !clientState.IsClientIdle())
            return;

        if (!TryGetNearbyPlayerContext(out var nearbyName, out var nearbyCount))
            return;

        var category = nearbyCount >= 4
            ? CommentaryCategory.NearbyCrowdObservation
            : CommentaryCategory.NearbyPlayerObservation;
        var context = new CommentaryContext(
            Event: nearbyCount >= 4 ? "nearby crowd" : "nearby player",
            NearbyPlayerName: nearbyName,
            NearbyPlayerCount: nearbyCount);
        var text = linePackService.GetLine(category, context);
        if (TryEnqueueAutomatic(category, text, nearbyCount >= 4 ? $"{nearbyCount} nearby players" : nearbyName))
            lastNearbyObservationAtUtc = now;
    }

    private void HandleExpandedConditionEvents(DateTime now)
    {
        if (!configuration.ExpandedEventCommentaryEnabled)
        {
            SyncExpandedConditionStates();
            return;
        }

        foreach (var transition in expandedTransitions)
        {
            var current = transition.IsActive(this);
            var previous = expandedConditionStates.GetValueOrDefault(transition.EventName);
            if (current == previous)
            {
                expandedConditionStates[transition.EventName] = current;
                continue;
            }

            expandedConditionStates[transition.EventName] = current;
            var category = current ? transition.StartCategory : transition.EndCategory;
            var stateName = current ? "start" : "end";
            TryTriggerExpandedEvent(category, transition.EventName, $"{transition.EventName} {stateName}", now);
        }
    }

    private void SyncExpandedConditionStates()
    {
        foreach (var transition in expandedTransitions)
            expandedConditionStates[transition.EventName] = transition.IsActive(this);

        expandedConditionStatesInitialized = true;
    }

    private void OnClassJobChanged(uint classJobId)
    {
        var jobName = ResolveClassJobName(classJobId);
        var context = new CommentaryContext(Job: jobName, Level: ResolveCurrentLevel(), Event: "class/job change");
        TryTriggerExpandedEvent(CommentaryCategory.ClassJobChange, context, $"class/job {jobName}", DateTime.UtcNow);
    }

    private void OnLevelChanged(uint classJobId, uint level)
    {
        var jobName = ResolveClassJobName(classJobId);
        var context = new CommentaryContext(Job: jobName, Level: level, Event: "level change");
        TryTriggerExpandedEvent(CommentaryCategory.LevelChange, context, $"{jobName} level {level}", DateTime.UtcNow);
    }

    private void OnEnterPvp()
        => TryTriggerExpandedEvent(CommentaryCategory.PvpEnter, "PvP", "PvP enter", DateTime.UtcNow);

    private void OnLeavePvp()
        => TryTriggerExpandedEvent(CommentaryCategory.PvpLeave, "PvP", "PvP leave", DateTime.UtcNow);

    private bool TryTriggerExpandedEvent(CommentaryCategory category, string eventName, string reason, DateTime now)
    {
        var context = new CommentaryContext(
            Job: ResolveCurrentClassJobName(),
            Level: ResolveCurrentLevel(),
            Event: eventName);
        return TryTriggerExpandedEvent(category, context, reason, now);
    }

    private bool TryTriggerExpandedEvent(CommentaryCategory category, CommentaryContext context, string reason, DateTime now)
    {
        if (!IsReadingRoegadynActive())
            return false;

        if (!clientState.IsLoggedIn || Plugin.ObjectTable.LocalPlayer == null)
        {
            LastDecision = $"Ignored {category} because the player is not ready.";
            return false;
        }

        if (!configuration.ExpandedEventCommentaryEnabled)
        {
            LastDecision = $"Ignored {category} because expanded event commentary is disabled.";
            return false;
        }

        if (!CooldownElapsed(lastExpandedCommentaryAtUtc, configuration.ExpandedEventCooldownSeconds, now))
        {
            LastDecision = $"Suppressed {category} by expanded event cooldown.";
            return false;
        }

        var text = linePackService.GetLine(category, context);
        if (!TryEnqueueAutomatic(category, text, reason))
            return false;

        lastExpandedCommentaryAtUtc = now;
        return true;
    }

    private bool TryEnqueueAutomatic(CommentaryCategory category, string text, string reason)
    {
        if (speechQueueService.IsBusy)
        {
            LastDecision = $"Skipped {category}: speech is already busy.";
            return false;
        }

        var triggerChance = Math.Clamp(configuration.ReadingRoegadynTriggerChancePercent, 0, 100);
        if (triggerChance <= 0)
        {
            LastDecision = $"Skipped {category}: automatic trigger chance is 0%.";
            return false;
        }

        if (triggerChance < 100 && Random.Shared.Next(100) >= triggerChance)
        {
            LastDecision = $"Skipped {category}: automatic trigger chance roll missed at {triggerChance}%.";
            return false;
        }

        return Enqueue(category, text, reason);
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

    private bool ConditionAny(params ConditionFlag[] flags)
        => flags.Any(flag => condition[flag]);

    private bool IsCutsceneActive()
        => ConditionAny(ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78, ConditionFlag.OccupiedInCutSceneEvent);

    private string ResolveCutsceneContext()
    {
        if (IsKnownTreasureDungeonTerritory())
            return "treasure dungeon";

        if (ConditionAny(ConditionFlag.BoundByDuty, ConditionFlag.BoundByDuty56, ConditionFlag.BoundByDuty95))
            return "duty";

        return "non-duty";
    }

    private bool IsKnownTreasureDungeonTerritory()
    {
        var territoryName = ResolveTerritoryName(clientState.TerritoryType);
        return territoryName.Contains("Aquapolis", StringComparison.OrdinalIgnoreCase) ||
               territoryName.Contains("Uznair", StringComparison.OrdinalIgnoreCase) ||
               territoryName.Contains("Lyhe Ghiah", StringComparison.OrdinalIgnoreCase) ||
               territoryName.Contains("Excitatron", StringComparison.OrdinalIgnoreCase) ||
               territoryName.Contains("Agonon", StringComparison.OrdinalIgnoreCase) ||
               territoryName.Contains("Cenote", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetNearbyPlayerContext(out string nearbyName, out int nearbyCount)
    {
        nearbyName = string.Empty;
        nearbyCount = 0;

        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return false;

        var localPosition = localPlayer.Position;
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj == null ||
                obj.ObjectKind != ObjectKind.Pc ||
                obj.Address == localPlayer.Address)
            {
                continue;
            }

            if (Vector3.Distance(localPosition, obj.Position) > 12f)
                continue;

            nearbyCount++;
            if (string.IsNullOrWhiteSpace(nearbyName))
                nearbyName = obj.Name.ToString();
        }

        if (nearbyCount <= 0)
            return false;

        if (string.IsNullOrWhiteSpace(nearbyName))
            nearbyName = "nearby adventurer";

        return true;
    }

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

    private string ResolveCurrentClassJobName()
    {
        try
        {
            if (playerState.ClassJob.IsValid)
            {
                var row = playerState.ClassJob.Value;
                var name = row.Name.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;

                var abbreviation = row.Abbreviation.ToString();
                if (!string.IsNullOrWhiteSpace(abbreviation))
                    return abbreviation;
            }
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "[Dheacon] Failed to resolve current class/job from player state.");
        }

        return "adventurer";
    }

    private string ResolveClassJobName(uint classJobId)
    {
        if (classJobId == 0)
            return "adventurer";

        try
        {
            var sheet = dataManager.GetExcelSheet<ClassJob>();
            if (sheet.TryGetRow(classJobId, out var row))
            {
                var name = row.Name.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;

                var abbreviation = row.Abbreviation.ToString();
                if (!string.IsNullOrWhiteSpace(abbreviation))
                    return abbreviation;
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, $"[Dheacon] Failed to resolve class/job name for {classJobId}.");
        }

        return $"job {classJobId}";
    }

    private uint ResolveCurrentLevel()
        => (uint)Math.Max(0, (int)playerState.Level);

    private static IReadOnlyList<ConditionTransition> CreateExpandedTransitions()
        => new List<ConditionTransition>
        {
            new(
                "mount",
                CommentaryCategory.MountStart,
                CommentaryCategory.MountEnd,
                service => service.ConditionAny(ConditionFlag.Mounted, ConditionFlag.RidingPillion)),
            new(
                "flight",
                CommentaryCategory.FlightStart,
                CommentaryCategory.FlightEnd,
                service => service.ConditionAny(ConditionFlag.InFlight)),
            new(
                "duty queue",
                CommentaryCategory.DutyQueueStart,
                CommentaryCategory.DutyQueueEnd,
                service => service.ConditionAny(ConditionFlag.InDutyQueue, ConditionFlag.WaitingForDuty, ConditionFlag.WaitingForDutyFinder)),
            new(
                "duty",
                CommentaryCategory.DutyStart,
                CommentaryCategory.DutyEnd,
                service => service.ConditionAny(ConditionFlag.BoundByDuty, ConditionFlag.BoundByDuty56, ConditionFlag.BoundByDuty95)),
            new(
                "crafting",
                CommentaryCategory.CraftingStart,
                CommentaryCategory.CraftingEnd,
                service => service.ConditionAny(ConditionFlag.Crafting, ConditionFlag.PreparingToCraft, ConditionFlag.ExecutingCraftingAction)),
            new(
                "gathering",
                CommentaryCategory.GatheringStart,
                CommentaryCategory.GatheringEnd,
                service => !service.condition[ConditionFlag.Fishing] &&
                           service.ConditionAny(ConditionFlag.Gathering, ConditionFlag.ExecutingGatheringAction)),
            new(
                "fishing",
                CommentaryCategory.FishingStart,
                CommentaryCategory.FishingEnd,
                service => service.ConditionAny(ConditionFlag.Fishing)),
            new(
                "performance",
                CommentaryCategory.PerformanceStart,
                CommentaryCategory.PerformanceEnd,
                service => service.ConditionAny(ConditionFlag.Performing)),
            new(
                "mini-game",
                CommentaryCategory.MinigameStart,
                CommentaryCategory.MinigameEnd,
                service => service.ConditionAny(ConditionFlag.PlayingMiniGame, ConditionFlag.PlayingLordOfVerminion, ConditionFlag.ChocoboRacing)),
            new(
                "summoning bell",
                CommentaryCategory.SummoningBellStart,
                CommentaryCategory.SummoningBellEnd,
                service => service.ConditionAny(ConditionFlag.OccupiedSummoningBell)),
            new(
                "Party Finder",
                CommentaryCategory.PartyFinderStart,
                CommentaryCategory.PartyFinderEnd,
                service => service.ConditionAny(ConditionFlag.UsingPartyFinder)),
            new(
                "swimming",
                CommentaryCategory.SwimmingStart,
                CommentaryCategory.SwimmingEnd,
                service => service.ConditionAny(ConditionFlag.Swimming)),
            new(
                "diving",
                CommentaryCategory.DivingStart,
                CommentaryCategory.DivingEnd,
                service => service.ConditionAny(ConditionFlag.Diving)),
            new(
                "unconscious",
                CommentaryCategory.Unconscious,
                CommentaryCategory.Recovered,
                service => service.ConditionAny(ConditionFlag.Unconscious)),
        };

    private sealed record ConditionTransition(
        string EventName,
        CommentaryCategory StartCategory,
        CommentaryCategory EndCategory,
        Func<CommentaryTriggerService, bool> IsActive);
}
