namespace Dheacon;

internal static class PluginInfo
{
    public const string DisplayName = "Dheacon";
    public const string InternalName = "dheacon";
    public const string Command = "/dheacon";
    public const string Visibility = "Public";
    public const string Summary = "Funny aetheryte audio feedback scaffold with cooldown protection.";
    public const string SupportUrl = "https://ko-fi.com/mcvaxius";
    public static readonly string[] Concept = new[]
    {
        "Detect aetheryte-use once.",
        "Route through a local playback service.",
        "Keep audio optional and testable."
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
        "Toggle settings and save",
        "Check DTR toggle"
    };
}
