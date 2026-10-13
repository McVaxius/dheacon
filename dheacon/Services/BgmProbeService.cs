using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Dheacon.Services;

public sealed unsafe class BgmProbeService
{
    private const int PrioritySlotCount = 12;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IPluginLog log;
    private DateTime nextPollAtUtc = DateTime.MinValue;

    public BgmProbeService(IPluginLog log)
    {
        this.log = log;
    }

    public bool Available { get; private set; }
    public ushort CurrentBgmId { get; private set; }
    public DateTime LastPollAtUtc { get; private set; } = DateTime.MinValue;
    public string Status { get; private set; } = "BGM probe not initialized.";

    public bool Update()
    {
        var now = DateTime.UtcNow;
        if (now < nextPollAtUtc)
            return false;

        nextPollAtUtc = now + PollInterval;
        LastPollAtUtc = now;

        try
        {
            var bgmSystem = BGMSystem.Instance();
            if (bgmSystem == null)
            {
                Available = false;
                Status = "BGM control block pointer is not ready.";
                return false;
            }

            var scenes = bgmSystem->Scenes;
            if (scenes.First == null || scenes.LongCount <= 0)
            {
                Available = false;
                Status = "BGM scene list pointer is not ready.";
                return false;
            }

            var previous = CurrentBgmId;
            var current = ReadCurrentBgmId(scenes.First, scenes.LongCount);
            CurrentBgmId = current;
            Available = true;
            Status = current == 0
                ? "BGM probe active; no valid song id in priority slots."
                : $"BGM probe active; current BGM ID {current}.";

            return previous != current;
        }
        catch (Exception ex)
        {
            Available = false;
            Status = $"BGM probe read failed: {ex.Message}";
            log.Warning(ex, "[Dheacon] BGM probe read failed.");
            return false;
        }
    }

    private static ushort ReadCurrentBgmId(BGMSystem.Scene* scenes, long sceneCount)
    {
        var slotCount = Math.Min(sceneCount, PrioritySlotCount);
        for (var slot = 0; slot < slotCount; slot++)
        {
            var songId = scenes[slot].PlayingBgmId;
            if (songId != 0 && songId != 9999)
                return songId;
        }

        return 0;
    }
}
