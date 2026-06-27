using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dheacon.Services;

namespace Dheacon.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private static readonly string[] DtrModes = { "Text only", "Icon + text", "Icon only" };
    private static readonly string[] TtsBackendLabels = { "Modern Windows", "Legacy SAPI", "Piper local" };
    private static readonly string[] PiperInstalledFilters = { "All", "Installed", "Not installed" };

    private readonly Plugin plugin;
    private bool piperCatalogAutoRefreshStarted;
    private int piperInstalledFilter;
    private string piperLanguageFilter = "All";
    private string piperGenderFilter = "All";
    private string piperQualityFilter = "All";
    private string piperSourceFilter = "All";
    private string piperSearchText = string.Empty;
    private string selectedPiperCatalogId = string.Empty;
    private string piperPreviewText = "Reading Roegadyn reports FFXIV BGM 85 near Limsa Lominsa for Aelwyn Frost.";

    public ConfigWindow(Plugin plugin) : base($"{PluginInfo.DisplayName} Settings##Config")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(720f, 560f), MaximumSize = new Vector2(1500f, 1300f) };
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("DheaconSettingsTabs"))
            return;

        if (ImGui.BeginTabItem("General"))
        {
            DrawGeneralTab(plugin.Configuration);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Speech"))
        {
            DrawSpeechTab(plugin.Configuration);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Piper Voices"))
        {
            DrawPiperVoicesTab(plugin.Configuration);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Diagnostics"))
        {
            DrawDiagnosticsTab(plugin.Configuration);
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawGeneralTab(Configuration cfg)
    {
        var enabled = cfg.PluginEnabled;
        if (ImGui.Checkbox("Plugin enabled", ref enabled))
        {
            cfg.PluginEnabled = enabled;
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        ImGui.SameLine();
        DrawModeSelector(cfg);

        ImGui.Separator();
        ImGui.TextUnformatted("DTR");

        var dtr = cfg.DtrBarEnabled;
        if (ImGui.Checkbox("Show DTR bar entry", ref dtr))
        {
            cfg.DtrBarEnabled = dtr;
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        var dtrMode = cfg.DtrBarMode;
        if (ImGui.Combo("DTR mode", ref dtrMode, DtrModes, DtrModes.Length))
        {
            cfg.DtrBarMode = dtrMode;
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        var onIcon = cfg.DtrIconEnabled;
        if (ImGui.InputText("DTR enabled glyph", ref onIcon, 8))
        {
            cfg.DtrIconEnabled = onIcon.Length <= 3 ? onIcon : onIcon[..3];
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        var offIcon = cfg.DtrIconDisabled;
        if (ImGui.InputText("DTR disabled glyph", ref offIcon, 8))
        {
            cfg.DtrIconDisabled = offIcon.Length <= 3 ? offIcon : offIcon[..3];
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        ImGui.Separator();
        if (cfg.CommentaryMode == CommentaryMode.Dheacon)
            DrawDheaconSettings(cfg);
        else
            DrawReadingRoegadynGeneralSettings(cfg);
    }

    private void DrawSpeechTab(Configuration cfg)
    {
        if (ImGui.Button("Test speech"))
        {
            var queued = plugin.CommentaryTriggerService.SpeakManual();
            plugin.PrintStatus(queued ? "Speech queued." : plugin.CommentaryTriggerService.LastDecision);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear cache"))
            ClearCacheToChat();

        ImGui.SameLine();
        if (ImGui.Button("Clear Piper WAV cache"))
            ClearPiperCacheToChat();

        ImGui.Separator();
        DrawBackendSelector(cfg);
        ImGui.TextWrapped("Selected voice: " + plugin.SpeechCacheService.GetSelectedVoiceLabel());
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
        EnsureSelectedPiperEntry(entries, cfg);

        DrawPiperSetupStrip(cfg);
        ImGui.Separator();

        ImGui.SetNextItemWidth(Math.Min(360f, ImGui.GetContentRegionAvail().X));
        ImGui.InputText("Search", ref piperSearchText, 160);
        DrawPiperFilters(entries);

        var filtered = SortPiperEntries(FilterPiperEntries(entries), cfg).ToList();
        ImGui.TextDisabled($"Showing {filtered.Count} of {entries.Count} catalog entr{(entries.Count == 1 ? "y" : "ies")}.");
        DrawPiperSelectedActionBar(entries, cfg);
        DrawPiperCatalogTable(filtered, cfg);
        DrawPiperSelectedVoicePanel(entries, cfg);
    }

    private void DrawDiagnosticsTab(Configuration cfg)
    {
        if (ImGui.Button("Refresh voices"))
        {
            plugin.SpeechCacheService.RefreshInstalledVoices();
            var modernCount = plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.ModernWindows).Count;
            var legacyCount = plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.LegacySapi).Count;
            var piperCount = plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.PiperLocal).Count;
            plugin.PrintStatus($"Detected {modernCount} Modern Windows voice(s), {legacyCount} Legacy SAPI voice(s), {piperCount} Piper voice(s).");
        }

        ImGui.SameLine();
        if (ImGui.Button("Status to chat"))
            plugin.PrintStatus(cfg.CommentaryMode == CommentaryMode.Dheacon ? plugin.AetheryteTriggerService.LastDecision : plugin.CommentaryTriggerService.LastDecision);

        ImGui.Separator();
        ImGui.Text($"Modern Windows voices: {plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.ModernWindows).Count}");
        ImGui.Text($"Legacy SAPI voices: {plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.LegacySapi).Count}");
        ImGui.Text($"Piper voices: {plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.PiperLocal).Count}");
        ImGui.TextWrapped("Piper runtime: " + plugin.PiperVoiceCatalogService.RefreshRuntimeStatus(save: false));
        ImGui.TextWrapped("Piper catalog: " + plugin.PiperVoiceCatalogService.LastStatus);
        if (!string.IsNullOrWhiteSpace(plugin.PiperVoiceCatalogService.LastError))
            ImGui.TextWrapped("Piper warning: " + plugin.PiperVoiceCatalogService.LastError);

        ImGui.Separator();
        ImGui.TextWrapped("Adapter service: " + plugin.SpokenTextAdapterService.LastStatus);
        if (!string.IsNullOrWhiteSpace(plugin.SpokenTextAdapterService.LastError))
            ImGui.TextWrapped("Adapter warning: " + plugin.SpokenTextAdapterService.LastError);
        ImGui.TextWrapped($"Last original: {plugin.SpeechCacheService.LastOriginalText}");
        ImGui.TextWrapped($"Last adapted: {plugin.SpeechCacheService.LastAdaptedText}");
        if (!string.IsNullOrWhiteSpace(plugin.SpeechCacheService.LastTextAdapterId))
        {
            ImGui.TextWrapped(
                $"Last adapter: {plugin.SpeechCacheService.LastTextAdapterId} {plugin.SpeechCacheService.LastTextAdapterVersion} {ShortHash(plugin.SpeechCacheService.LastTextAdapterContentHash)}");
        }

        ImGui.Separator();
        ImGui.TextWrapped($"Trigger status: {plugin.CommentaryTriggerService.LastDecision}");
        ImGui.TextWrapped($"Queue status: {plugin.SpeechQueueService.LastStatus}");
        if (!string.IsNullOrWhiteSpace(plugin.SpeechQueueService.LastError))
            ImGui.TextWrapped("Queue error: " + plugin.SpeechQueueService.LastError);
        ImGui.Text($"Pending speech requests: {plugin.SpeechQueueService.PendingCount}");
        ImGui.Text($"Speech busy: {plugin.SpeechQueueService.IsBusy}");
        ImGui.TextWrapped($"BGM status: {plugin.BgmProbeService.Status}");
        ImGui.Text($"Current BGM ID: {plugin.BgmProbeService.CurrentBgmId}");
        ImGui.TextWrapped($"Cache status: {plugin.SpeechCacheService.LastStatus}");
        if (!string.IsNullOrWhiteSpace(plugin.SpeechCacheService.LastError))
            ImGui.TextWrapped("Speech warning: " + plugin.SpeechCacheService.LastError);
    }

    private void DrawModeSelector(Configuration cfg)
    {
        if (ImGui.RadioButton("Dheacon", cfg.CommentaryMode == CommentaryMode.Dheacon))
        {
            cfg.CommentaryMode = CommentaryMode.Dheacon;
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Reading Roegadyn", cfg.CommentaryMode == CommentaryMode.ReadingRoegadyn))
        {
            cfg.CommentaryMode = CommentaryMode.ReadingRoegadyn;
            cfg.Save();
            plugin.UpdateDtrBar();
        }
    }

    private void DrawDheaconSettings(Configuration cfg)
    {
        ImGui.TextUnformatted("Dheacon");

        var suppressTeleport = cfg.SuppressTeleportAndReturnTransitions;
        if (ImGui.Checkbox("Suppress teleports and return", ref suppressTeleport))
        {
            cfg.SuppressTeleportAndReturnTransitions = suppressTeleport;
            cfg.Save();
        }

        var soundPath = cfg.AlertSoundRelativePath;
        if (ImGui.InputText("Alert sound path", ref soundPath, 260))
        {
            cfg.AlertSoundRelativePath = soundPath;
            cfg.Save();
        }

        ImGui.TextWrapped("Alert sound: " + plugin.AudioPlaybackService.GetResolvedAlertPath());
    }

    private void DrawReadingRoegadynGeneralSettings(Configuration cfg)
    {
        ImGui.TextUnformatted("Reading Roegadyn");

        var suppressTeleport = cfg.SuppressTeleportAndReturnTransitions;
        if (ImGui.Checkbox("Suppress teleports and return", ref suppressTeleport))
        {
            cfg.SuppressTeleportAndReturnTransitions = suppressTeleport;
            cfg.Save();
        }

        DrawCommentaryToggles(cfg);
        DrawCooldowns(cfg);
    }

    private void DrawSpeechControls(Configuration cfg)
    {
        var rate = cfg.TtsRate;
        if (ImGui.SliderInt("Rate", ref rate, -10, 10))
        {
            cfg.TtsRate = rate;
            cfg.Save();
        }

        var volume = cfg.TtsVolume;
        if (ImGui.SliderInt("Synth volume", ref volume, 0, 100))
        {
            cfg.TtsVolume = volume;
            cfg.Save();
        }

        var pitch = (float)cfg.TtsPitch;
        if (ImGui.SliderFloat("Pitch", ref pitch, 0.25f, 2.0f, "%.2f"))
        {
            cfg.TtsPitch = Math.Clamp(pitch, 0.0f, 2.0f);
            cfg.Save();
        }

        var outputGain = cfg.TtsOutputGainPercent;
        if (ImGui.SliderInt("Output gain %", ref outputGain, 0, 400))
        {
            cfg.TtsOutputGainPercent = Math.Clamp(outputGain, 0, 400);
            cfg.Save();
        }
    }

    private void DrawSpeechCacheSettings(Configuration cfg)
    {
        var cacheDirectory = cfg.TtsCacheDirectory;
        if (ImGui.InputText("Cache folder", ref cacheDirectory, 512))
        {
            cfg.TtsCacheDirectory = cacheDirectory;
            cfg.Save();
        }

        ImGui.TextWrapped("Resolved cache folder: " + cfg.GetResolvedTtsCacheDirectory());

        var maxMb = cfg.TtsMaxCacheMegabytes;
        if (ImGui.InputInt("Max cache MB", ref maxMb))
        {
            cfg.TtsMaxCacheMegabytes = Math.Max(1, maxMb);
            cfg.Save();
        }

        ImGui.Text($"Cache size: {plugin.SpeechCacheService.GetCacheSizeMegabytes():F1} MB");
        ImGui.TextWrapped("Cache status: " + plugin.SpeechCacheService.LastStatus);
    }

    private void DrawTextAdapterSettings(Configuration cfg)
    {
        var adapterEnabled = cfg.TtsPiperTextAdapterEnabled;
        if (ImGui.Checkbox("Piper text adapter", ref adapterEnabled))
        {
            cfg.TtsPiperTextAdapterEnabled = adapterEnabled;
            cfg.Save();
        }

        var adapters = plugin.SpokenTextAdapterService.GetAdapters();
        var adapterLabel = string.IsNullOrWhiteSpace(cfg.TtsPiperTextAdapterId) ? SpokenTextAdapterService.DefaultAdapterId : cfg.TtsPiperTextAdapterId;
        if (ImGui.BeginCombo("Adapter", adapterLabel))
        {
            foreach (var adapter in adapters)
            {
                var selected = string.Equals(cfg.TtsPiperTextAdapterId, adapter.Id, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable($"{adapter.Id} - {adapter.SourceLanguage} to {adapter.TargetLanguage}", selected))
                {
                    cfg.TtsPiperTextAdapterId = adapter.Id;
                    cfg.Save();
                }
            }

            ImGui.EndCombo();
        }

        var selectedAdapter = plugin.SpokenTextAdapterService.GetAdapterInfo(cfg.TtsPiperTextAdapterId)
            ?? plugin.SpokenTextAdapterService.GetAdapterInfo(SpokenTextAdapterService.DefaultAdapterId);
        if (selectedAdapter != null)
            ImGui.TextWrapped($"Adapter version: {selectedAdapter.Version}  Hash: {ShortHash(selectedAdapter.ContentHash)}");

        ImGui.InputText("Preview text", ref piperPreviewText, 1024);
        var preview = plugin.SpeechCacheService.PreviewPiperText(piperPreviewText);
        ImGui.TextWrapped($"Preview adapter: {(string.IsNullOrWhiteSpace(preview.AdapterId) ? "none" : preview.AdapterId)} {preview.AdapterVersion} {ShortHash(preview.AdapterContentHash)}");

        if (ImGui.BeginTable("AdapterPreviewTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Original");
            ImGui.TableSetupColumn("Adapted");
            ImGui.TableHeadersRow();
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextWrapped(preview.Original);
            ImGui.TableSetColumnIndex(1);
            ImGui.TextWrapped(preview.Adapted);
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
        var currentVoice = plugin.SpeechCacheService.GetSelectedVoiceLabel();

        if (!ImGui.BeginCombo("Voice", currentVoice))
            return;

        var defaultSelected = cfg.TtsBackend == TtsBackend.ModernWindows
            ? string.IsNullOrWhiteSpace(cfg.TtsModernVoiceId) && string.IsNullOrWhiteSpace(cfg.TtsVoiceName)
            : string.IsNullOrWhiteSpace(cfg.TtsVoiceName);
        if (ImGui.Selectable("Windows default", defaultSelected))
        {
            if (cfg.TtsBackend == TtsBackend.ModernWindows)
                cfg.TtsModernVoiceId = string.Empty;

            cfg.TtsVoiceName = string.Empty;
            cfg.Save();
        }

        foreach (var voice in voices)
        {
            var selected = cfg.TtsBackend == TtsBackend.ModernWindows
                ? string.Equals(cfg.TtsModernVoiceId, voice.Id, StringComparison.OrdinalIgnoreCase) ||
                  (string.IsNullOrWhiteSpace(cfg.TtsModernVoiceId) &&
                   string.Equals(cfg.TtsVoiceName, voice.DisplayName, StringComparison.OrdinalIgnoreCase))
                : string.Equals(cfg.TtsVoiceName, voice.DisplayName, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(voice.Label, selected))
            {
                if (cfg.TtsBackend == TtsBackend.ModernWindows)
                    cfg.TtsModernVoiceId = voice.Id;

                cfg.TtsVoiceName = voice.DisplayName;
                cfg.Save();
            }
        }

        ImGui.EndCombo();
    }

    private void DrawBackendSelector(Configuration cfg)
    {
        var backendIndex = cfg.TtsBackend switch
        {
            TtsBackend.LegacySapi => 1,
            TtsBackend.PiperLocal => 2,
            _ => 0,
        };

        if (ImGui.Combo("Backend", ref backendIndex, TtsBackendLabels, TtsBackendLabels.Length))
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
    }

    private void DrawVoiceActions()
    {
        if (ImGui.Button("Refresh voices"))
        {
            plugin.SpeechCacheService.RefreshInstalledVoices();
            var modernCount = plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.ModernWindows).Count;
            var legacyCount = plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.LegacySapi).Count;
            var piperCount = plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.PiperLocal).Count;
            plugin.PrintStatus($"Detected {modernCount} Modern Windows voice(s), {legacyCount} Legacy SAPI voice(s), {piperCount} Piper voice(s).");
        }

        ImGui.SameLine();
        if (ImGui.Button("Pick Swedish Windows"))
        {
            var selected = plugin.SpeechCacheService.SelectFirstSwedishVoice(out var usedMaleVoice);
            if (selected == null)
            {
                plugin.PrintStatus("No Swedish Modern Windows voice was detected.");
            }
            else if (usedMaleVoice)
            {
                plugin.PrintStatus($"Selected Swedish male voice: {selected.Label}.");
            }
            else
            {
                plugin.PrintStatus($"No Swedish male voice was detected; selected first Swedish voice: {selected.Label}.");
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Set up Piper NST"))
            StartPiperRecommendedSetup(switchBackendWhenReady: true);
    }

    private void DrawPiperInstalledVoiceSelector(Configuration cfg)
    {
        var voices = plugin.PiperVoiceCatalogService.GetInstalledVoices();
        var currentVoice = plugin.PiperVoiceCatalogService.FindInstalledVoice(cfg.TtsPiperVoiceId) is { } selectedVoice
            ? FormatPiperInstalledVoiceLabel(selectedVoice)
            : "No Piper voice installed";

        if (!ImGui.BeginCombo("Piper voice", currentVoice))
            return;

        foreach (var voice in voices)
        {
            var label = FormatPiperInstalledVoiceLabel(voice);
            var selected = string.Equals(cfg.TtsPiperVoiceId, voice.CatalogId, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(label, selected))
            {
                plugin.PiperVoiceCatalogService.SelectVoice(voice.CatalogId);
                cfg.Save();
            }
        }

        if (voices.Count == 0)
            ImGui.TextDisabled("No Piper voices installed.");

        ImGui.EndCombo();
    }

    private void DrawPiperSetupStrip(Configuration cfg)
    {
        ImGui.TextWrapped(plugin.PiperVoiceCatalogService.RefreshRuntimeStatus(save: false));
        ImGui.TextWrapped(plugin.PiperVoiceCatalogService.LastStatus);
        if (!string.IsNullOrWhiteSpace(plugin.PiperVoiceCatalogService.LastError))
            ImGui.TextWrapped("Piper warning: " + plugin.PiperVoiceCatalogService.LastError);

        if (plugin.PiperVoiceCatalogService.IsBusy)
        {
            if (plugin.PiperVoiceCatalogService.OperationProgress >= 0d)
                ImGui.ProgressBar((float)plugin.PiperVoiceCatalogService.OperationProgress, new Vector2(-1f, 0f));
            else
                ImGui.TextDisabled("Piper operation in progress...");
        }

        if (ImGui.Button("Refresh catalog"))
            StartPiperCatalogRefresh();

        ImGui.SameLine();
        if (ImGui.Button("Install runtime"))
            StartPiperRuntimeInstall();

        ImGui.SameLine();
        if (ImGui.Button("Open folder"))
            plugin.PiperVoiceCatalogService.OpenFolder(cfg.GetResolvedPiperRootDirectory());

        var recommendedInstalled = plugin.PiperVoiceCatalogService.FindInstalledVoice(PiperVoiceCatalogService.RecommendedVoiceCatalogId) != null;
        ImGui.SameLine();
        if (recommendedInstalled)
        {
            if (ImGui.Button("Select NST"))
                plugin.PiperVoiceCatalogService.SelectVoice(PiperVoiceCatalogService.RecommendedVoiceCatalogId);
        }
        else if (ImGui.Button("Install NST"))
        {
            StartPiperRecommendedSetup(switchBackendWhenReady: false);
        }

        var runtimePath = cfg.TtsPiperRuntimePath;
        if (ImGui.InputText("Piper runtime path", ref runtimePath, 512))
        {
            cfg.TtsPiperRuntimePath = runtimePath;
            plugin.PiperVoiceCatalogService.RefreshRuntimeStatus();
        }
    }

    private void DrawPiperFilters(IReadOnlyList<PiperVoiceCatalogEntry> entries)
    {
        ImGui.Combo("Installed filter", ref piperInstalledFilter, PiperInstalledFilters, PiperInstalledFilters.Length);

        var languages = CreateLanguageFilterOptions(entries);
        DrawStringFilter("Language", languages, ref piperLanguageFilter);

        var genders = CreateFilterOptions(entries.Select(entry => entry.Gender));
        DrawStringFilter("Gender", genders, ref piperGenderFilter);

        var qualities = CreateFilterOptions(entries.Select(entry => entry.Quality));
        DrawStringFilter("Quality", qualities, ref piperQualityFilter);

        var sources = CreateFilterOptions(entries.Select(entry => entry.Source));
        DrawStringFilter("Source", sources, ref piperSourceFilter);
    }

    private static void DrawStringFilter(string label, string[] options, ref string selected)
    {
        var current = selected;
        var index = Array.FindIndex(options, option => string.Equals(option, current, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            index = 0;

        if (ImGui.Combo(label, ref index, options, options.Length))
            selected = options[Math.Clamp(index, 0, options.Length - 1)];
    }

    private void DrawPiperSelectedActionBar(IReadOnlyList<PiperVoiceCatalogEntry> entries, Configuration cfg)
    {
        var entry = entries.FirstOrDefault(candidate => string.Equals(candidate.CatalogId, selectedPiperCatalogId, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            ImGui.TextDisabled("Select a Piper voice to manage it.");
            return;
        }

        var isCurrent = entry.Installed && string.Equals(cfg.TtsPiperVoiceId, entry.CatalogId, StringComparison.OrdinalIgnoreCase);

        ImGui.PushID("SelectedPiperActionBar");
        ImGui.TextUnformatted("Voice:");
        ImGui.SameLine();
        ImGui.TextWrapped(FormatPiperVoiceLabel(entry));

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Actions:");
        ImGui.SameLine();

        ImGui.PushID(entry.CatalogId);
        if (!entry.Installed)
        {
            if (ImGui.Button("Install"))
                StartPiperInstall(entry.CatalogId);
        }
        else
        {
            if (isCurrent)
            {
                ImGui.TextDisabled("Selected");
            }
            else if (ImGui.Button("Select"))
            {
                plugin.PiperVoiceCatalogService.SelectVoice(entry.CatalogId);
            }

            ImGui.SameLine();
            if (ImGui.Button("Uninstall"))
                plugin.PiperVoiceCatalogService.UninstallVoice(entry.CatalogId);

            ImGui.SameLine();
            if (ImGui.Button("Open folder"))
                plugin.PiperVoiceCatalogService.OpenFolder(entry.InstalledDirectory);
        }

        ImGui.PopID();
        ImGui.PopID();
    }

    private void DrawPiperCatalogTable(IReadOnlyList<PiperVoiceCatalogEntry> entries, Configuration cfg)
    {
        var tableHeight = Math.Max(220f, ImGui.GetContentRegionAvail().Y * 0.48f);
        var tableFlags = ImGuiTableFlags.Borders |
                         ImGuiTableFlags.RowBg |
                         ImGuiTableFlags.Resizable |
                         ImGuiTableFlags.ScrollY |
                         ImGuiTableFlags.SizingStretchProp |
                         ImGuiTableFlags.NoHostExtendX;

        if (!ImGui.BeginTable(
                "PiperCatalogTable",
                8,
                tableFlags,
                new Vector2(-1f, tableHeight)))
            return;

        ImGui.TableSetupColumn("Voice", ImGuiTableColumnFlags.WidthStretch, 2.2f);
        ImGui.TableSetupColumn("Language", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("Gender", ImGuiTableColumnFlags.WidthFixed, 76f);
        ImGui.TableSetupColumn("Quality", ImGuiTableColumnFlags.WidthFixed, 74f);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, 78f);
        ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 76f);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 190f);
        ImGui.TableHeadersRow();

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
        if (ImGui.Selectable($"{entry.VoiceKey}##voice", selected))
            selectedPiperCatalogId = entry.CatalogId;

        ImGui.TableSetColumnIndex(1);
        ImGui.TextWrapped(FormatPiperLanguage(entry.LanguageName, entry.LanguageCode));
        ImGui.TableSetColumnIndex(2);
        ImGui.TextUnformatted(entry.Gender);
        ImGui.TableSetColumnIndex(3);
        ImGui.TextUnformatted(entry.Quality);
        ImGui.TableSetColumnIndex(4);
        ImGui.TextWrapped(entry.Source);
        ImGui.TableSetColumnIndex(5);
        ImGui.TextUnformatted(entry.SizeLabel);
        ImGui.TableSetColumnIndex(6);
        ImGui.TextUnformatted(isCurrent ? "Selected" : entry.Installed ? "Installed" : "Catalog");
        ImGui.TableSetColumnIndex(7);
        DrawPiperCatalogRowActions(entry, isCurrent);
        ImGui.PopID();
    }

    private void DrawPiperCatalogRowActions(PiperVoiceCatalogEntry entry, bool isCurrent)
    {
        if (ImGui.SmallButton("Details"))
            selectedPiperCatalogId = entry.CatalogId;

        if (!entry.Installed)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Install"))
                StartPiperInstall(entry.CatalogId);
            return;
        }

        if (!isCurrent)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Select"))
                plugin.PiperVoiceCatalogService.SelectVoice(entry.CatalogId);
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Uninstall"))
            plugin.PiperVoiceCatalogService.UninstallVoice(entry.CatalogId);
    }

    private void DrawPiperSelectedVoicePanel(IReadOnlyList<PiperVoiceCatalogEntry> entries, Configuration cfg)
    {
        var entry = entries.FirstOrDefault(candidate => string.Equals(candidate.CatalogId, selectedPiperCatalogId, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return;

        ImGui.Separator();
        var isCurrent = entry.Installed && string.Equals(cfg.TtsPiperVoiceId, entry.CatalogId, StringComparison.OrdinalIgnoreCase);
        ImGui.TextUnformatted("Selected voice details");
        ImGui.TextWrapped(isCurrent ? "State: selected Piper voice" : entry.Installed ? "State: installed" : "State: not installed");
        ImGui.TextWrapped(FormatPiperVoiceLabel(entry));
        ImGui.TextWrapped($"Language: {FormatPiperLanguage(entry.LanguageName, entry.LanguageCode)}");
        ImGui.TextWrapped($"{entry.DisplayName}  {entry.SizeLabel}");
        ImGui.TextWrapped($"License: {entry.License}");
        if (!string.IsNullOrWhiteSpace(entry.Notes))
            ImGui.TextWrapped(entry.Notes);

        ImGui.PushID(entry.CatalogId);
        if (entry.Installed)
        {
            if (isCurrent)
                ImGui.TextDisabled("Selected");
            else if (ImGui.Button("Select"))
                plugin.PiperVoiceCatalogService.SelectVoice(entry.CatalogId);

            ImGui.SameLine();
            if (ImGui.Button("Uninstall"))
                plugin.PiperVoiceCatalogService.UninstallVoice(entry.CatalogId);

            ImGui.SameLine();
            if (ImGui.Button("Open folder"))
                plugin.PiperVoiceCatalogService.OpenFolder(entry.InstalledDirectory);
        }
        else
        {
            if (ImGui.Button("Install"))
                StartPiperInstall(entry.CatalogId);
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

    private void DrawCommentaryToggles(Configuration cfg)
    {
        var login = cfg.LoginCommentaryEnabled;
        if (ImGui.Checkbox("Login", ref login))
        {
            cfg.LoginCommentaryEnabled = login;
            cfg.Save();
        }

        var territory = cfg.TerritoryCommentaryEnabled;
        if (ImGui.Checkbox("Territory change", ref territory))
        {
            cfg.TerritoryCommentaryEnabled = territory;
            cfg.Save();
        }

        var idle = cfg.IdleCommentaryEnabled;
        if (ImGui.Checkbox("Idle", ref idle))
        {
            cfg.IdleCommentaryEnabled = idle;
            cfg.Save();
        }

        var combat = cfg.CombatCommentaryEnabled;
        if (ImGui.Checkbox("Combat start/end", ref combat))
        {
            cfg.CombatCommentaryEnabled = combat;
            cfg.Save();
        }

        var bgm = cfg.BgmMachinationsCommentaryEnabled;
        if (ImGui.Checkbox("BGM Machinations", ref bgm))
        {
            cfg.BgmMachinationsCommentaryEnabled = bgm;
            cfg.Save();
        }

        var expanded = cfg.ExpandedEventCommentaryEnabled;
        if (ImGui.Checkbox("Expanded events", ref expanded))
        {
            cfg.ExpandedEventCommentaryEnabled = expanded;
            cfg.Save();
        }

        var triggerChance = cfg.ReadingRoegadynTriggerChancePercent;
        if (ImGui.SliderInt("Automatic trigger chance %", ref triggerChance, 0, 100))
        {
            cfg.ReadingRoegadynTriggerChancePercent = Math.Clamp(triggerChance, 0, 100);
            cfg.Save();
        }
    }

    private void DrawCooldowns(Configuration cfg)
    {
        var territoryCooldown = cfg.TerritoryCommentaryCooldownSeconds;
        if (ImGui.InputInt("Territory cooldown seconds", ref territoryCooldown))
        {
            cfg.TerritoryCommentaryCooldownSeconds = Math.Max(0, territoryCooldown);
            cfg.Save();
        }

        var idleCooldown = cfg.IdleCommentaryCooldownSeconds;
        if (ImGui.InputInt("Idle cooldown seconds", ref idleCooldown))
        {
            cfg.IdleCommentaryCooldownSeconds = Math.Max(30, idleCooldown);
            cfg.Save();
        }

        var combatCooldown = cfg.CombatCommentaryCooldownSeconds;
        if (ImGui.InputInt("Combat cooldown seconds", ref combatCooldown))
        {
            cfg.CombatCommentaryCooldownSeconds = Math.Max(0, combatCooldown);
            cfg.Save();
        }

        var bgmCooldown = cfg.BgmCommentaryCooldownSeconds;
        if (ImGui.InputInt("BGM cooldown seconds", ref bgmCooldown))
        {
            cfg.BgmCommentaryCooldownSeconds = Math.Max(0, bgmCooldown);
            cfg.Save();
        }

        var expandedCooldown = cfg.ExpandedEventCooldownSeconds;
        if (ImGui.InputInt("Expanded event cooldown seconds", ref expandedCooldown))
        {
            cfg.ExpandedEventCooldownSeconds = Math.Max(0, expandedCooldown);
            cfg.Save();
        }
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
