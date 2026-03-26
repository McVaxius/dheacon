using System.IO;
using System.Media;
using System.Reflection;
using Dalamud.Plugin.Services;

namespace Dheacon.Services;

public sealed class AudioPlaybackService
{
    private readonly IPluginLog log;
    private readonly Configuration configuration;

    public AudioPlaybackService(IPluginLog log, Configuration configuration)
    {
        this.log = log;
        this.configuration = configuration;
    }

    public string GetResolvedAlertPath()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        return Path.IsPathRooted(configuration.AlertSoundRelativePath)
            ? configuration.AlertSoundRelativePath
            : Path.GetFullPath(Path.Combine(assemblyDirectory, configuration.AlertSoundRelativePath));
    }

    public void PlayAlert()
    {
        var resolvedPath = GetResolvedAlertPath();
        if (File.Exists(resolvedPath))
        {
            using var player = new SoundPlayer(resolvedPath);
            player.Play();
            log.Information($"[Dheacon] Played alert sound: {resolvedPath}");
            return;
        }

        SystemSounds.Exclamation.Play();
        log.Warning($"[Dheacon] Alert sound missing at '{resolvedPath}', used SystemSounds.Exclamation instead.");
    }
}
