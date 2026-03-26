using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace Dheacon.Services;

public sealed class AetheryteTriggerService : IDisposable
{
    private readonly IClientState clientState;
    private readonly ICondition condition;
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly Action<ushort, ushort> onTriggered;

    private ushort lastTerritory;
    private DateTime lastCastingObservedAt = DateTime.MinValue;

    public DateTime LastTriggeredAtUtc { get; private set; } = DateTime.MinValue;
    public string LastDecision { get; private set; } = "Waiting for first territory change.";

    public AetheryteTriggerService(
        IClientState clientState,
        ICondition condition,
        IPluginLog log,
        Configuration configuration,
        Action<ushort, ushort> onTriggered)
    {
        this.clientState = clientState;
        this.condition = condition;
        this.log = log;
        this.configuration = configuration;
        this.onTriggered = onTriggered;

        lastTerritory = clientState.TerritoryType;
        clientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose()
    {
        clientState.TerritoryChanged -= OnTerritoryChanged;
    }

    public void Update()
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return;

        if (localPlayer.IsCasting || condition[ConditionFlag.Casting])
        {
            lastCastingObservedAt = DateTime.UtcNow;
        }
    }

    private void OnTerritoryChanged(ushort newTerritory)
    {
        var previousTerritory = lastTerritory;
        lastTerritory = newTerritory;

        if (previousTerritory == 0 || previousTerritory == newTerritory)
        {
            LastDecision = $"Territory initialized to {newTerritory}.";
            return;
        }

        if (!configuration.PluginEnabled)
        {
            LastDecision = $"Ignored {previousTerritory} -> {newTerritory} because the plugin is disabled.";
            return;
        }

        var sinceCasting = DateTime.UtcNow - lastCastingObservedAt;
        if (configuration.SuppressTeleportAndReturnTransitions && sinceCasting <= TimeSpan.FromSeconds(8))
        {
            LastDecision = $"Suppressed {previousTerritory} -> {newTerritory} after a recent cast ({sinceCasting.TotalSeconds:F1}s ago).";
            log.Information($"[Dheacon] {LastDecision}");
            return;
        }

        LastTriggeredAtUtc = DateTime.UtcNow;
        LastDecision = $"Triggered on territory change {previousTerritory} -> {newTerritory}.";
        log.Information($"[Dheacon] {LastDecision}");
        onTriggered(previousTerritory, newTerritory);
    }
}
