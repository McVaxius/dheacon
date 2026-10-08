using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace Dheacon.Ui;

internal enum UiFontRole { Body, BodyStrong, Title, PluginName, CompactTitle }

internal static class DheaconPresentation
{
    // Dalamud owns the shared texture through render submission; callers borrow its wrapper.
    internal static Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? OriginalIcon
        => Plugin.TextureProvider.GetFromManifestResource(typeof(Plugin).Assembly, "Dheacon.images.icon.png").GetWrapOrDefault();

    internal static void DrawPluginIcon(ImDrawListPtr drawList, Vector2 min, Vector2 max)
    {
        var texture = OriginalIcon;
        if (texture is not null)
            MaterialCanvas.DrawImage(drawList, texture.Handle, new Vector2(texture.Width, texture.Height), min, max);
    }

    // Approved regular: 1046×892 content envelope, 14px gaps, 37% preset pane,
    // 558px body and 122px status cards. Compact: 982×704, 10px gaps,
    // 35% preset pane, 436px body and 94px status strip. Native chrome is host-owned.
    internal const uint ReferenceAccent = 0xB98DEB;
    internal static readonly float[] FontSizes = [16, 16, 36, 24, 28];
    internal static readonly string[] FontFiles = ["segoeui.ttf", "seguisb.ttf", "segoeuib.ttf", "seguisb.ttf", "segoeuib.ttf"];
    internal static float AtlasHeight(UiFontRole role) => FontSizes[(int)role] * 4 / 3;
    internal static bool Compact => MaterialTheme.Current.Density == MaterialDensity.Compact;
    internal static float HeaderHeight => Compact ? 95 : 110;
    internal static float Gap => Compact ? 10 : 14;
    internal static float ControlHeight => Compact ? 32 : 40;
    internal static Vector4 Rgb(uint rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);

    internal static MaterialTheme Theme(uint accent)
    {
        accent &= 0xFFFFFF;
        var selected = Rgb(accent);
        var reference = Rgb(ReferenceAccent);
        var seed = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(selected.X, selected.Y, selected.Z)));
        var original = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(reference.X, reference.Y, reference.Z)));
        var hue = seed.Y < .001f ? 0 : seed.Z - original.Z;
        var chroma = seed.Y < .001f ? 0 : seed.Y / original.Y;
        Vector4 Relative(uint rgb)
        {
            var color = Rgb(rgb);
            if (accent == ReferenceAccent) return color;
            var lch = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(color.X, color.Y, color.Z)));
            return new(MaterialColor.GamutMap(lch.X, lch.Y * chroma, lch.Z + hue), 1);
        }
        var background = Relative(0x171D29);
        var foreground = Relative(0xF5F2FF);
        var primary = Relative(ReferenceAccent);
        var palette = new OklchPaletteGenerator().Generate(new(selected.X, selected.Y, selected.Z));
        var colors = new MaterialColorScheme(palette)
        {
            Background = background, OnBackground = foreground,
            Surface = Relative(0x1A202C), OnSurface = foreground,
            SurfaceContainerLowest = Relative(0x151C27), SurfaceContainerLow = Relative(0x1A202C),
            SurfaceContainer = Relative(0x1D2431), SurfaceContainerHigh = Relative(0x202734), SurfaceContainerHighest = Relative(0x252C3C),
            SurfaceVariant = Relative(0x323153), OnSurfaceVariant = Relative(0xBABDE3),
            Outline = Relative(0x64708A), OutlineVariant = Relative(0x354156),
            Primary = primary, OnPrimary = MaterialColor.Contrast(primary, background) >= MaterialColor.Contrast(primary, foreground) ? background : foreground,
            PrimaryContainer = Relative(0x3B315B), OnPrimaryContainer = foreground,
            Secondary = Relative(0xC9B8EF), OnSecondary = background, SecondaryContainer = Relative(0x323153), OnSecondaryContainer = foreground,
            Tertiary = Relative(0xB8BDE8), OnTertiary = background, TertiaryContainer = Relative(0x30374A), OnTertiaryContainer = foreground,
            InverseSurface = foreground, InverseOnSurface = background, InversePrimary = Relative(0x644882),
        };
        return new(colors, MaterialDensity.Standard) { SurfaceOpacity = 1 };
    }

    internal static MaterialControlMetrics Controls(float height = 0)
    {
        if (height <= 0) height = ControlHeight;
        var s = MaterialTheme.Metrics.Scale;
        return new() { Height = height * s, Padding = new(12 * s, Math.Max(0, (height * s - ImGui.GetTextLineHeight()) * .5f)),
            Gap = 8 * s, IconSize = 22 * s, Rounding = 4 * s, ItemSpacing = new(10 * s, 6 * s), CellPadding = new(12 * s, 6 * s) };
    }

    internal static void Surface(Vector2 min, Vector2 max)
    {
        var c = MaterialTheme.Current.Colors;
        MaterialCanvas.Surface(min, max, c.SurfaceContainerHigh, c.Surface, 4 * MaterialTheme.Metrics.Scale);
        ImGui.GetWindowDrawList().AddRect(min, max, MaterialCanvas.Color(c.OutlineVariant), 4 * MaterialTheme.Metrics.Scale);
    }

    internal static void Brand(Vector2 origin, float size)
    {
        var dl=ImGui.GetWindowDrawList(); var ink=MaterialCanvas.Color(MaterialTheme.Current.Colors.Primary);
        dl.AddRectFilled(origin,origin+new Vector2(1,.8f)*size,ink,size*.18f);
        dl.PathLineTo(origin+new Vector2(.28f,.75f)*size);
        dl.PathLineTo(origin+new Vector2(.28f,1.02f)*size);
        dl.PathLineTo(origin+new Vector2(.56f,.75f)*size);
        dl.PathFillConvex(ink);
        dl.AddLine(origin+new Vector2(.25f,.28f)*size,origin+new Vector2(.75f,.28f)*size,MaterialCanvas.Color(MaterialTheme.Current.Colors.Background),size*.09f);
        dl.AddLine(origin+new Vector2(.25f,.50f)*size,origin+new Vector2(.55f,.50f)*size,MaterialCanvas.Color(MaterialTheme.Current.Colors.Background),size*.09f);
    }

    internal static void Music(Vector2 origin, float size)
    {
        var dl = ImGui.GetWindowDrawList(); var ink = MaterialCanvas.Color(MaterialTheme.Current.Colors.Primary);
        dl.AddLine(origin + new Vector2(.35f, .18f) * size, origin + new Vector2(.35f, .8f) * size, ink, size * .10f);
        dl.AddLine(origin + new Vector2(.86f, .06f) * size, origin + new Vector2(.86f, .66f) * size, ink, size * .10f);
        dl.AddLine(origin + new Vector2(.35f, .18f) * size, origin + new Vector2(.86f, .06f) * size, ink, size * .14f);
        dl.AddCircleFilled(origin + new Vector2(.20f, .83f) * size, size * .16f, ink, 20);
        dl.AddCircleFilled(origin + new Vector2(.71f, .69f) * size, size * .16f, ink, 20);
    }
}
