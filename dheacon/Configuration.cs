using Dalamud.Configuration;
using System;
using System.IO;

namespace Dheacon;

public enum CommentaryMode
{
    Dheacon = 0,
    ReadingRoegadyn = 1,
}

public enum TtsBackend
{
    ModernWindows = 0,
    LegacySapi = 1,
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;
    public bool PluginEnabled { get; set; } = false;
    public CommentaryMode CommentaryMode { get; set; } = CommentaryMode.ReadingRoegadyn;
    public bool DtrBarEnabled { get; set; } = true;
    public int DtrBarMode { get; set; } = 1;
    public string DtrIconEnabled { get; set; } = "\uE044";
    public string DtrIconDisabled { get; set; } = "\uE04C";
    public bool SuppressTeleportAndReturnTransitions { get; set; } = true;
    public string AlertSoundRelativePath { get; set; } = @"data\transition-alert.wav";
    public string LastAccountId { get; set; } = string.Empty;

    public string TtsCacheDirectory { get; set; } = string.Empty;
    public int TtsMaxCacheMegabytes { get; set; } = 256;
    public TtsBackend TtsBackend { get; set; } = TtsBackend.ModernWindows;
    public string TtsModernVoiceId { get; set; } = string.Empty;
    public string TtsVoiceName { get; set; } = string.Empty;
    public int TtsRate { get; set; } = 0;
    public int TtsVolume { get; set; } = 100;
    public double TtsPitch { get; set; } = 0.75d;
    public int TtsOutputGainPercent { get; set; } = 200;

    public bool LoginCommentaryEnabled { get; set; } = true;
    public bool TerritoryCommentaryEnabled { get; set; } = true;
    public bool IdleCommentaryEnabled { get; set; } = true;
    public bool CombatCommentaryEnabled { get; set; } = true;
    public bool BgmMachinationsCommentaryEnabled { get; set; } = true;

    public int TerritoryCommentaryCooldownSeconds { get; set; } = 8;
    public int IdleCommentaryCooldownSeconds { get; set; } = 600;
    public int CombatCommentaryCooldownSeconds { get; set; } = 20;
    public int BgmCommentaryCooldownSeconds { get; set; } = 120;

    public string GetResolvedTtsCacheDirectory()
    {
        if (!string.IsNullOrWhiteSpace(TtsCacheDirectory))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(TtsCacheDirectory));

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Dheacon", "tts-cache");
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
