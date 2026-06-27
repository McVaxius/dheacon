using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;

namespace Dheacon.Services;

public sealed unsafe class BgmProbeService
{
    private const string BgmControlSignature = "48 8B 05 ?? ?? ?? ?? 48 85 C0 74 42 83 78 08 0A";
    private const int SceneListOffset = 0xC0;
    private const int PrioritySlotCount = 12;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ISigScanner sigScanner;
    private readonly IPluginLog log;
    private IntPtr baseAddress;
    private bool resolutionAttempted;
    private bool signatureFailed;
    private DateTime nextPollAtUtc = DateTime.MinValue;

    public BgmProbeService(ISigScanner sigScanner, IPluginLog log)
    {
        this.sigScanner = sigScanner;
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

        if (!EnsureResolved())
            return false;

        try
        {
            var baseObject = Marshal.ReadIntPtr(baseAddress);
            if (baseObject == IntPtr.Zero)
            {
                Available = false;
                Status = "BGM control block pointer is not ready.";
                return false;
            }

            var sceneListPointer = Marshal.ReadIntPtr(baseObject + SceneListOffset);
            if (sceneListPointer == IntPtr.Zero)
            {
                Available = false;
                Status = "BGM scene list pointer is not ready.";
                return false;
            }

            var previous = CurrentBgmId;
            var current = ReadCurrentBgmId((BgmScene*)sceneListPointer);
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

    private bool EnsureResolved()
    {
        if (baseAddress != IntPtr.Zero)
            return true;

        if (signatureFailed)
            return false;

        if (resolutionAttempted)
            return false;

        resolutionAttempted = true;

        try
        {
            if (!sigScanner.TryGetStaticAddressFromSig(BgmControlSignature, out baseAddress, 0))
            {
                signatureFailed = true;
                Status = "BGM signature not resolved; BGM reactions disabled.";
                Available = false;
                return false;
            }

            Status = "BGM signature resolved; waiting for first poll.";
            return true;
        }
        catch (Exception ex)
        {
            signatureFailed = true;
            Status = $"BGM signature resolution failed: {ex.Message}";
            Available = false;
            log.Warning(ex, "[Dheacon] BGM signature resolution failed.");
            return false;
        }
    }

    private static ushort ReadCurrentBgmId(BgmScene* scenes)
    {
        for (var slot = 0; slot < PrioritySlotCount; slot++)
        {
            var songId2 = scenes[slot].BgmId;
            if (songId2 != 0 && songId2 != 9999)
                return songId2;
        }

        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct BgmScene
    {
        public int SceneIndex;
        public int Flags;
        public int Padding1;
        public ushort BgmReference;
        public ushort BgmId;
        public ushort PreviousBgmId;
        public byte TimerEnable;
        public byte Padding2;
        public float Timer;
        public fixed byte DisableRestartList[24];
        public byte Unknown1;
        public uint Unknown2;
        public uint Unknown3;
        public uint Unknown4;
        public uint Unknown5;
        public uint Unknown6;
        public ulong Unknown7;
        public uint Unknown8;
        public byte Unknown9;
        public byte Unknown10;
        public byte Unknown11;
        public byte Unknown12;
        public float Unknown13;
        public uint Unknown14;
    }
}
