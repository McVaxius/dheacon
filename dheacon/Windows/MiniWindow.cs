using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Dheacon.Windows;

public sealed class MiniWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MiniWindow(Plugin plugin) : base($"{PluginInfo.DisplayName} Mini##Mini")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320f, 120f),
            MaximumSize = new Vector2(720f, 360f),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextUnformatted(plugin.PresetService.ActivePreset.Name);
        ImGui.Separator();

        var speech = plugin.SpeechQueueService;
        var text = string.IsNullOrWhiteSpace(speech.CurrentText)
            ? speech.LastText
            : speech.CurrentText;

        if (string.IsNullOrWhiteSpace(text))
        {
            ImGui.TextDisabled("No speech yet.");
            return;
        }

        ImGui.TextWrapped(text);
    }
}
