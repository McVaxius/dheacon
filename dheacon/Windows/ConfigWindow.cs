using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Dheacon.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private static readonly string[] DtrModes = { "Text only", "Icon + text", "Icon only" };
    private static readonly string[] TtsBackendLabels = { "Modern Windows", "Legacy SAPI" };
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin) : base($"{PluginInfo.DisplayName} Settings##Config")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(620f, 520f), MaximumSize = new Vector2(1500f, 1300f) };
    }

    public void Dispose() { }

    public override void Draw()
    {
        var cfg = plugin.Configuration;

        var enabled = cfg.PluginEnabled;
        if (ImGui.Checkbox("Plugin enabled", ref enabled))
        {
            cfg.PluginEnabled = enabled;
            cfg.Save();
            plugin.UpdateDtrBar();
        }

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
        DrawModeSelector(cfg);
        ImGui.Separator();

        if (cfg.CommentaryMode == CommentaryMode.Dheacon)
            DrawDheaconSettings(cfg);
        else
            DrawReadingRoegadynSettings(cfg);
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

        ImGui.TextDisabled("Replace this WAV file to change the sound: " + plugin.AudioPlaybackService.GetResolvedAlertPath());
        ImGui.TextWrapped("Dheacon mode only plays the packaged transition-alert WAV on qualifying territory changes.");
    }

    private void DrawReadingRoegadynSettings(Configuration cfg)
    {
        ImGui.TextUnformatted("Reading Roegadyn");

        if (ImGui.Button("Test speech"))
        {
            var queued = plugin.CommentaryTriggerService.SpeakManual();
            plugin.PrintStatus(queued ? "Speech queued." : plugin.CommentaryTriggerService.LastDecision);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear cache"))
            ClearCacheToChat();

        var suppressTeleport = cfg.SuppressTeleportAndReturnTransitions;
        if (ImGui.Checkbox("Suppress teleports and return", ref suppressTeleport))
        {
            cfg.SuppressTeleportAndReturnTransitions = suppressTeleport;
            cfg.Save();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Speech cache");

        var cacheDirectory = cfg.TtsCacheDirectory;
        if (ImGui.InputText("Cache folder", ref cacheDirectory, 512))
        {
            cfg.TtsCacheDirectory = cacheDirectory;
            cfg.Save();
        }

        ImGui.TextDisabled("Resolved cache folder: " + cfg.GetResolvedTtsCacheDirectory());

        var maxMb = cfg.TtsMaxCacheMegabytes;
        if (ImGui.InputInt("Max cache MB", ref maxMb))
        {
            cfg.TtsMaxCacheMegabytes = Math.Max(1, maxMb);
            cfg.Save();
        }

        ImGui.Text($"Cache size: {plugin.SpeechCacheService.GetCacheSizeMegabytes():F1} MB");
        ImGui.TextWrapped("Cache status: " + plugin.SpeechCacheService.LastStatus);

        ImGui.Separator();
        ImGui.TextUnformatted("Voice");
        DrawBackendSelector(cfg);
        DrawVoiceActions();
        DrawVoiceSelector(cfg);

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

        var voiceCount = plugin.SpeechCacheService.GetInstalledVoices(cfg.TtsBackend).Count;
        ImGui.TextDisabled($"Detected {cfg.TtsBackend} voices: {voiceCount}");
        if (!string.IsNullOrWhiteSpace(plugin.SpeechCacheService.LastError))
            ImGui.TextWrapped("Speech warning: " + plugin.SpeechCacheService.LastError);

        ImGui.Separator();
        ImGui.TextUnformatted("Commentary");
        DrawCommentaryToggles(cfg);
        DrawCooldowns(cfg);

        ImGui.Separator();
        ImGui.TextUnformatted("BGM probe");
        ImGui.TextWrapped(plugin.BgmProbeService.Status);
        ImGui.Text($"Current BGM ID: {plugin.BgmProbeService.CurrentBgmId}");
        ImGui.TextWrapped("Trigger status: " + plugin.CommentaryTriggerService.LastDecision);
        ImGui.TextWrapped("Queue status: " + plugin.SpeechQueueService.LastStatus);
        if (!string.IsNullOrWhiteSpace(plugin.SpeechQueueService.LastError))
            ImGui.TextWrapped("Queue error: " + plugin.SpeechQueueService.LastError);
    }

    private void DrawVoiceSelector(Configuration cfg)
    {
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
        var backendIndex = cfg.TtsBackend == TtsBackend.LegacySapi ? 1 : 0;
        if (ImGui.Combo("Backend", ref backendIndex, TtsBackendLabels, TtsBackendLabels.Length))
        {
            cfg.TtsBackend = backendIndex == 1 ? TtsBackend.LegacySapi : TtsBackend.ModernWindows;
            cfg.Save();
        }
    }

    private void DrawVoiceActions()
    {
        if (ImGui.Button("Refresh voices"))
        {
            plugin.SpeechCacheService.RefreshInstalledVoices();
            var modernCount = plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.ModernWindows).Count;
            var legacyCount = plugin.SpeechCacheService.GetInstalledVoices(TtsBackend.LegacySapi).Count;
            plugin.PrintStatus($"Detected {modernCount} Modern Windows voice(s), {legacyCount} Legacy SAPI voice(s).");
        }

        ImGui.SameLine();
        if (ImGui.Button("Pick Swedish"))
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
    }

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
}
