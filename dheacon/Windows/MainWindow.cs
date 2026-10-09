using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using AethertekUI;
using AethertekUI.Dalamud;
using Dheacon.Ui;

namespace Dheacon.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly MaterialWindowMotion windowMotion = new();
    private readonly AethertekUI.MaterialWindowOpacity windowOpacity = new();
    private static readonly TimeSpan StatusRenderWarningInterval = TimeSpan.FromSeconds(10);

    private readonly Plugin plugin;
    private DateTime lastStatusRenderWarningUtc = DateTime.MinValue;
    private Vector2 contentFramePadding;
    private ImGuiDir menuButtonPosition;
    private static string MainTitle => UiText.T("Dheacon — main (speech settings)") + " v" + (typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0");

    public MainWindow(Plugin plugin) : base($"{PluginInfo.DisplayName}##Main")
    {
        this.plugin = plugin;
        Flags |= ImGuiWindowFlags.HorizontalScrollbar;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(560f, 430f), MaximumSize = new Vector2(1400f, 1200f) };
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Cog, Priority = 0, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.ToggleConfigUi(); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("Settings")),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.WindowMinimize, Priority = -10, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.ToggleMiniUi(); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("Mini Window")),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.PowerOff, Priority = -20, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.SetPluginEnabledFromUi(!plugin.Configuration.PluginEnabled); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("Enabled") + "\n" + UiText.T(plugin.Configuration.PluginEnabled ? "On" : "Off")),
        });
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        var style = ImGui.GetStyle(); var scale = MaterialTheme.Metrics.Scale;
        contentFramePadding = style.FramePadding; menuButtonPosition = style.WindowMenuButtonPosition;
        style.WindowMenuButtonPosition = ImGuiDir.Right;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, (DheaconPresentation.Compact ? new Vector2(12, 4) : new Vector2(15)) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(contentFramePadding.X, Math.Max(0, ((DheaconPresentation.Compact ? 32 : 37) * scale - ImGui.GetFontSize()) * .5f)));
        UiGui.ReserveTitleSpace(this, MainTitle, 560);
        windowMotion.Prepare(this, reducedMotion: false, roundedCorners: true);
    }

    public override void PostDraw()
    {
        windowMotion.Restore(this);
        UiGui.PaintTitleWithImage(this, MainTitle);
        plugin.Appearance.ApplyWindowOpacity(windowOpacity, WindowName);
        ImGui.PopStyleVar(2);
        ImGui.GetStyle().WindowMenuButtonPosition = menuButtonPosition;
    }

    public override void Draw()
    {
        windowMotion.DrawChrome();
        using var bodyStyle = new MaterialStyleScope();
        bodyStyle.Style(ImGuiStyleVar.FramePadding, contentFramePadding);
        var cfg = plugin.Configuration;
        var s = MaterialTheme.Metrics.Scale;
        var compact = DheaconPresentation.Compact;
        var gap = DheaconPresentation.Gap * s;
        var c = MaterialTheme.Current.Colors;
        var nativeWindow = ImGuiP.GetCurrentWindow();
        var start = ImGui.GetCursorScreenPos() + new Vector2(0, compact ? 8 * s : 0);
        var width = Math.Min(ImGui.GetContentRegionAvail().X,
            nativeWindow.Size.X - 2 * ImGui.GetStyle().WindowPadding.X - nativeWindow.ScrollbarSizes.X);
        var iconMin = start + (compact ? new Vector2(12, 8) * s : Vector2.Zero);
        DheaconPresentation.DrawPluginIcon(ImGui.GetWindowDrawList(), iconMin, iconMin + new Vector2((compact ? 42 : 44) * s));
        ImGui.SetCursorScreenPos(start + new Vector2((compact ? 76 : 66) * s, 0));
        using (UiText.Font(compact ? UiFontRole.CompactTitle : UiFontRole.Title)) MaterialText.Text(PluginInfo.DisplayName);
        if (ImGui.IsItemHovered()) UiGui.SetTooltip(UiText.F("Command: {0} · v{1}", PluginInfo.Command, Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0"));
        ImGui.SetCursorScreenPos(start + new Vector2((compact ? 76 : 66) * s, (compact ? 46 : 50) * s));
        MaterialText.TextColored(c.OnSurfaceVariant, UiText.T("Text to speech for FFXIV (Dalamud plugin)"));
        var subtitleBottom = ImGui.GetItemRectMax().Y;
        var selectorWidth = cfg.UiLanguageVisibleOnMainWindow ? 180 * s : 0;
        var toggleHeight = (compact ? 22 : 24) * s;
        float ToggleWidth(string label) => toggleHeight + ImGui.GetStyle().ItemInnerSpacing.X + MaterialText.Measure(UiText.T(label)).X;
        var topWidth = UiGui.IconButtonWidth("Ko-fi", UiText.T("Support on Ko-fi"), MaterialIcon.ExternalLink) + selectorWidth + ImGui.GetStyle().ItemSpacing.X;
        var bottomWidth = (cfg.UiCompactVisibleOnMainWindow ? ToggleWidth("C") + ImGui.GetStyle().ItemSpacing.X : 0) + ToggleWidth("Transparency") + ToggleWidth("Enabled") + ToggleWidth("DTR Bar") + UiGui.IconButtonWidth("Settings") + UiGui.IconButtonWidth("Status to chat") + ImGui.GetStyle().ItemSpacing.X * 4;
        var rightWidth = Math.Max(topWidth, bottomWidth);
        var leftWidth = (compact ? 76 : 66) * s + MaterialText.Measure(UiText.T("Text to speech for FFXIV (Dalamud plugin)")).X;
        var right = width >= leftWidth + rightWidth + gap;
        ImGui.SetCursorScreenPos(right ? start + new Vector2(width - topWidth, 0) : new Vector2(start.X, Math.Max(subtitleBottom + gap, start.Y + (compact ? 70 : 84) * s)));
        if (UiGui.IconButton("Ko-fi", MaterialIcon.Heart, UiText.T("Support on Ko-fi"), MaterialIcon.ExternalLink))
            Process.Start(new ProcessStartInfo { FileName = PluginInfo.SupportUrl, UseShellExecute = true });
        if (cfg.UiLanguageVisibleOnMainWindow)
        { UiGui.SameLineIfFits(selectorWidth); plugin.Appearance.DrawLanguage(); }
        var toolbarY = start.Y + Math.Max((compact ? 48 : 57) * s, ImGui.GetItemRectMax().Y - start.Y + ImGui.GetStyle().ItemSpacing.Y);
        if (right) ImGui.SetCursorScreenPos(new Vector2(start.X + width - bottomWidth, toolbarY + (compact ? 7 : 8) * s));
        using (var toggleStyle = new MaterialStyleScope())
        {
            toggleStyle.Style(ImGuiStyleVar.FramePadding, new Vector2(ImGui.GetStyle().FramePadding.X, Math.Max(0, (toggleHeight - ImGui.GetFontSize()) * .5f)));
            if (cfg.UiCompactVisibleOnMainWindow)
            {
                var density = cfg.UiCompact;
                if (UiGui.Checkbox("C", ref density)) { cfg.UiCompact = density; cfg.Save(); }
                if (ImGui.IsItemHovered()) UiGui.SetTooltip("Compact mode");
            }
            UiGui.SameLineIfFits(ToggleWidth("Transparency"));
            plugin.Appearance.DrawTransparency();
            UiGui.SameLineIfFits(ToggleWidth("Enabled"));
            var enabled = cfg.PluginEnabled;
            if (UiGui.Checkbox("Enabled", ref enabled))
            {
                plugin.SetPluginEnabledFromUi(enabled);
            }
            UiGui.SameLineIfFits(ToggleWidth("DTR Bar"));
            var dtr = cfg.DtrBarEnabled;
            if (UiGui.Checkbox("DTR Bar", ref dtr)) { cfg.DtrBarEnabled = dtr; cfg.Save(); plugin.UpdateDtrBar(); }
        }
        UiGui.SameLineIfFits(UiGui.IconButtonWidth("Settings"));
        if (right) ImGui.SetCursorPosY(toolbarY - ImGui.GetWindowPos().Y);
        if (UiGui.IconButton("Settings", MaterialIcon.Settings)) plugin.ToggleConfigUi();
        UiGui.SameLineIfFits(UiGui.IconButtonWidth("Status to chat"));
        if (right) ImGui.SetCursorPosY(toolbarY - ImGui.GetWindowPos().Y);
        if (UiGui.IconButton("Status to chat", MaterialIcon.Chat)) plugin.PrintStatus(GetModeStatus());
        // Wrap translated toolbar groups before measuring the body. Never reset a saved user width.
        var headerBottom = Math.Max(start.Y + (right ? DheaconPresentation.HeaderHeight + (compact ? 0 : 1) : (compact ? 148 : 170)) * s, ImGui.GetItemRectMax().Y + gap);
        ImGui.SetCursorScreenPos(new Vector2(start.X, headerBottom));
        ImGui.Separator();
        var availableHeight = nativeWindow.Pos.Y + nativeWindow.Size.Y - nativeWindow.ScrollbarSizes.Y
            - ImGui.GetStyle().WindowPadding.Y - ImGui.GetCursorScreenPos().Y - nativeWindow.Scroll.Y;
        var footerGap = (compact ? 8 : DheaconPresentation.Gap) * s;
        var footerHeight = MathF.Ceiling((compact ? 94 : 122) * s + footerGap + ImGui.GetStyle().ItemSpacing.Y) + 1;
        var bodyHeight = Math.Max((compact ? 260 : 310) * s, availableHeight - footerHeight);
        var sideBySide = width >= 700 * s;
        var presetWidth = sideBySide ? MathF.Floor(width * (compact ? .354f : .3632f)) : width;
        var root = ImGuiP.GetCurrentWindow().ID;
        DrawPresetList(new Vector2(presetWidth, sideBySide ? bodyHeight : 190 * s));
        if (sideBySide) ImGui.SameLine(0, gap);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(compact ? 16 : 18) * s);
        var speechPanelVisible = ImGui.BeginChild("##DheaconSpeechPanel", new Vector2(sideBySide ? width - presetWidth - gap : width, bodyHeight), true, ImGuiWindowFlags.HorizontalScrollbar);
        try
        {
            if (speechPanelVisible)
            {
            ImGuiP.PushOverrideID(root);
            try { DrawStatusSafely(plugin.PresetService.ActivePreset.Mode == CommentaryMode.Dheacon ? DrawDheaconStatus : DrawReadingRoegadynStatus); }
            finally { ImGui.PopID(); }
            }
        }
        finally { ImGui.EndChild(); ImGui.PopStyleVar(); }
        ImGui.SetCursorScreenPos(new Vector2(start.X, ImGui.GetItemRectMax().Y + footerGap));
        DrawStatusSafely(DrawStatusCards);
    }

    private void DrawPresetList(Vector2 size)
    {
        var origin = ImGui.GetCursorScreenPos(); var scale = MaterialTheme.Metrics.Scale;
        DheaconPresentation.Surface(origin, origin + size);
        ImGui.SetCursorScreenPos(origin + new Vector2(16, 14) * scale);
        Heading("Presets");
        var presets = plugin.PresetService.Presets.ToList();
        var active = plugin.PresetService.ActivePreset;
        // Keep the original child directly under Main, preserving its window and selectable roots.
        var insetTop = DheaconPresentation.Compact ? 48 : 57;
        ImGui.SetCursorScreenPos(origin + new Vector2(10, insetTop) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var childVisible = ImGui.BeginChild("##DheaconPresetListMain", size - new Vector2(20, insetTop + 10) * scale, true, ImGuiWindowFlags.HorizontalScrollbar);
        try
        {
            if (childVisible)
            {
            foreach (var preset in presets)
            {
                var selected = string.Equals(preset.Id, active.Id, StringComparison.OrdinalIgnoreCase);
                var suffix = preset.Protected ? "  [template]" : "  [user]";
                var raw = $"{preset.Name}{suffix}##MainPreset-{preset.Id}";
                var min = ImGui.GetCursorScreenPos();
                var height = (DheaconPresentation.Compact ? 56 : 64) * MaterialTheme.Metrics.Scale;
                var nameLabel = UiText.T(preset.Protected ? "[template]" : "[user]") + " " + preset.Name;
                float nameWidth;
                float nameHeight;
                using (UiText.Font(UiFontRole.BodyStrong)) { var nameSize = MaterialText.Measure(nameLabel); nameWidth = nameSize.X; nameHeight = nameSize.Y; }
                var descriptionSize = UiGui.ScaledTextSize(preset.Description, 14f / 16);
                var descriptionY = MaterialText.RequiresShaping(nameLabel) ? Math.Max(37 * scale, 10 * scale + nameHeight + 3 * scale) : 37 * scale;
                if (MaterialText.RequiresShaping(nameLabel) || MaterialText.RequiresShaping(preset.Description))
                    height = Math.Max(height, descriptionY + descriptionSize.Y + 10 * scale);
                var rowWidth = Math.Max(ImGui.GetContentRegionAvail().X, 74 * scale + Math.Max(nameWidth, descriptionSize.X));
                ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
                var clicked = ImGui.Selectable(raw, selected, ImGuiSelectableFlags.None, new Vector2(rowWidth, height));
                ImGui.PopStyleColor();
                var max = ImGui.GetItemRectMax(); var c = MaterialTheme.Current.Colors;
                var dl = ImGui.GetWindowDrawList(); dl.PushClipRect(min, max, true);
                try
                {
                if (selected) dl.AddRectFilled(min, new Vector2(min.X + 5 * scale, max.Y), MaterialCanvas.Color(c.Primary));
                MaterialIcons.Draw(preset.Protected ? MaterialIcon.Document : MaterialIcon.Person, min + new Vector2(22, 13) * scale, 26 * scale, selected ? c.Primary : c.OnSurface);
                using (UiText.Font(UiFontRole.BodyStrong))
                    MaterialText.AddText(dl,ImGui.GetFont(), ImGui.GetFontSize(), min + new Vector2(68, 10) * scale, MaterialCanvas.Color(c.OnSurface), nameLabel);
                MaterialText.AddText(dl,ImGui.GetFont(), ImGui.GetFontSize() * 14 / 16, min + new Vector2(68 * scale, descriptionY), MaterialCanvas.Color(c.OnSurfaceVariant), preset.Description);
                }
                finally { dl.PopClipRect(); }
                if (ImGui.IsItemHovered()) MaterialText.SetTooltip(preset.Name + "\n" + preset.Description);
                if (clicked)
                {
                    plugin.PresetService.SetActivePreset(preset.Id, out var message); plugin.PrintStatus(message);
                    plugin.UpdateDtrBar(); plugin.KranglerImaginaryFrenIpcClient.ReconcileNow();
                }
            }
            }
        }
        finally { ImGui.EndChild(); ImGui.PopStyleVar(); }
        ImGui.SetCursorScreenPos(origin); ImGui.Dummy(size);
    }

    private static void Heading(string label)
    {
        var originalScale = ImGuiP.GetCurrentWindow().FontWindowScale;
        ImGui.SetWindowFontScale(originalScale * 22 / 16);
        try { using (UiText.Font(UiFontRole.BodyStrong)) UiGui.TextUnformatted(label); }
        finally { ImGui.SetWindowFontScale(originalScale); }
    }

    private void DrawDheaconStatus()
    {
        Heading("Classic alert preset");
        ImGui.Separator();
        UiGui.TextWrapped("Dheacon mode uses the legacy packaged WAV only.");
        Field("Last transition decision", UiText.T(plugin.AetheryteTriggerService.LastDecision));
        Field("Alert sound path", plugin.AudioPlaybackService.GetResolvedAlertPath());
        Field("Last alert (UTC)", FormatUtc(plugin.AetheryteTriggerService.LastTriggeredAtUtc));
    }

    private void DrawReadingRoegadynStatus()
    {
        var scale = MaterialTheme.Metrics.Scale;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - (DheaconPresentation.Compact ? 6 : 5) * scale);
        var header = ImGui.GetCursorScreenPos();
        Heading("Spoken preset");
        var buttonsWidth = UiGui.IconButtonWidth("Test speech") + UiGui.IconButtonWidth("Clear cache") + ImGui.GetStyle().ItemSpacing.X;
        var window = ImGuiP.GetCurrentWindow();
        var right = window.Pos.X + window.Size.X - window.ScrollbarSizes.X - ImGui.GetStyle().WindowPadding.X - window.Scroll.X;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + buttonsWidth <= right)
        {
            ImGui.SameLine();
            ImGui.SetCursorScreenPos(new Vector2(right - buttonsWidth, header.Y - 2 * scale));
        }
        else UiGui.SameLineIfFits(buttonsWidth);
        if (UiGui.IconButton("Test speech", MaterialIcon.Play))
        {
            var queued = plugin.CommentaryTriggerService.SpeakManual();
            plugin.PrintStatus(queued ? "Speech queued." : plugin.CommentaryTriggerService.LastDecision);
        }
        UiGui.SameLineIfFits(UiGui.IconButtonWidth("Clear cache"));
        if (UiGui.IconButton("Clear cache", MaterialIcon.Delete))
        {
            try { var deleted = plugin.SpeechCacheService.ClearCache(); plugin.PrintStatus($"Cleared {deleted} cached speech WAV file(s)."); }
            catch (Exception ex) { plugin.PrintStatus($"Failed to clear cache: {ex.Message}"); }
        }
        ImGui.Separator();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (DheaconPresentation.Compact ? 4 : 12) * scale);
        var cfg = plugin.Configuration;
        var backend = cfg.TtsBackend switch { TtsBackend.PiperLocal => "Piper local", TtsBackend.LegacySapi => "Legacy SAPI", _ => "Modern Windows" };
        Field("Speech backend", UiText.T(backend), "Text to speech engine (plugin setting)", false);
        Field("Voice", UiText.VoiceLabel(plugin.SpeechCacheService.GetSelectedVoiceLabel()), "TTS voice (separate from UI language)", false);
        Field("Trigger", UiText.T(plugin.CommentaryTriggerService.LastDecision), "Current trigger status");
        Field("Queue", UiText.T(plugin.SpeechQueueService.IsBusy ? "Busy" : "Idle") + " · " + UiText.F("{0} pending", plugin.SpeechQueueService.PendingCount), "Number of pending utterances");
        var speech = plugin.SpeechQueueService;
        var text = string.IsNullOrWhiteSpace(speech.CurrentText) ? speech.LastText : speech.CurrentText;
        Field("Current text", string.IsNullOrWhiteSpace(text) ? UiText.T("Waiting") : text, "Text currently in queue or being spoken");
        var category = string.IsNullOrWhiteSpace(speech.CurrentCategory) ? speech.LastCategory : speech.CurrentCategory;
        var reason = string.IsNullOrWhiteSpace(speech.CurrentReason) ? speech.LastReason : speech.CurrentReason;
        Field("Speech context", string.IsNullOrWhiteSpace(category + reason) ? UiText.T("None") : UiText.T(category) + " " + UiText.CommentaryReason(category, reason), "Additional context for speech generation");
        if (cfg.TtsBackend == TtsBackend.PiperLocal)
        {
            UiGui.TextWrapped(UiText.F("Piper runtime: {0}", UiText.T(GetLastPiperRuntimeStatus())));
            UiGui.Text(UiText.F("Piper speed: {0:F2} · Sentence pause: {1:F2}s · Pitch: {2} st · Playback gain: {3}%", cfg.TtsPiperLengthScale, cfg.TtsPiperSentenceSilence, FormatPiperSemitones(cfg.TtsPiperPitchShiftSemitones), cfg.TtsOutputGainPercent));
            UiGui.TextWrapped(UiText.F("Last Piper pitch shift: {0}", UiText.T(plugin.SpeechCacheService.LastPiperPitchShiftStatus)));
        }
        else UiGui.Text(UiText.F("Pitch: {0:F2} · Output gain: {1}%", cfg.TtsPitch, cfg.TtsOutputGainPercent));
        if (!string.IsNullOrWhiteSpace(speech.LastError)) UiGui.TextWrapped(UiText.F("Queue error: {0}", speech.LastError));
    }

    private static void Field(string label, string value, string? hint = null, bool outlined = true)
    {
        var scale = MaterialTheme.Metrics.Scale; var c = MaterialTheme.Current.Colors;
        var originalScale = ImGuiP.GetCurrentWindow().FontWindowScale;
        ImGui.SetWindowFontScale(originalScale * (DheaconPresentation.Compact ? 1 : 18f / 16));
        try
        {
            float labelWidth;
            using (UiText.Font(UiFontRole.BodyStrong))
                labelWidth = Math.Max(160 * scale, new[] { "Speech backend", "Voice", "Trigger", "Queue", "Current text", "Speech context", label }.Max(key => MaterialText.Measure(UiText.T(key)).X) + 14 * scale);
            var side = ImGui.GetContentRegionAvail().X > labelWidth + 200 * scale;
            var start = ImGui.GetCursorScreenPos();
            using (UiText.Font(UiFontRole.BodyStrong)) UiGui.TextUnformatted(label);
            if (side) ImGui.SameLine(start.X - ImGui.GetWindowPos().X + labelWidth);
            var remaining = ImGui.GetContentRegionAvail().X;
            var min = ImGui.GetCursorScreenPos();
            var dl = ImGui.GetWindowDrawList(); dl.ChannelsSplit(2); dl.ChannelsSetCurrent(1);
            float bottom;
            try
            {
            var insetY = (DheaconPresentation.Compact ? 5 : 8) * scale;
            if (outlined) ImGui.SetCursorScreenPos(min + new Vector2(12 * scale, insetY));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(40, remaining - (outlined ? 24 * scale : 0)));
            try { MaterialText.Text(value); } finally { ImGui.PopTextWrapPos(); }
            bottom = ImGui.GetItemRectMax().Y + (outlined ? insetY : 0);
            if (outlined)
            {
                dl.ChannelsSetCurrent(0);
                dl.AddRectFilled(min, new Vector2(min.X + remaining, bottom), MaterialCanvas.Color(c.SurfaceContainerLowest), 4 * scale);
                dl.AddRect(min, new Vector2(min.X + remaining, bottom), MaterialCanvas.Color(c.OutlineVariant), 4 * scale);
            }
            }
            finally { dl.ChannelsMerge(); }
            ImGui.SetCursorScreenPos(new Vector2(side ? min.X : start.X, bottom + 6 * scale));
            if (hint is not null)
            {
                ImGui.SetWindowFontScale(originalScale * 14 / 16);
                UiGui.TextColored(c.OnSurfaceVariant, hint);
            }
            ImGui.SetCursorScreenPos(new Vector2(start.X, ImGui.GetCursorScreenPos().Y + (DheaconPresentation.Compact ? 5 : 10) * scale));
        }
        finally { ImGui.SetWindowFontScale(originalScale); }
    }

    private void DrawStatusCards()
    {
        var scale = MaterialTheme.Metrics.Scale; var nativeWindow = ImGuiP.GetCurrentWindow();
        var width = Math.Min(ImGui.GetContentRegionAvail().X,
            nativeWindow.Size.X - 2 * ImGui.GetStyle().WindowPadding.X - nativeWindow.ScrollbarSizes.X);
        var gap = DheaconPresentation.Gap * scale;
        float minimum;
        using (UiText.Font(UiFontRole.BodyStrong)) minimum = new[] { "Follower status", "Cache status", "BGM status" }.Max(title => MaterialText.Measure(UiText.T(title)).X) * (DheaconPresentation.Compact ? 1 : 18f / 16) + 80 * scale + ImGui.GetStyle().WindowPadding.X * 2;
        var columns = width >= Math.Max(760 * scale, minimum * 3 + gap * 2) ? 3 : 1;
        var size = new Vector2((width - (columns - 1) * gap) / columns, (DheaconPresentation.Compact ? 94 : 122) * scale);
        if (DheaconPresentation.Compact)
        {
            var origin = ImGui.GetCursorScreenPos();
            DheaconPresentation.Surface(origin, origin + new Vector2(width, columns == 3 ? size.Y : size.Y * 3 + gap * 2));
            for (var divider = 1; divider < 3; divider++)
            {
                var start = columns == 3 ? origin + new Vector2(divider * (size.X + gap) - gap * .5f, 12 * scale) : origin + new Vector2(12 * scale, divider * (size.Y + gap) - gap * .5f);
                var end = columns == 3 ? new Vector2(start.X, origin.Y + size.Y - 12 * scale) : new Vector2(origin.X + width - 12 * scale, start.Y);
                ImGui.GetWindowDrawList().AddLine(start, end, MaterialCanvas.Color(MaterialTheme.Current.Colors.OutlineVariant));
            }
        }
        StatusCard("Follower status", UiText.FollowerStatus(plugin.KranglerImaginaryFrenIpcClient.LastStatus), UiText.FollowerStatus(plugin.KranglerImaginaryFrenIpcClient.LastError), MaterialIcon.Person, size);
        if (columns > 1) ImGui.SameLine(0, gap);
        else ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, ImGui.GetItemRectMax().Y + gap));
        StatusCard("Cache status", UiText.F("{0:F1} MB", plugin.SpeechCacheService.GetCacheSizeMegabytes()), UiText.T(plugin.SpeechCacheService.LastStatus) + "\n" + plugin.Configuration.GetResolvedTtsCacheDirectory() + (string.IsNullOrWhiteSpace(plugin.SpeechCacheService.LastError) ? "" : "\n" + UiText.SpeechWarning(plugin.SpeechCacheService.LastError)), MaterialIcon.Database, size);
        if (columns > 1) ImGui.SameLine(0, gap);
        else ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, ImGui.GetItemRectMax().Y + gap));
        StatusCard("BGM status", UiText.T(plugin.BgmProbeService.Status), UiText.F("Current BGM ID: {0}", plugin.BgmProbeService.CurrentBgmId), MaterialIcon.None, size);
    }

    private static void StatusCard(string title, string value, string detail, MaterialIcon icon, Vector2 size)
    {
        if (DheaconPresentation.Compact) ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        var childVisible = ImGui.BeginChild("##Status-" + title, size, !DheaconPresentation.Compact, ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding);
        try
        {
        if (childVisible)
        {
            var start = ImGui.GetCursorScreenPos(); var scale = MaterialTheme.Metrics.Scale;
            var insetX = (DheaconPresentation.Compact ? 12 : 8) * scale;
            start += new Vector2(insetX, (DheaconPresentation.Compact ? 8 : 4) * scale);
            var textOffset = (DheaconPresentation.Compact ? 67 : 70) * scale;
            var nativeWindow = ImGuiP.GetCurrentWindow();
            float WrapWidth(float leading = 0) => Math.Max(40 * scale, nativeWindow.Size.X - 2 * ImGui.GetStyle().WindowPadding.X
                - nativeWindow.ScrollbarSizes.X - insetX - textOffset - leading);
            if (icon == MaterialIcon.None) DheaconPresentation.Music(start, 34 * scale);
            else if (icon == MaterialIcon.Person)
            {
                MaterialIcons.Draw(icon, start + new Vector2(-6, 4) * scale, 22 * scale, MaterialTheme.Current.Colors.Primary);
                MaterialIcons.Draw(icon, start + new Vector2(20, 4) * scale, 22 * scale, MaterialTheme.Current.Colors.Primary);
                MaterialIcons.Draw(icon, start + new Vector2(4, 0) * scale, 30 * scale, MaterialTheme.Current.Colors.Primary);
            }
            else MaterialIcons.Draw(icon, start, 34 * scale, MaterialTheme.Current.Colors.Primary);
            ImGui.SetCursorScreenPos(start + new Vector2(textOffset, 0));
            using (UiText.Font(UiFontRole.BodyStrong)) UiGui.TextUnformatted(title);
            ImGui.SetCursorScreenPos(new Vector2(start.X + textOffset, Math.Max(start.Y + 27 * scale, ImGui.GetItemRectMax().Y + 4 * scale)));
            var marker = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddCircleFilled(marker + new Vector2(7 * scale, ImGui.GetTextLineHeight() * .5f), 7 * scale,
                MaterialCanvas.Color(MaterialColor.Layer(MaterialTheme.Current.Colors.OnSurfaceVariant, MaterialTheme.Current.Colors.Surface, .35f)), 20);
            ImGui.Dummy(new Vector2(14 * scale, ImGui.GetTextLineHeight())); ImGui.SameLine(0, 8 * scale);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + WrapWidth(22 * scale));
            try { MaterialText.Text(value); } finally { ImGui.PopTextWrapPos(); }
            ImGui.SetCursorScreenPos(new Vector2(start.X + textOffset, ImGui.GetCursorScreenPos().Y));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + WrapWidth());
            try { MaterialText.TextColored(MaterialTheme.Current.Colors.OnSurfaceVariant, detail); } finally { ImGui.PopTextWrapPos(); }
        }
        }
        finally { ImGui.EndChild(); if (DheaconPresentation.Compact) ImGui.PopStyleColor(); }
    }

    private void DrawStatusSafely(Action drawStatus)
    {
        try
        {
            drawStatus();
        }
        catch (Exception ex)
        {
            var now = DateTime.UtcNow;
            if (now - lastStatusRenderWarningUtc > StatusRenderWarningInterval)
            {
                lastStatusRenderWarningUtc = now;
                Plugin.Log.Warning(ex, "[Dheacon] Main window status rendering failed.");
            }

            UiGui.TextWrapped(UiText.F("Status temporarily unavailable: {0}", ex.Message));
        }
    }

    private string GetModeStatus()
        => plugin.PresetService.ActivePreset.Mode == CommentaryMode.Dheacon
            ? plugin.AetheryteTriggerService.LastDecision
            : plugin.CommentaryTriggerService.LastDecision;

    private string GetLastPiperRuntimeStatus()
        => string.IsNullOrWhiteSpace(plugin.Configuration.TtsPiperRuntimeStatus)
            ? "Piper runtime status has not been checked yet."
            : plugin.Configuration.TtsPiperRuntimeStatus;

    private static string FormatPiperSemitones(double semitones)
        => semitones.ToString("+0.0;-0.0;0.0", UiText.Current.Culture);

    private static string FormatUtc(DateTime value)
        => value == DateTime.MinValue ? UiText.T("Never") : value.ToString("g", UiText.Current.Culture);
}
