namespace Dheacon;

internal static class PluginInfo
{
    public const string DisplayName = "Dheacon";
    public const string InternalName = "dheacon";
    public const string Command = "/dheacon";
    public const string Visibility = "Public";
    public const string Summary = "Dual-mode audio cue and local TTS commentator with teleport/return suppression heuristics.";
    public const string SupportUrl = "https://ko-fi.com/mcvaxius";
    public static readonly string[] Concept = new[]
    {
        "Dheacon mode preserves the legacy transition-alert WAV behavior.",
        "Reading Roegadyn mode speaks local cached Windows TTS commentary.",
        "Route fragile BGM detection through a read-only fail-soft probe."
    };
    public static readonly string[] Services = new[]
    {
        "AetheryteTriggerService",
        "AudioPlaybackService",
        "CommentaryLinePackService",
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
        "Load plugin and open UI",
        "Verify Reading Roegadyn is the default selected mode",
        "Verify Dheacon mode still plays data\\transition-alert.wav only",
        "Verify repeated TTS uses a cached WAV"
    };
}
