namespace Dheacon;

internal static class PluginInfo
{
    public const string DisplayName = "Dheacon";
    public const string InternalName = "dheacon";
    public const string Command = "/dheacon";
    public const string Visibility = "Public";
    public const string Punchline = "Local TTS commentary and classic transition alerts.";
    public const string Summary = "Preset-driven commentary for login, travel, combat, idle, BGM, and other events; cached Piper or Windows speech; configurable triggers and cooldowns; and optional Krangler integration.";
    public const string SupportUrl = "https://ko-fi.com/mcvaxius";
    public static readonly string[] Concept = new[]
    {
        "Dheacon mode preserves the legacy transition-alert WAV behavior.",
        "Reading Roegadyn mode speaks local cached TTS commentary.",
        "Route fragile BGM detection through a read-only fail-soft probe."
    };
    public static readonly string[] Services = new[]
    {
        "AetheryteTriggerService",
        "AudioPlaybackService",
        "CommentaryLinePackService",
        "PiperVoiceCatalogService",
        "SpokenTextAdapterService",
        "SpeechCacheService",
        "SpeechQueueService",
        "CommentaryTriggerService",
        "BgmProbeService"
    };
    public static readonly string[] Phases = new[]
    {
        "Legacy WAV preservation",
        "Local TTS cache",
        "Commentary triggers",
        "BGM Machinations probe"
    };
    public static readonly string[] Tests = new[]
    {
        "Verify Quick Setup auto-opens only for a new v12 configuration",
        "Verify migrated configurations suppress automatic Quick Setup",
        "Load plugin and open UI",
        "Verify Reading Roegadyn is the default selected mode",
        "Verify Dheacon mode still plays data\\transition-alert.wav only",
        "Verify repeated TTS uses a cached WAV"
    };
}
