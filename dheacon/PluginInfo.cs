namespace Dheacon;

internal static class PluginInfo
{
    public const string DisplayName = "Dheacon";
    public const string InternalName = "dheacon";
    public const string Command = "/dheacon";
    public const string Visibility = "Public";
    public const string Summary = "Area-transition audio cue with teleport/return suppression heuristics.";
    public const string SupportUrl = "https://ko-fi.com/mcvaxius";
    public static readonly string[] Concept = new[]
    {
        "Detect territory changes that were not likely caused by teleport/return.",
        "Alert on non-teleport territory transitions.",
        "Route through a replaceable local WAV file."
    };
    public static readonly string[] Services = new[]
    {
        "AetheryteTriggerService",
        "AudioPlaybackService"
    };
    public static readonly string[] Phases = new[]
    {
        "Shell and docs",
        "Event detection",
        "Playback",
        "Asset swap and polish"
    };
    public static readonly string[] Tests = new[]
    {
        "Load plugin and open UI",
        "Change the alert WAV path and save",
        "Verify non-teleport territory transitions trigger audio"
    };
}
