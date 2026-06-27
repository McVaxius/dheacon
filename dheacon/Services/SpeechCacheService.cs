using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
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

public sealed record PiperTextPreview(
    string Original,
    string Adapted,
    bool WasAdapted,
    string AdapterId,
    string AdapterVersion,
    string AdapterContentHash,
    bool AdapterEnabled,
    string Status);

public sealed partial class SpeechCacheService
{
    private const string DefaultVoiceLabel = "Windows default";
    private const string PiperPitchProcessingVersion = "piper-pitch-v1";
    private const double PiperPitchShiftEpsilon = 0.0001d;
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly PiperVoiceCatalogService piperVoiceCatalogService;
    private readonly SpokenTextAdapterService spokenTextAdapterService;
    private readonly object voiceLock = new();
    private readonly object cacheSizeLock = new();
    private List<SpeechVoiceInfo>? modernVoices;
    private List<SpeechVoiceInfo>? legacyVoices;
    private DateTime cacheSizeComputedAtUtc = DateTime.MinValue;
    private double cachedCacheSizeMegabytes;

    private static readonly TimeSpan CacheSizeRefreshInterval = TimeSpan.FromSeconds(2);

    public SpeechCacheService(
        IPluginLog log,
        Configuration configuration,
        PiperVoiceCatalogService piperVoiceCatalogService,
        SpokenTextAdapterService spokenTextAdapterService)
    {
        this.log = log;
        this.configuration = configuration;
        this.piperVoiceCatalogService = piperVoiceCatalogService;
        this.spokenTextAdapterService = spokenTextAdapterService;
    }

    public string LastStatus { get; private set; } = "No speech generated yet.";
    public string LastError { get; private set; } = string.Empty;
    public string LastWavPath { get; private set; } = string.Empty;
    public string LastOriginalText { get; private set; } = string.Empty;
    public string LastAdaptedText { get; private set; } = string.Empty;
    public bool LastTextWasAdapted { get; private set; }
    public string LastTextAdapterId { get; private set; } = string.Empty;
    public string LastTextAdapterVersion { get; private set; } = string.Empty;
    public string LastTextAdapterContentHash { get; private set; } = string.Empty;

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
                TtsBackend.PiperLocal => LoadPiperVoices(),
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
        if (backend == TtsBackend.PiperLocal)
        {
            var piperVoice = piperVoiceCatalogService.FindExactInstalledVoice(configuration.TtsPiperVoiceId);
            return piperVoice == null
                ? "No exact Piper voice selected"
                : $"{piperVoice.VoiceKey} - {piperVoice.LanguageCode} - {piperVoice.Gender} - {piperVoice.Quality} - {piperVoice.Source}";
        }

        var voices = GetInstalledVoices(backend);
        var voice = backend == TtsBackend.ModernWindows
            ? FindModernVoice(voices, configuration.TtsModernVoiceId, configuration.TtsVoiceName)
            : FindLegacyVoice(voices, configuration.TtsVoiceName);
        return voice?.Label ?? DefaultVoiceLabel;
    }

    public PiperTextPreview PreviewPiperText(string text)
    {
        var normalizedText = NormalizeText(text);
        if (string.IsNullOrWhiteSpace(normalizedText))
            return new PiperTextPreview(string.Empty, string.Empty, false, string.Empty, string.Empty, string.Empty, configuration.TtsPiperTextAdapterEnabled, "No text.");

        var selectedVoice = piperVoiceCatalogService.FindExactInstalledVoice(configuration.TtsPiperVoiceId);
        var assumesSwedishPiper = selectedVoice == null &&
                                  !string.IsNullOrWhiteSpace(configuration.TtsPiperVoiceId) &&
                                  configuration.TtsPiperVoiceId.Contains("sv_SE", StringComparison.OrdinalIgnoreCase);
        var targetLanguage = selectedVoice?.LanguageCode ?? (assumesSwedishPiper ? "sv_SE" : string.Empty);
        var shouldAdapt = configuration.TtsPiperTextAdapterEnabled &&
                          ((selectedVoice != null && IsSwedishCulture(selectedVoice.LanguageCode)) || assumesSwedishPiper);
        if (!shouldAdapt)
            return new PiperTextPreview(normalizedText, normalizedText, false, string.Empty, string.Empty, string.Empty, configuration.TtsPiperTextAdapterEnabled, "Adapter not applied.");

        var adaptation = spokenTextAdapterService.AdaptForTarget(configuration.TtsPiperTextAdapterId, targetLanguage, normalizedText);
        return new PiperTextPreview(
            adaptation.Original,
            adaptation.Adapted,
            adaptation.WasAdapted,
            adaptation.AdapterId,
            adaptation.AdapterVersion,
            adaptation.AdapterContentHash,
            true,
            adaptation.Status);
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
        catch (Exception ex) when (backend != TtsBackend.LegacySapi)
        {
            log.Warning(ex, $"[Dheacon] {backend} speech failed; attempting Legacy SAPI fallback.");
            var fallbackPath = GetOrCreateWavForBackend(TtsBackend.LegacySapi, normalizedText, cancellationToken);
            LastError = $"{backend} speech failed; used Legacy SAPI fallback: {ex.Message}";
            LastStatus = $"{backend} failed; used Legacy SAPI fallback: {Path.GetFileName(fallbackPath)}";
            return fallbackPath;
        }
    }

    public string GetOrCreatePiperPreviewWav(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedText = NormalizeText(text);
        if (string.IsNullOrWhiteSpace(normalizedText))
            throw new InvalidOperationException("Cannot generate speech for empty text.");

        return GetOrCreateWavForBackend(TtsBackend.PiperLocal, normalizedText, cancellationToken);
    }

    public int ClearCache()
    {
        var cacheDirectory = configuration.GetResolvedTtsCacheDirectory();
        if (!Directory.Exists(cacheDirectory))
            return 0;

        var deleted = 0;
        foreach (var file in EnumerateFilesSnapshot(cacheDirectory, "*.wav"))
        {
            if (IsTemporaryWav(file))
                continue;

            if (TryDelete(file))
                deleted++;
        }

        foreach (var file in EnumerateFilesSnapshot(cacheDirectory, "*.tmp.wav"))
            TryDelete(file);

        LastStatus = $"Cleared {deleted} cached WAV file(s).";
        LastWavPath = string.Empty;
        LastError = string.Empty;
        InvalidateCacheSizeSnapshot();
        return deleted;
    }

    public int ClearPiperWavCache()
    {
        var cacheDirectory = configuration.GetResolvedTtsCacheDirectory();
        if (!Directory.Exists(cacheDirectory))
            return 0;

        var deleted = 0;
        foreach (var file in EnumerateFilesSnapshot(cacheDirectory, "piper-*.wav"))
        {
            if (IsTemporaryWav(file))
                continue;

            if (TryDelete(file))
                deleted++;
        }

        foreach (var file in EnumerateFilesSnapshot(cacheDirectory, "piper-*.tmp.wav"))
            TryDelete(file);

        LastStatus = $"Cleared {deleted} cached Piper WAV file(s).";
        LastWavPath = string.Empty;
        LastError = string.Empty;
        InvalidateCacheSizeSnapshot();
        return deleted;
    }

    public double GetCacheSizeMegabytes()
    {
        var now = DateTime.UtcNow;
        lock (cacheSizeLock)
        {
            if (now - cacheSizeComputedAtUtc < CacheSizeRefreshInterval)
                return cachedCacheSizeMegabytes;

            try
            {
                cachedCacheSizeMegabytes = ComputeCacheSizeMegabytes();
            }
            catch
            {
                // Status UI must never fail because the cache folder is changing.
            }

            cacheSizeComputedAtUtc = now;
            return cachedCacheSizeMegabytes;
        }
    }

    private string GetOrCreateWavForBackend(TtsBackend backend, string normalizedText, CancellationToken cancellationToken)
    {
        var cacheDirectory = configuration.GetResolvedTtsCacheDirectory();
        Directory.CreateDirectory(cacheDirectory);

        var settings = CreateSettings(backend);
        var synthesisText = CreateSynthesisText(backend, normalizedText, settings);
        var cacheKey = string.Join(
            "\n",
            "v7",
            settings.Backend,
            settings.VoiceId,
            settings.VoiceName,
            settings.PiperModelVersion,
            settings.PiperModelSha256,
            settings.PiperLanguageCode,
            settings.PiperQuality,
            backend == TtsBackend.PiperLocal ? synthesisText.AdapterId : string.Empty,
            backend == TtsBackend.PiperLocal ? synthesisText.AdapterVersion : string.Empty,
            backend == TtsBackend.PiperLocal ? synthesisText.AdapterContentHash : string.Empty,
            synthesisText.Text,
            backend == TtsBackend.PiperLocal ? settings.PiperLengthScale.ToString("0.###", CultureInfo.InvariantCulture) : settings.Rate.ToString(CultureInfo.InvariantCulture),
            backend == TtsBackend.PiperLocal ? settings.PiperSentenceSilence.ToString("0.###", CultureInfo.InvariantCulture) : settings.SynthVolume.ToString(CultureInfo.InvariantCulture),
            backend == TtsBackend.PiperLocal ? PiperPitchProcessingVersion : string.Empty,
            backend == TtsBackend.PiperLocal ? settings.PiperPitchShiftSemitones.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture) : settings.Pitch.ToString("0.###", CultureInfo.InvariantCulture),
            settings.OutputGainPercent.ToString(CultureInfo.InvariantCulture));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey))).ToLowerInvariant();
        var wavPrefix = backend == TtsBackend.PiperLocal ? "piper-" : string.Empty;
        var wavPath = Path.Combine(cacheDirectory, $"{wavPrefix}{hash}.wav");

        if (File.Exists(wavPath))
        {
            Touch(wavPath);
            LastWavPath = wavPath;
            LastStatus = $"Cache hit: {Path.GetFileName(wavPath)}";
            LastError = string.Empty;
            return wavPath;
        }

        var tempPath = Path.Combine(cacheDirectory, $"{wavPrefix}{hash}.{Guid.NewGuid():N}.tmp.wav");

        try
        {
            if (backend == TtsBackend.ModernWindows)
                SynthesizeModernWindowsWav(synthesisText.Text, tempPath, settings, cancellationToken);
            else if (backend == TtsBackend.PiperLocal)
                SynthesizePiperWav(synthesisText.Text, tempPath, settings, cancellationToken);
            else
                SynthesizeLegacySapiWav(synthesisText.Text, tempPath, settings, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            var pitchWarning = backend == TtsBackend.PiperLocal
                ? ApplyPiperPitchShift(tempPath, settings.PiperPitchShiftSemitones)
                : null;
            cancellationToken.ThrowIfCancellationRequested();
            var gainWarning = ApplyOutputGain(tempPath, GetEffectiveOutputGainPercent(settings));

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
            InvalidateCacheSizeSnapshot();

            LastWavPath = wavPath;
            LastStatus = $"Generated {settings.Backend}: {Path.GetFileName(wavPath)}";
            LastError = CombineWarnings(pitchWarning, gainWarning);
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
        var piperLengthScale = Math.Clamp(configuration.TtsPiperLengthScale, 0.5d, 2.0d);
        var piperSentenceSilence = Math.Clamp(configuration.TtsPiperSentenceSilence, 0.0d, 5.0d);
        var piperPitchShiftSemitones = Math.Clamp(configuration.TtsPiperPitchShiftSemitones, -12.0d, 12.0d);

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

        if (backend == TtsBackend.PiperLocal)
        {
            var selectedVoice = piperVoiceCatalogService.FindExactInstalledVoice(configuration.TtsPiperVoiceId)
                ?? throw new InvalidOperationException("No installed Piper voice is selected.");
            var modelVersion = !string.IsNullOrWhiteSpace(selectedVoice.ModelDigest)
                ? selectedVoice.ModelDigest
                : selectedVoice.Version;
            var modelSha256 = !string.IsNullOrWhiteSpace(selectedVoice.ModelSha256)
                ? selectedVoice.ModelSha256
                : modelVersion;

            return new SpeechSynthesisSettings(
                backend,
                selectedVoice.CatalogId,
                selectedVoice.VoiceKey,
                rate,
                100,
                pitch,
                outputGainPercent,
                selectedVoice.ModelPath,
                selectedVoice.ConfigPath,
                modelVersion,
                modelSha256,
                selectedVoice.LanguageCode,
                selectedVoice.Quality,
                piperLengthScale,
                piperSentenceSilence,
                piperPitchShiftSemitones);
        }

        var legacyVoiceName = configuration.TtsVoiceName.Trim();
        if (string.IsNullOrWhiteSpace(legacyVoiceName))
            legacyVoiceName = DefaultVoiceLabel;

        return new SpeechSynthesisSettings(backend, legacyVoiceName, legacyVoiceName, rate, synthVolume, pitch, outputGainPercent);
    }

    private SynthesisTextResult CreateSynthesisText(TtsBackend backend, string normalizedText, SpeechSynthesisSettings settings)
    {
        LastOriginalText = normalizedText;
        LastAdaptedText = normalizedText;
        LastTextWasAdapted = false;
        LastTextAdapterId = string.Empty;
        LastTextAdapterVersion = string.Empty;
        LastTextAdapterContentHash = string.Empty;

        if (backend != TtsBackend.PiperLocal ||
            !configuration.TtsPiperTextAdapterEnabled ||
            !IsSwedishCulture(settings.PiperLanguageCode))
            return new SynthesisTextResult(normalizedText);

        var adaptation = spokenTextAdapterService.AdaptForTarget(configuration.TtsPiperTextAdapterId, settings.PiperLanguageCode, normalizedText);
        LastAdaptedText = adaptation.Adapted;
        LastTextWasAdapted = adaptation.WasAdapted;
        LastTextAdapterId = adaptation.AdapterId;
        LastTextAdapterVersion = adaptation.AdapterVersion;
        LastTextAdapterContentHash = adaptation.AdapterContentHash;

        return new SynthesisTextResult(
            adaptation.Adapted,
            adaptation.AdapterId,
            adaptation.AdapterVersion,
            adaptation.AdapterContentHash);
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

    private void SynthesizePiperWav(
        string synthesisText,
        string tempPath,
        SpeechSynthesisSettings settings,
        CancellationToken cancellationToken)
    {
        var runtimePath = piperVoiceCatalogService.ResolveRuntimePath()
            ?? throw new FileNotFoundException("piper.exe was not found. Set the Piper runtime path in settings or place piper.exe in the managed runtime folder.");

        if (!File.Exists(settings.PiperModelPath))
            throw new FileNotFoundException("Selected Piper model file is missing.", settings.PiperModelPath);

        if (!File.Exists(settings.PiperConfigPath))
            throw new FileNotFoundException("Selected Piper config file is missing.", settings.PiperConfigPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = runtimePath,
            WorkingDirectory = Path.GetDirectoryName(runtimePath) ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(settings.PiperModelPath);
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(settings.PiperConfigPath);
        startInfo.ArgumentList.Add("--output_file");
        startInfo.ArgumentList.Add(tempPath);
        startInfo.ArgumentList.Add("--length_scale");
        startInfo.ArgumentList.Add(settings.PiperLengthScale.ToString("0.###", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--sentence_silence");
        startInfo.ArgumentList.Add(settings.PiperSentenceSilence.ToString("0.###", CultureInfo.InvariantCulture));

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Failed to start piper.exe.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.StandardInput.WriteLine(synthesisText);
        process.StandardInput.Close();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            process.WaitForExitAsync(timeoutCts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException("piper.exe did not finish within 120 seconds.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"piper.exe exited with code {process.ExitCode}: {TrimProcessOutput(stderr)}");

        if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
            throw new InvalidOperationException($"piper.exe did not produce a WAV file. {TrimProcessOutput(stdout + Environment.NewLine + stderr)}");
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

    private List<SpeechVoiceInfo> LoadPiperVoices()
        => piperVoiceCatalogService.GetInstalledVoices()
            .Select(voice => new SpeechVoiceInfo(
                TtsBackend.PiperLocal,
                voice.CatalogId,
                $"{voice.VoiceKey} ({voice.Source}, {voice.Quality})",
                string.IsNullOrWhiteSpace(voice.LanguageCode) ? "unknown" : voice.LanguageCode,
                string.IsNullOrWhiteSpace(voice.Gender) ? "Unknown" : voice.Gender))
            .OrderBy(voice => voice.Culture, StringComparer.OrdinalIgnoreCase)
            .ThenBy(voice => voice.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

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

    private static int GetEffectiveOutputGainPercent(SpeechSynthesisSettings settings)
        => settings.OutputGainPercent;

    private string? ApplyPiperPitchShift(string wavPath, double semitones)
    {
        if (Math.Abs(semitones) < PiperPitchShiftEpsilon)
            return null;

        var shiftedPath = Path.Combine(
            Path.GetDirectoryName(wavPath) ?? ".",
            $"{Path.GetFileNameWithoutExtension(wavPath)}.{Guid.NewGuid():N}.pitch.tmp.wav");

        try
        {
            var pitchFactor = Math.Pow(2.0d, semitones / 12.0d);
            using var reader = new WaveFileReader(wavPath);
            var pitchProvider = new SmbPitchShiftingSampleProvider(reader.ToSampleProvider())
            {
                PitchFactor = (float)pitchFactor,
            };

            WaveFileWriter.CreateWaveFile16(shiftedPath, pitchProvider);
            File.Copy(shiftedPath, wavPath, overwrite: true);
            return null;
        }
        catch (Exception ex)
        {
            var warning = $"Piper pitch shift failed ({semitones.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture)} semitones): {ex.Message}";
            log.Warning(ex, $"[Dheacon] {warning} Using unshifted WAV.");
            return warning;
        }
        finally
        {
            TryDelete(shiftedPath);
        }
    }

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
        var files = EnumerateFilesSnapshot(cacheDirectory, "*.wav")
            .Where(path => !IsTemporaryWav(path))
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .OrderBy(file => file.LastAccessTimeUtc)
            .ThenBy(file => file.LastWriteTimeUtc)
            .ToList();

        var totalBytes = files.Sum(file => TryGetFileLength(file.FullName, out var length) ? length : 0L);
        foreach (var file in files)
        {
            if (totalBytes <= maxBytes)
                break;

            if (!TryGetFileLength(file.FullName, out var length))
                continue;

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

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort cleanup for stuck piper.exe processes.
        }
    }

    private static string TrimProcessOutput(string output)
    {
        output = WhitespaceRegex().Replace(output.Trim(), " ");
        return output.Length <= 300 ? output : output[..300] + "...";
    }

    private static bool Fail(string message, out string warning)
    {
        warning = message;
        return false;
    }

    private static string CombineWarnings(params string?[] warnings)
        => string.Join(" ", warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)));

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

    private double ComputeCacheSizeMegabytes()
    {
        var cacheDirectory = configuration.GetResolvedTtsCacheDirectory();
        if (!Directory.Exists(cacheDirectory))
            return 0d;

        long bytes = 0;
        foreach (var path in EnumerateFilesSnapshot(cacheDirectory, "*.wav"))
        {
            if (IsTemporaryWav(path))
                continue;

            if (TryGetFileLength(path, out var length))
                bytes += length;
        }

        return bytes / 1024d / 1024d;
    }

    private void InvalidateCacheSizeSnapshot()
    {
        lock (cacheSizeLock)
            cacheSizeComputedAtUtc = DateTime.MinValue;
    }

    private static IReadOnlyList<string> EnumerateFilesSnapshot(string directory, string searchPattern)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly).ToList()
                : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool TryGetFileLength(string path, out long length)
    {
        length = 0;
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
                return false;

            length = file.Length;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsTemporaryWav(string path)
        => path.EndsWith(".tmp.wav", StringComparison.OrdinalIgnoreCase);

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch
        {
            // Best effort cleanup for temp files.
        }

        return false;
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
        int OutputGainPercent,
        string PiperModelPath = "",
        string PiperConfigPath = "",
        string PiperModelVersion = "",
        string PiperModelSha256 = "",
        string PiperLanguageCode = "",
        string PiperQuality = "",
        double PiperLengthScale = 1.0d,
        double PiperSentenceSilence = 0.2d,
        double PiperPitchShiftSemitones = 0.0d);

    private sealed record SynthesisTextResult(
        string Text,
        string AdapterId = "",
        string AdapterVersion = "",
        string AdapterContentHash = "");
}
