using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Dheacon.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin) : base($"{PluginInfo.DisplayName}##Main")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(560f, 430f), MaximumSize = new Vector2(1400f, 1200f) };
    }

    public void Dispose() { }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

        ImGui.Text($"{PluginInfo.DisplayName} v{version}");
        ImGui.SameLine(ImGui.GetWindowWidth() - 120f);
        if (ImGui.SmallButton("Ko-fi"))
            Process.Start(new ProcessStartInfo { FileName = PluginInfo.SupportUrl, UseShellExecute = true });

        ImGui.Separator();

        var enabled = cfg.PluginEnabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            cfg.PluginEnabled = enabled;
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        ImGui.SameLine();
        var dtr = cfg.DtrBarEnabled;
        if (ImGui.Checkbox("DTR Bar", ref dtr))
        {
            cfg.DtrBarEnabled = dtr;
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Settings"))
            plugin.ToggleConfigUi();

        ImGui.SameLine();
        if (ImGui.SmallButton("Status to chat"))
            plugin.PrintStatus(GetModeStatus());

        ImGui.TextWrapped(PluginInfo.Summary);
        DrawModeSelector(cfg);
        ImGui.Separator();

        if (cfg.CommentaryMode == CommentaryMode.Dheacon)
            DrawDheaconStatus();
        else
            DrawReadingRoegadynStatus();

        ImGui.Separator();
        ImGui.Text($"Command: {PluginInfo.Command}");
    }

    private void DrawModeSelector(Configuration cfg)
    {
        ImGui.TextUnformatted("Mode");

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

    private void DrawDheaconStatus()
    {
        ImGui.TextWrapped("Dheacon mode uses the legacy packaged WAV only.");
        ImGui.TextWrapped($"Last transition decision: {plugin.AetheryteTriggerService.LastDecision}");
        ImGui.TextWrapped($"Alert sound path: {plugin.AudioPlaybackService.GetResolvedAlertPath()}");
        ImGui.Text($"Last alert (UTC): {FormatUtc(plugin.AetheryteTriggerService.LastTriggeredAtUtc)}");
    }

    private void DrawReadingRoegadynStatus()
    {
        if (ImGui.Button("Test speech"))
        {
            var queued = plugin.CommentaryTriggerService.SpeakManual();
            plugin.PrintStatus(queued ? "Speech queued." : plugin.CommentaryTriggerService.LastDecision);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear cache"))
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

        ImGui.TextWrapped("Reading Roegadyn uses cached local Windows TTS commentary.");
        ImGui.TextWrapped($"Speech backend: {plugin.Configuration.TtsBackend}");
        ImGui.TextWrapped($"Speech voice: {plugin.SpeechCacheService.GetSelectedVoiceLabel()}");
        ImGui.Text($"Pitch: {plugin.Configuration.TtsPitch:F2}  Output gain: {plugin.Configuration.TtsOutputGainPercent}%");
        ImGui.TextWrapped($"Trigger status: {plugin.CommentaryTriggerService.LastDecision}");
        ImGui.TextWrapped($"Queue status: {plugin.SpeechQueueService.LastStatus}");
        ImGui.Text($"Pending speech requests: {plugin.SpeechQueueService.PendingCount}");
        ImGui.TextWrapped($"Cache folder: {plugin.Configuration.GetResolvedTtsCacheDirectory()}");
        ImGui.Text($"Cache size: {plugin.SpeechCacheService.GetCacheSizeMegabytes():F1} MB");
        ImGui.TextWrapped($"Cache status: {plugin.SpeechCacheService.LastStatus}");
        ImGui.TextWrapped($"BGM status: {plugin.BgmProbeService.Status}");
        ImGui.Text($"Current BGM ID: {plugin.BgmProbeService.CurrentBgmId}");
    }

    private string GetModeStatus()
        => plugin.Configuration.CommentaryMode == CommentaryMode.Dheacon
            ? plugin.AetheryteTriggerService.LastDecision
            : plugin.CommentaryTriggerService.LastDecision;

    private static string FormatUtc(DateTime value)
        => value == DateTime.MinValue ? "Never" : value.ToString("yyyy-MM-dd HH:mm:ss");
}
