using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dheacon.Services;
using Dheacon.Windows;

namespace Dheacon;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;

    public Configuration Configuration { get; }
    public AetheryteTriggerService AetheryteTriggerService { get; }
    public AudioPlaybackService AudioPlaybackService { get; }
    public CommentaryLinePackService CommentaryLinePackService { get; }
    public PiperVoiceCatalogService PiperVoiceCatalogService { get; }
    public SpokenTextAdapterService SpokenTextAdapterService { get; }
    public SpeechCacheService SpeechCacheService { get; }
    public SpeechQueueService SpeechQueueService { get; }
    public CommentaryTriggerService CommentaryTriggerService { get; }
    public BgmProbeService BgmProbeService { get; }
    public WindowSystem WindowSystem { get; } = new(PluginInfo.InternalName);
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private IDtrBarEntry? dtrEntry;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        MigrateConfiguration();
        AudioPlaybackService = new AudioPlaybackService(Log, Configuration);
        CommentaryLinePackService = new CommentaryLinePackService(Log);
        PiperVoiceCatalogService = new PiperVoiceCatalogService(Log, Configuration);
        SpokenTextAdapterService = new SpokenTextAdapterService(Log);
        SpeechCacheService = new SpeechCacheService(Log, Configuration, PiperVoiceCatalogService, SpokenTextAdapterService);
        SpeechQueueService = new SpeechQueueService(Log, SpeechCacheService, AudioPlaybackService);
        BgmProbeService = new BgmProbeService(SigScanner, Log);
        CommentaryTriggerService = new CommentaryTriggerService(
            ClientState,
            PlayerState,
            Condition,
            DataManager,
            Log,
            Configuration,
            CommentaryLinePackService,
            SpeechQueueService,
            BgmProbeService);
        AetheryteTriggerService = new AetheryteTriggerService(ClientState, Condition, Log, Configuration, OnTriggeredAreaTransition);
        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);
        CommandManager.AddHandler(PluginInfo.Command, new CommandInfo(OnCommand) { HelpMessage = $"Open {PluginInfo.DisplayName}. Use {PluginInfo.Command} config, mode dheacon|roe, say, voices, piperpreview, clearcache, on, or off." });
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        Framework.Update += OnFrameworkUpdate;
        SetupDtrBar();
        UpdateDtrBar();
        Log.Information("[Dheacon] Plugin loaded.");
    }

    public void Dispose()
    {
        AetheryteTriggerService.Dispose();
        CommentaryTriggerService.Dispose();
        SpeechQueueService.Dispose();
        PiperVoiceCatalogService.Dispose();
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        CommandManager.RemoveHandler(PluginInfo.Command);
        WindowSystem.RemoveAllWindows();
        dtrEntry?.Remove();
    }

    public void ToggleMainUi() => mainWindow.Toggle();
    public void ToggleConfigUi() => configWindow.Toggle();
    public void PrintStatus(string m) => ChatGui.Print($"[{PluginInfo.DisplayName}] {m}");

    private void OnCommand(string command, string arguments)
    {
        var trimmed = arguments.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            ToggleMainUi();
            return;
        }

        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var verb = parts[0];
        var rest = parts.Length > 1 ? parts[1] : string.Empty;

        if (verb.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            ToggleConfigUi();
            return;
        }

        if (verb.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            Configuration.PluginEnabled = true;
            Configuration.Save();
            UpdateDtrBar();
            PrintStatus("Enabled.");
            return;
        }

        if (verb.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            Configuration.PluginEnabled = false;
            Configuration.Save();
            UpdateDtrBar();
            PrintStatus("Disabled.");
            return;
        }

        if (verb.Equals("mode", StringComparison.OrdinalIgnoreCase))
        {
            SetMode(rest);
            return;
        }

        if (verb.Equals("say", StringComparison.OrdinalIgnoreCase))
        {
            var queued = CommentaryTriggerService.SpeakManual(rest);
            PrintStatus(queued ? "Speech queued." : CommentaryTriggerService.LastDecision);
            return;
        }

        if (verb.Equals("voices", StringComparison.OrdinalIgnoreCase))
        {
            PrintVoiceDiagnostics();
            return;
        }

        if (verb.Equals("piperpreview", StringComparison.OrdinalIgnoreCase) ||
            verb.Equals("previewpiper", StringComparison.OrdinalIgnoreCase))
        {
            PrintPiperPreview(rest);
            return;
        }

        if (verb.Equals("pipercatalog", StringComparison.OrdinalIgnoreCase))
        {
            _ = RefreshPiperCatalogToChatAsync();
            return;
        }

        if (verb.Equals("clearcache", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var deleted = SpeechCacheService.ClearCache();
                PrintStatus($"Cleared {deleted} cached speech WAV file(s).");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Dheacon] Failed to clear TTS cache.");
                PrintStatus($"Failed to clear cache: {ex.Message}");
            }

            return;
        }

        ToggleMainUi();
    }

    private void MigrateConfiguration()
    {
        var changed = false;

        if (Configuration.Version < 4)
        {
            Configuration.TtsBackend = TtsBackend.LegacySapi;
            changed = true;
        }

        if (Configuration.Version < 5)
        {
#pragma warning disable CS0618
            Configuration.TtsPiperTextAdapterEnabled = Configuration.TtsPiperSwedishAccentAdapterEnabled;
#pragma warning restore CS0618
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(Configuration.TtsPiperTextAdapterId))
        {
            Configuration.TtsPiperTextAdapterId = SpokenTextAdapterService.DefaultAdapterId;
            changed = true;
        }

        var clampedChance = Math.Clamp(Configuration.ReadingRoegadynTriggerChancePercent, 0, 100);
        if (Configuration.ReadingRoegadynTriggerChancePercent != clampedChance)
        {
            Configuration.ReadingRoegadynTriggerChancePercent = clampedChance;
            changed = true;
        }

        var clampedExpandedCooldown = Math.Max(0, Configuration.ExpandedEventCooldownSeconds);
        if (Configuration.ExpandedEventCooldownSeconds != clampedExpandedCooldown)
        {
            Configuration.ExpandedEventCooldownSeconds = clampedExpandedCooldown;
            changed = true;
        }

        if (Configuration.Version < 7)
        {
            Configuration.TtsPiperLengthScale = 1.0d;
            Configuration.TtsPiperSentenceSilence = 0.2d;
            changed = true;
        }

        if (Configuration.Version < 8)
        {
            Configuration.TtsPiperPitchShiftSemitones = 0.0d;
            changed = true;
        }

        var clampedPiperLengthScale = Math.Clamp(Configuration.TtsPiperLengthScale, 0.5d, 2.0d);
        if (Math.Abs(Configuration.TtsPiperLengthScale - clampedPiperLengthScale) > 0.0001d)
        {
            Configuration.TtsPiperLengthScale = clampedPiperLengthScale;
            changed = true;
        }

        var clampedPiperSentenceSilence = Math.Clamp(Configuration.TtsPiperSentenceSilence, 0.0d, 5.0d);
        if (Math.Abs(Configuration.TtsPiperSentenceSilence - clampedPiperSentenceSilence) > 0.0001d)
        {
            Configuration.TtsPiperSentenceSilence = clampedPiperSentenceSilence;
            changed = true;
        }

        var clampedPiperPitchShift = Math.Clamp(Configuration.TtsPiperPitchShiftSemitones, -12.0d, 12.0d);
        if (Math.Abs(Configuration.TtsPiperPitchShiftSemitones - clampedPiperPitchShift) > 0.0001d)
        {
            Configuration.TtsPiperPitchShiftSemitones = clampedPiperPitchShift;
            changed = true;
        }

        if (Configuration.Version != Configuration.CurrentVersion)
        {
            Configuration.Version = Configuration.CurrentVersion;
            changed = true;
        }

        if (changed)
            Configuration.Save();
    }

    private void SetupDtrBar()
    {
        dtrEntry = DtrBar.Get(PluginInfo.DisplayName);
        dtrEntry.OnClick = _ => { Configuration.PluginEnabled = !Configuration.PluginEnabled; Configuration.Save(); UpdateDtrBar(); };
    }

    public void UpdateDtrBar()
    {
        if (dtrEntry == null) return; dtrEntry.Shown = Configuration.DtrBarEnabled; if (!Configuration.DtrBarEnabled) return; var g = Configuration.PluginEnabled ? Configuration.DtrIconEnabled : Configuration.DtrIconDisabled; var s = Configuration.PluginEnabled ? "On" : "Off"; dtrEntry.Text = Configuration.DtrBarMode switch { 1 => new SeString(new TextPayload($"{g} DH")), 2 => new SeString(new TextPayload(g)), _ => new SeString(new TextPayload("DH: " + s)), }; var mode = Configuration.CommentaryMode == CommentaryMode.ReadingRoegadyn ? "Reading Roegadyn" : "Dheacon"; dtrEntry.Tooltip = new SeString(new TextPayload($"{PluginInfo.DisplayName} {s}. Mode: {mode}. Click to toggle."));
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        AetheryteTriggerService.Update();
        CommentaryTriggerService.Update();
        UpdateDtrBar();
    }

    private void OnTriggeredAreaTransition(uint fromTerritory, uint toTerritory)
    {
        if (Configuration.CommentaryMode == CommentaryMode.Dheacon)
        {
            AudioPlaybackService.PlayAlert();
            PrintStatus($"Alert triggered for territory change {fromTerritory} -> {toTerritory}.");
            return;
        }

        CommentaryTriggerService.TriggerTerritoryChange(fromTerritory, toTerritory);
        PrintStatus(CommentaryTriggerService.LastDecision);
    }

    private void SetMode(string requestedMode)
    {
        if (requestedMode.Equals("dheacon", StringComparison.OrdinalIgnoreCase))
        {
            Configuration.CommentaryMode = CommentaryMode.Dheacon;
            Configuration.Save();
            UpdateDtrBar();
            PrintStatus("Mode set to Dheacon.");
            return;
        }

        if (requestedMode.Equals("roe", StringComparison.OrdinalIgnoreCase) ||
            requestedMode.Equals("roegadyn", StringComparison.OrdinalIgnoreCase) ||
            requestedMode.Equals("reading", StringComparison.OrdinalIgnoreCase) ||
            requestedMode.Equals("readingroegadyn", StringComparison.OrdinalIgnoreCase))
        {
            Configuration.CommentaryMode = CommentaryMode.ReadingRoegadyn;
            Configuration.Save();
            UpdateDtrBar();
            PrintStatus("Mode set to Reading Roegadyn.");
            return;
        }

        PrintStatus($"Unknown mode '{requestedMode}'. Use: {PluginInfo.Command} mode dheacon or {PluginInfo.Command} mode roe.");
    }

    private void PrintVoiceDiagnostics()
    {
        SpeechCacheService.RefreshInstalledVoices();
        PrintVoiceDiagnostics(TtsBackend.ModernWindows);
        PrintVoiceDiagnostics(TtsBackend.LegacySapi);
        PrintVoiceDiagnostics(TtsBackend.PiperLocal);
        PrintStatus($"Piper catalog entries: {PiperVoiceCatalogService.GetCatalogEntries().Count}. {PiperVoiceCatalogService.LastStatus}");
        PrintStatus("Piper runtime: " + PiperVoiceCatalogService.RefreshRuntimeStatus());
        PrintStatus($"Piper settings: speed {Configuration.TtsPiperLengthScale:F2}, sentence pause {Configuration.TtsPiperSentenceSilence:F2}s, pitch {FormatPiperSemitones(Configuration.TtsPiperPitchShiftSemitones)} st, gain {Configuration.TtsOutputGainPercent}%.");
        PrintStatus($"Last Piper pitch shift: {SpeechCacheService.LastPiperPitchShiftStatus} Applied: {SpeechCacheService.LastPiperPitchShiftApplied}. Semitones: {FormatPiperSemitones(SpeechCacheService.LastPiperPitchShiftSemitones)} st.");

        if (!string.IsNullOrWhiteSpace(SpeechCacheService.LastError))
            PrintStatus("Speech warning: " + SpeechCacheService.LastError);
    }

    private void PrintVoiceDiagnostics(TtsBackend backend)
    {
        var voices = SpeechCacheService.GetInstalledVoices(backend);
        PrintStatus($"{backend} voices detected: {voices.Count}.");

        foreach (var voice in voices.Take(80))
            PrintStatus($"- {voice.Label}");

        if (voices.Count > 80)
            PrintStatus($"...and {voices.Count - 80} more {backend} voice(s).");
    }

    private void PrintPiperPreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            text = "Reading Roegadyn reports FFXIV BGM 85 near Limsa Lominsa for Aelwyn Frost.";

        var preview = SpeechCacheService.PreviewPiperText(text);
        PrintStatus("Piper preview original: " + preview.Original);
        PrintStatus("Piper preview adapted: " + preview.Adapted);
    }

    private static string FormatPiperSemitones(double semitones)
        => semitones.ToString("+0.0;-0.0;0.0");

    private async Task RefreshPiperCatalogToChatAsync()
    {
        try
        {
            await PiperVoiceCatalogService.RefreshCatalogAsync(CancellationToken.None).ConfigureAwait(false);
            PrintStatus(PiperVoiceCatalogService.LastStatus);
        }
        catch (Exception ex)
        {
            PrintStatus("Piper catalog refresh failed: " + ex.Message);
        }
    }
}
