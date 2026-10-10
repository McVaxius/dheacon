using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dheacon.Services;
using Dheacon.Ui;
using AethertekUI;
using AethertekUI.Dalamud;

namespace Dheacon.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly AethertekUI.Dalamud.MaterialSupportLog supportLog = new();
    private readonly MaterialWindowMotion windowMotion = new();
    private readonly AethertekUI.MaterialWindowOpacity windowOpacity = new();
    private static readonly string[] DtrModes = { "Text only", "Icon + text", "Icon only" };
    private static readonly string[] TtsBackendLabels = { "Modern Windows", "Legacy SAPI", "Piper local" };
    private static readonly string[] PiperInstalledFilters = { "All", "Installed", "Not installed" };

    private readonly Plugin plugin;
    private bool piperCatalogAutoRefreshStarted;
    private bool piperRecommendedAutoSetupStarted;
    private volatile bool piperPreviewSpeechInProgress;
    private int piperInstalledFilter;
    private string piperLanguageFilter = "All";
    private string piperGenderFilter = "All";
    private string piperQualityFilter = "All";
    private string piperSourceFilter = "All";
    private string piperSearchText = string.Empty;
    private string selectedPiperCatalogId = string.Empty;
    private string piperPreviewText = "Reading Roegadyn reports FFXIV BGM 85 near Limsa Lominsa for Aelwyn Frost.";
    private string presetImportText = string.Empty;
    private string presetRenameText = string.Empty;
    private string presetRenameTargetId = string.Empty;
    private string kranglerPresetSearchText = string.Empty;
    private int quickSetupStep;
    private bool quickSetupTabRequested;
    private bool quickSetupTestAttempted;
    private bool? quickSetupEnableChoice;
    private bool quickSetupFinishedThisSession;
    private bool quickSetupPiperPreparationPending;
    private volatile bool quickSetupPiperPreparationInProgress;
    private int quickSetupSpeechSelectionVersion;
    private int quickSetupPendingPiperVersion;
    private string quickSetupSpeechStatus = string.Empty;
    private string quickSetupTestStatus = string.Empty;

    public ConfigWindow(Plugin plugin) : base($"{PluginInfo.DisplayName} Settings##Config")
    {
        this.plugin = plugin;
        Flags |= ImGuiWindowFlags.HorizontalScrollbar;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(720f, 560f), MaximumSize = new Vector2(1500f, 1300f) };
    }

    public void Dispose() { }

    internal void OpenQuickSetup()
    {
        quickSetupTabRequested = true;
        IsOpen = true;
    }

    public override void PreDraw() => windowMotion.Prepare(this, reducedMotion: false, roundedCorners: true);

    public override void PostDraw()
    {
        windowMotion.Restore(this);
        plugin.Appearance.ApplyWindowOpacity(windowOpacity, WindowName);
    }

    public override void Draw()
    {
        windowMotion.DrawChrome();
        UiGui.Title(PluginInfo.DisplayName + " Settings", UiText.T("Dheacon Settings"));
        using var tabHeight = MaterialText.PushLineHeight(new[] { "Quick Setup", "General", "Speech", "Piper Voices", "Window appearance", "Diagnostics" }.Select(UiText.T).ToArray());
        var appearanceRoot = ImGui.GetID("");
        var tabsOpen = ImGui.BeginTabBar("DheaconSettingsTabs", ImGuiTabBarFlags.FittingPolicyScroll);
        tabHeight.Dispose();
        if (!tabsOpen)
            return;

        try
        {

        var quickSetupFlags = quickSetupTabRequested ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        var quickSetupOpen = UiGui.BeginTabItem("Quick Setup", quickSetupFlags);
        quickSetupTabRequested = false;
        if (quickSetupOpen)
        {
            try { DrawQuickSetupTab(plugin.Configuration); } finally { ImGui.EndTabItem(); }
        }

        if (UiGui.BeginTabItem("General"))
        {
            try { DrawGeneralTab(plugin.Configuration); } finally { ImGui.EndTabItem(); }
        }

        if (UiGui.BeginTabItem("Speech"))
        {
            try { DrawSpeechTab(plugin.Configuration); } finally { ImGui.EndTabItem(); }
        }

        if (UiGui.BeginTabItem("Piper Voices"))
        {
            try { DrawPiperVoicesTab(plugin.Configuration); } finally { ImGui.EndTabItem(); }
        }

        if (UiGui.BeginTabItem("Window appearance", ImGuiTabItemFlags.NoPushId))
        {
            ImGuiP.PushOverrideID(appearanceRoot);
            try { plugin.Appearance.DrawWindowAppearance(); } finally { ImGui.PopID(); ImGui.EndTabItem(); }
        }
        if (UiGui.BeginTabItem("Diagnostics"))
        {
            try { DrawDiagnosticsTab(plugin.Configuration); } finally { ImGui.EndTabItem(); }
        }

        }
        finally { ImGui.EndTabBar(); }
    }

    private void DrawQuickSetupTab(Configuration cfg)
    {
        UiGui.TextUnformatted("Quick Setup");
        UiGui.TextWrapped("Choose how Dheacon should sound, prepare local speech if needed, test the result, and decide whether to enable automatic triggers.");

        if (quickSetupFinishedThisSession)
        {
            ImGui.Separator();
            UiGui.TextWrapped(quickSetupTestStatus);
            DrawWrappedStatus(
                UiText.Interpolated($"Saved: {UiText.T(FormatQuickSetupMode())} | {plugin.PresetService.ActivePreset.Name} | {FormatQuickSetupSpeechBackend(cfg)} | {UiText.T(cfg.PluginEnabled ? "enabled" : "disabled")}"),
                "The configuration saved when Quick Setup finished.");

            if (UiGui.Button("Run Quick Setup again"))
            {
                quickSetupFinishedThisSession = false;
                quickSetupStep = 0;
                quickSetupTestAttempted = false;
                quickSetupEnableChoice = null;
                quickSetupTestStatus = string.Empty;
            }
            TooltipLastItem("Starts the three setup steps again without resetting unrelated settings.");

            ImGui.SameLine();
            if (UiGui.Button("Close settings"))
                IsOpen = false;

            return;
        }

        ImGui.Separator();
        UiGui.TextDisabled(UiText.Interpolated($"Step {quickSetupStep + 1} of 3"));

        switch (quickSetupStep)
        {
            case 0:
                DrawQuickSetupModeStep();
                break;
            case 1:
                DrawQuickSetupSpeechStep(cfg);
                break;
            default:
                DrawQuickSetupReviewStep(cfg);
                break;
        }
    }

    private void DrawQuickSetupModeStep()
    {
        UiGui.TextUnformatted("1. Choose an operating mode");
        ImGui.Spacing();

        var active = plugin.PresetService.ActivePreset;
        var classicSelected = active.Mode == CommentaryMode.Dheacon;
        if (UiGui.RadioButton("Classic transition alerts", classicSelected))
            SelectPreset(DheaconPresetIds.Dheacon, printStatus: false);
        TooltipLastItem("Plays the packaged transition alert for eligible area changes and does not speak commentary.");
        UiGui.TextWrapped("Classic mode preserves Dheacon's original transition-alert sound, including the existing teleport and Return suppression option.");

        ImGui.Spacing();
        var spokenSelected = plugin.PresetService.ActivePreset.Mode == CommentaryMode.ReadingRoegadyn;
        if (UiGui.RadioButton("Spoken commentary", spokenSelected) && !spokenSelected)
            SelectPreset(DheaconPresetIds.ReadingRoegadyn, printStatus: false);
        TooltipLastItem("Uses the selected preset to speak commentary for enabled game events.");
        UiGui.TextWrapped("Spoken mode uses preset-driven lines for login, travel, combat, idle, BGM, and other enabled events, subject to trigger chance and cooldowns.");

        active = plugin.PresetService.ActivePreset;
        if (active.Mode == CommentaryMode.ReadingRoegadyn)
        {
            var spokenPresets = plugin.PresetService.Presets
                .Where(preset => preset.Mode == CommentaryMode.ReadingRoegadyn)
                .ToList();
            var presetIndex = Math.Max(0, spokenPresets.FindIndex(preset =>
                string.Equals(preset.Id, active.Id, StringComparison.OrdinalIgnoreCase)));
            var labels = spokenPresets.Select(preset => preset.Name).ToArray();

            if (labels.Length > 0)
            {
                ImGui.SetNextItemWidth(Math.Min(420f, ImGui.GetContentRegionAvail().X));
                if (UiGui.Combo("Commentary preset", ref presetIndex, labels, labels.Length))
                    SelectPreset(spokenPresets[presetIndex].Id, printStatus: false);
                TooltipLastItem("Selects one of the existing spoken-commentary presets.");
            }
        }

        active = plugin.PresetService.ActivePreset;
        DrawWrappedStatus(UiText.Interpolated($"Selected preset: {active.Name}"), "The preset Quick Setup will keep active.");
        if (!string.IsNullOrWhiteSpace(active.Description))
            DrawWrappedStatus(active.Description, "Description supplied by the selected preset.", translate: false);

        if (active.ImaginaryFren?.Enabled == true)
        {
            UiGui.TextWrapped(UiText.Interpolated($"Optional Imaginary Fren: this preset can ask Krangler to spawn the local-only follower '{active.ImaginaryFren.Name}' when Dheacon is enabled. Krangler is optional; commentary still works without it."));
        }
        else
        {
            UiGui.TextWrapped("Optional Imaginary Fren integration is off for this preset. It can be configured later in General settings and requires Krangler.");
        }

        ImGui.Spacing();
        if (UiGui.Button("Next: Speech"))
        {
            quickSetupStep = 1;
            quickSetupTestAttempted = false;
            quickSetupEnableChoice = null;
            quickSetupTestStatus = string.Empty;
        }
    }

    private void DrawQuickSetupSpeechStep(Configuration cfg)
    {
        TryStartPendingQuickSetupPiperPreparation(cfg);

        UiGui.TextUnformatted("2. Choose local speech");
        UiGui.TextWrapped(plugin.PresetService.ActivePreset.Mode == CommentaryMode.Dheacon
            ? "Classic mode does not speak, but this choice is saved for any spoken preset you select later."
            : "Speech is generated on your PC and cached as WAV files for reuse.");
        ImGui.Spacing();

        var piperSelected = cfg.TtsBackend == TtsBackend.PiperLocal;
        if (UiGui.RadioButton("Recommended local Piper", piperSelected) && !piperSelected)
            RequestQuickSetupPiperPreparation(cfg);
        TooltipLastItem("Uses the managed local Piper runtime and recommended English Arctic voice.");
        UiGui.TextWrapped("Piper provides consistent local speech. Its runtime and selected voice are one-time downloads; generated speech remains cached locally.");

        var piperReady = IsQuickSetupPiperReady(cfg);
        if (cfg.TtsBackend == TtsBackend.PiperLocal)
        {
            DrawWrappedStatus(plugin.PiperVoiceCatalogService.RefreshRuntimeStatus(save: false), "Current local Piper runtime status.");
            DrawWrappedStatus(plugin.PiperVoiceCatalogService.LastStatus, "Current Piper download, install, or selection status.");
            if (!string.IsNullOrWhiteSpace(plugin.PiperVoiceCatalogService.LastError))
                DrawWrappedStatus(UiText.T("Piper warning: ") + plugin.PiperVoiceCatalogService.LastError, "The last Piper preparation error. You can retry or use Windows default speech.");
            if (!string.IsNullOrWhiteSpace(quickSetupSpeechStatus))
                DrawWrappedStatus(quickSetupSpeechStatus, "Quick Setup speech preparation status.");

            if (plugin.PiperVoiceCatalogService.IsBusy && plugin.PiperVoiceCatalogService.OperationProgress >= 0d)
                ImGui.ProgressBar((float)plugin.PiperVoiceCatalogService.OperationProgress, new Vector2(-1f, 0f));
            else if (plugin.PiperVoiceCatalogService.IsBusy || quickSetupPiperPreparationInProgress || quickSetupPiperPreparationPending)
                UiGui.TextDisabled("Piper preparation is in progress...");

            if (!piperReady)
            {
                ImGui.BeginDisabled(plugin.PiperVoiceCatalogService.IsBusy || quickSetupPiperPreparationInProgress);
                if (UiGui.Button(string.IsNullOrWhiteSpace(plugin.PiperVoiceCatalogService.LastError) ? "Prepare Piper" : "Retry Piper"))
                    RequestQuickSetupPiperPreparation(cfg);
                ImGui.EndDisabled();
                TooltipLastItem("Downloads or repairs the managed Piper runtime and recommended voice.");

                ImGui.SameLine();
                if (UiGui.Button("Use Windows default instead"))
                    SelectQuickSetupWindowsDefault(cfg, "Selected Windows default speech instead of Piper.");
                TooltipLastItem("Stops waiting for Piper and selects the built-in Windows speech backend with its default voice.");
            }
            else
            {
                DrawDisabledStatus("Piper is ready.", "The recommended voice and a usable Piper runtime were found.");
            }
        }

        ImGui.Spacing();
        var windowsSelected = IsQuickSetupWindowsDefaultSelected(cfg);
        if (UiGui.RadioButton("Windows default speech", windowsSelected) && !windowsSelected)
            SelectQuickSetupWindowsDefault(cfg, "Selected Windows default speech.");
        TooltipLastItem("Uses the modern Windows speech backend and the current Windows default voice.");
        UiGui.TextWrapped("Windows default speech needs no Dheacon-managed download and remains available as the fallback if Piper setup fails.");
        if (windowsSelected)
            DrawWrappedStatus(UiText.T("Selected voice: ") + UiText.VoiceLabel(plugin.SpeechCacheService.GetSelectedVoiceLabel()), "The Windows voice currently selected for generated speech.");
        if (windowsSelected && !string.IsNullOrWhiteSpace(quickSetupSpeechStatus))
            DrawWrappedStatus(quickSetupSpeechStatus, "Quick Setup speech selection or fallback status.");

        ImGui.Spacing();
        if (UiGui.Button("Back"))
            quickSetupStep = 0;

        ImGui.SameLine();
        var speechReady = piperReady || windowsSelected;
        ImGui.BeginDisabled(!speechReady);
        if (UiGui.Button("Next: Test and finish"))
        {
            quickSetupStep = 2;
            quickSetupTestAttempted = false;
            quickSetupEnableChoice = null;
            quickSetupTestStatus = string.Empty;
        }
        ImGui.EndDisabled();
        TooltipLastItem(speechReady ? "Reviews and tests the selected configuration." : "Prepare Piper or choose Windows default speech before continuing.");
    }

    private void DrawQuickSetupReviewStep(Configuration cfg)
    {
        UiGui.TextUnformatted("3. Test and finish");
        var active = plugin.PresetService.ActivePreset;
        DrawWrappedStatus(UiText.T("Mode: ") + UiText.T(FormatQuickSetupMode()), "Selected operating mode.");
        DrawWrappedStatus(UiText.T("Preset: ") + active.Name, "Selected preset.");
        if (!string.IsNullOrWhiteSpace(active.Description))
            DrawWrappedStatus(active.Description, "Description supplied by the selected preset.", translate: false);
        DrawWrappedStatus(UiText.T("Speech: ") + FormatQuickSetupSpeechBackend(cfg), "Selected speech backend and voice.");
        DrawWrappedStatus(
            active.ImaginaryFren?.Enabled == true
                ? $"Imaginary Fren: optional '{active.ImaginaryFren.Name}' follower through Krangler"
                : "Imaginary Fren: off for this preset",
            "Krangler integration is optional and does not affect alert or commentary playback.");

        ImGui.Separator();
        if (UiGui.Button(active.Mode == CommentaryMode.Dheacon ? "Test transition alert" : "Test spoken commentary"))
            RunQuickSetupTest(active);
        TooltipLastItem("Makes one audio test attempt without enabling automatic triggers.");

        if (!string.IsNullOrWhiteSpace(quickSetupTestStatus))
            DrawWrappedStatus(quickSetupTestStatus, "Result of the required Quick Setup test attempt.");
        if (quickSetupTestAttempted && active.Mode == CommentaryMode.ReadingRoegadyn)
        {
            DrawWrappedStatus(UiText.T("Speech queue: ") + UiText.T(plugin.SpeechQueueService.LastStatus), "Live status for the queued test line.");
            if (!string.IsNullOrWhiteSpace(plugin.SpeechQueueService.LastError))
                DrawWrappedStatus(UiText.T("Speech warning: ") + plugin.SpeechQueueService.LastError, "The latest speech queue error; the attempt still counts so you can change speech settings and retry.");
        }

        ImGui.Separator();
        UiGui.TextWrapped("Choose the plugin state to save. This is explicit: finishing will not enable Dheacon unless you select the enabled option.");
        if (UiGui.RadioButton("Enable Dheacon now", quickSetupEnableChoice == true))
            quickSetupEnableChoice = true;
        TooltipLastItem("Enables automatic transition alerts or commentary after setup finishes.");
        if (UiGui.RadioButton("Leave Dheacon disabled", quickSetupEnableChoice == false))
            quickSetupEnableChoice = false;
        TooltipLastItem("Saves setup but leaves automatic triggers disabled until you enable the plugin later.");

        ImGui.Spacing();
        if (UiGui.Button("Back"))
            quickSetupStep = 1;

        ImGui.SameLine();
        ImGui.BeginDisabled(!quickSetupTestAttempted || !quickSetupEnableChoice.HasValue);
        if (UiGui.Button("Finish Quick Setup"))
        {
            cfg.PluginEnabled = quickSetupEnableChoice == true;
            cfg.SetupWizardCompleted = true;
            cfg.Save();
            plugin.UpdateDtrBar();
            plugin.KranglerImaginaryFrenIpcClient.ReconcileNow();
            quickSetupFinishedThisSession = true;
            quickSetupTestStatus = $"Quick Setup complete. Dheacon is {(cfg.PluginEnabled ? "enabled" : "disabled")}.";
            plugin.PrintStatus(quickSetupTestStatus);
        }
        ImGui.EndDisabled();
        TooltipLastItem(!quickSetupTestAttempted
            ? "Run the audio test before finishing."
            : !quickSetupEnableChoice.HasValue
                ? "Choose whether Dheacon should be enabled."
                : "Saves setup and prevents future automatic opening.");
    }

    private void RunQuickSetupTest(DheaconPreset active)
    {
        quickSetupTestAttempted = true;

        try
        {
            if (active.Mode == CommentaryMode.Dheacon)
            {
                plugin.AudioPlaybackService.PlayAlert();
                quickSetupTestStatus = "Transition-alert test attempted. If the configured WAV was unavailable, Dheacon used the Windows exclamation sound.";
                return;
            }

            var text = plugin.CommentaryLinePackService.GetLine(CommentaryCategory.ManualTest);
            var queued = plugin.SpeechQueueService.TryEnqueue(new CommentaryRequest(CommentaryCategory.ManualTest, text, "Quick Setup test"));
            quickSetupTestStatus = queued
                ? "Spoken-commentary test queued. Watch the live speech status below; cached audio will be reused on future matches."
                : "Spoken-commentary test was attempted, but the speech queue did not accept the line. Review the status below and retry.";
        }
        catch (Exception ex)
        {
            quickSetupTestStatus = "Audio test failed: " + ex.Message;
        }
    }

    private void RequestQuickSetupPiperPreparation(Configuration cfg)
    {
        var requestVersion = Interlocked.Increment(ref quickSetupSpeechSelectionVersion);
        quickSetupPendingPiperVersion = requestVersion;
        quickSetupPiperPreparationPending = true;
        quickSetupSpeechStatus = "Preparing the managed Piper runtime and recommended English Arctic voice...";
        cfg.TtsBackend = TtsBackend.PiperLocal;
        cfg.TtsPiperVoiceId = PiperVoiceCatalogService.RecommendedVoiceCatalogId;
        cfg.Save();
        TryStartPendingQuickSetupPiperPreparation(cfg);
    }

    private void TryStartPendingQuickSetupPiperPreparation(Configuration cfg)
    {
        if (!quickSetupPiperPreparationPending ||
            quickSetupPiperPreparationInProgress ||
            plugin.PiperVoiceCatalogService.IsBusy)
            return;

        quickSetupPiperPreparationPending = false;
        quickSetupPiperPreparationInProgress = true;
        var requestVersion = quickSetupPendingPiperVersion;
        _ = Task.Run(async () =>
        {
            try
            {
                await plugin.PiperVoiceCatalogService
                    .EnsureRecommendedVoiceInstalledAsync(switchBackendWhenReady: true, CancellationToken.None)
                    .ConfigureAwait(false);

                if (requestVersion != Volatile.Read(ref quickSetupSpeechSelectionVersion))
                {
                    ApplyQuickSetupWindowsDefault(cfg);
                    return;
                }

                if (IsQuickSetupPiperReady(cfg))
                {
                    quickSetupSpeechStatus = "Piper is ready with the recommended English Arctic voice.";
                }
                else
                {
                    ApplyQuickSetupWindowsDefault(cfg);
                    quickSetupSpeechStatus = "Piper could not be prepared, so Quick Setup selected Windows default speech. You can retry Piper here.";
                }
            }
            catch (Exception ex)
            {
                if (requestVersion == Volatile.Read(ref quickSetupSpeechSelectionVersion))
                {
                    ApplyQuickSetupWindowsDefault(cfg);
                    quickSetupSpeechStatus = "Piper setup failed, so Quick Setup selected Windows default speech: " + ex.Message;
                }
            }
            finally
            {
                quickSetupPiperPreparationInProgress = false;
            }
        });
    }

    private void SelectQuickSetupWindowsDefault(Configuration cfg, string status)
    {
        Interlocked.Increment(ref quickSetupSpeechSelectionVersion);
        quickSetupPiperPreparationPending = false;
        ApplyQuickSetupWindowsDefault(cfg);
        quickSetupSpeechStatus = status;
    }

    private static void ApplyQuickSetupWindowsDefault(Configuration cfg)
    {
        cfg.TtsBackend = TtsBackend.ModernWindows;
        cfg.TtsModernVoiceId = string.Empty;
        cfg.TtsVoiceName = string.Empty;
        cfg.Save();
    }

    private bool IsQuickSetupPiperReady(Configuration cfg)
        => cfg.TtsBackend == TtsBackend.PiperLocal &&
           plugin.PiperVoiceCatalogService.FindExactInstalledVoice(PiperVoiceCatalogService.RecommendedVoiceCatalogId) != null &&
           plugin.PiperVoiceCatalogService.ResolveRuntimePath() != null;

    private static bool IsQuickSetupWindowsDefaultSelected(Configuration cfg)
        => cfg.TtsBackend == TtsBackend.ModernWindows &&
           string.IsNullOrWhiteSpace(cfg.TtsModernVoiceId) &&
           string.IsNullOrWhiteSpace(cfg.TtsVoiceName);

    private string FormatQuickSetupMode()
        => plugin.PresetService.ActivePreset.Mode == CommentaryMode.Dheacon
            ? "Classic transition alerts"
            : "Spoken commentary";

    private string FormatQuickSetupSpeechBackend(Configuration cfg)
        => cfg.TtsBackend switch
        {
            TtsBackend.PiperLocal => UiText.T("Local Piper - ") + UiText.VoiceLabel(plugin.SpeechCacheService.GetSelectedVoiceLabel()),
            TtsBackend.ModernWindows => UiText.T("Windows default - ") + UiText.VoiceLabel(plugin.SpeechCacheService.GetSelectedVoiceLabel()),
            _ => UiText.T("Legacy SAPI - ") + UiText.VoiceLabel(plugin.SpeechCacheService.GetSelectedVoiceLabel()),
        };

    private void DrawGeneralTab(Configuration cfg)
    {
        var enabled = cfg.PluginEnabled;
        if (UiGui.Checkbox("Plugin enabled", ref enabled))
        {
            cfg.PluginEnabled = enabled;
            cfg.Save();
            plugin.UpdateDtrBar();
            plugin.KranglerImaginaryFrenIpcClient.ReconcileNow();
        }
        TooltipLastItem("Turns all Dheacon/Reading Roegadyn triggers on or off immediately.");

        ImGui.Separator();
        DrawPresetManager();

        ImGui.Separator();
        DrawImaginaryFrenPanel();

        ImGui.Separator();
        UiGui.TextUnformatted("DTR");

        var dtr = cfg.DtrBarEnabled;
        if (UiGui.Checkbox("Show DTR bar entry", ref dtr))
        {
            cfg.DtrBarEnabled = dtr;
            cfg.Save();
            plugin.UpdateDtrBar();
        }
        TooltipLastItem("Shows or hides the clickable status entry in the Dalamud DTR bar.");

        var dtrMode = cfg.DtrBarMode;
        if (UiGui.Combo("DTR mode", ref dtrMode, DtrModes, DtrModes.Length))
        {
            cfg.DtrBarMode = dtrMode;
            cfg.Save();
            plugin.UpdateDtrBar();
        }
        TooltipLastItem("Changes whether the DTR bar shows text, icon plus text, or only the icon.");

        var onIcon = cfg.DtrIconEnabled;
        if (UiGui.InputText("DTR enabled glyph", ref onIcon, 8))
        {
            cfg.DtrIconEnabled = onIcon.Length <= 3 ? onIcon : onIcon[..3];
            cfg.Save();
            plugin.UpdateDtrBar();
        }
        TooltipLastItem("Sets the DTR glyph shown while the plugin is enabled; very long input is trimmed.");

        var offIcon = cfg.DtrIconDisabled;
        if (UiGui.InputText("DTR disabled glyph", ref offIcon, 8))
        {
            cfg.DtrIconDisabled = offIcon.Length <= 3 ? offIcon : offIcon[..3];
            cfg.Save();
            plugin.UpdateDtrBar();
        }
        TooltipLastItem("Sets the DTR glyph shown while the plugin is disabled; very long input is trimmed.");

        ImGui.Separator();
        UiGui.TextUnformatted("Mini Window");

        var miniAutoOpen = cfg.MiniAutoOpenOnSpeech;
        if (UiGui.Checkbox("Open mini window when speech starts", ref miniAutoOpen))
        {
            cfg.MiniAutoOpenOnSpeech = miniAutoOpen;
            cfg.Save();
        }
        TooltipLastItem("When enabled, the mini window opens on the next spoken line and stays open.");

        ImGui.Separator();
        if (plugin.PresetService.ActivePreset.Mode == CommentaryMode.Dheacon)
            DrawDheaconSettings(cfg);
        else
            DrawReadingRoegadynGeneralSettings(cfg);
    }

    private void DrawSpeechTab(Configuration cfg)
    {
        if (UiGui.Button("Test speech"))
        {
            var queued = plugin.CommentaryTriggerService.SpeakManual();
            plugin.PrintStatus(queued ? "Speech queued." : plugin.CommentaryTriggerService.LastDecision);
        }
        TooltipLastItem("Queues a Reading Roegadyn test line using the selected speech backend and current voice.");

        ImGui.SameLine();
        if (UiGui.Button("Clear cache"))
            ClearCacheToChat();
        TooltipLastItem("Deletes generated speech WAV files for all backends; future speech regenerates them.");

        ImGui.SameLine();
        if (UiGui.Button("Clear Piper WAV cache"))
            ClearPiperCacheToChat();
        TooltipLastItem("Deletes only cached Piper WAV files; Piper output regenerates using current speed, pause, pitch, gain, and adapter settings.");

        ImGui.Separator();
        DrawBackendSelector(cfg);
        DrawWrappedStatus(UiText.T("Selected voice: ") + UiText.VoiceLabel(plugin.SpeechCacheService.GetSelectedVoiceLabel()), "Current voice that will be used for generated speech.");
        DrawVoiceActions();
        DrawVoiceSelector(cfg);

        ImGui.Separator();
        DrawSpeechControls(cfg);

        ImGui.Separator();
        DrawSpeechCacheSettings(cfg);

        ImGui.Separator();
        DrawTextAdapterSettings(cfg);
    }

    private void DrawPiperVoicesTab(Configuration cfg)
    {
        if (!piperCatalogAutoRefreshStarted && plugin.PiperVoiceCatalogService.IsCatalogStale(TimeSpan.FromHours(24)))
        {
            piperCatalogAutoRefreshStarted = true;
            StartPiperCatalogRefresh();
        }

        var entries = plugin.PiperVoiceCatalogService.GetCatalogEntries();
        EnsurePiperRecommendedVoiceIfNeeded(cfg);
        EnsureSelectedPiperEntry(entries, cfg);

        DrawPiperSetupStrip(cfg);
        ImGui.Separator();

        ImGui.SetNextItemWidth(Math.Min(360f, ImGui.GetContentRegionAvail().X));
        UiGui.InputText("Search", ref piperSearchText, 160);
        TooltipLastItem("Filters the Piper catalog by voice key, language, gender, quality, source, or catalog id.");
        DrawPiperFilters(entries);

        var filtered = SortPiperEntries(FilterPiperEntries(entries), cfg).ToList();
        DrawDisabledStatus(UiText.Interpolated($"Showing {filtered.Count} of {entries.Count} catalog entries."), "Current Piper catalog count after search and filters.");
        DrawPiperSelectedActionBar(entries, cfg);
        DrawPiperCatalogTable(filtered, cfg);
        DrawPiperSelectedVoicePanel(entries, cfg);
    }

    private void DrawDiagnosticsTab(Configuration cfg)
    {
        supportLog.Draw(Plugin.PluginInterface, key => UiText.T(key),
            plugin.PiperVoiceCatalogService.OpenFolder, ex => Plugin.Log.Error(ex, "Dalamud log export failed."), Plugin.CommandManager);
        if (UiGui.Button("Refresh voices"))
        {
            plugin.SpeechCacheService.RefreshInstalledVoices();
            var modernCount = plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.ModernWindows).Count;
            var legacyCount = plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.LegacySapi).Count;
            var piperCount = plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.PiperLocal).Count;
            plugin.PrintStatus($"Detected {modernCount} Modern Windows voice(s), {legacyCount} Legacy SAPI voice(s), {piperCount} Piper voice(s).");
        }
        TooltipLastItem("Refreshes detected speech voices and reports counts to chat.");

        ImGui.SameLine();
        if (UiGui.Button("Status to chat"))
            plugin.PrintStatus(plugin.PresetService.ActivePreset.Mode == CommentaryMode.Dheacon ? plugin.AetheryteTriggerService.LastDecision : plugin.CommentaryTriggerService.LastDecision);
        TooltipLastItem("Prints the current mode decision/status message to chat.");

        ImGui.Separator();
        UiGui.Text(UiText.Interpolated($"Modern Windows voices: {plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.ModernWindows).Count}"));
        TooltipLastItem("Number of detected Modern Windows speech voices.");
        UiGui.Text(UiText.Interpolated($"Legacy SAPI voices: {plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.LegacySapi).Count}"));
        TooltipLastItem("Number of detected Legacy SAPI speech voices.");
        UiGui.Text(UiText.Interpolated($"Piper voices: {plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.PiperLocal).Count}"));
        TooltipLastItem("Number of installed managed Piper voices.");
        DrawWrappedStatus(UiText.T("Piper runtime: ") + UiText.T(plugin.PiperVoiceCatalogService.RefreshRuntimeStatus(save: false)), "Current Piper runtime discovery status.");
        DrawWrappedStatus(UiText.T("Piper catalog: ") + UiText.T(plugin.PiperVoiceCatalogService.LastStatus), "Last Piper catalog or setup status.");
        if (!string.IsNullOrWhiteSpace(plugin.PiperVoiceCatalogService.LastError))
            DrawWrappedStatus(UiText.T("Piper warning: ") + plugin.PiperVoiceCatalogService.LastError, "Last Piper catalog, runtime, or install warning.");
        DrawWrappedStatus(
            UiText.Interpolated($"Piper settings: speed {cfg.TtsPiperLengthScale:F2}, sentence pause {cfg.TtsPiperSentenceSilence:F2}s, pitch {FormatPiperSemitones(cfg.TtsPiperPitchShiftSemitones)} st, gain {cfg.TtsOutputGainPercent}%"),
            "Piper synthesis and post-processing settings that affect future Piper WAV cache entries.");
        DrawWrappedStatus(
            UiText.Interpolated($"Last Piper pitch shift: {UiText.T(plugin.SpeechCacheService.LastPiperPitchShiftStatus)} Applied: {plugin.SpeechCacheService.LastPiperPitchShiftApplied}. Semitones: {FormatPiperSemitones(plugin.SpeechCacheService.LastPiperPitchShiftSemitones)} st."),
            "Most recent Piper pitch-shift processing result.");

        ImGui.Separator();
        DrawWrappedStatus(UiText.T("Adapter service: ") + UiText.T(plugin.SpokenTextAdapterService.LastStatus), "Last spoken text adapter load status.");
        if (!string.IsNullOrWhiteSpace(plugin.SpokenTextAdapterService.LastError))
            DrawWrappedStatus(UiText.T("Adapter warning: ") + plugin.SpokenTextAdapterService.LastError, "Last spoken text adapter warning.");
        DrawWrappedStatus(UiText.Interpolated($"Last original: {plugin.SpeechCacheService.LastOriginalText}"), "Most recent normalized text before Piper adapter changes.");
        DrawWrappedStatus(UiText.Interpolated($"Last adapted: {plugin.SpeechCacheService.LastAdaptedText}"), "Most recent text sent to Piper after adapter changes.");
        if (!string.IsNullOrWhiteSpace(plugin.SpeechCacheService.LastTextAdapterId))
        {
            DrawWrappedStatus(
                UiText.Interpolated($"Last adapter: {plugin.SpeechCacheService.LastTextAdapterId} {plugin.SpeechCacheService.LastTextAdapterVersion} {ShortHash(plugin.SpeechCacheService.LastTextAdapterContentHash)}"),
                "Adapter identity used for the most recent Piper synthesis cache key.");
        }

        ImGui.Separator();
        DrawWrappedStatus(UiText.Interpolated($"Trigger status: {UiText.T(plugin.CommentaryTriggerService.LastDecision)}"), "Last Reading Roegadyn trigger decision.");
        DrawWrappedStatus(UiText.Interpolated($"Queue status: {UiText.T(plugin.SpeechQueueService.LastStatus)}"), "Last speech queue state.");
        if (!string.IsNullOrWhiteSpace(plugin.SpeechQueueService.LastError))
            DrawWrappedStatus(UiText.T("Queue error: ") + plugin.SpeechQueueService.LastError, "Last speech queue error.");
        UiGui.Text(UiText.Interpolated($"Pending speech requests: {plugin.SpeechQueueService.PendingCount}"));
        TooltipLastItem("Number of queued speech requests waiting to be prepared or played.");
        UiGui.Text(UiText.Interpolated($"Speech busy: {plugin.SpeechQueueService.IsBusy}"));
        TooltipLastItem("Whether the speech queue is currently preparing or playing audio.");
        DrawWrappedStatus(UiText.Interpolated($"BGM status: {UiText.T(plugin.BgmProbeService.Status)}"), "Current BGM probe status.");
        UiGui.Text(UiText.Interpolated($"Current BGM ID: {plugin.BgmProbeService.CurrentBgmId}"));
        TooltipLastItem("Current BGM id observed by the BGM probe.");
        DrawWrappedStatus(UiText.Interpolated($"Cache status: {UiText.T(plugin.SpeechCacheService.LastStatus)}"), "Last speech cache result.");
        if (!string.IsNullOrWhiteSpace(plugin.SpeechCacheService.LastError))
            DrawWrappedStatus(UiText.T("Speech warning: ") + UiText.SpeechWarning(plugin.SpeechCacheService.LastError), "Last speech synthesis or cache warning.");
    }

    private void DrawPresetManager()
    {
        UiGui.TextUnformatted("Presets");
        var presets = plugin.PresetService.Presets.ToList();
        var active = plugin.PresetService.ActivePreset;
        EnsurePresetRenameBuffer(active);

        var listWidth = Math.Min(420f, ImGui.GetContentRegionAvail().X);
        var visibleRows = Math.Clamp(presets.Count, 4, 8);
        var listHeight = (ImGui.GetTextLineHeightWithSpacing() * visibleRows) + 8f;
        ImGui.BeginChild("##DheaconPresetList", new Vector2(listWidth, listHeight), true, ImGuiWindowFlags.HorizontalScrollbar);
        try
        {
        foreach (var preset in presets)
        {
            var selected = string.Equals(preset.Id, active.Id, StringComparison.OrdinalIgnoreCase);
            var suffix = preset.Protected ? "  [template]" : "  [user]";
            if (UiGui.Selectable($"{preset.Name}{suffix}##Preset-{preset.Id}", selected, preset.Name + UiText.T(suffix)))
                SelectPreset(preset.Id);
            TooltipLastItemRaw(preset.Description);
        }
        }
        finally { ImGui.EndChild(); }

        if (UiGui.SmallButton("+"))
        {
            if (plugin.PresetService.DuplicateActivePreset(out var duplicated, out var message))
            {
                plugin.PrintStatus(message);
                if (duplicated != null)
                    EnsurePresetRenameBuffer(duplicated);
                plugin.UpdateDtrBar();
                plugin.KranglerImaginaryFrenIpcClient.ReconcileNow();
            }
            else
            {
                plugin.PrintStatus("Preset duplicate failed: " + message);
            }
        }
        TooltipLastItem("Duplicates the active preset into a new editable user preset.");

        ImGui.SameLine();
        ImGui.BeginDisabled(active.Protected);
        if (UiGui.SmallButton("-"))
        {
            if (plugin.PresetService.DeleteUserPreset(active.Id, out var message))
            {
                plugin.PrintStatus(message);
                EnsurePresetRenameBuffer(plugin.PresetService.ActivePreset);
                plugin.UpdateDtrBar();
                plugin.KranglerImaginaryFrenIpcClient.ReconcileNow();
            }
            else
            {
                plugin.PrintStatus("Preset delete failed: " + message);
            }
        }
        ImGui.EndDisabled();
        TooltipLastItem(active.Protected ? "Protected bundled templates cannot be deleted." : "Deletes the active user preset.");

        DrawWrappedStatus(UiText.Interpolated($"Active preset: {active.Name}"), "Current active preset.");
        if (!string.IsNullOrWhiteSpace(active.Description))
            DrawWrappedStatus(active.Description, "Description supplied by the active preset.", translate: false);
        DrawWrappedStatus(UiText.Interpolated($"Preset source: {UiText.T(active.Bundled ? "Bundled" : "User")}  Protected: {active.Protected}"), "Protected bundled presets cannot be renamed, deleted, or overwritten.");
        DrawWrappedStatus(UiText.T("Preset status: ") + UiText.T(plugin.PresetService.LastStatus), "Preset load/import/export status.");
        DrawLinePackSelector(active);

        ImGui.BeginDisabled(active.Protected);
        ImGui.SetNextItemWidth(Math.Min(320f, ImGui.GetContentRegionAvail().X));
        UiGui.InputText("Name", ref presetRenameText, 96);
        TooltipLastItem(active.Protected ? "Duplicate this template with + before renaming." : "Edit the active user preset display name.");

        if (UiGui.Button("Rename"))
        {
            if (plugin.PresetService.RenameUserPreset(active.Id, presetRenameText, out var message))
            {
                plugin.PrintStatus(message);
                EnsurePresetRenameBuffer(plugin.PresetService.ActivePreset);
                plugin.UpdateDtrBar();
            }
            else
            {
                plugin.PrintStatus("Preset rename failed: " + message);
            }
        }
        TooltipLastItem("Renames the active user preset.");

        ImGui.SameLine();
        if (UiGui.Button("Save current settings"))
        {
            if (plugin.PresetService.SaveActivePresetFromConfiguration(out var message))
                plugin.PrintStatus(message);
            else
                plugin.PrintStatus("Preset save failed: " + message);
        }
        TooltipLastItem("Writes the current live settings into the active user preset.");
        ImGui.EndDisabled();

        if (active.Protected)
            DrawDisabledStatus("Duplicate this template with + before renaming or saving over it.", "Bundled templates are read-only.");

        if (UiGui.Button("Export active preset"))
        {
            try
            {
                var encoded = plugin.PresetService.ExportPresetBase64(active.Id, plugin.KranglerImaginaryFrenIpcClient.TryExportPresetBase64);
                ImGui.SetClipboardText(encoded);
                plugin.PrintStatus($"Exported preset '{active.Name}' to clipboard.");
            }
            catch (Exception ex)
            {
                plugin.PrintStatus("Preset export failed: " + ex.Message);
            }
        }
        TooltipLastItem("Copies one portable base64 preset JSON blob to the clipboard.");

        ImGui.SameLine();
        if (UiGui.Button("Paste import"))
            presetImportText = ImGui.GetClipboardText() ?? string.Empty;
        TooltipLastItem("Reads a portable preset blob from the clipboard into the import box.");

        ImGui.SetNextItemWidth(-1f);
        UiGui.InputTextWithHint("##PresetImport", "Paste preset base64 here", ref presetImportText, 16384);

        if (UiGui.Button("Import preset") && !string.IsNullOrWhiteSpace(presetImportText))
        {
            if (plugin.PresetService.ImportPresetBase64(presetImportText, out var imported, out var message))
            {
                if (!string.IsNullOrWhiteSpace(imported?.ImaginaryFren?.EmbeddedKranglerPresetBase64))
                    plugin.KranglerImaginaryFrenIpcClient.TryImportPresetBase64(imported.ImaginaryFren.EmbeddedKranglerPresetBase64, out _);

                if (imported != null)
                    SelectPreset(imported.Id, printStatus: false);

                plugin.PrintStatus(message);
                presetImportText = string.Empty;
            }
            else
            {
                plugin.PrintStatus("Preset import failed: " + message);
            }
        }
        TooltipLastItem("Imports one portable preset into the user preset folder. Protected bundled preset IDs are imported under a new ID.");
    }

    private void DrawLinePackSelector(DheaconPreset active)
    {
        if (active.Mode != CommentaryMode.ReadingRoegadyn)
        {
            DrawDisabledStatus("Line pack: not used in Dheacon mode.", "Dheacon mode plays the packaged alert sound instead of spoken commentary.");
            return;
        }

        var linePacks = plugin.CommentaryLinePackService.LinePacks.ToList();
        if (linePacks.Count == 0)
        {
            DrawWrappedStatus(UiText.T("Line pack status: ") + UiText.T(plugin.CommentaryLinePackService.LastLoadStatus), "No selectable line packs were loaded.");
            return;
        }

        var activeLinePackId = plugin.CommentaryLinePackService.CanonicalizeLinePackId(active.LinePackId);
        if (string.IsNullOrWhiteSpace(activeLinePackId))
            activeLinePackId = CommentaryLinePackService.ReadingRoegadynLinePackId;

        var currentIndex = linePacks.FindIndex(info => string.Equals(info.Id, activeLinePackId, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
            currentIndex = Math.Max(0, linePacks.FindIndex(info => string.Equals(info.Id, CommentaryLinePackService.ReadingRoegadynLinePackId, StringComparison.OrdinalIgnoreCase)));

        var labels = linePacks
            .Select(info => $"{info.Name} [{(info.Bundled ? "bundled" : "user")}]")
            .ToArray();
        var displayLabels = linePacks
            .Select(info => info.Name + " [" + UiText.T(info.Bundled ? "Bundled" : "User") + "]")
            .ToArray();

        ImGui.SetNextItemWidth(Math.Min(420f, ImGui.GetContentRegionAvail().X));
        if (UiGui.Combo("Line pack", ref currentIndex, labels, labels.Length, displayLabels))
        {
            if (plugin.PresetService.UpdateActiveLinePack(linePacks[currentIndex].Id, out var message))
                plugin.PrintStatus(message);
            else
                plugin.PrintStatus("Line pack update failed: " + message);
        }
        TooltipLastItem(active.Protected ? "Changes this protected template for the current session. Duplicate it with + to persist a line-pack choice." : "Selects the line pack used after any inline preset lines.");

        var selected = linePacks[Math.Clamp(currentIndex, 0, linePacks.Count - 1)];
        DrawWrappedStatus(UiText.Interpolated($"Line pack status: {UiText.T(plugin.CommentaryLinePackService.LastLoadStatus)}"), "Line-pack loader status.");
        if (!string.IsNullOrWhiteSpace(selected.Description))
            DrawWrappedStatus(selected.Description, "Selected line-pack description.", translate: false);
    }

    private void DrawImaginaryFrenPanel()
    {
        var active = plugin.PresetService.ActivePreset;
        var fren = active.ImaginaryFren ?? new KranglerImaginaryFrenPreset();

        UiGui.TextUnformatted("Imaginary Fren");
        var enabled = fren.Enabled;
        if (UiGui.Checkbox("Spawn Fren for this preset", ref enabled))
            UpdateActiveFrenSettings(enabled, fren.Name, fren.PresetKey);
        TooltipLastItem("When enabled, the active preset asks Krangler to spawn its local-only Imaginary Fren follower.");

        var name = fren.Name;
        ImGui.SetNextItemWidth(Math.Min(320f, ImGui.GetContentRegionAvail().X));
        if (UiGui.InputText("Display name", ref name, 64))
            UpdateActiveFrenSettings(enabled, name, fren.PresetKey);
        TooltipLastItem("Name Krangler writes onto the local-only follower actor.");

        var presetKey = fren.PresetKey;
        var kranglerPresets = plugin.KranglerImaginaryFrenIpcClient.GetPresetSummaries();
        if (kranglerPresets.Count > 0)
        {
            ImGui.SetNextItemWidth(Math.Min(420f, ImGui.GetContentRegionAvail().X));
            if (DrawKranglerPresetSelector(ref presetKey, kranglerPresets))
                UpdateActiveFrenSettings(enabled, name, presetKey);
            TooltipLastItem("Selects a Krangler preset by friendly name and stores its stable key in this Dheacon preset.");
        }
        else
        {
            ImGui.SetNextItemWidth(Math.Min(420f, ImGui.GetContentRegionAvail().X));
            if (UiGui.InputText("Krangler preset", ref presetKey, 160))
                UpdateActiveFrenSettings(enabled, name, presetKey);
            TooltipLastItem("Krangler preset name, identifier, or source filename to apply to the follower. Shown when the Krangler preset list is unavailable.");
        }

        if (UiGui.Button("Reconcile now"))
        {
            plugin.KranglerImaginaryFrenIpcClient.GetPresetSummaries(forceRefresh: true);
            plugin.KranglerImaginaryFrenIpcClient.ReconcileNow();
        }
        TooltipLastItem("Immediately sends the active preset's Fren state to Krangler if Krangler is loaded.");

        DrawWrappedStatus(UiText.T("Follower status: ") + UiText.FollowerStatus(plugin.KranglerImaginaryFrenIpcClient.LastStatus), "Last Krangler Imaginary Fren IPC status.");
        DrawWrappedStatus(UiText.T("Preset list: ") + UiText.FollowerStatus(plugin.KranglerImaginaryFrenIpcClient.PresetListStatus), "Last Krangler preset-list IPC status.");
        if (!string.IsNullOrWhiteSpace(plugin.KranglerImaginaryFrenIpcClient.PresetListError))
            DrawWrappedStatus(UiText.T("Preset list warning: ") + UiText.FollowerStatus(plugin.KranglerImaginaryFrenIpcClient.PresetListError), "Krangler is optional; when the list is unavailable the text field fallback remains active.");
        if (!string.IsNullOrWhiteSpace(plugin.KranglerImaginaryFrenIpcClient.LastError))
            DrawWrappedStatus(UiText.T("Follower warning: ") + UiText.FollowerStatus(plugin.KranglerImaginaryFrenIpcClient.LastError), "Krangler is optional; this warning does not stop Dheacon speech.");
        if (active.Protected)
            DrawDisabledStatus("Fren edits on this protected template are runtime-only. Duplicate with + to persist them.", "Bundled templates are read-only.");
    }

    private bool DrawKranglerPresetSelector(ref string presetKey, IReadOnlyList<KranglerPresetSummary> presets)
    {
        var selected = FindKranglerPreset(presets, presetKey);
        var preview = selected != null
            ? FormatKranglerPresetLabel(selected)
            : string.IsNullOrWhiteSpace(presetKey)
                ? UiText.T("Select preset")
                : presetKey;
        var changed = false;

        if (!UiGui.BeginCombo("Krangler preset", preview))
            return false;

        try
        {

        ImGui.SetNextItemWidth(-1f);
        UiGui.InputTextWithHint("##KranglerPresetSearch", "Search Krangler presets...", ref kranglerPresetSearchText, 128);
        ImGui.Separator();

        var filteredPresets = string.IsNullOrWhiteSpace(kranglerPresetSearchText)
            ? presets
            : presets
                .Where(summary =>
                    summary.Name.Contains(kranglerPresetSearchText, StringComparison.OrdinalIgnoreCase) ||
                    summary.Key.Contains(kranglerPresetSearchText, StringComparison.OrdinalIgnoreCase) ||
                    summary.SourceFileName.Contains(kranglerPresetSearchText, StringComparison.OrdinalIgnoreCase))
                .ToList();

        foreach (var summary in filteredPresets)
        {
            var isSelected = string.Equals(summary.Key, presetKey, StringComparison.OrdinalIgnoreCase);
            if (UiGui.Selectable($"{FormatKranglerPresetLabel(summary)}##KranglerPreset-{summary.Key}", isSelected, FormatKranglerPresetLabel(summary)))
            {
                presetKey = summary.Key;
                changed = true;
            }

            TooltipLastItem(UiText.Interpolated($"Key: {summary.Key}\nSource: {summary.SourceFileName}"));
        }

        if (!filteredPresets.Any())
            UiGui.TextDisabled("No Krangler presets match the current search.");

        }
        finally { UiGui.EndCombo(); }
        return changed;
    }

    private static KranglerPresetSummary? FindKranglerPreset(IReadOnlyList<KranglerPresetSummary> presets, string presetKey)
        => presets.FirstOrDefault(summary =>
            string.Equals(summary.Key, presetKey, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(summary.Name, presetKey, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(summary.SourceFileName, presetKey, StringComparison.OrdinalIgnoreCase));

    private static string FormatKranglerPresetLabel(KranglerPresetSummary summary)
        => string.IsNullOrWhiteSpace(summary.Name) || string.Equals(summary.Name, summary.Key, StringComparison.OrdinalIgnoreCase)
            ? summary.Key
            : summary.Name;

    private void SelectPreset(string presetId, bool printStatus = true)
    {
        if (plugin.PresetService.SetActivePreset(presetId, out var message))
        {
            if (printStatus)
                plugin.PrintStatus(message);
            EnsurePresetRenameBuffer(plugin.PresetService.ActivePreset);
            plugin.UpdateDtrBar();
            plugin.KranglerImaginaryFrenIpcClient.ReconcileNow();
            return;
        }

        plugin.PrintStatus(message);
    }

    private void UpdateActiveFrenSettings(bool enabled, string name, string presetKey)
    {
        if (plugin.PresetService.UpdateActiveImaginaryFren(enabled, name, presetKey, out _))
            plugin.KranglerImaginaryFrenIpcClient.ReconcileNow();
    }

    private void EnsurePresetRenameBuffer(DheaconPreset active)
    {
        if (string.Equals(presetRenameTargetId, active.Id, StringComparison.OrdinalIgnoreCase))
            return;

        presetRenameTargetId = active.Id;
        presetRenameText = active.Name;
    }

    private void DrawDheaconSettings(Configuration cfg)
    {
        UiGui.TextUnformatted("Dheacon");

        var suppressTeleport = cfg.SuppressTeleportAndReturnTransitions;
        if (UiGui.Checkbox("Suppress teleports and return", ref suppressTeleport))
        {
            cfg.SuppressTeleportAndReturnTransitions = suppressTeleport;
            cfg.Save();
        }
        TooltipLastItem("Prevents teleport and Return transitions from playing the packaged alert sound.");

        var soundPath = cfg.AlertSoundRelativePath;
        if (UiGui.InputText("Alert sound path", ref soundPath, 260))
        {
            cfg.AlertSoundRelativePath = soundPath;
            cfg.Save();
        }
        TooltipLastItem("Sets the alert WAV path for Dheacon mode; relative paths resolve under the plugin folder.");

        DrawWrappedStatus(UiText.T("Alert sound: ") + plugin.AudioPlaybackService.GetResolvedAlertPath(), "Resolved path used when Dheacon mode plays its alert sound.");
    }

    private void DrawReadingRoegadynGeneralSettings(Configuration cfg)
    {
        UiGui.TextUnformatted("Reading Roegadyn");

        var suppressTeleport = cfg.SuppressTeleportAndReturnTransitions;
        if (UiGui.Checkbox("Suppress teleports and return", ref suppressTeleport))
        {
            cfg.SuppressTeleportAndReturnTransitions = suppressTeleport;
            cfg.Save();
        }
        TooltipLastItem("Prevents teleport and Return transitions from triggering Reading Roegadyn speech.");

        DrawCommentaryToggles(cfg);
        DrawCooldowns(cfg);
    }

    private void DrawSpeechControls(Configuration cfg)
    {
        if (cfg.TtsBackend == TtsBackend.PiperLocal)
        {
            var piperLengthScale = (float)cfg.TtsPiperLengthScale;
            if (UiGui.SliderFloat("Piper speed", ref piperLengthScale, 0.5f, 2.0f, "%.2f"))
            {
                cfg.TtsPiperLengthScale = Math.Clamp(piperLengthScale, 0.5f, 2.0f);
                cfg.Save();
            }
            TooltipLastItem("Controls Piper --length_scale. Lower values speak faster; higher values speak slower. Changing this regenerates Piper WAV cache entries.");

            var sentencePause = (float)cfg.TtsPiperSentenceSilence;
            if (UiGui.SliderFloat("Sentence pause", ref sentencePause, 0.0f, 2.0f, "%.2f sec"))
            {
                cfg.TtsPiperSentenceSilence = Math.Clamp(sentencePause, 0.0f, 5.0f);
                cfg.Save();
            }
            TooltipLastItem("Controls Piper --sentence_silence. Higher values add more pause between sentences and regenerate Piper WAV cache entries.");

            var piperPitch = (float)cfg.TtsPiperPitchShiftSemitones;
            if (UiGui.SliderFloat("Piper pitch", ref piperPitch, -12.0f, 12.0f, "%+.1f semitones"))
            {
                cfg.TtsPiperPitchShiftSemitones = Math.Clamp(piperPitch, -12.0f, 12.0f);
                cfg.Save();
            }
            TooltipLastItem("Post-WAV processing only; this is not a Piper synthesis parameter and can introduce artifacts. Negative values make the voice deeper. Changing it regenerates Piper WAV cache entries.");

            var playbackGain = cfg.TtsOutputGainPercent;
            if (UiGui.SliderInt("Playback gain %", ref playbackGain, 0, 400))
            {
                cfg.TtsOutputGainPercent = Math.Clamp(playbackGain, 0, 400);
                cfg.Save();
            }
            TooltipLastItem("Applies post-WAV playback gain after Piper synthesis. This is not Piper synth volume and changing it regenerates cached Piper WAVs.");

            return;
        }

        var rate = cfg.TtsRate;
        if (UiGui.SliderInt("Rate", ref rate, -10, 10))
        {
            cfg.TtsRate = rate;
            cfg.Save();
        }
        TooltipLastItem("Controls Windows/SAPI speaking rate. Changing this regenerates cached speech for those backends.");

        var volume = cfg.TtsVolume;
        if (UiGui.SliderInt("Synth volume", ref volume, 0, 100))
        {
            cfg.TtsVolume = volume;
            cfg.Save();
        }
        TooltipLastItem("Controls Windows/SAPI synthesis volume before the WAV is cached.");

        var pitch = (float)cfg.TtsPitch;
        if (UiGui.SliderFloat("Pitch", ref pitch, 0.25f, 2.0f, "%.2f"))
        {
            cfg.TtsPitch = Math.Clamp(pitch, 0.0f, 2.0f);
            cfg.Save();
        }
        TooltipLastItem("Controls Modern Windows pitch where supported; Legacy SAPI may ignore it.");

        var outputGain = cfg.TtsOutputGainPercent;
        if (UiGui.SliderInt("Output gain %", ref outputGain, 0, 400))
        {
            cfg.TtsOutputGainPercent = Math.Clamp(outputGain, 0, 400);
            cfg.Save();
        }
        TooltipLastItem("Applies post-WAV gain before playback. Changing it regenerates cached speech files.");
    }

    private void DrawSpeechCacheSettings(Configuration cfg)
    {
        var cacheDirectory = cfg.TtsCacheDirectory;
        if (UiGui.InputText("Cache folder", ref cacheDirectory, 512))
        {
            cfg.TtsCacheDirectory = cacheDirectory;
            cfg.Save();
        }
        TooltipLastItem("Sets where generated speech WAV files are cached; empty uses the default LocalAppData folder.");

        DrawWrappedStatus(UiText.T("Resolved cache folder: ") + cfg.GetResolvedTtsCacheDirectory(), "Actual folder used after expanding environment variables and defaults.");

        var maxMb = cfg.TtsMaxCacheMegabytes;
        if (UiGui.InputInt("Max cache MB", ref maxMb))
        {
            cfg.TtsMaxCacheMegabytes = Math.Max(1, maxMb);
            cfg.Save();
        }
        TooltipLastItem("Maximum total WAV cache size. Older cache files are pruned after new speech is generated.");

        UiGui.Text(UiText.Interpolated($"Cache size: {plugin.SpeechCacheService.GetCacheSizeMegabytes():F1} MB"));
        TooltipLastItem("Current approximate size of cached generated WAV files.");
        DrawWrappedStatus(UiText.T("Cache status: ") + UiText.T(plugin.SpeechCacheService.LastStatus), "Last cache operation result, including cache hits and generated files.");
        if (!string.IsNullOrWhiteSpace(plugin.SpeechCacheService.LastError))
            DrawWrappedStatus(UiText.T("Speech warning: ") + UiText.SpeechWarning(plugin.SpeechCacheService.LastError), "Last speech synthesis or cache warning.");
    }

    private void DrawTextAdapterSettings(Configuration cfg)
    {
        var adapterEnabled = cfg.TtsPiperTextAdapterEnabled;
        if (UiGui.Checkbox("Piper text adapter", ref adapterEnabled))
        {
            cfg.TtsPiperTextAdapterEnabled = adapterEnabled;
            cfg.Save();
        }
        TooltipLastItem("Enables the text adapter before Swedish Piper synthesis; changing it affects future Piper cache keys.");

        var adapters = plugin.SpokenTextAdapterService.GetAdapters();
        var adapterLabel = string.IsNullOrWhiteSpace(cfg.TtsPiperTextAdapterId) ? SpokenTextAdapterService.DefaultAdapterId : cfg.TtsPiperTextAdapterId;
        var adapterComboOpen = UiGui.BeginCombo("Adapter", adapterLabel);
        try
        {
        TooltipLastItem("Selects which text adapter runs before Piper synthesis.");
        if (adapterComboOpen)
        {
            foreach (var adapter in adapters)
            {
                var selected = string.Equals(cfg.TtsPiperTextAdapterId, adapter.Id, StringComparison.OrdinalIgnoreCase);
                if (UiGui.Selectable($"{adapter.Id} - {adapter.SourceLanguage} to {adapter.TargetLanguage}", selected, UiText.F("{0} - {1} to {2}", adapter.Id, adapter.SourceLanguage, adapter.TargetLanguage)))
                {
                    cfg.TtsPiperTextAdapterId = adapter.Id;
                    cfg.Save();
                }
                TooltipLastItem(UiText.Interpolated($"Use adapter {adapter.Id} for {adapter.SourceLanguage} to {adapter.TargetLanguage} text before Piper synthesis."));
            }

        }
        }
        finally { if (adapterComboOpen) UiGui.EndCombo(); }

        var selectedAdapter = plugin.SpokenTextAdapterService.GetAdapterInfo(cfg.TtsPiperTextAdapterId)
            ?? plugin.SpokenTextAdapterService.GetAdapterInfo(SpokenTextAdapterService.DefaultAdapterId);
        if (selectedAdapter != null)
            DrawWrappedStatus(UiText.Interpolated($"Adapter version: {selectedAdapter.Version}  Hash: {ShortHash(selectedAdapter.ContentHash)}"), "Adapter version and content hash are included in Piper WAV cache keys.");

        UiGui.InputText("Preview text", ref piperPreviewText, 1024);
        TooltipLastItem("Text to run through the current Piper adapter preview.");
        var preview = plugin.SpeechCacheService.PreviewPiperText(piperPreviewText);
        DrawWrappedStatus(UiText.Interpolated($"Preview adapter: {(string.IsNullOrWhiteSpace(preview.AdapterId) ? UiText.T("None") : preview.AdapterId)} {preview.AdapterVersion} {ShortHash(preview.AdapterContentHash)}"), "Adapter that would be applied to this preview text.");
        DrawWrappedStatus(UiText.T("Adapter status: ") + UiText.T(preview.Status), "Explains whether the adapter is enabled and applicable to the selected Piper voice.");

        if (UiGui.Button(piperPreviewSpeechInProgress ? "Testing adapted speech..." : "Test adapted speech"))
            StartPiperPreviewSpeech(preview.Original);
        TooltipLastItem("Generates and plays this preview through the configured Piper voice without changing the main speech backend.");

        if (ImGui.BeginTable("AdapterPreviewTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Original");
            ImGui.TableSetupColumn("Adapted");
            UiGui.TableHeadersRow();
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            MaterialText.TextWrapped(preview.Original);
            TooltipLastItem("Normalized source text before adapter substitutions.");
            ImGui.TableSetColumnIndex(1);
            MaterialText.TextWrapped(preview.Adapted);
            TooltipLastItem("Text that will be sent to Piper after adapter substitutions.");
            ImGui.EndTable();
        }
    }

    private void DrawVoiceSelector(Configuration cfg)
    {
        if (cfg.TtsBackend == TtsBackend.PiperLocal)
        {
            DrawPiperInstalledVoiceSelector(cfg);
            return;
        }

        var voices = plugin.SpeechCacheService.GetInstalledVoices(cfg.TtsBackend);
        var currentVoice = UiText.VoiceLabel(plugin.SpeechCacheService.GetSelectedVoiceLabel());

        var voiceComboOpen = UiGui.BeginCombo("Voice", currentVoice);
        try
        {
        TooltipLastItem("Selects the installed Windows/SAPI voice used for generated speech.");
        if (!voiceComboOpen)
            return;

        var defaultSelected = cfg.TtsBackend == TtsBackend.ModernWindows
            ? string.IsNullOrWhiteSpace(cfg.TtsModernVoiceId) && string.IsNullOrWhiteSpace(cfg.TtsVoiceName)
            : string.IsNullOrWhiteSpace(cfg.TtsVoiceName);
        if (UiGui.Selectable("Windows default", defaultSelected))
        {
            if (cfg.TtsBackend == TtsBackend.ModernWindows)
                cfg.TtsModernVoiceId = string.Empty;

            cfg.TtsVoiceName = string.Empty;
            cfg.Save();
        }
        TooltipLastItem("Uses the current Windows default voice for this backend.");

        foreach (var voice in voices)
        {
            var selected = cfg.TtsBackend == TtsBackend.ModernWindows
                ? string.Equals(cfg.TtsModernVoiceId, voice.Id, StringComparison.OrdinalIgnoreCase) ||
                  (string.IsNullOrWhiteSpace(cfg.TtsModernVoiceId) &&
                   string.Equals(cfg.TtsVoiceName, voice.DisplayName, StringComparison.OrdinalIgnoreCase))
                : string.Equals(cfg.TtsVoiceName, voice.DisplayName, StringComparison.OrdinalIgnoreCase);
            if (UiGui.Selectable(voice.Label, selected, voice.Label))
            {
                if (cfg.TtsBackend == TtsBackend.ModernWindows)
                    cfg.TtsModernVoiceId = voice.Id;

                cfg.TtsVoiceName = voice.DisplayName;
                cfg.Save();
            }
            TooltipLastItem(UiText.Interpolated($"Select {voice.Label} for future generated speech."));
        }

        }
        finally { if (voiceComboOpen) UiGui.EndCombo(); }
    }

    private void DrawBackendSelector(Configuration cfg)
    {
        var backendIndex = cfg.TtsBackend switch
        {
            TtsBackend.LegacySapi => 1,
            TtsBackend.PiperLocal => 2,
            _ => 0,
        };

        if (UiGui.Combo("Backend", ref backendIndex, TtsBackendLabels, TtsBackendLabels.Length))
        {
            var nextBackend = backendIndex switch
            {
                1 => TtsBackend.LegacySapi,
                2 => TtsBackend.PiperLocal,
                _ => TtsBackend.ModernWindows,
            };

            cfg.TtsBackend = nextBackend;
            cfg.Save();
            if (nextBackend == TtsBackend.PiperLocal)
                StartPiperRecommendedSetup(switchBackendWhenReady: true);
        }
        TooltipLastItem("Selects the speech engine. Choosing Piper automatically prepares the recommended English Arctic voice if needed.");
    }

    private void DrawVoiceActions()
    {
        if (UiGui.Button("Refresh voices"))
        {
            plugin.SpeechCacheService.RefreshInstalledVoices();
            var modernCount = plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.ModernWindows).Count;
            var legacyCount = plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.LegacySapi).Count;
            var piperCount = plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.PiperLocal).Count;
            plugin.PrintStatus($"Detected {modernCount} Modern Windows voice(s), {legacyCount} Legacy SAPI voice(s), {piperCount} Piper voice(s).");
        }
        TooltipLastItem("Refreshes Modern Windows, Legacy SAPI, and installed Piper voice lists.");
    }

    private void DrawPiperInstalledVoiceSelector(Configuration cfg)
    {
        var voices = plugin.PiperVoiceCatalogService.GetInstalledVoices();
        var currentVoice = plugin.PiperVoiceCatalogService.FindExactInstalledVoice(cfg.TtsPiperVoiceId) is { } selectedVoice
            ? FormatPiperInstalledVoiceLabel(selectedVoice)
            : string.IsNullOrWhiteSpace(cfg.TtsPiperVoiceId)
                ? UiText.T("No Piper voice selected")
                : UiText.F("{0} (not installed)", cfg.TtsPiperVoiceId.Trim());

        var piperVoiceComboOpen = UiGui.BeginCombo("Piper voice", currentVoice);
        try
        {
        TooltipLastItem("Selects the installed Piper voice used for Piper synthesis.");
        if (!piperVoiceComboOpen)
            return;

        foreach (var voice in voices)
        {
            var label = FormatPiperInstalledVoiceLabel(voice);
            var selected = string.Equals(cfg.TtsPiperVoiceId, voice.CatalogId, StringComparison.OrdinalIgnoreCase);
            if (UiGui.Selectable(label, selected, label))
            {
                plugin.PiperVoiceCatalogService.SelectVoice(voice.CatalogId);
                cfg.Save();
            }
            TooltipLastItem(UiText.Interpolated($"Select installed Piper voice {label}."));
        }

        if (voices.Count == 0)
            DrawDisabledStatus("No Piper voices installed.", "Open the Piper Voices tab to install the recommended English Arctic voice.");

        }
        finally { if (piperVoiceComboOpen) UiGui.EndCombo(); }
    }

    private void DrawPiperSetupStrip(Configuration cfg)
    {
        DrawWrappedStatus(plugin.PiperVoiceCatalogService.RefreshRuntimeStatus(save: false), "Piper runtime path currently used for local synthesis.");
        DrawWrappedStatus(plugin.PiperVoiceCatalogService.LastStatus, "Last Piper catalog, runtime, install, or selection operation status.");
        if (!string.IsNullOrWhiteSpace(plugin.PiperVoiceCatalogService.LastError))
            DrawWrappedStatus(UiText.T("Piper warning: ") + plugin.PiperVoiceCatalogService.LastError, "Last Piper warning or error reported by setup or catalog operations.");

        if (plugin.PiperVoiceCatalogService.IsBusy)
        {
            if (plugin.PiperVoiceCatalogService.OperationProgress >= 0d)
            {
                ImGui.ProgressBar((float)plugin.PiperVoiceCatalogService.OperationProgress, new Vector2(-1f, 0f));
                TooltipLastItem("Current Piper setup, download, or install progress.");
            }
            else
            {
                DrawDisabledStatus("Piper operation in progress...", "A Piper catalog, runtime, or voice operation is already running.");
            }
        }

        if (UiGui.Button("Refresh catalog"))
            StartPiperCatalogRefresh();
        TooltipLastItem("Downloads the latest Piper voice catalog and refreshes installed voice state.");

        ImGui.SameLine();
        if (UiGui.Button("Install runtime"))
            StartPiperRuntimeInstall();
        TooltipLastItem("Downloads and installs the managed portable Windows Piper runtime.");

        ImGui.SameLine();
        if (UiGui.Button("Open folder"))
            plugin.PiperVoiceCatalogService.OpenFolder(cfg.GetResolvedPiperRootDirectory());
        TooltipLastItem("Opens the managed Piper folder containing runtime, voices, cache, and manifests.");

        var recommendedInstalled = plugin.PiperVoiceCatalogService.FindExactInstalledVoice(PiperVoiceCatalogService.RecommendedVoiceCatalogId) != null;
        ImGui.SameLine();
        if (recommendedInstalled)
        {
            if (UiGui.Button("Select Arctic"))
                plugin.PiperVoiceCatalogService.SelectVoice(PiperVoiceCatalogService.RecommendedVoiceCatalogId);
            TooltipLastItem("Selects the installed recommended en_US-arctic-medium Piper voice.");
        }
        else if (UiGui.Button("Install Arctic"))
        {
            StartPiperRecommendedSetup(switchBackendWhenReady: false);
            TooltipLastItem("Installs and selects the recommended en_US-arctic-medium Piper voice.");
        }
        else
        {
            TooltipLastItem("Installs and selects the recommended en_US-arctic-medium Piper voice.");
        }

        var runtimePath = cfg.TtsPiperRuntimePath;
        if (UiGui.InputText("Piper runtime path", ref runtimePath, 512))
        {
            cfg.TtsPiperRuntimePath = runtimePath;
            plugin.PiperVoiceCatalogService.RefreshRuntimeStatus();
        }
        TooltipLastItem("Optional explicit piper.exe path. Empty uses the managed runtime folder or PATH lookup.");
    }

    private void DrawPiperFilters(IReadOnlyList<PiperVoiceCatalogEntry> entries)
    {
        UiGui.Combo("Installed filter", ref piperInstalledFilter, PiperInstalledFilters, PiperInstalledFilters.Length);
        TooltipLastItem("Filters Piper voices by whether they are already installed.");

        var languages = CreateLanguageFilterOptions(entries);
        DrawStringFilter("Language", languages, ref piperLanguageFilter, "Filters Piper voices by language.");

        var genders = CreateFilterOptions(entries.Select(entry => entry.Gender));
        DrawStringFilter("Gender", genders, ref piperGenderFilter, "Filters Piper voices by catalog gender metadata.");

        var qualities = CreateFilterOptions(entries.Select(entry => entry.Quality));
        DrawStringFilter("Quality", qualities, ref piperQualityFilter, "Filters Piper voices by model quality tier.");

        var sources = CreateFilterOptions(entries.Select(entry => entry.Source));
        DrawStringFilter("Source", sources, ref piperSourceFilter, "Filters Piper voices by official or community source.");
    }

    private static void DrawStringFilter(string label, string[] options, ref string selected, string tooltip)
    {
        var current = selected;
        var index = Array.FindIndex(options, option => string.Equals(option, current, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            index = 0;

        if (UiGui.Combo(label, ref index, options, options.Length))
            selected = options[Math.Clamp(index, 0, options.Length - 1)];
        TooltipLastItem(tooltip);
    }

    private void DrawPiperSelectedActionBar(IReadOnlyList<PiperVoiceCatalogEntry> entries, Configuration cfg)
    {
        var entry = entries.FirstOrDefault(candidate => string.Equals(candidate.CatalogId, selectedPiperCatalogId, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            DrawDisabledStatus("Select a Piper voice to manage it.", "Choose a row in the Piper catalog to show install/select/uninstall actions here.");
            return;
        }

        var isCurrent = entry.Installed && string.Equals(cfg.TtsPiperVoiceId, entry.CatalogId, StringComparison.OrdinalIgnoreCase);

        ImGui.PushID("SelectedPiperActionBar");
        UiGui.TextUnformatted("Selected:");
        TooltipLastItem("Currently highlighted Piper catalog voice.");
        ImGui.SameLine();
        MaterialText.TextWrapped(FormatPiperVoiceLabel(entry));
        TooltipLastItem(FormatPiperVoiceLabel(entry));

        ImGui.PushID(entry.CatalogId);
        if (!entry.Installed)
        {
            if (UiGui.SmallButton("Install"))
                StartPiperInstall(entry.CatalogId);
            TooltipLastItem("Downloads and installs the selected Piper voice.");
        }
        else
        {
            if (isCurrent)
            {
                DrawDisabledStatus("Selected", "This installed Piper voice is currently selected.");
            }
            else if (UiGui.SmallButton("Select"))
            {
                plugin.PiperVoiceCatalogService.SelectVoice(entry.CatalogId);
                TooltipLastItem("Makes this installed Piper voice the active Piper voice.");
            }
            else
            {
                TooltipLastItem("Makes this installed Piper voice the active Piper voice.");
            }

            ImGui.SameLine();
            if (UiGui.SmallButton("Uninstall"))
                plugin.PiperVoiceCatalogService.UninstallVoice(entry.CatalogId);
            TooltipLastItem("Removes this Piper voice from the managed voices folder.");

            ImGui.SameLine();
            if (UiGui.SmallButton("Open folder"))
                plugin.PiperVoiceCatalogService.OpenFolder(entry.InstalledDirectory);
            TooltipLastItem("Opens the folder containing this installed Piper voice.");
        }

        ImGui.PopID();
        ImGui.PopID();
    }

    private void DrawPiperCatalogTable(IReadOnlyList<PiperVoiceCatalogEntry> entries, Configuration cfg)
    {
        using var tightRows = cfg.UiCompact ? MaterialTable.PushTightRows() : default;
        var tableHeight = Math.Max(220f, ImGui.GetContentRegionAvail().Y * 0.48f);
        var widths = CalculatePiperCatalogColumnWidths(entries);
        var innerWidth = widths.Voice + widths.Language + widths.Gender + widths.Quality + widths.Source + widths.Size + widths.State + widths.Actions
            + 16 * ImGui.GetStyle().CellPadding.X + 17 * MaterialTheme.Metrics.Scale;
        var tableFlags = ImGuiTableFlags.Borders |
                         ImGuiTableFlags.RowBg |
                         ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX |
                         ImGuiTableFlags.SizingFixedFit |
                         ImGuiTableFlags.NoHostExtendX;

        if (!ImGui.BeginTable(
                "PiperCatalogTable",
                8,
                tableFlags,
                new Vector2(-1f, tableHeight), innerWidth))
            return;

        ImGui.TableSetupColumn("Voice", ImGuiTableColumnFlags.WidthFixed, widths.Voice);
        ImGui.TableSetupColumn("Language", ImGuiTableColumnFlags.WidthFixed, widths.Language);
        ImGui.TableSetupColumn("Gender", ImGuiTableColumnFlags.WidthFixed, widths.Gender);
        ImGui.TableSetupColumn("Quality", ImGuiTableColumnFlags.WidthFixed, widths.Quality);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, widths.Source);
        ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, widths.Size);
        ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, widths.State);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, widths.Actions);
        UiGui.TableHeadersRow();

        foreach (var entry in entries)
            DrawPiperCatalogRow(entry, cfg);

        ImGui.EndTable();
    }

    private void DrawPiperCatalogRow(PiperVoiceCatalogEntry entry, Configuration cfg)
    {
        var selected = string.Equals(selectedPiperCatalogId, entry.CatalogId, StringComparison.OrdinalIgnoreCase);
        var isCurrent = entry.Installed && string.Equals(cfg.TtsPiperVoiceId, entry.CatalogId, StringComparison.OrdinalIgnoreCase);

        ImGui.PushID(entry.CatalogId);
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        if (UiGui.Selectable($"{entry.VoiceKey}##voice", selected, entry.VoiceKey))
            selectedPiperCatalogId = entry.CatalogId;
        TooltipLastItem(FormatPiperVoiceLabel(entry));

        ImGui.TableSetColumnIndex(1);
        DrawClippedTableText(FormatPiperLanguage(entry.LanguageName, entry.LanguageCode), "Piper voice language.");
        ImGui.TableSetColumnIndex(2);
        DrawClippedTableText(entry.Gender, "Catalog gender metadata.");
        ImGui.TableSetColumnIndex(3);
        DrawClippedTableText(entry.Quality, "Piper model quality tier.");
        ImGui.TableSetColumnIndex(4);
        DrawClippedTableText(entry.Source, "Catalog source for this Piper voice.");
        ImGui.TableSetColumnIndex(5);
        DrawClippedTableText(entry.SizeLabel, "Installed or download size for this Piper voice.");
        ImGui.TableSetColumnIndex(6);
        DrawClippedTableText(UiText.T(isCurrent ? "Selected" : entry.Installed ? "Installed" : "Catalog"), "Whether this Piper voice is selected, installed, or only available in the catalog.");
        ImGui.TableSetColumnIndex(7);
        DrawPiperCatalogRowActions(entry, isCurrent);
        ImGui.PopID();
    }

    private void DrawPiperCatalogRowActions(PiperVoiceCatalogEntry entry, bool isCurrent)
    {
        if (UiGui.SmallButton("Details"))
            selectedPiperCatalogId = entry.CatalogId;
        TooltipLastItem("Shows details and the compact action bar for this Piper voice.");

        if (!entry.Installed)
        {
            ImGui.SameLine();
            if (UiGui.SmallButton("Install"))
                StartPiperInstall(entry.CatalogId);
            TooltipLastItem("Downloads and installs this Piper voice.");
            return;
        }

        if (!isCurrent)
        {
            ImGui.SameLine();
            if (UiGui.SmallButton("Select"))
                plugin.PiperVoiceCatalogService.SelectVoice(entry.CatalogId);
            TooltipLastItem("Makes this installed Piper voice the active Piper voice.");
        }

        ImGui.SameLine();
        if (UiGui.SmallButton("Uninstall"))
            plugin.PiperVoiceCatalogService.UninstallVoice(entry.CatalogId);
        TooltipLastItem("Removes this installed Piper voice from the managed voices folder.");
    }

    private void DrawPiperSelectedVoicePanel(IReadOnlyList<PiperVoiceCatalogEntry> entries, Configuration cfg)
    {
        var entry = entries.FirstOrDefault(candidate => string.Equals(candidate.CatalogId, selectedPiperCatalogId, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return;

        ImGui.Separator();
        var isCurrent = entry.Installed && string.Equals(cfg.TtsPiperVoiceId, entry.CatalogId, StringComparison.OrdinalIgnoreCase);
        UiGui.TextUnformatted("Selected voice details");
        TooltipLastItem("Details for the highlighted Piper catalog row.");
        DrawWrappedStatus(isCurrent ? "State: selected Piper voice" : entry.Installed ? "State: installed" : "State: not installed", "Install and selection state for the highlighted Piper voice.");
        DrawWrappedStatus(FormatPiperVoiceLabel(entry), "Full Piper voice label.", translate: false);
        DrawWrappedStatus(UiText.Interpolated($"Language: {FormatPiperLanguage(entry.LanguageName, entry.LanguageCode)}"), "Language metadata for this Piper voice.");
        DrawWrappedStatus(UiText.Interpolated($"{entry.DisplayName}  {entry.SizeLabel}"), "Display name and local/download size for this Piper voice.");
        DrawWrappedStatus(UiText.Interpolated($"License: {entry.License}"), "License metadata from the voice catalog.");
        if (!string.IsNullOrWhiteSpace(entry.Notes))
            DrawWrappedStatus(entry.Notes, "Additional notes from the Piper voice catalog.", translate: false);

        ImGui.PushID(entry.CatalogId);
        if (entry.Installed)
        {
            if (isCurrent)
                DrawDisabledStatus("Selected", "This Piper voice is already active.");
            else
            {
                if (UiGui.Button("Select"))
                    plugin.PiperVoiceCatalogService.SelectVoice(entry.CatalogId);
                TooltipLastItem("Makes this installed Piper voice active.");
            }

            ImGui.SameLine();
            if (UiGui.Button("Uninstall"))
                plugin.PiperVoiceCatalogService.UninstallVoice(entry.CatalogId);
            TooltipLastItem("Removes this Piper voice from the managed voices folder.");

            ImGui.SameLine();
            if (UiGui.Button("Open folder"))
                plugin.PiperVoiceCatalogService.OpenFolder(entry.InstalledDirectory);
            TooltipLastItem("Opens the folder containing this installed Piper voice.");
        }
        else
        {
            if (UiGui.Button("Install"))
                StartPiperInstall(entry.CatalogId);
            TooltipLastItem("Downloads and installs this Piper voice.");
        }

        ImGui.PopID();
    }

    private void EnsureSelectedPiperEntry(IReadOnlyList<PiperVoiceCatalogEntry> entries, Configuration cfg)
    {
        if (entries.Count == 0)
        {
            selectedPiperCatalogId = string.Empty;
            return;
        }

        if (!string.IsNullOrWhiteSpace(selectedPiperCatalogId) &&
            entries.Any(entry => string.Equals(entry.CatalogId, selectedPiperCatalogId, StringComparison.OrdinalIgnoreCase)))
            return;

        selectedPiperCatalogId = entries.FirstOrDefault(entry =>
                entry.Installed &&
                string.Equals(entry.CatalogId, cfg.TtsPiperVoiceId, StringComparison.OrdinalIgnoreCase))?.CatalogId
            ?? entries.FirstOrDefault(entry => string.Equals(entry.CatalogId, PiperVoiceCatalogService.RecommendedVoiceCatalogId, StringComparison.OrdinalIgnoreCase))?.CatalogId
            ?? entries[0].CatalogId;
    }

    private void EnsurePiperRecommendedVoiceIfNeeded(Configuration cfg)
    {
        if (piperRecommendedAutoSetupStarted ||
            plugin.PiperVoiceCatalogService.IsBusy ||
            plugin.PiperVoiceCatalogService.FindExactInstalledVoice(cfg.TtsPiperVoiceId) != null)
            return;

        piperRecommendedAutoSetupStarted = true;
        StartPiperRecommendedSetup(switchBackendWhenReady: false);
    }

    private IEnumerable<PiperVoiceCatalogEntry> FilterPiperEntries(IEnumerable<PiperVoiceCatalogEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (piperInstalledFilter == 1 && !entry.Installed)
                continue;
            if (piperInstalledFilter == 2 && entry.Installed)
                continue;
            if (!LanguageFilterMatches(piperLanguageFilter, entry))
                continue;
            if (!FilterMatches(piperGenderFilter, entry.Gender))
                continue;
            if (!FilterMatches(piperQualityFilter, entry.Quality))
                continue;
            if (!FilterMatches(piperSourceFilter, entry.Source))
                continue;
            if (!SearchMatches(entry))
                continue;

            yield return entry;
        }
    }

    private IEnumerable<PiperVoiceCatalogEntry> SortPiperEntries(IEnumerable<PiperVoiceCatalogEntry> entries, Configuration cfg)
        => entries
            .OrderBy(entry => GetPiperPinRank(entry, cfg))
            .ThenBy(entry => entry.LanguageName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.LanguageCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.VoiceKey, StringComparer.OrdinalIgnoreCase);

    private static int GetPiperPinRank(PiperVoiceCatalogEntry entry, Configuration cfg)
    {
        if (entry.Installed && string.Equals(entry.CatalogId, cfg.TtsPiperVoiceId, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (string.Equals(entry.CatalogId, PiperVoiceCatalogService.RecommendedVoiceCatalogId, StringComparison.OrdinalIgnoreCase))
            return 1;

        return 2;
    }

    private bool SearchMatches(PiperVoiceCatalogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(piperSearchText))
            return true;

        var search = piperSearchText.Trim();
        return entry.CatalogId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               entry.VoiceKey.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               entry.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               entry.LanguageCode.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               entry.LanguageName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               entry.Gender.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               entry.Quality.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               entry.Source.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void StartPiperCatalogRefresh()
    {
        if (plugin.PiperVoiceCatalogService.IsBusy)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await plugin.PiperVoiceCatalogService.RefreshCatalogAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The service status is displayed in the UI.
            }
        });
    }

    private void StartPiperInstall(string catalogId)
    {
        if (plugin.PiperVoiceCatalogService.IsBusy)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await plugin.PiperVoiceCatalogService.InstallVoiceAsync(catalogId, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The service status is displayed in the UI.
            }
        });
    }

    private void StartPiperRuntimeInstall()
    {
        if (plugin.PiperVoiceCatalogService.IsBusy)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await plugin.PiperVoiceCatalogService.InstallPortableRuntimeAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The service status is displayed in the UI.
            }
        });
    }

    private void StartPiperPreviewSpeech(string text)
    {
        if (piperPreviewSpeechInProgress)
            return;

        var previewText = string.IsNullOrWhiteSpace(text) ? piperPreviewText : text;
        piperPreviewSpeechInProgress = true;
        _ = Task.Run(() =>
        {
            try
            {
                var wavPath = plugin.SpeechCacheService.GetOrCreatePiperPreviewWav(previewText, CancellationToken.None);
                plugin.AudioPlaybackService.PlayWavFileSync(wavPath, "Piper adapter preview");
                plugin.PrintStatus("Piper adapter preview played.");
            }
            catch (Exception ex)
            {
                plugin.PrintStatus("Piper adapter preview failed: " + ex.Message);
            }
            finally
            {
                piperPreviewSpeechInProgress = false;
            }
        });
    }

    private void StartPiperRecommendedSetup(bool switchBackendWhenReady)
    {
        if (plugin.PiperVoiceCatalogService.IsBusy)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await plugin.PiperVoiceCatalogService.EnsureRecommendedVoiceInstalledAsync(switchBackendWhenReady, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                if (switchBackendWhenReady && plugin.Configuration.TtsBackend == TtsBackend.PiperLocal)
                {
                    plugin.Configuration.TtsBackend = TtsBackend.LegacySapi;
                    plugin.Configuration.Save();
                }
            }
        });
    }

    private static string[] CreateFilterOptions(IEnumerable<string> values)
        => new[] { "All" }
            .Concat(values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            .ToArray();

    private static string[] CreateLanguageFilterOptions(IEnumerable<PiperVoiceCatalogEntry> entries)
        => new[] { "All" }
            .Concat(entries
                .Select(entry => FormatPiperLanguage(entry.LanguageName, entry.LanguageCode))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            .ToArray();

    private static bool FilterMatches(string filter, string value)
        => string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(filter, value, StringComparison.OrdinalIgnoreCase);

    private static bool LanguageFilterMatches(string filter, PiperVoiceCatalogEntry entry)
        => string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(filter, entry.LanguageCode, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(filter, entry.LanguageName, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(filter, FormatPiperLanguage(entry.LanguageName, entry.LanguageCode), StringComparison.OrdinalIgnoreCase);

    private static string FormatPiperVoiceLabel(PiperVoiceCatalogEntry entry)
        => $"{entry.VoiceKey} - {FormatPiperLanguage(entry.LanguageName, entry.LanguageCode)} - {entry.Gender} - {entry.Quality} - {entry.Source}";

    private static string FormatPiperInstalledVoiceLabel(PiperInstalledVoice voice)
        => $"{voice.VoiceKey} - {FormatPiperLanguage(voice.LanguageName, voice.LanguageCode)} - {voice.Gender} - {voice.Quality} - {voice.Source}";

    private static string FormatPiperLanguage(string languageName, string languageCode)
    {
        var name = languageName.Trim();
        var code = languageCode.Trim();

        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(code) && !string.Equals(name, code, StringComparison.OrdinalIgnoreCase))
            return $"{name} ({code})";

        if (!string.IsNullOrWhiteSpace(code))
            return code;

        return string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
    }

    private static string ShortHash(string hash)
        => string.IsNullOrWhiteSpace(hash) ? string.Empty : hash[..Math.Min(12, hash.Length)];

    private static string FormatPiperSemitones(double semitones)
        => semitones.ToString("+0.0;-0.0;0.0", UiText.Current.Culture);

    private static PiperCatalogColumnWidths CalculatePiperCatalogColumnWidths(IReadOnlyList<PiperVoiceCatalogEntry> entries)
    {
        var actionsWidth = new[] { "Details", "Install", "Select", "Uninstall" }.Sum(UiGui.ButtonWidth) + 36 * MaterialTheme.Metrics.Scale;
        var padding = 18 * MaterialTheme.Metrics.Scale;
        var states = entries.Select(entry => entry.Installed ? "Installed" : "Catalog").Append("Selected");
        return new PiperCatalogColumnWidths(
            NaturalColumnWidth("Voice", entries.Select(entry => entry.VoiceKey), padding),
            NaturalColumnWidth("Language", entries.Select(entry => FormatPiperLanguage(entry.LanguageName, entry.LanguageCode)), padding),
            NaturalColumnWidth("Gender", entries.Select(entry => entry.Gender), padding),
            NaturalColumnWidth("Quality", entries.Select(entry => entry.Quality), padding),
            NaturalColumnWidth("Source", entries.Select(entry => entry.Source), padding),
            NaturalColumnWidth("Size", entries.Select(entry => entry.SizeLabel), padding),
            NaturalColumnWidth("State", states, padding),
            actionsWidth);
    }

    private static float NaturalColumnWidth(string header, IEnumerable<string> values, float padding)
    {
        var maxText = MaterialText.Measure(UiText.T(header)).X;
        foreach (var value in values)
            maxText = Math.Max(maxText, MaterialText.Measure(string.IsNullOrWhiteSpace(value) ? " " : UiText.T(value)).X);

        return MathF.Ceiling((maxText * 1.1f) + padding);
    }

    private static void TooltipLastItemRaw(string text) { if (!string.IsNullOrWhiteSpace(text) && ImGui.IsItemHovered()) MaterialText.SetTooltip(text); }

    private static void TooltipLastItem(FormattableString text) => TooltipLastItem(UiText.Interpolated(text));

    private static void TooltipLastItem(string text)
    {
        if (!string.IsNullOrWhiteSpace(text) && ImGui.IsItemHovered())
            UiGui.SetTooltip(text);
    }

    private static void DrawWrappedStatus(FormattableString text, string tooltip) => DrawWrappedStatus(UiText.Interpolated(text), tooltip);

    private static void DrawWrappedStatus(string text, string tooltip, bool translate = true)
    {
        MaterialText.TextWrapped(translate ? UiText.T(text) : text);
        TooltipLastItem(tooltip);
    }

    private static void DrawDisabledStatus(FormattableString text, string tooltip) => DrawDisabledStatus(UiText.Interpolated(text), tooltip);

    private static void DrawDisabledStatus(string text, string tooltip)
    {
        UiGui.TextDisabled(text);
        TooltipLastItem(tooltip);
    }

    private static void DrawClippedTableText(string text, string tooltip)
    {
        MaterialText.Text(text);
        TooltipLastItem(string.IsNullOrWhiteSpace(tooltip) ? text : tooltip);
    }

    private sealed record PiperCatalogColumnWidths(
        float Voice,
        float Language,
        float Gender,
        float Quality,
        float Source,
        float Size,
        float State,
        float Actions);

    private void DrawCommentaryToggles(Configuration cfg)
    {
        var login = cfg.LoginCommentaryEnabled;
        if (UiGui.Checkbox("Login", ref login))
        {
            cfg.LoginCommentaryEnabled = login;
            cfg.Save();
        }
        TooltipLastItem("Allows one spoken line after the local player becomes ready this session.");

        var territory = cfg.TerritoryCommentaryEnabled;
        if (UiGui.Checkbox("Territory change", ref territory))
        {
            cfg.TerritoryCommentaryEnabled = territory;
            cfg.Save();
        }
        TooltipLastItem("Allows spoken lines when moving between territories, subject to its cooldown.");

        var idle = cfg.IdleCommentaryEnabled;
        if (UiGui.Checkbox("Idle", ref idle))
        {
            cfg.IdleCommentaryEnabled = idle;
            cfg.Save();
        }
        TooltipLastItem("Allows occasional spoken lines after the client has been idle long enough.");

        var combat = cfg.CombatCommentaryEnabled;
        if (UiGui.Checkbox("Combat start/end", ref combat))
        {
            cfg.CombatCommentaryEnabled = combat;
            cfg.Save();
        }
        TooltipLastItem("Allows spoken lines when combat starts or ends, subject to its cooldown.");

        var bgm = cfg.BgmMachinationsCommentaryEnabled;
        if (UiGui.Checkbox("BGM Machinations", ref bgm))
        {
            cfg.BgmMachinationsCommentaryEnabled = bgm;
            cfg.Save();
        }
        TooltipLastItem("Allows a spoken line when the Machinations BGM is detected, subject to its cooldown.");

        var expanded = cfg.ExpandedEventCommentaryEnabled;
        if (UiGui.Checkbox("Expanded events", ref expanded))
        {
            cfg.ExpandedEventCommentaryEnabled = expanded;
            cfg.Save();
        }
        TooltipLastItem("Allows extra condition-based event commentary such as mount, duty, crafting, and gathering transitions.");

        var triggerChance = cfg.ReadingRoegadynTriggerChancePercent;
        if (UiGui.SliderInt("Automatic trigger chance %", ref triggerChance, 0, 100))
        {
            cfg.ReadingRoegadynTriggerChancePercent = Math.Clamp(triggerChance, 0, 100);
            cfg.Save();
        }
        TooltipLastItem("Lower this if you want sequentially quick things to only sometimes trigger a comment instead of every eligible event speaking.");
    }

    private void DrawCooldowns(Configuration cfg)
    {
        var territoryCooldown = cfg.TerritoryCommentaryCooldownSeconds;
        if (UiGui.InputInt("Territory cooldown seconds", ref territoryCooldown))
        {
            cfg.TerritoryCommentaryCooldownSeconds = Math.Max(0, territoryCooldown);
            cfg.Save();
        }
        TooltipLastItem("Minimum time between territory-change comments; lower values can make travel chatty.");

        var idleCooldown = cfg.IdleCommentaryCooldownSeconds;
        if (UiGui.InputInt("Idle cooldown seconds", ref idleCooldown))
        {
            cfg.IdleCommentaryCooldownSeconds = Math.Max(30, idleCooldown);
            cfg.Save();
        }
        TooltipLastItem("Minimum idle time before another idle comment; values below 30 seconds are raised to 30.");

        var combatCooldown = cfg.CombatCommentaryCooldownSeconds;
        if (UiGui.InputInt("Combat cooldown seconds", ref combatCooldown))
        {
            cfg.CombatCommentaryCooldownSeconds = Math.Max(0, combatCooldown);
            cfg.Save();
        }
        TooltipLastItem("Minimum time between combat start/end comments.");

        var bgmCooldown = cfg.BgmCommentaryCooldownSeconds;
        if (UiGui.InputInt("BGM cooldown seconds", ref bgmCooldown))
        {
            cfg.BgmCommentaryCooldownSeconds = Math.Max(0, bgmCooldown);
            cfg.Save();
        }
        TooltipLastItem("Minimum time between Machinations BGM comments.");

        var expandedCooldown = cfg.ExpandedEventCooldownSeconds;
        if (UiGui.InputInt("Expanded event cooldown seconds", ref expandedCooldown))
        {
            cfg.ExpandedEventCooldownSeconds = Math.Max(0, expandedCooldown);
            cfg.Save();
        }
        TooltipLastItem("Minimum time between expanded event comments.");
    }

    private void ClearCacheToChat()
    {
        try
        {
            var deleted = plugin.SpeechCacheService.ClearCache();
            plugin.PrintStatus($"Cleared {deleted} cached speech WAV file(s).");
        }
        catch (Exception ex)
        {
            plugin.PrintStatus($"Failed to clear cache: {ex.Message}");
        }
    }

    private void ClearPiperCacheToChat()
    {
        try
        {
            var deleted = plugin.SpeechCacheService.ClearPiperWavCache();
            plugin.PrintStatus($"Cleared {deleted} cached Piper WAV file(s).");
        }
        catch (Exception ex)
        {
            plugin.PrintStatus($"Failed to clear Piper cache: {ex.Message}");
        }
    }
}
