using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Dheacon.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private static readonly TimeSpan StatusRenderWarningInterval = TimeSpan.FromSeconds(10);

    private readonly Plugin plugin;
    private DateTime lastStatusRenderWarningUtc = DateTime.MinValue;

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
            plugin.KranglerImaginaryFrenIpcClient.ReconcileNow();
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
        DrawPresetSelector();
        ImGui.Separator();

        Action drawStatus = plugin.PresetService.ActivePreset.Mode == CommentaryMode.Dheacon
            ? DrawDheaconStatus
            : DrawReadingRoegadynStatus;
        DrawStatusSafely(drawStatus);

        ImGui.Separator();
        ImGui.Text($"Command: {PluginInfo.Command}");
    }

    private void DrawPresetSelector()
    {
        ImGui.TextUnformatted("Preset");
        var presets = plugin.PresetService.Presets.ToList();
        var active = plugin.PresetService.ActivePreset;
        var currentIndex = Math.Max(0, presets.FindIndex(preset => string.Equals(preset.Id, active.Id, StringComparison.OrdinalIgnoreCase)));
        var labels = presets.Select(preset => preset.Protected ? $"{preset.Name} *" : preset.Name).ToArray();

        ImGui.SetNextItemWidth(Math.Min(360f, ImGui.GetContentRegionAvail().X));
        if (ImGui.Combo("##DheaconPreset", ref currentIndex, labels, labels.Length))
        {
            plugin.PresetService.SetActivePreset(presets[currentIndex].Id, out var message);
            plugin.PrintStatus(message);
            plugin.UpdateDtrBar();
            plugin.KranglerImaginaryFrenIpcClient.ReconcileNow();
        }

        ImGui.TextWrapped(active.Description);
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

        ImGui.TextWrapped("Reading Roegadyn uses cached local TTS commentary.");
        ImGui.TextWrapped($"Speech backend: {plugin.Configuration.TtsBackend}");
        ImGui.TextWrapped($"Speech voice: {plugin.SpeechCacheService.GetSelectedVoiceLabel()}");
        if (plugin.Configuration.TtsBackend == TtsBackend.PiperLocal)
        {
            ImGui.TextWrapped("Piper runtime: " + GetLastPiperRuntimeStatus());
            ImGui.Text($"Piper speed: {plugin.Configuration.TtsPiperLengthScale:F2}  Sentence pause: {plugin.Configuration.TtsPiperSentenceSilence:F2}s  Pitch: {FormatPiperSemitones(plugin.Configuration.TtsPiperPitchShiftSemitones)} st  Playback gain: {plugin.Configuration.TtsOutputGainPercent}%");
            ImGui.TextWrapped("Last Piper pitch shift: " + plugin.SpeechCacheService.LastPiperPitchShiftStatus);
        }
        else
        {
            ImGui.Text($"Pitch: {plugin.Configuration.TtsPitch:F2}  Output gain: {plugin.Configuration.TtsOutputGainPercent}%");
        }
        ImGui.TextWrapped($"Trigger status: {plugin.CommentaryTriggerService.LastDecision}");
        ImGui.TextWrapped($"Queue status: {plugin.SpeechQueueService.LastStatus}");
        var currentText = string.IsNullOrWhiteSpace(plugin.SpeechQueueService.CurrentText)
            ? plugin.SpeechQueueService.LastText
            : plugin.SpeechQueueService.CurrentText;
        var currentCategory = string.IsNullOrWhiteSpace(plugin.SpeechQueueService.CurrentCategory)
            ? plugin.SpeechQueueService.LastCategory
            : plugin.SpeechQueueService.CurrentCategory;
        var currentReason = string.IsNullOrWhiteSpace(plugin.SpeechQueueService.CurrentReason)
            ? plugin.SpeechQueueService.LastReason
            : plugin.SpeechQueueService.CurrentReason;
        if (!string.IsNullOrWhiteSpace(currentText))
            ImGui.TextWrapped($"Speaking: {currentText}");
        if (!string.IsNullOrWhiteSpace(currentCategory) || !string.IsNullOrWhiteSpace(currentReason))
            ImGui.TextWrapped($"Speech context: {currentCategory} {currentReason}".Trim());
        ImGui.Text($"Pending speech requests: {plugin.SpeechQueueService.PendingCount}");
        ImGui.TextWrapped($"Follower: {plugin.KranglerImaginaryFrenIpcClient.LastStatus}");
        ImGui.TextWrapped($"Cache folder: {plugin.Configuration.GetResolvedTtsCacheDirectory()}");
        ImGui.Text($"Cache size: {plugin.SpeechCacheService.GetCacheSizeMegabytes():F1} MB");
        ImGui.TextWrapped($"Cache status: {plugin.SpeechCacheService.LastStatus}");
        if (!string.IsNullOrWhiteSpace(plugin.SpeechCacheService.LastError))
            ImGui.TextWrapped("Speech warning: " + plugin.SpeechCacheService.LastError);

        ImGui.TextWrapped($"BGM status: {plugin.BgmProbeService.Status}");
        ImGui.Text($"Current BGM ID: {plugin.BgmProbeService.CurrentBgmId}");
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

            ImGui.TextWrapped("Status temporarily unavailable: " + ex.Message);
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
        => semitones.ToString("+0.0;-0.0;0.0");

    private static string FormatUtc(DateTime value)
        => value == DateTime.MinValue ? "Never" : value.ToString("yyyy-MM-dd HH:mm:ss");
}
