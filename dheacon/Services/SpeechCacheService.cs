using System.Globalization;
using System.Security.Cryptography;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;
using LegacySpeechSynthesizer = System.Speech.Synthesis.SpeechSynthesizer;
using ModernSpeechSynthesizer = Windows.Media.SpeechSynthesis.SpeechSynthesizer;

namespace Dheacon.Services;

public sealed record SpeechVoiceInfo(
    TtsBackend Backend,
    string Id,
    string DisplayName,
    string Culture,
    string Gender)
{
    public string Label => $"{DisplayName} - {Culture} - {Gender}";
}

public sealed partial class SpeechCacheService
{
    private const string DefaultVoiceLabel = "Windows default";
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly object voiceLock = new();
    private List<SpeechVoiceInfo>? modernVoices;
    private List<SpeechVoiceInfo>? legacyVoices;

    public SpeechCacheService(IPluginLog log, Configuration configuration)
    {
        this.log = log;
        this.configuration = configuration;
    }

    public string LastStatus { get; private set; } = "No speech generated yet.";
    public string LastError { get; private set; } = string.Empty;
    public string LastWavPath { get; private set; } = string.Empty;

    public IReadOnlyList<SpeechVoiceInfo> GetInstalledVoices()
        => GetInstalledVoices(configuration.TtsBackend);

    public IReadOnlyList<SpeechVoiceInfo> GetInstalledVoices(TtsBackend backend)
    {
        lock (voiceLock)
        {
            return backend switch
            {
                TtsBackend.ModernWindows => modernVoices ??= LoadModernVoices(),
                TtsBackend.LegacySapi => legacyVoices ??= LoadLegacyVoices(),
                _ => Array.Empty<SpeechVoiceInfo>(),
            };
        }
    }

    public void RefreshInstalledVoices()
    {
        lock (voiceLock)
        {
            modernVoices = LoadModernVoices();
            legacyVoices = LoadLegacyVoices();
        }
    }

    public string GetSelectedVoiceLabel()
    {
        var backend = configuration.TtsBackend;
        var voices = GetInstalledVoices(backend);
        var voice = backend == TtsBackend.ModernWindows
            ? FindModernVoice(voices, configuration.TtsModernVoiceId, configuration.TtsVoiceName)
            : FindLegacyVoice(voices, configuration.TtsVoiceName);

        return voice?.Label ?? DefaultVoiceLabel;
    }

    public SpeechVoiceInfo? SelectFirstSwedishVoice(out bool usedMaleVoice)
    {
        usedMaleVoice = false;

        var voices = GetInstalledVoices(TtsBackend.ModernWindows);
        var swedishVoices = voices
            .Where(voice => IsSwedishCulture(voice.Culture))
            .ToList();

        var selected = swedishVoices.FirstOrDefault(voice => IsMaleGender(voice.Gender));
        if (selected != null)
            usedMaleVoice = true;
        else
            selected = swedishVoices.FirstOrDefault();

        if (selected == null)
            return null;

        configuration.TtsBackend = TtsBackend.ModernWindows;
        configuration.TtsModernVoiceId = selected.Id;
        configuration.TtsVoiceName = selected.DisplayName;
        configuration.Save();
        return selected;
    }

    public string GetOrCreateWav(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedText = NormalizeText(text);
        if (string.IsNullOrWhiteSpace(normalizedText))
            throw new InvalidOperationException("Cannot generate speech for empty text.");

        var backend = configuration.TtsBackend;
        try
        {
            return GetOrCreateWavForBackend(backend, normalizedText, cancellationToken);
        }
        catch (Exception ex) when (backend == TtsBackend.ModernWindows)
        {
            log.Warning(ex, "[Dheacon] Modern Windows speech failed; attempting Legacy SAPI fallback.");
            var fallbackPath = GetOrCreateWavForBackend(TtsBackend.LegacySapi, normalizedText, cancellationToken);
            LastError = "Modern Windows speech failed; used Legacy SAPI fallback: " + ex.Message;
            LastStatus = $"Modern Windows failed; used Legacy SAPI fallback: {Path.GetFileName(fallbackPath)}";
            return fallbackPath;
        }
    }

    public int ClearCache()
    {
        var cacheDirectory = configuration.GetResolvedTtsCacheDirectory();
        if (!Directory.Exists(cacheDirectory))
            return 0;

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(cacheDirectory, "*.wav", SearchOption.TopDirectoryOnly))
        {
            File.Delete(file);
            deleted++;
        }

        foreach (var file in Directory.EnumerateFiles(cacheDirectory, "*.tmp.wav", SearchOption.TopDirectoryOnly))
            TryDelete(file);

        LastStatus = $"Cleared {deleted} cached WAV file(s).";
        LastWavPath = string.Empty;
        LastError = string.Empty;
        return deleted;
    }

    public double GetCacheSizeMegabytes()
    {
        var cacheDirectory = configuration.GetResolvedTtsCacheDirectory();
        if (!Directory.Exists(cacheDirectory))
            return 0d;

        var bytes = Directory.EnumerateFiles(cacheDirectory, "*.wav", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path).Length)
            .Sum();

        return bytes / 1024d / 1024d;
    }

    private string GetOrCreateWavForBackend(TtsBackend backend, string normalizedText, CancellationToken cancellationToken)
    {
        var cacheDirectory = configuration.GetResolvedTtsCacheDirectory();
        Directory.CreateDirectory(cacheDirectory);

        var settings = CreateSettings(backend);
        var cacheKey = string.Join(
            "\n",
            "v3",
            settings.Backend,
            settings.VoiceId,
            settings.VoiceName,
            normalizedText,
            settings.Rate.ToString(CultureInfo.InvariantCulture),
            settings.SynthVolume.ToString(CultureInfo.InvariantCulture),
            settings.Pitch.ToString("0.###", CultureInfo.InvariantCulture),
            settings.OutputGainPercent.ToString(CultureInfo.InvariantCulture));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey))).ToLowerInvariant();
        var wavPath = Path.Combine(cacheDirectory, $"{hash}.wav");

        if (File.Exists(wavPath))
        {
            Touch(wavPath);
            LastWavPath = wavPath;
            LastStatus = $"Cache hit: {Path.GetFileName(wavPath)}";
            LastError = string.Empty;
            return wavPath;
        }

        var tempPath = Path.Combine(cacheDirectory, $"{hash}.{Guid.NewGuid():N}.tmp.wav");

        try
        {
            if (backend == TtsBackend.ModernWindows)
                SynthesizeModernWindowsWav(normalizedText, tempPath, settings, cancellationToken);
            else
                SynthesizeLegacySapiWav(normalizedText, tempPath, settings, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            var gainWarning = ApplyOutputGain(tempPath, settings.OutputGainPercent);

            if (File.Exists(wavPath))
            {
                File.Delete(tempPath);
            }
            else
            {
                File.Move(tempPath, wavPath);
            }

            Touch(wavPath);
            PruneCache(cacheDirectory);

            LastWavPath = wavPath;
            LastStatus = $"Generated {settings.Backend}: {Path.GetFileName(wavPath)}";
            LastError = gainWarning ?? string.Empty;
            return wavPath;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private SpeechSynthesisSettings CreateSettings(TtsBackend backend)
    {
        var rate = Math.Clamp(configuration.TtsRate, -10, 10);
        var synthVolume = Math.Clamp(configuration.TtsVolume, 0, 100);
        var pitch = Math.Clamp(configuration.TtsPitch, 0.0d, 2.0d);
        var outputGainPercent = Math.Clamp(configuration.TtsOutputGainPercent, 0, 400);

        if (backend == TtsBackend.ModernWindows)
        {
            var voices = GetInstalledVoices(TtsBackend.ModernWindows);
            var voice = FindModernVoice(voices, configuration.TtsModernVoiceId, configuration.TtsVoiceName);
            var voiceId = voice?.Id ?? configuration.TtsModernVoiceId.Trim();
            var voiceName = voice?.DisplayName ?? configuration.TtsVoiceName.Trim();
            if (string.IsNullOrWhiteSpace(voiceId))
                voiceId = DefaultVoiceLabel;
            if (string.IsNullOrWhiteSpace(voiceName))
                voiceName = DefaultVoiceLabel;

            return new SpeechSynthesisSettings(backend, voiceId, voiceName, rate, synthVolume, pitch, outputGainPercent);
        }

        var legacyVoiceName = configuration.TtsVoiceName.Trim();
        if (string.IsNullOrWhiteSpace(legacyVoiceName))
            legacyVoiceName = DefaultVoiceLabel;

        return new SpeechSynthesisSettings(backend, legacyVoiceName, legacyVoiceName, rate, synthVolume, pitch, outputGainPercent);
    }

    private void SynthesizeModernWindowsWav(
        string normalizedText,
        string tempPath,
        SpeechSynthesisSettings settings,
        CancellationToken cancellationToken)
    {
        using var synthesizer = new ModernSpeechSynthesizer();
        synthesizer.Options.AudioPitch = settings.Pitch;
        synthesizer.Options.AudioVolume = settings.SynthVolume / 100d;
        synthesizer.Options.SpeakingRate = MapModernSpeakingRate(settings.Rate);

        var selectedVoice = ResolveModernVoiceInformation(settings.VoiceId, settings.VoiceName);
        if (selectedVoice != null)
            synthesizer.Voice = selectedVoice;

        using var speechStream = synthesizer
            .SynthesizeTextToStreamAsync(normalizedText)
            .AsTask(cancellationToken)
            .GetAwaiter()
            .GetResult();

        cancellationToken.ThrowIfCancellationRequested();

        if (speechStream.Size > uint.MaxValue)
            throw new InvalidOperationException("Generated speech stream is too large to cache.");

        using var reader = new DataReader(speechStream.GetInputStreamAt(0));
        var loaded = reader
            .LoadAsync((uint)speechStream.Size)
            .AsTask(cancellationToken)
            .GetAwaiter()
            .GetResult();
        var bytes = new byte[loaded];
        reader.ReadBytes(bytes);
        File.WriteAllBytes(tempPath, bytes);
    }

    private void SynthesizeLegacySapiWav(
        string normalizedText,
        string tempPath,
        SpeechSynthesisSettings settings,
        CancellationToken cancellationToken)
    {
        using var synthesizer = new LegacySpeechSynthesizer();
        synthesizer.Rate = settings.Rate;
        synthesizer.Volume = settings.SynthVolume;

        var voiceName = configuration.TtsVoiceName.Trim();
        if (!string.IsNullOrWhiteSpace(voiceName) && LegacyVoiceExists(synthesizer, voiceName))
            synthesizer.SelectVoice(voiceName);

        synthesizer.SetOutputToWaveFile(tempPath);
        synthesizer.Speak(normalizedText);
        synthesizer.SetOutputToNull();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private List<SpeechVoiceInfo> LoadModernVoices()
    {
        try
        {
            return ModernSpeechSynthesizer.AllVoices
                .Select(voice => new SpeechVoiceInfo(
                    TtsBackend.ModernWindows,
                    voice.Id ?? string.Empty,
                    voice.DisplayName ?? string.Empty,
                    string.IsNullOrWhiteSpace(voice.Language) ? "unknown" : voice.Language,
                    voice.Gender.ToString()))
                .Where(voice => !string.IsNullOrWhiteSpace(voice.DisplayName))
                .OrderBy(voice => voice.Culture, StringComparer.OrdinalIgnoreCase)
                .ThenBy(voice => voice.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            log.Warning(ex, "[Dheacon] Failed to enumerate Modern Windows speech voices.");
            return new List<SpeechVoiceInfo>();
        }
    }

    private List<SpeechVoiceInfo> LoadLegacyVoices()
    {
        try
        {
            using var synthesizer = new LegacySpeechSynthesizer();
            return synthesizer.GetInstalledVoices()
                .Where(voice => voice.Enabled)
                .Select(voice => new SpeechVoiceInfo(
                    TtsBackend.LegacySapi,
                    voice.VoiceInfo.Name,
                    voice.VoiceInfo.Name,
                    string.IsNullOrWhiteSpace(voice.VoiceInfo.Culture?.Name) ? "unknown" : voice.VoiceInfo.Culture.Name,
                    voice.VoiceInfo.Gender.ToString()))
                .OrderBy(voice => voice.Culture, StringComparer.OrdinalIgnoreCase)
                .ThenBy(voice => voice.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            log.Warning(ex, "[Dheacon] Failed to enumerate Legacy SAPI speech voices.");
            return new List<SpeechVoiceInfo>();
        }
    }

    private VoiceInformation? ResolveModernVoiceInformation(string voiceId, string voiceName)
    {
        try
        {
            return ModernSpeechSynthesizer.AllVoices.FirstOrDefault(voice =>
                    !string.IsNullOrWhiteSpace(voiceId) &&
                    string.Equals(voice.Id, voiceId, StringComparison.OrdinalIgnoreCase))
                ?? ModernSpeechSynthesizer.AllVoices.FirstOrDefault(voice =>
                    !string.IsNullOrWhiteSpace(voiceName) &&
                    string.Equals(voice.DisplayName, voiceName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[Dheacon] Failed to resolve selected Modern Windows speech voice.");
            return null;
        }
    }

    private static SpeechVoiceInfo? FindModernVoice(IReadOnlyList<SpeechVoiceInfo> voices, string voiceId, string legacyVoiceName)
    {
        var selected = voices.FirstOrDefault(voice =>
            !string.IsNullOrWhiteSpace(voiceId) &&
            string.Equals(voice.Id, voiceId, StringComparison.OrdinalIgnoreCase));

        if (selected != null)
            return selected;

        return voices.FirstOrDefault(voice =>
            !string.IsNullOrWhiteSpace(legacyVoiceName) &&
            string.Equals(voice.DisplayName, legacyVoiceName, StringComparison.OrdinalIgnoreCase));
    }

    private static SpeechVoiceInfo? FindLegacyVoice(IReadOnlyList<SpeechVoiceInfo> voices, string voiceName)
    {
        return voices.FirstOrDefault(voice =>
            !string.IsNullOrWhiteSpace(voiceName) &&
            string.Equals(voice.DisplayName, voiceName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LegacyVoiceExists(LegacySpeechSynthesizer synthesizer, string voiceName)
    {
        return synthesizer.GetInstalledVoices()
            .Any(voice => voice.Enabled && string.Equals(voice.VoiceInfo.Name, voiceName, StringComparison.OrdinalIgnoreCase));
    }

    private static double MapModernSpeakingRate(int rate)
        => rate < 0
            ? Math.Clamp(1.0d + (rate * 0.05d), 0.5d, 1.0d)
            : Math.Clamp(1.0d + (rate * 0.1d), 1.0d, 2.0d);

    private string? ApplyOutputGain(string wavPath, int outputGainPercent)
    {
        if (outputGainPercent == 100)
            return null;

        try
        {
            var bytes = File.ReadAllBytes(wavPath);
            if (!TryApplyWavGain(bytes, outputGainPercent / 100d, out var warning))
            {
                log.Warning($"[Dheacon] Could not apply speech output gain to '{wavPath}': {warning}. Using unmodified WAV.");
                return warning;
            }

            File.WriteAllBytes(wavPath, bytes);
            return null;
        }
        catch (Exception ex)
        {
            log.Warning(ex, $"[Dheacon] Failed to apply speech output gain to '{wavPath}'. Using unmodified WAV.");
            return ex.Message;
        }
    }

    private static bool TryApplyWavGain(byte[] bytes, double gain, out string warning)
    {
        warning = string.Empty;
        if (bytes.Length < 12 || !BytesEqual(bytes, 0, "RIFF") || !BytesEqual(bytes, 8, "WAVE"))
        {
            warning = "not a RIFF/WAVE file";
            return false;
        }

        var offset = 12;
        var formatTag = 0;
        var bitsPerSample = 0;
        var dataOffset = -1;
        var dataSize = 0;

        while (offset + 8 <= bytes.Length)
        {
            var chunkId = Encoding.ASCII.GetString(bytes, offset, 4);
            var chunkSize = ReadUInt32(bytes, offset + 4);
            var chunkDataOffset = offset + 8;
            if (chunkSize > int.MaxValue || chunkDataOffset + chunkSize > bytes.Length)
            {
                warning = $"invalid WAV chunk '{chunkId}'";
                return false;
            }

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16)
                {
                    warning = "fmt chunk is too small";
                    return false;
                }

                formatTag = ReadUInt16(bytes, chunkDataOffset);
                bitsPerSample = ReadUInt16(bytes, chunkDataOffset + 14);
            }
            else if (chunkId == "data")
            {
                dataOffset = chunkDataOffset;
                dataSize = (int)chunkSize;
            }

            offset = chunkDataOffset + (int)chunkSize + ((chunkSize & 1) == 1 ? 1 : 0);
        }

        if (dataOffset < 0 || dataSize <= 0)
        {
            warning = "missing data chunk";
            return false;
        }

        return formatTag switch
        {
            1 => TryApplyPcmGain(bytes, dataOffset, dataSize, bitsPerSample, gain, out warning),
            3 => TryApplyFloatGain(bytes, dataOffset, dataSize, bitsPerSample, gain, out warning),
            _ => Fail($"unsupported WAV format tag {formatTag}", out warning),
        };
    }

    private static bool TryApplyPcmGain(byte[] bytes, int dataOffset, int dataSize, int bitsPerSample, double gain, out string warning)
    {
        warning = string.Empty;
        switch (bitsPerSample)
        {
            case 8:
                for (var i = dataOffset; i < dataOffset + dataSize; i++)
                {
                    var centered = bytes[i] - 128;
                    bytes[i] = (byte)Math.Clamp((int)Math.Round((centered * gain) + 128), 0, 255);
                }

                return true;

            case 16:
                if ((dataSize % 2) != 0)
                    return Fail("16-bit PCM data is not sample aligned", out warning);

                for (var i = dataOffset; i < dataOffset + dataSize; i += 2)
                {
                    var sample = (short)(bytes[i] | (bytes[i + 1] << 8));
                    var scaled = Math.Clamp((int)Math.Round(sample * gain), short.MinValue, short.MaxValue);
                    bytes[i] = (byte)(scaled & 0xFF);
                    bytes[i + 1] = (byte)((scaled >> 8) & 0xFF);
                }

                return true;

            case 24:
                if ((dataSize % 3) != 0)
                    return Fail("24-bit PCM data is not sample aligned", out warning);

                for (var i = dataOffset; i < dataOffset + dataSize; i += 3)
                {
                    var sample = bytes[i] | (bytes[i + 1] << 8) | (bytes[i + 2] << 16);
                    if ((sample & 0x800000) != 0)
                        sample |= unchecked((int)0xFF000000);

                    var scaled = Math.Clamp((int)Math.Round(sample * gain), -8388608, 8388607);
                    bytes[i] = (byte)(scaled & 0xFF);
                    bytes[i + 1] = (byte)((scaled >> 8) & 0xFF);
                    bytes[i + 2] = (byte)((scaled >> 16) & 0xFF);
                }

                return true;

            case 32:
                if ((dataSize % 4) != 0)
                    return Fail("32-bit PCM data is not sample aligned", out warning);

                for (var i = dataOffset; i < dataOffset + dataSize; i += 4)
                {
                    var sample = BitConverter.ToInt32(bytes, i);
                    var scaled = Math.Clamp(sample * gain, int.MinValue, int.MaxValue);
                    WriteInt32(bytes, i, (int)Math.Round(scaled));
                }

                return true;

            default:
                return Fail($"unsupported PCM bit depth {bitsPerSample}", out warning);
        }
    }

    private static bool TryApplyFloatGain(byte[] bytes, int dataOffset, int dataSize, int bitsPerSample, double gain, out string warning)
    {
        warning = string.Empty;
        if (bitsPerSample != 32)
            return Fail($"unsupported IEEE float bit depth {bitsPerSample}", out warning);

        if ((dataSize % 4) != 0)
            return Fail("float WAV data is not sample aligned", out warning);

        for (var i = dataOffset; i < dataOffset + dataSize; i += 4)
        {
            var sample = BitConverter.ToSingle(bytes, i);
            var scaled = Math.Clamp(sample * gain, -1.0d, 1.0d);
            WriteSingle(bytes, i, (float)scaled);
        }

        return true;
    }

    private void PruneCache(string cacheDirectory)
    {
        var maxBytes = Math.Max(1, configuration.TtsMaxCacheMegabytes) * 1024L * 1024L;
        var files = Directory.EnumerateFiles(cacheDirectory, "*.wav", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .OrderBy(file => file.LastAccessTimeUtc)
            .ThenBy(file => file.LastWriteTimeUtc)
            .ToList();

        var totalBytes = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (totalBytes <= maxBytes)
                break;

            var length = file.Length;
            try
            {
                file.Delete();
                totalBytes -= length;
            }
            catch (Exception ex)
            {
                log.Warning(ex, $"[Dheacon] Failed to prune cached speech file: {file.FullName}");
            }
        }
    }

    private static bool IsSwedishCulture(string culture)
        => culture.StartsWith("sv", StringComparison.OrdinalIgnoreCase);

    private static bool IsMaleGender(string gender)
        => gender.Equals("Male", StringComparison.OrdinalIgnoreCase);

    private static bool Fail(string message, out string warning)
    {
        warning = message;
        return false;
    }

    private static bool BytesEqual(byte[] bytes, int offset, string expected)
    {
        if (offset + expected.Length > bytes.Length)
            return false;

        for (var i = 0; i < expected.Length; i++)
        {
            if (bytes[offset + i] != expected[i])
                return false;
        }

        return true;
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
        => (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

    private static uint ReadUInt32(byte[] bytes, int offset)
        => (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));

    private static void WriteInt32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value & 0xFF);
        bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
        bytes[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static void WriteSingle(byte[] bytes, int offset, float value)
    {
        var scaled = BitConverter.GetBytes(value);
        System.Buffer.BlockCopy(scaled, 0, bytes, offset, scaled.Length);
    }

    private static string NormalizeText(string text)
        => WhitespaceRegex().Replace(text.Trim(), " ");

    private static void Touch(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch
        {
            // Best effort only; cache correctness does not depend on access timestamps.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort cleanup for temp files.
        }
    }

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    private sealed record SpeechSynthesisSettings(
        TtsBackend Backend,
        string VoiceId,
        string VoiceName,
        int Rate,
        int SynthVolume,
        double Pitch,
        int OutputGainPercent);
}
