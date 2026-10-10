using System.Numerics;
using AethertekUI;
using AethertekUI.Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace Dheacon.Ui;

internal sealed class DheaconAppearance : IDisposable
{
    private readonly Plugin plugin;
    private UiText text;
    private DheaconFonts fonts;
    private MaterialTheme theme;
    private readonly MaterialTextHost shapedText;
    private readonly MaterialWindowFold fontStatusMotion = new();
    private readonly MaterialWindowDecorations fontStatusDecorations = new();
    private readonly MaterialWindowOpacity fontStatusOpacity = new();
    private readonly MaterialOptions<string> languages = new(UiText.Languages.Select(l => new MaterialOption<string>(l.Code, l.Code, l.Name)).ToArray());
    private string appliedLanguage = "";
    private uint appliedAccent;
    private Vector3 accentDraft;
    private int checkedGeneration = -1;
    private bool fontIssueLogged;

    private void Apply()
    {
        var language = UiText.Languages.Any(l => l.Code == plugin.Configuration.UiLanguage) ? plugin.Configuration.UiLanguage : "en";
        if (language != appliedLanguage)
        {
            fonts?.Dispose();
            text?.Dispose();
            text = new(language, PushFont);
            fonts = new(Plugin.PluginInterface.UiBuilder.FontAtlas, text.GlyphRanges(), language);
            appliedLanguage = language;
            checkedGeneration = -1;
            fontIssueLogged = false;
        }
        if (theme is null || appliedAccent != (plugin.Configuration.UiAccentRgb & 0xFFFFFF))
        {
            appliedAccent = plugin.Configuration.UiAccentRgb & 0xFFFFFF;
            theme = DheaconPresentation.Theme(appliedAccent);
            var rgb = DheaconPresentation.Rgb(appliedAccent);
            accentDraft = new(rgb.X, rgb.Y, rgb.Z);
        }
        theme.Density = plugin.Configuration.UiCompact ? MaterialDensity.Compact : MaterialDensity.Standard;
    }

    internal void Draw(WindowSystem windows)
    {
        Apply();
        if (!windows.Windows.Any(window => window.IsOpen)) return;
        using var resources = text.Enter();
        using var shaping = shapedText.Push();
        if (fonts.Ready && checkedGeneration != fonts.Generation)
        {
            try
            {
                var generation = fonts.Generation;
                foreach (var size in DheaconPresentation.FontSizes)
                    shapedText.Renderer.CheckGlyphs(text.RequiredText, size * ImGuiHelpers.GlobalScale);
                fonts.CheckGlyphs(text.RequiredText);
                var hindiLabel = UiText.Languages.Single(l => l.Code == "hi").Name;
                var hindiAvailable = true;
                foreach (var size in DheaconPresentation.FontSizes)
                    hindiAvailable &= shapedText.Renderer.TryCheckGlyphs([hindiLabel], size * ImGuiHelpers.GlobalScale, out _);
                languages.Replace(UiText.Languages.Select(l => new MaterialOption<string>(l.Code, l.Code,
                    l.Code == "hi" && !hindiAvailable ? "Hindi (unavailable)" : l.Name,
                    l.Code == "hi" && !hindiAvailable)).ToArray());
                checkedGeneration = generation;
            }
            catch (Exception ex)
            {
                if (!fontIssueLogged) { Plugin.Log.Error(ex, "[Dheacon] Required UI glyph coverage failed."); fontIssueLogged = true; }
            }
        }
        using var palette = MaterialTheme.Push(theme, ImGuiHelpers.GlobalScale, MaterialStyleMode.ColorsOnly);
        using var chrome = MaterialWindowChrome.Push();
        if (!fonts.Ready || checkedGeneration != fonts.Generation)
        {
            if (!fontIssueLogged && fonts.LoadException is { } error) { Plugin.Log.Error(error, "[Dheacon] Required UI fonts failed to load."); fontIssueLogged = true; }
            ImGui.SetNextWindowSize(new Vector2(460 * ImGuiHelpers.GlobalScale, 0));
            fontStatusMotion.PreDraw("Dheacon##FontStatus", null, null, reducedMotion: false, prepareDecorations: fontStatusDecorations.Prepare);
            var visible = ImGui.Begin("Dheacon##FontStatus", ImGuiWindowFlags.AlwaysAutoResize);
            try
            {
                if (visible)
                {
                fontStatusDecorations.Paint();
                var failed = fonts.LoadException is not null || fontIssueLogged;
                ImGui.TextWrapped(appliedLanguage == "hi" && failed ? "Hindi UI fonts are unavailable. Use English to continue."
                    : failed ? "UI fonts failed to load. See the plugin log." : "Loading UI fonts...");
                if (appliedLanguage == "hi" && failed && ImGui.Button("Use English"))
                {
                    plugin.Configuration.UiLanguage = "en";
                    plugin.Configuration.Save();
                }
                }
            }
            finally
            {
                ImGui.End();
                fontStatusDecorations.Paint();
                fontStatusMotion.PostDraw();
                ApplyWindowOpacity(fontStatusOpacity, "Dheacon##FontStatus");
            }
            return;
        }
        using var style = new MaterialStyleScope();
        var s = ImGuiHelpers.GlobalScale;
        style.Style(ImGuiStyleVar.WindowPadding, new Vector2(plugin.Configuration.UiCompact ? 12 : 20) * s);
        style.Style(ImGuiStyleVar.ItemSpacing, new Vector2(plugin.Configuration.UiCompact ? 8 : 12, plugin.Configuration.UiCompact ? 5 : 10) * s);
        style.Style(ImGuiStyleVar.FramePadding, new Vector2(plugin.Configuration.UiCompact ? 10 : 14, plugin.Configuration.UiCompact ? 4 : 7) * s);
        style.Style(ImGuiStyleVar.CellPadding, new Vector2(plugin.Configuration.UiCompact ? 6 : 10, plugin.Configuration.UiCompact ? 4 : 8) * s);
        style.Style(ImGuiStyleVar.FrameRounding, 4 * s);
        style.Style(ImGuiStyleVar.ChildRounding, 4 * s);
        using var body = fonts.Push(UiFontRole.Body);
        windows.Draw();
    }

    internal void DrawSelector()
    {
        var language = appliedLanguage;
        using var controls = MaterialControls.Push(DheaconPresentation.Controls(28));
        var changed = MaterialAppearanceSelector.Draw("appearance", ref accentDraft, ref language, languages,
            new(UiText.T("Color"), UiText.T("Language"), UiText.T("Teal"), UiText.T("Blue"), UiText.T("Pink"), UiText.T("Custom RGB")), 140);
        if (changed.AccentChanged)
            plugin.Configuration.UiAccentRgb = ((uint)Math.Clamp((int)MathF.Round(accentDraft.X * 255), 0, 255) << 16)
                | ((uint)Math.Clamp((int)MathF.Round(accentDraft.Y * 255), 0, 255) << 8) | (uint)Math.Clamp((int)MathF.Round(accentDraft.Z * 255), 0, 255);
        if (changed.LanguageChanged) plugin.Configuration.UiLanguage = language;
        if (changed.AccentChanged || changed.LanguageChanged) plugin.Configuration.Save();
    }

    internal DheaconAppearance(Plugin plugin)
    {
        this.plugin = plugin;
        shapedText = new(Plugin.TextureProvider);
        appliedLanguage = UiText.Languages.Any(l => l.Code == plugin.Configuration.UiLanguage) ? plugin.Configuration.UiLanguage : "en";
        text = new(appliedLanguage, PushFont);
        fonts = new(Plugin.PluginInterface.UiBuilder.FontAtlas, text.GlyphRanges(), appliedLanguage);
        appliedAccent = plugin.Configuration.UiAccentRgb & 0xFFFFFF;
        theme = DheaconPresentation.Theme(appliedAccent);
        var rgb = DheaconPresentation.Rgb(appliedAccent);
        accentDraft = new(rgb.X, rgb.Y, rgb.Z);
    }
    private IDisposable PushFont(UiFontRole role) => fonts.Push(role);
    internal string Label(string key) => text.Label(key);
    internal string Format(string key, params object?[] arguments) => text.Format(key, arguments);
    internal bool NativeEnglish => text.Language is "en" or "hi";

    public void Dispose() { shapedText.Dispose(); fonts?.Dispose(); text?.Dispose(); }
    internal void DrawLanguage(string id = "appearance")
    {
        var language = appliedLanguage;
        using var controls = MaterialControls.Push(DheaconPresentation.Controls(28));
        if (!MaterialAppearanceSelector.DrawLanguage(id, ref language, languages, 140)) return;
        plugin.Configuration.UiLanguage = language; plugin.Configuration.Save();
    }

    internal void ApplyWindowOpacity(MaterialWindowOpacity opacity, string name)
    {
        var config = plugin.Configuration;
        opacity.Apply(name, Math.Clamp(config.UiWindowOpacityPercent, 10, 100) / 100f,
            config.UiTransparencyEnabled, config.UiAutoFade, Math.Clamp(config.UiFadedOpacityPercent, 10, 100) / 100f,
            float.IsFinite(config.UiUnfocusedDelaySeconds) ? Math.Max(0, config.UiUnfocusedDelaySeconds) : 10);
    }

    internal void DrawWindowAppearance()
    {
        UiGui.TextUnformatted("Window appearance");
        DrawSelector();
        var compact = plugin.Configuration.UiCompact;
        if (UiGui.Checkbox("C", ref compact)) { plugin.Configuration.UiCompact = compact; plugin.Configuration.Save(); }
        if (ImGui.IsItemHovered()) UiGui.SetTooltip("Compact mode");
        var config = plugin.Configuration;
        var changed = false;
        var compactVisibleOnMainWindow = config.UiCompactVisibleOnMainWindow;
        if (UiGui.AppearanceCheckbox("Compact visible on main window###UiCompactVisibleOnMainWindowSettings", ref compactVisibleOnMainWindow))
        { config.UiCompactVisibleOnMainWindow = compactVisibleOnMainWindow; changed = true; }
        var transparencyVisibleOnMainWindow = config.UiTransparencyVisibleOnMainWindow;
        if (UiGui.AppearanceCheckbox("Transparency visible on main window###UiTransparencyVisibleOnMainWindowSettings", ref transparencyVisibleOnMainWindow))
        { config.UiTransparencyVisibleOnMainWindow = transparencyVisibleOnMainWindow; changed = true; }
        var languageVisibleOnMainWindow = config.UiLanguageVisibleOnMainWindow;
        if (UiGui.AppearanceCheckbox("Language visible on main window###UiLanguageVisibleOnMainWindowSettings", ref languageVisibleOnMainWindow))
        { config.UiLanguageVisibleOnMainWindow = languageVisibleOnMainWindow; changed = true; }
        var transparencyEnabled = config.UiTransparencyEnabled;
        if (UiGui.AppearanceCheckbox("Transparency###UiTransparencyEnabledSettings", ref transparencyEnabled))
        { config.UiTransparencyEnabled = transparencyEnabled; changed = true; }
        var autoFade = config.UiAutoFade;
        if (UiGui.AppearanceCheckbox("Auto-fade when unfocused###UiAutoFadeSettings", ref autoFade))
        { config.UiAutoFade = autoFade; changed = true; }
        ImGui.BeginDisabled(!transparencyEnabled);
        try
        {
        var opacity = Math.Clamp(config.UiWindowOpacityPercent, 10, 100);
        ImGui.SetNextItemWidth(180 * MaterialTheme.Metrics.Scale);
        if (UiGui.AppearanceSliderInt("Opacity (%)###UiWindowOpacityPercentSettings", ref opacity, 10, 100, "%d%%", ImGuiSliderFlags.AlwaysClamp))
        { config.UiWindowOpacityPercent = opacity; changed = true; }
        var fadedOpacity = Math.Clamp(config.UiFadedOpacityPercent, 10, 100);
        ImGui.SetNextItemWidth(180 * MaterialTheme.Metrics.Scale);
        if (UiGui.AppearanceSliderInt("Unfocused opacity (%)###UiFadedOpacityPercentSettings", ref fadedOpacity, 10, 100, "%d%%", ImGuiSliderFlags.AlwaysClamp))
        { config.UiFadedOpacityPercent = fadedOpacity; changed = true; }
        ImGui.BeginDisabled(!autoFade);
        try
        {
        var delay = float.IsFinite(config.UiUnfocusedDelaySeconds) ? Math.Max(0, config.UiUnfocusedDelaySeconds) : 10;
        ImGui.SetNextItemWidth(180 * MaterialTheme.Metrics.Scale);
        if (UiGui.AppearanceInputFloat("Unfocused delay (seconds)###UiUnfocusedDelaySecondsSettings", ref delay))
        { delay = float.IsFinite(delay) ? Math.Max(0, delay) : 10; config.UiUnfocusedDelaySeconds = delay; changed = true; }
        }
        finally { ImGui.EndDisabled(); }
        }
        finally { ImGui.EndDisabled(); }
        if (changed) config.Save();
    }

    internal void DrawTransparency()
    {
        var enabled = plugin.Configuration.UiTransparencyEnabled;
        if (UiGui.AppearanceCheckbox("Transparency###UiTransparencyHeader", ref enabled)) { plugin.Configuration.UiTransparencyEnabled = enabled; plugin.Configuration.Save(); }
    }

}
