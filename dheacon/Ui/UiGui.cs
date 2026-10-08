using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Dheacon.Ui;

// Native widgets receive their original English labels/IDs. Only their visible label is painted in the selected locale.
// This also preserves English-derived helper IDs and existing saved window identities.
internal static class UiGui
{
    internal static Vector2 ScaledTextSize(string text, float scale)
    {
        if (!MaterialText.RequiresShaping(text)) return MaterialText.Measure(text) * scale;
        var callerScale = ImGuiP.GetCurrentWindow().FontWindowScale;
        ImGui.SetWindowFontScale(callerScale * scale);
        try { return MaterialText.Measure(text); }
        finally { ImGui.SetWindowFontScale(callerScale); }
    }
    internal static float ButtonWidth(string label) => MaterialText.Measure(UiText.T(label)).X + 2 * ImGui.GetStyle().FramePadding.X;
    internal static float CheckboxWidth(string label) => ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + MaterialText.Measure(UiText.T(label)).X;
    internal static void SameLineIfFits(float width)
    {
        var edge = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width <= edge) ImGui.SameLine();
    }
    internal static bool BeginCombo(string label, string preview)
    {
        return OpenCombo(label, preview, MaterialText.Measure(preview).X);
    }
    private static bool OpenCombo(string label, string preview, float textWidth)
    {
        BeginField(label, textWidth + ImGui.GetFrameHeight() + 2 * ImGui.GetStyle().FramePadding.X);
        try
        {
            var open = MaterialText.BeginCombo("", preview);
            if (!open) ImGui.PopID();
            return open;
        }
        catch { ImGui.PopID(); throw; }
    }
    internal static void EndCombo() { try { ImGui.EndCombo(); } finally { ImGui.PopID(); } }
    internal static bool BeginTabItem(string label, ImGuiTabItemFlags flags = ImGuiTabItemFlags.None)
    {
        using var height = MaterialText.PushLineHeight(UiText.T(label));
        var style = ImGui.GetStyle();
        ImGui.SetNextItemWidth(MaterialText.Measure(UiText.T(label)).X + 2 * style.FramePadding.X);
        var open = ImGui.BeginTabItem(label, flags);
        var bar = ImGui.GetCurrentContext().CurrentTabBar;
        var dl = ImGui.GetWindowDrawList(); dl.PushClipRect(bar.BarRect.Min, bar.BarRect.Max, true);
        try
        {
            Label(label, ImGui.GetItemRectMin() + new Vector2(style.FramePadding.X, style.FramePadding.Y),
                style.Colors[(int)(open ? ImGuiCol.TabActive : ImGui.IsItemHovered() ? ImGuiCol.TabHovered : ImGuiCol.Tab)], style.Colors[(int)ImGuiCol.Text]);
        }
        catch { if (open) ImGui.EndTabItem(); throw; }
        finally { dl.PopClipRect(); }
        return open;
    }
    internal static bool Selectable(string raw, bool selected, string? display = null)
    {
        var translated = display ?? UiText.T(raw.Split("##", 2)[0]);
        var size = new Vector2(Math.Max(ImGui.GetContentRegionAvail().X, MaterialText.Measure(translated).X),
            MaterialText.RequiresShaping(translated) ? Math.Max(ImGui.GetTextLineHeight(), MaterialText.Measure(translated).Y) : 0);
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var clicked = ImGui.Selectable(raw, selected, ImGuiSelectableFlags.None, size);
        ImGui.PopStyleColor();
        var min = ImGui.GetItemRectMin(); var dl = ImGui.GetWindowDrawList();
        MaterialText.AddText(dl,min, MaterialCanvas.Color(ImGui.GetStyle().Colors[(int)ImGuiCol.Text]), translated);
        return clicked;
    }
    internal static bool InputTextWithHint(string label, string hint, ref string value, int length)
    {
        BeginField(label);
        try { using var height = MaterialText.PushLineHeight(value, UiText.T(hint)); return MaterialShapedInput.SingleLine("", UiText.T(hint), ref value, length); }
        finally { ImGui.PopID(); }
    }
    internal static bool SliderFloat(string label, ref float value, float min, float max, string format = "%.3f")
    {
        BeginField(label); try { return ImGui.SliderFloat("", ref value, min, max, UiText.T(format)); } finally { ImGui.PopID(); }
    }
    internal static bool SliderInt(string label, ref int value, int min, int max)
    {
        BeginField(label); try { return ImGui.SliderInt("", ref value, min, max); } finally { ImGui.PopID(); }
    }
    // Appearance controls retain their native numeric field, format, steps and caption placement.
    internal static bool AppearanceCheckbox(string label, ref bool value)
    {
        var visible = label.Split("##", 2)[0]; var nativeLabel = UiText.T(visible) + label[visible.Length..];
        if (!MaterialText.RequiresShaping(nativeLabel)) return ImGui.Checkbox(nativeLabel, ref value);
        using var height = MaterialText.PushLineHeight(UiText.T(visible));
        var gap = ImGui.GetStyle().ItemInnerSpacing;
        using var style = new MaterialStyleScope();
        style.Style(ImGuiStyleVar.ItemInnerSpacing, new Vector2(Math.Max(0, gap.X + MaterialText.Measure(UiText.T(visible)).X - MaterialText.Measure(visible).X), gap.Y));
        style.Color(ImGuiCol.Text, Vector4.Zero);
        var changed = ImGui.Checkbox(label, ref value);
        style.Dispose();
        var position = ImGui.GetItemRectMin() + new Vector2(ImGui.GetFrameHeight() + gap.X, ImGui.GetStyle().FramePadding.Y);
        MaterialText.AddText(ImGui.GetWindowDrawList(), position, ImGui.GetColorU32(ImGuiCol.Text), UiText.T(visible));
        return changed;
    }
    internal static bool AppearanceSliderInt(string label, ref int value, int min, int max, string format, ImGuiSliderFlags flags)
    {
        var nativeLabel = UiText.T(label.Split("##", 2)[0]) + label[label.Split("##", 2)[0].Length..];
        if (!MaterialText.RequiresShaping(nativeLabel)) return ImGui.SliderInt(nativeLabel, ref value, min, max, format, flags);
        using var height = MaterialText.PushLineHeight(UiText.T(label.Split("##", 2)[0]));
        var origin = ImGui.GetCursorScreenPos(); var width = ImGui.CalcItemWidth();
        var drawing = ImGui.GetWindowDrawList(); var parent = ImGuiP.GetCurrentWindow(); var previousMax = parent.DC.CursorMaxPos;
        drawing.PushClipRect(new Vector2(origin.X, parent.Pos.Y), new Vector2(origin.X + width, parent.Pos.Y + parent.Size.Y), true);
        bool changed;
        try { changed = ImGui.SliderInt(label, ref value, min, max, format, flags); }
        finally { drawing.PopClipRect(); }
        AppearanceFieldLabel(label, origin, width, drawing, parent, previousMax);
        return changed;
    }
    internal static bool AppearanceInputFloat(string label, ref float value)
    {
        var visible = label.Split("##", 2)[0]; var nativeLabel = UiText.T(visible) + label[visible.Length..];
        if (!MaterialText.RequiresShaping(nativeLabel)) return ImGui.InputFloat(nativeLabel, ref value);
        using var height = MaterialText.PushLineHeight(UiText.T(visible));
        var origin = ImGui.GetCursorScreenPos(); var width = ImGui.CalcItemWidth();
        var drawing = ImGui.GetWindowDrawList(); var parent = ImGuiP.GetCurrentWindow(); var previousMax = parent.DC.CursorMaxPos;
        drawing.PushClipRect(new Vector2(origin.X, parent.Pos.Y), new Vector2(origin.X + width, parent.Pos.Y + parent.Size.Y), true);
        bool changed;
        try { changed = ImGui.InputFloat(label, ref value); }
        finally { drawing.PopClipRect(); }
        AppearanceFieldLabel(label, origin, width, drawing, parent, previousMax);
        return changed;
    }
    private static void AppearanceFieldLabel(string label, Vector2 origin, float width, ImDrawListPtr drawing, ImGuiWindowPtr parent, Vector2 previousMax)
    {
        var translated = UiText.T(label.Split("##", 2)[0]);
        var position = origin + new Vector2(width + ImGui.GetStyle().ItemInnerSpacing.X, ImGui.GetStyle().FramePadding.Y);
        MaterialText.AddText(drawing, position, ImGui.GetColorU32(ImGuiCol.Text), translated);
        var right = position.X + MaterialText.Measure(translated).X;
        parent.DC.CursorMaxPos = new Vector2(Math.Max(previousMax.X, right), parent.DC.CursorMaxPos.Y);
        parent.DC.CursorPosPrevLine = new Vector2(right, parent.DC.CursorPosPrevLine.Y);
    }
    private static void BeginField(string label, float minimum = 0, bool hasStepButtons = false)
    {
        var requested = ImGui.CalcItemWidth();
        minimum = Math.Max(minimum, Math.Max(80 * MaterialTheme.Metrics.Scale, MaterialText.Measure("00000000").X + 2 * ImGui.GetStyle().FramePadding.X));
        if (hasStepButtons) minimum += 2 * (ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X);
        var visible = label.Split("##", 2)[0];
        if (visible.Length != 0) MaterialText.Text(UiText.T(visible));
        ImGui.SetNextItemWidth(MaterialLayout.FitNextItemWidth(requested, MathF.Ceiling(minimum)));
        ImGuiP.PushOverrideID(ImGui.GetID(label));
    }
    internal static void TextWrapped(FormattableString text) => TextWrapped(UiText.Interpolated(text));
    internal static void SetTooltip(FormattableString text) => SetTooltip(UiText.Interpolated(text));
    internal static void TextDisabled(string text)
    {
        ImGui.PushTextWrapPos(0); try { MaterialText.TextDisabled(UiText.T(text)); } finally { ImGui.PopTextWrapPos(); }
    }
    internal static void TextDisabled(FormattableString text) => TextDisabled(UiText.Interpolated(text));
    internal static bool InputText(string label,ref string value,int length,ImGuiInputTextFlags flags=ImGuiInputTextFlags.None)
    {
        BeginField(label);
        try { using var height=MaterialText.PushLineHeight(value); return MaterialShapedInput.SingleLine("","",ref value,length,flags); }
        finally { ImGui.PopID(); }
    }
    internal static bool InputInt(string label,ref int value,int step=0,int fast=0)
    {
        BeginField(label, hasStepButtons: step > 0);
        try { return ImGui.InputInt("",ref value,step,fast); } finally { ImGui.PopID(); }
    }
    internal static bool RadioButton(string label,bool active)
    {
        using var height=MaterialText.PushLineHeight(UiText.T(label));
        var gap = ImGui.GetStyle().ItemInnerSpacing;
        MaterialLayout.FitNextItemWidth(0, ImGui.GetFrameHeight() + gap.X + Math.Max(MaterialText.Measure(label).X, MaterialText.Measure(UiText.T(label)).X));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(Math.Max(0, gap.X + MaterialText.Measure(UiText.T(label)).X - MaterialText.Measure(label).X), gap.Y));
        var color = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var changed=ImGui.RadioButton(label,active);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        color.W *= ImGui.GetStyle().Alpha;
        MaterialText.AddText(ImGui.GetWindowDrawList(),ImGui.GetItemRectMin()+new Vector2(ImGui.GetFrameHeight()+gap.X,ImGui.GetStyle().FramePadding.Y), MaterialCanvas.Color(color), UiText.T(label));
        return changed;
    }
    internal static bool Combo(string label,ref int index,string[] options,int count,string[]? displays=null)
    {
        var preview = index >= 0 && index < count ? displays?[index] ?? UiText.T(options[index]) : string.Empty;
        var textWidth = options.Take(count).Select((option, position) => MaterialText.Measure(displays?[position] ?? UiText.T(option)).X).DefaultIfEmpty(0).Max();
        if (!OpenCombo(label, preview, textWidth)) return false;
        var changed = false;
        try
        {
        for (var option = 0; option < count; option++)
        {
            // Native Combo uses an index scope plus the original option label for each item.
            ImGui.PushID(option);
            try
            {
            var selected = index == option;
            if (Selectable(options[option], selected, displays?[option])) { index = option; changed = true; }
            if (selected) ImGui.SetItemDefaultFocus();
            }
            finally { ImGui.PopID(); }
        }
        }
        finally { EndCombo(); }
        return changed;
    }
    internal static void TextUnformatted(string text) => MaterialText.Text(UiText.T(text));
    internal static void TextWrapped(string text) => MaterialText.TextWrapped(UiText.T(text));
    internal static void Text(string text)
    {
        ImGui.PushTextWrapPos(0);
        try { MaterialText.Text(UiText.T(text)); } finally { ImGui.PopTextWrapPos(); }
    }
    internal static void Text(FormattableString text) => Text(UiText.Interpolated(text));
    internal static void TextColored(Vector4 color, string text)
    {
        ImGui.PushTextWrapPos(0);
        try { MaterialText.TextColored(color, UiText.T(text)); } finally { ImGui.PopTextWrapPos(); }
    }
    internal static void TextColored(Vector4 color, FormattableString text) => TextColored(color, UiText.Interpolated(text));
    internal static void SetTooltip(string text) => MaterialText.SetTooltip(UiText.T(text));
    private static void Label(string original,Vector2 position,Vector4 background,Vector4 foreground,Vector2? clip=null,string? display=null,ImDrawListPtr? drawList=null)
    {
        var visible=original.Split("##",2)[0];
        var translated=display ?? UiText.T(visible);
        if(translated==visible && !MaterialText.RequiresShaping(translated)) return;
        var dl=drawList ?? ImGui.GetWindowDrawList();
        var width=Math.Max(MaterialText.Measure(visible).X,MaterialText.Measure(translated).X);
        if(clip is { } max) dl.PushClipRect(position,max,true);
        try
        {
        dl.AddRectFilled(position,position+new Vector2(width,Math.Max(ImGui.GetTextLineHeight(), MaterialText.Measure(translated).Y)),ImGui.ColorConvertFloat4ToU32(background));
        foreground.W*=ImGui.GetStyle().Alpha;
        MaterialText.AddText(dl,position,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        }
        finally { if(clip.HasValue) dl.PopClipRect(); }
    }
    internal static bool Button(string label,string? display=null)
    {
        var translated=display ?? UiText.T(label.Split("##",2)[0]);
        using var controls = ImGui.GetStyle().FramePadding.Y == 0 || MaterialControls.Context == MaterialControlContext.Dense
            ? default(MaterialControls.ControlScope) : MaterialControls.Push(MaterialControlContext.Toolbar);
        using var height=MaterialText.PushLineHeight(translated);
        var width=MaterialLayout.FitNextItemWidth(0,MaterialText.Measure(translated).X+2*ImGui.GetStyle().FramePadding.X);
        var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var clicked=ImGui.Button(label,new Vector2(width,0));
        ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin(); var max=ImGui.GetItemRectMax();
        foreground.W*=ImGui.GetStyle().Alpha;
        MaterialText.AddText(ImGui.GetWindowDrawList(),min+(max-min-MaterialText.Measure(translated))*.5f,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        return clicked;
    }
    internal static bool Button(string label, Vector2 pixels)
    {
        var translated = UiText.T(label.Split("##", 2)[0]);
        using var controls = ImGui.GetStyle().FramePadding.Y == 0 || MaterialControls.Context == MaterialControlContext.Dense
            ? default(MaterialControls.ControlScope) : MaterialControls.Push(MaterialControlContext.Toolbar);
        using var height=MaterialText.PushLineHeight(translated);
        pixels.Y=Math.Max(pixels.Y,ImGui.GetFrameHeight());
        var color = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        pixels.X = MaterialLayout.FitNextItemWidth(pixels.X, MaterialText.Measure(translated).X + 2 * ImGui.GetStyle().FramePadding.X);
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var clicked = ImGui.Button(label, pixels);
        ImGui.PopStyleColor();
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(min, max, true);
        try { MaterialText.AddText(dl,min + (max - min - MaterialText.Measure(translated)) * .5f, MaterialCanvas.Color(color), translated); }
        finally { dl.PopClipRect(); }
        return clicked;
    }
    internal static float IconButtonWidth(string original, string? display = null, MaterialIcon trailing = MaterialIcon.None)
        => MaterialText.Measure(display ?? UiText.T(original.Split("##", 2)[0])).X + (trailing == MaterialIcon.None ? 30 : 58) * MaterialTheme.Metrics.Scale
            + 2 * MaterialControlMetrics.Measure(MaterialTheme.Metrics, ImGui.GetTextLineHeight(), MaterialControls.Context == MaterialControlContext.Dense ? MaterialControlContext.Dense : MaterialControlContext.Toolbar).NativePadding.X;
    internal static bool IconButton(string original, MaterialIcon icon, string? display = null, MaterialIcon trailing = MaterialIcon.None)
    {
        var s = MaterialTheme.Metrics.Scale;
        var colors = MaterialTheme.Current.Colors;
        var fill = icon == MaterialIcon.Play ? colors.PrimaryContainer : colors.SurfaceContainerHigh;
        using var appearance = new MaterialStyleScope();
        appearance.Style(ImGuiStyleVar.FrameBorderSize, s);
        appearance.Color(ImGuiCol.Border, icon == MaterialIcon.Play ? colors.Primary : colors.Outline);
        appearance.Color(ImGuiCol.Button, fill);
        appearance.Color(ImGuiCol.ButtonHovered, MaterialColor.Layer(fill, colors.OnSurface, .08f));
        appearance.Color(ImGuiCol.ButtonActive, MaterialColor.Layer(fill, colors.OnSurface, .14f));
        var label = display ?? UiText.T(original.Split("##", 2)[0]);
        using var controls = MaterialControls.Context == MaterialControlContext.Dense
            ? default(MaterialControls.ControlScope) : MaterialControls.Push(MaterialControlContext.Toolbar);
        using var height=MaterialText.PushLineHeight(label);
        var vertical = MaterialControls.Context == MaterialControlContext.Dense ? DheaconPresentation.Compact ? 1 : 2 : DheaconPresentation.Compact ? 2 : 4;
        var size = new Vector2(MaterialLayout.FitNextItemWidth(0, IconButtonWidth(original, display, trailing)), Math.Max(ImGui.GetFrameHeight(), (22 + 2 * vertical) * s));
        var foreground = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var clicked = ImGui.Button(original, size);
        ImGui.PopStyleColor();
        foreground.W *= ImGui.GetStyle().Alpha;
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList(); dl.PushClipRect(min, max, true);
        try
        {
        var iconPosition = min + new Vector2(ImGui.GetStyle().FramePadding.X, (size.Y - 22 * s) * .5f);
        var iconInk = icon == MaterialIcon.Heart ? new Vector4(1, .47f, .58f, foreground.W) : foreground;
        if (icon == MaterialIcon.None) DheaconPresentation.Brand(iconPosition, 22 * s);
        else MaterialIcons.Draw(icon, iconPosition, 22 * s, iconInk);
        MaterialText.AddText(dl,min + new Vector2(ImGui.GetStyle().FramePadding.X + 30 * s, (size.Y - MaterialText.Measure(label).Y) * .5f), MaterialCanvas.Color(foreground), label);
        if (trailing != MaterialIcon.None) MaterialIcons.Draw(trailing, new Vector2(max.X - ImGui.GetStyle().FramePadding.X - 20 * s, min.Y + (size.Y - 20 * s) * .5f), 20 * s, foreground);
        }
        finally { dl.PopClipRect(); }
        return clicked;
    }
    internal static bool SmallButton(string label,string? display=null)
    {
        // Native small buttons use the same ID and behavior with zero vertical padding.
        using var padding=new MaterialStyleScope();
        padding.Style(ImGuiStyleVar.FramePadding,new Vector2(ImGui.GetStyle().FramePadding.X,0));
        return Button(label,display);
    }
    internal static bool Checkbox(string label,ref bool value)
    {
        var colors=MaterialTheme.Current.Colors;
        var fill=value ? colors.Primary : colors.SurfaceContainerHighest;
        using var appearance=new MaterialStyleScope();
        appearance.Style(ImGuiStyleVar.FrameBorderSize, MaterialTheme.Metrics.Scale);
        appearance.Color(ImGuiCol.Border,value ? colors.Primary : colors.Outline);
        appearance.Color(ImGuiCol.FrameBg,fill);
        appearance.Color(ImGuiCol.FrameBgHovered,MaterialColor.Layer(fill,colors.OnSurface,.08f));
        appearance.Color(ImGuiCol.FrameBgActive,MaterialColor.Layer(fill,colors.OnSurface,.14f));
        appearance.Color(ImGuiCol.CheckMark,colors.OnPrimary);
        var visible=label.Split("##",2)[0];
        var translated=UiText.T(visible);
        using var height=MaterialText.PushLineHeight(translated);
        var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        var gap=ImGui.GetStyle().ItemInnerSpacing;
        MaterialLayout.FitNextItemWidth(0, ImGui.GetFrameHeight() + gap.X + Math.Max(MaterialText.Measure(visible).X, MaterialText.Measure(translated).X));
        // Native Checkbox sizes its hit area from the original label. Adjust that size for the
        // translated ink while keeping the native widget and its original ID.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing,new Vector2(Math.Max(0,gap.X+MaterialText.Measure(translated).X-MaterialText.Measure(visible).X),gap.Y));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var changed=ImGui.Checkbox(label,ref value);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        var p=ImGui.GetItemRectMin()+new Vector2(ImGui.GetFrameHeight()+gap.X,ImGui.GetStyle().FramePadding.Y);
        foreground.W*=ImGui.GetStyle().Alpha;
        MaterialText.AddText(ImGui.GetWindowDrawList(),p,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        return changed;
    }
    internal static void Title(string original,string translated)
        => TitleWithButtons(original, translated, null);

    internal static void PaintTitleWithImage(Window owner, string display, bool collapsedOnly = false)
    {
        var window = ImGuiP.FindWindowByName(owner.WindowName);
        if (window.IsNull || (collapsedOnly && !window.Collapsed)) return;
        var extraRightWidth = AdditionalTitleButtonWidth(owner, ImGuiP.CalcFontSize(window));
        var texture = DheaconPresentation.OriginalIcon;
        using var font = UiText.Font(UiFontRole.Body);
        MaterialWindowHeader.PaintTitle(window, display, texture?.Handle ?? default,
            texture is null ? Vector2.Zero : new Vector2(texture.Width, texture.Height), extraRightWidth, owner.ShowCloseButton);
    }

    internal static void ReserveTitleSpace(Window owner, string visible, float minimumWidth, float paintedTitleWidth = 0)
    {
        var style = ImGui.GetStyle();
        var fontSize = ImGui.GetFontSize();
        var collapse = (owner.Flags & (ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.Modal)) == 0
            && style.WindowMenuButtonPosition != ImGuiDir.None;
        var controls = AdditionalTitleButtonWidth(owner, fontSize)
            + ((owner.ShowCloseButton ? 1 : 0) + (collapse ? 1 : 0)) * (fontSize + style.ItemInnerSpacing.X);
        var titleWidth = Math.Max(MaterialText.Measure(visible).X + fontSize + style.ItemInnerSpacing.X, paintedTitleWidth);
        var required = (titleWidth + controls + style.FramePadding.X * 2 + style.ItemInnerSpacing.X)
            / ImGui.GetIO().FontGlobalScale;
        var bounds = owner.SizeConstraints ?? new WindowSizeConstraints();
        bounds.MinimumSize = new(Math.Max(minimumWidth, required), bounds.MinimumSize.Y);
        owner.SizeConstraints = bounds;
    }

    private static float AdditionalTitleButtonWidth(Window? owner, float fontSize)
    {
        if (owner is null) return 0;
        var count = owner.TitleBarButtons.Count(button => !owner.IsClickthrough || button.AvailableClickthrough);
        if (owner.AllowPinning || owner.AllowClickthrough || owner.AllowBackgroundBlur) count++;
        return count * (fontSize + ImGui.GetStyle().ItemInnerSpacing.X);
    }

    internal static void TitleWithButtons(string original,string translated, Window? owner)
    {
        var s=ImGui.GetStyle(); var size=ImGui.GetFontSize();var height=ImGui.GetFrameHeight();
        var flags=ImGuiP.GetCurrentWindow().Flags;
        var collapseOnLeft=(flags & (ImGuiWindowFlags.NoCollapse|ImGuiWindowFlags.Modal))==0 && s.WindowMenuButtonPosition==ImGuiDir.Left;
        var position=ImGui.GetWindowPos()+new Vector2(s.FramePadding.X+(collapseOnLeft?size+s.ItemInnerSpacing.X:0),s.FramePadding.Y);
        var originalWidth=MaterialText.Measure(original).X;
        using var font=UiText.Font(UiFontRole.Body);
        var translatedWidth=ScaledTextSize(translated,size/ImGui.GetFontSize()).X;
        var dl=ImGui.GetWindowDrawList();
        var rightButtons = (owner is null || owner.ShowCloseButton ? size : 0) + s.FramePadding.X * 2 + AdditionalTitleButtonWidth(owner, size);
        if ((flags & ImGuiWindowFlags.NoCollapse) == 0 && s.WindowMenuButtonPosition == ImGuiDir.Right) rightButtons += size + s.ItemInnerSpacing.X;
        dl.PushClipRect(position-new Vector2(2*MaterialTheme.Metrics.Scale,0),ImGui.GetWindowPos()+new Vector2(Math.Max(0,ImGui.GetWindowSize().X-rightButtons),height),false);
        try
        {
        var bg=s.Colors[(int)(ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)?ImGuiCol.TitleBgActive:ImGuiCol.TitleBg)];
        dl.AddRectFilled(position,position+new Vector2(Math.Max(originalWidth,translatedWidth),height-s.FramePadding.Y),ImGui.ColorConvertFloat4ToU32(bg));
        MaterialText.AddText(dl,ImGui.GetFont(),size,position,ImGui.ColorConvertFloat4ToU32(s.Colors[(int)ImGuiCol.Text]),translated);
        }
        finally { dl.PopClipRect(); }
    }
    internal static void TableHeadersRow(float height=0)
    {
        for(var column=0;column<ImGui.TableGetColumnCount();column++)
        {
            var translated=UiText.T(ImGui.TableGetColumnName(column));
            if(MaterialText.RequiresShaping(translated)) height=Math.Max(height,MaterialText.Measure(translated).Y+2*ImGui.GetStyle().CellPadding.Y);
        }
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers,height);
        for(var index=0;index<ImGui.TableGetColumnCount();index++)
        {
            if(!ImGui.TableSetColumnIndex(index)) continue;
            var original=ImGui.TableGetColumnName(index);
            var position=ImGui.GetCursorScreenPos();
            var available=ImGui.GetContentRegionAvail().X;
            ImGui.TableHeader(original);
            var translated=UiText.T(original);
            Label(original,position,ImGui.GetStyle().Colors[(int)ImGuiCol.TableHeaderBg],ImGui.GetStyle().Colors[(int)ImGuiCol.Text],display:translated);
            if(translated!=original && MaterialText.Measure(translated).X>available-16*AethertekUI.MaterialTheme.Metrics.Scale && ImGui.IsItemHovered())
                MaterialText.SetTooltip(translated);
        }
    }
}
