using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using AethertekUI;
using AethertekUI.Dalamud;
using Dheacon.Ui;

namespace Dheacon.Windows;

public sealed class MiniWindow : Window, IDisposable
{
    private readonly MaterialWindowMotion windowMotion = new();
    private readonly AethertekUI.MaterialWindowOpacity windowOpacity = new();
    private readonly Plugin plugin;
    private Vector2 contentFramePadding;
    private ImGuiDir menuButtonPosition;
    private static string MiniTitle => DheaconPresentation.Compact ? UiText.T("Dheacon Mini — speech monitor") : "";

    public MiniWindow(Plugin plugin) : base($"{PluginInfo.DisplayName} Mini##Mini")
    {
        this.plugin = plugin;
        Flags |= ImGuiWindowFlags.HorizontalScrollbar;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320f, 120f),
            MaximumSize = new Vector2(720f, 360f),
        };
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.WindowMaximize, Priority = 0, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.ToggleMainUi(); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("Dheacon — main (speech settings)")),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Cog, Priority = -10, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.ToggleConfigUi(); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("Settings")),
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
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, (DheaconPresentation.Compact ? new Vector2(24, 15) : new Vector2(24, 20)) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(contentFramePadding.X, (44 * scale - ImGui.GetFontSize()) * .5f));
        var paintedTitleWidth = 0f;
        if (!DheaconPresentation.Compact)
        {
            using var titleFont = UiText.Font(UiFontRole.CompactTitle);
            paintedTitleWidth = 72 * scale + UiGui.ScaledTextSize("Dheacon Mini", 24.5f / 28).X;
        }
        UiGui.ReserveTitleSpace(this, DheaconPresentation.Compact ? MiniTitle : PluginInfo.DisplayName + " Mini", 320, paintedTitleWidth);
        windowMotion.Prepare(this, reducedMotion: false, roundedCorners: true);
    }

    public override void PostDraw()
    {
        windowMotion.Restore(this);
        UiGui.PaintTitleWithImage(this, DheaconPresentation.Compact ? MiniTitle : UiText.T(PluginInfo.DisplayName + " Mini"),
            collapsedOnly: !DheaconPresentation.Compact);
        plugin.Appearance.ApplyWindowOpacity(windowOpacity, WindowName);
        ImGui.PopStyleVar(2);
        ImGui.GetStyle().WindowMenuButtonPosition = menuButtonPosition;
    }

    public override void Draw()
    {
        windowMotion.DrawChrome();
        var compact = DheaconPresentation.Compact; var scale = MaterialTheme.Metrics.Scale;
        if (!compact) UiGui.TitleWithButtons(PluginInfo.DisplayName + " Mini", MiniTitle, this);
        using var bodyStyle = new MaterialStyleScope();
        bodyStyle.Style(ImGuiStyleVar.FramePadding, contentFramePadding);
        var window = ImGuiP.GetCurrentWindow(); var dl = ImGui.GetWindowDrawList();
        var originalScale = window.FontWindowScale;
        var origin = window.Pos - window.Scroll;
        var start = compact ? ImGui.GetCursorScreenPos() - new Vector2(2 * scale, 0) : window.Pos + new Vector2(20, 21) * scale;
        if (!compact)
        {
            var background = ImGui.GetStyle().Colors[(int)(ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) ? ImGuiCol.TitleBgActive : ImGuiCol.TitleBg)];
            dl.PushClipRect(window.Pos + Vector2.One * scale, window.Pos + new Vector2(window.Size.X - scale, 73 * scale), false);
            dl.AddRectFilled(window.Pos + new Vector2(scale, 44 * scale), window.Pos + new Vector2(window.Size.X - scale, 72 * scale), MaterialCanvas.Color(background));
        }
        try
        {
        DheaconPresentation.DrawPluginIcon(dl, start, start + new Vector2((compact ? 30 : 36) * scale));
        using (UiText.Font(compact ? UiFontRole.PluginName : UiFontRole.CompactTitle))
            MaterialText.AddText(dl,ImGui.GetFont(), ImGui.GetFontSize() * (compact ? 22f / 24 : 24.5f / 28),
                start + new Vector2(compact ? 53 : 52, compact ? -2 : -4) * scale, MaterialCanvas.Color(MaterialTheme.Current.Colors.OnSurface), "Dheacon Mini");
        if (!compact)
        {
            dl.AddLine(window.Pos + new Vector2(scale, 72 * scale), window.Pos + new Vector2(window.Size.X - scale, 72 * scale), MaterialCanvas.Color(MaterialTheme.Current.Colors.OutlineVariant));
        }
        else
        {
            var separator = origin + new Vector2(16, 102) * scale;
            dl.AddLine(separator, new Vector2(origin.X + window.Size.X - 18 * scale, separator.Y), MaterialCanvas.Color(MaterialTheme.Current.Colors.OutlineVariant));
            ImGui.SetCursorScreenPos(separator); ImGui.Dummy(Vector2.Zero);
        }
        }
        finally { if (!compact) dl.PopClipRect(); }
        try
        {
            ImGui.SetWindowFontScale(originalScale * (compact ? 1 : 17f / 16));
            var contentX = origin.X + ImGui.GetStyle().WindowPadding.X;
            var labelStart = new Vector2(contentX + (compact ? 0 : scale), origin.Y + (compact ? 114 : 93) * scale);
            ImGui.SetCursorScreenPos(labelStart);
            UiGui.TextColored(MaterialTheme.Current.Colors.OnSurfaceVariant, "Active preset");
            ImGui.SetCursorScreenPos(new Vector2(contentX, Math.Max(labelStart.Y + (compact ? 24 : 26) * scale, ImGui.GetItemRectMax().Y + 2 * scale)));
            ImGui.SetWindowFontScale(originalScale * (compact ? 19.5f : 20) / 16);
            MaterialText.Text(plugin.PresetService.ActivePreset.Name);
            var separator = new Vector2(contentX, Math.Max(origin.Y + (compact ? 181 : 161) * scale, ImGui.GetItemRectMax().Y + 14 * scale));
            dl.AddLine(separator, new Vector2(origin.X + window.Size.X - (compact ? 18 : 24) * scale, separator.Y), MaterialCanvas.Color(MaterialTheme.Current.Colors.OutlineVariant));
            ImGui.SetCursorScreenPos(separator); ImGui.Dummy(Vector2.Zero);

            ImGui.SetWindowFontScale(originalScale * (compact ? 1 : 17f / 16));
            var lastStart = new Vector2(labelStart.X, Math.Max(origin.Y + (compact ? 190 : 175) * scale, ImGui.GetCursorScreenPos().Y));
            ImGui.SetCursorScreenPos(lastStart);
            UiGui.TextColored(MaterialTheme.Current.Colors.OnSurfaceVariant, "Last speech");
            ImGui.SetCursorScreenPos(new Vector2(contentX - (compact ? scale : 0), Math.Max(lastStart.Y + (compact ? 25 : 29) * scale, ImGui.GetItemRectMax().Y + 2 * scale)));
            ImGui.SetWindowFontScale(originalScale * 19 / 16);
            var speech = plugin.SpeechQueueService;
            var text = string.IsNullOrWhiteSpace(speech.CurrentText) ? speech.LastText : speech.CurrentText;
            if (string.IsNullOrWhiteSpace(text)) UiGui.TextDisabled("No speech yet.");
            else
            {
                var visibleWidth = Math.Max(40 * scale, window.Size.X - 2 * ImGui.GetStyle().WindowPadding.X - window.ScrollbarSizes.X);
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + visibleWidth);
                try { MaterialText.Text(text); } finally { ImGui.PopTextWrapPos(); }
            }
        }
        finally { ImGui.SetWindowFontScale(originalScale); }
    }
}
