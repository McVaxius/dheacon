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
    PiperLocal = 2,
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 12;
    public const string DefaultPiperVoiceId = "official:en_US-arctic-medium";
    private const int SetupWizardIntroducedVersion = 12;

    public int Version { get; set; } = CurrentVersion;
    public bool SetupWizardCompleted { get; set; } = false;
    public bool PluginEnabled { get; set; } = false;
    public CommentaryMode CommentaryMode { get; set; } = CommentaryMode.ReadingRoegadyn;
    public string ActivePresetId { get; set; } = "reading-roegadyn";
    public bool DtrBarEnabled { get; set; } = true;
    public int DtrBarMode { get; set; } = 1;
    public string DtrIconEnabled { get; set; } = "\uE044";
    public string DtrIconDisabled { get; set; } = "\uE04C";
    public bool MiniAutoOpenOnSpeech { get; set; } = false;
    public bool SuppressTeleportAndReturnTransitions { get; set; } = true;
    public string AlertSoundRelativePath { get; set; } = @"data\transition-alert.wav";
    public string LastAccountId { get; set; } = string.Empty;

    public string TtsCacheDirectory { get; set; } = string.Empty;
    public int TtsMaxCacheMegabytes { get; set; } = 256;
    public TtsBackend TtsBackend { get; set; } = TtsBackend.PiperLocal;
    public string TtsModernVoiceId { get; set; } = string.Empty;
    public string TtsVoiceName { get; set; } = string.Empty;
    public string TtsPiperVoiceId { get; set; } = DefaultPiperVoiceId;
    public string TtsPiperInstalledVoicesManifestPath { get; set; } = string.Empty;
    public DateTime TtsPiperCatalogRefreshedAtUtc { get; set; } = DateTime.MinValue;
    public string TtsPiperRuntimePath { get; set; } = string.Empty;
    public string TtsPiperRuntimeStatus { get; set; } = string.Empty;
    public bool TtsPiperTextAdapterEnabled { get; set; } = true;
    public string TtsPiperTextAdapterId { get; set; } = "en_US-to-sv_SE";
    [Obsolete("Use TtsPiperTextAdapterEnabled instead.")]
    public bool TtsPiperSwedishAccentAdapterEnabled { get; set; } = true;
    public int TtsRate { get; set; } = 0;
    public int TtsVolume { get; set; } = 100;
    public double TtsPitch { get; set; } = 0.75d;
    public int TtsOutputGainPercent { get; set; } = 200;
    public double TtsPiperLengthScale { get; set; } = 1.0d;
    public double TtsPiperSentenceSilence { get; set; } = 0.2d;
    public double TtsPiperPitchShiftSemitones { get; set; } = -3.8d;

    public bool LoginCommentaryEnabled { get; set; } = true;
    public bool TerritoryCommentaryEnabled { get; set; } = true;
    public bool IdleCommentaryEnabled { get; set; } = true;
    public bool CombatCommentaryEnabled { get; set; } = true;
    public bool BgmMachinationsCommentaryEnabled { get; set; } = true;
    public bool ExpandedEventCommentaryEnabled { get; set; } = true;
    public bool NearbyObservationCommentaryEnabled { get; set; } = true;
    public int ReadingRoegadynTriggerChancePercent { get; set; } = 25;

    public int TerritoryCommentaryCooldownSeconds { get; set; } = 8;
    public int IdleCommentaryCooldownSeconds { get; set; } = 600;
    public int CombatCommentaryCooldownSeconds { get; set; } = 20;
    public int BgmCommentaryCooldownSeconds { get; set; } = 120;
    public int ExpandedEventCooldownSeconds { get; set; } = 45;
    public int NearbyObservationCooldownSeconds { get; set; } = 180;

    public string GetResolvedTtsCacheDirectory()
    {
        if (!string.IsNullOrWhiteSpace(TtsCacheDirectory))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(TtsCacheDirectory));

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Dheacon", "tts-cache");
    }

    public string GetResolvedPiperRootDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Dheacon", "piper");
    }

    public string GetResolvedPiperInstalledVoicesManifestPath()
    {
        if (!string.IsNullOrWhiteSpace(TtsPiperInstalledVoicesManifestPath))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(TtsPiperInstalledVoicesManifestPath));

        return Path.Combine(GetResolvedPiperRootDirectory(), "installed-voices.json");
    }

    public string GetResolvedPiperVoiceDirectory()
        => Path.Combine(Path.GetDirectoryName(GetResolvedPiperInstalledVoicesManifestPath()) ?? GetResolvedPiperRootDirectory(), "voices");

    public string GetResolvedPiperCatalogCachePath()
        => Path.Combine(GetResolvedPiperRootDirectory(), "voice-catalog-cache.json");

    public string GetResolvedPiperRuntimeDirectory()
        => Path.Combine(GetResolvedPiperRootDirectory(), "runtime");

    public string GetConfiguredPiperRuntimePath()
    {
        if (!string.IsNullOrWhiteSpace(TtsPiperRuntimePath))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(TtsPiperRuntimePath));

        return Path.Combine(GetResolvedPiperRuntimeDirectory(), "piper.exe");
    }

    internal bool NeedsSetupWizard => !SetupWizardCompleted;

    internal bool MigrateSetupWizardState()
    {
        if (Version >= SetupWizardIntroducedVersion)
            return false;

        SetupWizardCompleted = true;
        return true;
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
