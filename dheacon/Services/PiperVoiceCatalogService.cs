using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Dalamud.Plugin.Services;

namespace Dheacon.Services;

public sealed record PiperVoiceCatalogEntry
{
    public string CatalogId { get; init; } = string.Empty;
    public string VoiceKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
    public string LanguageName { get; init; } = string.Empty;
    public string Gender { get; init; } = "Unknown";
    public string Quality { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string License { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string SourceKey { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string ModelUrl { get; init; } = string.Empty;
    public string ConfigUrl { get; init; } = string.Empty;
    public string ModelCardUrl { get; init; } = string.Empty;
    public string PackageUrl { get; init; } = string.Empty;
    public string ModelFileName { get; init; } = string.Empty;
    public string ConfigFileName { get; init; } = string.Empty;
    public string ModelDigest { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public bool Installed { get; init; }
    public string InstalledModelPath { get; init; } = string.Empty;
    public string InstalledConfigPath { get; init; } = string.Empty;
    public string InstalledDirectory { get; init; } = string.Empty;
    public string InstalledModelSha256 { get; init; } = string.Empty;

    public string Label
        => $"{VoiceKey} - {LanguageCode} - {Gender} - {Quality} - {Source}";

    public string SizeLabel
        => SizeBytes > 0 ? $"{SizeBytes / 1024d / 1024d:F1} MB" : "unknown size";
}

public sealed record PiperInstalledVoice
{
    public string CatalogId { get; init; } = string.Empty;
    public string VoiceKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
    public string LanguageName { get; init; } = string.Empty;
    public string Gender { get; init; } = "Unknown";
    public string Quality { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string License { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string SourceKey { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string ModelDigest { get; init; } = string.Empty;
    public string ModelSha256 { get; init; } = string.Empty;
    public string ModelPath { get; init; } = string.Empty;
    public string ConfigPath { get; init; } = string.Empty;
    public string InstallDirectory { get; init; } = string.Empty;
    public DateTime InstalledAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class PiperVoiceCatalogService : IDisposable
{
    public const string RecommendedVoiceKey = "en_US-arctic-medium";
    public const string RecommendedVoiceCatalogId = OfficialSourceKey + ":" + RecommendedVoiceKey;

    private const string OfficialSourceKey = "official";
    private const string OfficialSourceName = "Official Piper";
    private const string CommunitySourceKey = "community-sv";
    private const string CommunitySourceName = "Swedish community";
    private const string OfficialCatalogUrl = "https://huggingface.co/rhasspy/piper-voices/raw/main/voices.json";
    private const string OfficialResolveBaseUrl = "https://huggingface.co/rhasspy/piper-voices/resolve/main/";
    private const string SwedishReleasesUrl = "https://api.github.com/repos/yeager/piper-voices-sv/releases";
    private const string PortableWindowsRuntimeUrl = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip";
    private const long PortableWindowsRuntimeSizeBytes = 22477236;
    private const string PortableWindowsRuntimeVersion = "rhasspy/piper 2023.11.14-2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static readonly IReadOnlyDictionary<string, SwedishVoiceMetadata> SwedishCommunityMetadata =
        new Dictionary<string, SwedishVoiceMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["sv_SE-axel-medium"] = new("Axel", "Male", "NST Swedish corpus", "Release asset; license unspecified", "Swedish male voice."),
            ["sv_SE-daniel-medium"] = new("Daniel", "Male", "Voice clone from a short sample", "Release asset; license unspecified", "Experimental voice clone; not auto-installed."),
            ["sv_SE-alma-medium"] = new("Alma", "Female", "Swedish voice", "Release asset; license unspecified", "Community Swedish female voice."),
        };

    private static readonly IReadOnlyDictionary<string, string> KnownOfficialGenders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sv_SE-alma-medium"] = "Female",
            ["sv_SE-lisa-medium"] = "Female",
            ["ar_JO-kareem-low"] = "Male",
            ["ar_JO-kareem-medium"] = "Male",
            ["bg_BG-dimitar-medium"] = "Male",
        };

    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly HttpClient httpClient;
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private readonly object catalogLock = new();
    private List<PiperVoiceCatalogEntry> catalogEntries = new();
    private PiperInstalledVoiceManifest installedManifest = new();

    public PiperVoiceCatalogService(IPluginLog log, Configuration configuration)
    {
        this.log = log;
        this.configuration = configuration;
        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Dheacon/1.0");
        LoadCachedCatalog();
        LoadInstalledManifest();
        RefreshRuntimeStatus(save: false);
    }

    public string LastStatus { get; private set; } = "Piper catalog not refreshed this session.";
    public string LastError { get; private set; } = string.Empty;
    public bool IsBusy { get; private set; }
    public double OperationProgress { get; private set; } = -1d;

    public void Dispose()
    {
        httpClient.Dispose();
        operationLock.Dispose();
    }

    public IReadOnlyList<PiperVoiceCatalogEntry> GetCatalogEntries()
    {
        lock (catalogLock)
        {
            var installedById = installedManifest.Voices.ToDictionary(voice => voice.CatalogId, StringComparer.OrdinalIgnoreCase);
            var entries = catalogEntries
                .Select(entry => installedById.TryGetValue(entry.CatalogId, out var installed)
                    ? ApplyInstalledState(entry, installed)
                    : entry with { Installed = false, InstalledModelPath = string.Empty, InstalledConfigPath = string.Empty, InstalledDirectory = string.Empty, InstalledModelSha256 = string.Empty })
                .ToList();

            foreach (var installed in installedManifest.Voices)
            {
                if (entries.Any(entry => string.Equals(entry.CatalogId, installed.CatalogId, StringComparison.OrdinalIgnoreCase)))
                    continue;

                entries.Add(CreateEntryFromInstalledVoice(installed));
            }

            return entries
                .OrderBy(entry => entry.LanguageName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.LanguageCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.VoiceKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public IReadOnlyList<PiperInstalledVoice> GetInstalledVoices()
    {
        return installedManifest.Voices
            .OrderBy(voice => voice.LanguageCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(voice => voice.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(voice => voice.Quality, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public PiperInstalledVoice? FindInstalledVoice(string catalogId)
    {
        var selected = FindExactInstalledVoice(catalogId);
        if (selected != null)
            return selected;

        return installedManifest.Voices.FirstOrDefault(voice =>
                string.Equals(voice.CatalogId, RecommendedVoiceCatalogId, StringComparison.OrdinalIgnoreCase))
            ?? installedManifest.Voices.FirstOrDefault();
    }

    public PiperInstalledVoice? FindExactInstalledVoice(string catalogId)
    {
        if (string.IsNullOrWhiteSpace(catalogId))
            return null;

        return installedManifest.Voices.FirstOrDefault(voice =>
            string.Equals(voice.CatalogId, catalogId, StringComparison.OrdinalIgnoreCase));
    }

    public PiperVoiceCatalogEntry? FindCatalogEntry(string catalogId)
        => GetCatalogEntries().FirstOrDefault(entry => string.Equals(entry.CatalogId, catalogId, StringComparison.OrdinalIgnoreCase));

    public bool IsCatalogStale(TimeSpan maxAge)
        => catalogEntries.Count == 0 ||
           configuration.TtsPiperCatalogRefreshedAtUtc == DateTime.MinValue ||
           DateTime.UtcNow - configuration.TtsPiperCatalogRefreshedAtUtc > maxAge;

    public async Task RefreshCatalogIfStaleAsync(TimeSpan maxAge, CancellationToken cancellationToken)
    {
        if (!IsCatalogStale(maxAge))
            return;

        await RefreshCatalogAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshCatalogAsync(CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetBusy("Refreshing Piper voice catalog...", 0d);
            var entries = new List<PiperVoiceCatalogEntry>();
            entries.AddRange(await LoadOfficialCatalogAsync(cancellationToken).ConfigureAwait(false));
            entries.AddRange(await LoadSwedishCommunityCatalogAsync(cancellationToken).ConfigureAwait(false));

            lock (catalogLock)
            {
                catalogEntries = entries
                    .GroupBy(entry => entry.CatalogId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
            }

            configuration.TtsPiperCatalogRefreshedAtUtc = DateTime.UtcNow;
            configuration.Save();
            SaveCatalogCache();
            LastError = string.Empty;
            LastStatus = $"Refreshed Piper catalog: {entries.Count} voice entries.";
            OperationProgress = 1d;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LastStatus = "Piper catalog refresh failed; using cached and installed voices.";
            log.Warning(ex, "[Dheacon] Piper catalog refresh failed.");
            throw;
        }
        finally
        {
            ClearBusy();
            operationLock.Release();
        }
    }

    public async Task EnsureRecommendedVoiceInstalledAsync(bool switchBackendWhenReady, CancellationToken cancellationToken)
    {
        var selectedVoiceExists = FindExactInstalledVoice(configuration.TtsPiperVoiceId) != null;
        try
        {
            await RefreshCatalogIfStaleAsync(TimeSpan.FromHours(24), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (!selectedVoiceExists && FindExactInstalledVoice(RecommendedVoiceCatalogId) == null)
                throw;
        }

        if (!selectedVoiceExists && FindExactInstalledVoice(RecommendedVoiceCatalogId) == null)
            await InstallVoiceAsync(RecommendedVoiceCatalogId, cancellationToken).ConfigureAwait(false);

        if (!selectedVoiceExists)
            configuration.TtsPiperVoiceId = RecommendedVoiceCatalogId;

        var activeVoiceKey = FindExactInstalledVoice(configuration.TtsPiperVoiceId)?.VoiceKey ?? RecommendedVoiceKey;
        var runtimePath = ResolveRuntimePath();
        if (switchBackendWhenReady && runtimePath == null)
        {
            try
            {
                await InstallPortableRuntimeAsync(cancellationToken).ConfigureAwait(false);
                runtimePath = ResolveRuntimePath();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                LastStatus = $"Piper voice {activeVoiceKey} is installed, but runtime setup failed; kept Legacy SAPI active.";
                log.Warning(ex, "[Dheacon] Piper runtime setup failed.");
            }
        }

        if (switchBackendWhenReady && runtimePath != null)
        {
            configuration.TtsBackend = TtsBackend.PiperLocal;
            LastStatus = $"Piper ready with {activeVoiceKey}.";
        }
        else if (switchBackendWhenReady)
        {
            configuration.TtsBackend = TtsBackend.LegacySapi;
            LastStatus = $"Piper voice {activeVoiceKey} is installed, but runtime is missing; kept Legacy SAPI active.";
        }

        RefreshRuntimeStatus(save: false);
        configuration.Save();
    }

    public async Task InstallPortableRuntimeAsync(CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? tempDirectory = null;
        try
        {
            SetBusy("Installing portable Piper runtime...", 0d);
            tempDirectory = Path.Combine(configuration.GetResolvedPiperRootDirectory(), "tmp", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var archivePath = Path.Combine(tempDirectory, "piper_windows_amd64.zip");
            await DownloadFileAsync(PortableWindowsRuntimeUrl, archivePath, PortableWindowsRuntimeSizeBytes, "runtime", cancellationToken).ConfigureAwait(false);

            var extractDirectory = Path.Combine(tempDirectory, "extract");
            ZipFile.ExtractToDirectory(archivePath, extractDirectory, overwriteFiles: true);
            var piperExe = Directory.EnumerateFiles(extractDirectory, "piper.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidOperationException("The portable Piper runtime archive did not contain piper.exe.");

            var sourceDirectory = Path.GetDirectoryName(piperExe)
                ?? throw new InvalidOperationException("Could not resolve the extracted Piper runtime folder.");
            var runtimeDirectory = configuration.GetResolvedPiperRuntimeDirectory();
            DeleteManagedRuntimeDirectoryIfSafe(runtimeDirectory);
            CopyDirectoryContents(sourceDirectory, runtimeDirectory);

            configuration.TtsPiperRuntimePath = Path.Combine(runtimeDirectory, "piper.exe");
            RefreshRuntimeStatus(save: false);
            configuration.Save();
            LastError = string.Empty;
            LastStatus = $"Installed portable Piper runtime ({PortableWindowsRuntimeVersion}).";
            OperationProgress = 1d;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LastStatus = "Portable Piper runtime install failed: " + ex.Message;
            log.Warning(ex, "[Dheacon] Portable Piper runtime install failed.");
            throw;
        }
        finally
        {
            if (tempDirectory != null)
                TryDeleteDirectory(tempDirectory);

            ClearBusy();
            operationLock.Release();
        }
    }

    public async Task InstallVoiceAsync(string catalogId, CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? tempDirectory = null;
        try
        {
            LoadInstalledManifest();
            var entry = FindCatalogEntry(catalogId)
                ?? throw new InvalidOperationException($"Piper voice '{catalogId}' is not in the current catalog.");

            if (installedManifest.Voices.Any(voice => string.Equals(voice.CatalogId, entry.CatalogId, StringComparison.OrdinalIgnoreCase)))
            {
                LastStatus = $"{entry.VoiceKey} is already installed.";
                LastError = string.Empty;
                return;
            }

            SetBusy($"Installing {entry.VoiceKey}...", 0d);
            var voiceDirectory = GetVoiceInstallDirectory(entry.CatalogId);
            DeleteVoiceDirectoryIfSafe(voiceDirectory);
            Directory.CreateDirectory(voiceDirectory);
            tempDirectory = Path.Combine(configuration.GetResolvedPiperRootDirectory(), "tmp", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            var installed = string.IsNullOrWhiteSpace(entry.PackageUrl)
                ? await InstallOfficialVoiceAsync(entry, voiceDirectory, cancellationToken).ConfigureAwait(false)
                : await InstallPackagedVoiceAsync(entry, voiceDirectory, tempDirectory, cancellationToken).ConfigureAwait(false);

            installedManifest.Voices.RemoveAll(voice => string.Equals(voice.CatalogId, installed.CatalogId, StringComparison.OrdinalIgnoreCase));
            installedManifest.Voices.Add(installed);
            SaveInstalledManifest();

            if (string.IsNullOrWhiteSpace(configuration.TtsPiperVoiceId))
            {
                configuration.TtsPiperVoiceId = installed.CatalogId;
                configuration.Save();
            }

            LastError = string.Empty;
            LastStatus = $"Installed Piper voice {installed.VoiceKey} ({installed.SizeBytes / 1024d / 1024d:F1} MB).";
            OperationProgress = 1d;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LastStatus = $"Piper voice install failed: {ex.Message}";
            log.Warning(ex, "[Dheacon] Piper voice install failed.");
            throw;
        }
        finally
        {
            if (tempDirectory != null)
                TryDeleteDirectory(tempDirectory);

            ClearBusy();
            operationLock.Release();
        }
    }

    public void UninstallVoice(string catalogId)
    {
        LoadInstalledManifest();
        var installed = installedManifest.Voices.FirstOrDefault(voice => string.Equals(voice.CatalogId, catalogId, StringComparison.OrdinalIgnoreCase));
        if (installed == null)
        {
            LastStatus = $"Piper voice '{catalogId}' is not installed.";
            return;
        }

        DeleteVoiceDirectoryIfSafe(installed.InstallDirectory);
        installedManifest.Voices.RemoveAll(voice => string.Equals(voice.CatalogId, catalogId, StringComparison.OrdinalIgnoreCase));
        SaveInstalledManifest();

        if (string.Equals(configuration.TtsPiperVoiceId, catalogId, StringComparison.OrdinalIgnoreCase))
        {
            configuration.TtsPiperVoiceId = installedManifest.Voices.FirstOrDefault()?.CatalogId ?? string.Empty;
            if (configuration.TtsBackend == TtsBackend.PiperLocal && string.IsNullOrWhiteSpace(configuration.TtsPiperVoiceId))
                configuration.TtsBackend = TtsBackend.LegacySapi;
            configuration.Save();
        }

        LastError = string.Empty;
        LastStatus = $"Uninstalled Piper voice {installed.VoiceKey}.";
    }

    public void SelectVoice(string catalogId)
    {
        var installed = FindInstalledVoice(catalogId)
            ?? throw new InvalidOperationException($"Piper voice '{catalogId}' is not installed.");

        configuration.TtsPiperVoiceId = installed.CatalogId;
        configuration.Save();
        LastStatus = $"Selected Piper voice {installed.VoiceKey}.";
        LastError = string.Empty;
    }

    public string GetVoiceFolder(string catalogId)
    {
        var installed = FindInstalledVoice(catalogId);
        return installed?.InstallDirectory ?? GetVoiceInstallDirectory(catalogId);
    }

    public double GetInstalledVoiceSizeMegabytes()
    {
        return installedManifest.Voices.Sum(voice => voice.SizeBytes) / 1024d / 1024d;
    }

    public string? ResolveRuntimePath()
    {
        var configured = configuration.GetConfiguredPiperRuntimePath();
        if (File.Exists(configured))
            return configured;

        var assemblyDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty;
        var candidates = new[]
        {
            Path.Combine(configuration.GetResolvedPiperRuntimeDirectory(), "piper.exe"),
            Path.Combine(assemblyDirectory, "runtimes", "piper", "piper.exe"),
            Path.Combine(assemblyDirectory, "piper", "piper.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(folder, "piper.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public string RefreshRuntimeStatus(bool save = true)
    {
        var runtimePath = ResolveRuntimePath();
        configuration.TtsPiperRuntimeStatus = runtimePath == null
            ? "Piper runtime missing. Set a portable piper.exe path or place piper.exe in " + configuration.GetResolvedPiperRuntimeDirectory()
            : "Piper runtime found: " + runtimePath;

        if (save)
            configuration.Save();

        return configuration.TtsPiperRuntimeStatus;
    }

    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }

    private async Task<IReadOnlyList<PiperVoiceCatalogEntry>> LoadOfficialCatalogAsync(CancellationToken cancellationToken)
    {
        SetBusy("Downloading official Piper voice catalog...", 0.1d);
        using var response = await httpClient.GetAsync(OfficialCatalogUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var entries = new List<PiperVoiceCatalogEntry>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var voice = property.Value;
            var voiceKey = GetString(voice, "key");
            if (string.IsNullOrWhiteSpace(voiceKey))
                voiceKey = property.Name;

            var name = GetString(voice, "name");
            var quality = GetString(voice, "quality");
            var language = voice.TryGetProperty("language", out var languageElement) ? languageElement : default;
            var languageCode = GetString(language, "code");
            var languageName = GetString(language, "name_english");
            if (string.IsNullOrWhiteSpace(languageCode))
                languageCode = ParseLanguageCode(voiceKey);
            if (string.IsNullOrWhiteSpace(languageName))
                languageName = languageCode;

            if (!voice.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Object)
                continue;

            var fileInfos = files.EnumerateObject().ToList();
            var model = fileInfos.FirstOrDefault(file => file.Name.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));
            var config = fileInfos.FirstOrDefault(file => file.Name.EndsWith(".onnx.json", StringComparison.OrdinalIgnoreCase));
            var modelCard = fileInfos.FirstOrDefault(file => Path.GetFileName(file.Name).Equals("MODEL_CARD", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(config.Name))
                continue;

            var sizeBytes = fileInfos.Sum(file => GetLong(file.Value, "size_bytes"));
            var modelDigest = GetString(model.Value, "md5_digest");
            var displayName = string.IsNullOrWhiteSpace(name) ? voiceKey : CultureInfoInvariantTitle(name);

            entries.Add(new PiperVoiceCatalogEntry
            {
                CatalogId = OfficialSourceKey + ":" + voiceKey,
                VoiceKey = voiceKey,
                DisplayName = displayName,
                LanguageCode = languageCode,
                LanguageName = languageName,
                Gender = KnownOfficialGenders.TryGetValue(voiceKey, out var gender) ? gender : "Unknown",
                Quality = quality,
                SizeBytes = sizeBytes,
                License = "See official voice model card",
                Source = OfficialSourceName,
                SourceKey = OfficialSourceKey,
                Version = modelDigest,
                ModelUrl = OfficialResolveBaseUrl + EscapeHuggingFacePath(model.Name),
                ConfigUrl = OfficialResolveBaseUrl + EscapeHuggingFacePath(config.Name),
                ModelCardUrl = string.IsNullOrWhiteSpace(modelCard.Name) ? string.Empty : OfficialResolveBaseUrl + EscapeHuggingFacePath(modelCard.Name),
                ModelFileName = Path.GetFileName(model.Name),
                ConfigFileName = Path.GetFileName(config.Name),
                ModelDigest = modelDigest,
            });
        }

        return entries;
    }

    private async Task<IReadOnlyList<PiperVoiceCatalogEntry>> LoadSwedishCommunityCatalogAsync(CancellationToken cancellationToken)
    {
        SetBusy("Downloading Swedish Piper release catalog...", 0.45d);
        using var response = await httpClient.GetAsync(SwedishReleasesUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var entries = new List<PiperVoiceCatalogEntry>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            var tag = GetString(release, "tag_name");
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var asset in assets.EnumerateArray())
            {
                var assetName = GetString(asset, "name");
                if (!assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                    !assetName.StartsWith("sv_SE-", StringComparison.OrdinalIgnoreCase))
                    continue;

                var voiceKey = assetName[..^".tar.gz".Length];
                var quality = ParseQuality(voiceKey);
                var meta = SwedishCommunityMetadata.TryGetValue(voiceKey, out var known)
                    ? known
                    : new SwedishVoiceMetadata(CultureInfoInvariantTitle(ParseVoiceName(voiceKey)), "Unknown", "Swedish community voice", "Release asset; license unspecified", string.Empty);

                entries.Add(new PiperVoiceCatalogEntry
                {
                    CatalogId = CommunitySourceKey + ":" + voiceKey,
                    VoiceKey = voiceKey,
                    DisplayName = meta.DisplayName,
                    LanguageCode = "sv_SE",
                    LanguageName = "Swedish",
                    Gender = meta.Gender,
                    Quality = quality,
                    SizeBytes = GetLong(asset, "size"),
                    License = meta.License,
                    Source = CommunitySourceName,
                    SourceKey = CommunitySourceKey,
                    Version = tag,
                    PackageUrl = GetString(asset, "browser_download_url"),
                    ModelFileName = voiceKey + ".onnx",
                    ConfigFileName = voiceKey + ".onnx.json",
                    ModelDigest = tag,
                    Notes = string.IsNullOrWhiteSpace(meta.Dataset) ? meta.Notes : $"{meta.Dataset}. {meta.Notes}".Trim(),
                });
            }
        }

        return entries;
    }

    private async Task<PiperInstalledVoice> InstallOfficialVoiceAsync(
        PiperVoiceCatalogEntry entry,
        string voiceDirectory,
        CancellationToken cancellationToken)
    {
        var modelPath = Path.Combine(voiceDirectory, entry.ModelFileName);
        var configPath = Path.Combine(voiceDirectory, entry.ConfigFileName);
        await DownloadFileAsync(entry.ModelUrl, modelPath, entry.SizeBytes, "model", cancellationToken).ConfigureAwait(false);
        await DownloadFileAsync(entry.ConfigUrl, configPath, 0, "config", cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(entry.ModelCardUrl))
            await DownloadFileAsync(entry.ModelCardUrl, Path.Combine(voiceDirectory, "MODEL_CARD"), 0, "model card", cancellationToken).ConfigureAwait(false);

        return CreateInstalledVoice(entry, voiceDirectory, modelPath, configPath);
    }

    private async Task<PiperInstalledVoice> InstallPackagedVoiceAsync(
        PiperVoiceCatalogEntry entry,
        string voiceDirectory,
        string tempDirectory,
        CancellationToken cancellationToken)
    {
        var archivePath = Path.Combine(tempDirectory, entry.VoiceKey + ".tar.gz");
        await DownloadFileAsync(entry.PackageUrl, archivePath, entry.SizeBytes, "package", cancellationToken).ConfigureAwait(false);

        var extractDirectory = Path.Combine(tempDirectory, "extract");
        Directory.CreateDirectory(extractDirectory);
        await using (var fileStream = File.OpenRead(archivePath))
        await using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
        {
            TarFile.ExtractToDirectory(gzipStream, extractDirectory, overwriteFiles: true);
        }

        CopyDirectoryContents(extractDirectory, voiceDirectory);
        var modelPath = Directory.EnumerateFiles(voiceDirectory, "*.onnx", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException($"Package for {entry.VoiceKey} did not contain an ONNX model.");
        var configPath = Directory.EnumerateFiles(voiceDirectory, "*.onnx.json", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException($"Package for {entry.VoiceKey} did not contain an ONNX config.");

        return CreateInstalledVoice(entry, voiceDirectory, modelPath, configPath);
    }

    private PiperInstalledVoice CreateInstalledVoice(PiperVoiceCatalogEntry entry, string voiceDirectory, string modelPath, string configPath)
    {
        var modelSha256 = ComputeSha256(modelPath);
        var sizeBytes = Directory.EnumerateFiles(voiceDirectory, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path).Length)
            .Sum();

        return new PiperInstalledVoice
        {
            CatalogId = entry.CatalogId,
            VoiceKey = entry.VoiceKey,
            DisplayName = entry.DisplayName,
            LanguageCode = entry.LanguageCode,
            LanguageName = entry.LanguageName,
            Gender = entry.Gender,
            Quality = entry.Quality,
            SizeBytes = sizeBytes,
            License = entry.License,
            Source = entry.Source,
            SourceKey = entry.SourceKey,
            Version = entry.Version,
            ModelDigest = entry.ModelDigest,
            ModelSha256 = modelSha256,
            ModelPath = modelPath,
            ConfigPath = configPath,
            InstallDirectory = voiceDirectory,
            InstalledAtUtc = DateTime.UtcNow,
        };
    }

    private async Task DownloadFileAsync(string url, string destinationPath, long expectedTotalBytes, string stage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException($"Missing download URL for {stage}.");

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? (expectedTotalBytes > 0 ? expectedTotalBytes : 0);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(destinationPath);
        var buffer = new byte[1024 * 128];
        long received = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            OperationProgress = totalBytes > 0 ? Math.Clamp(received / (double)totalBytes, 0d, 1d) : -1d;
            LastStatus = totalBytes > 0
                ? $"Downloading Piper {stage}: {received / 1024d / 1024d:F1} / {totalBytes / 1024d / 1024d:F1} MB"
                : $"Downloading Piper {stage}: {received / 1024d / 1024d:F1} MB";
        }
    }

    private void LoadCachedCatalog()
    {
        var path = configuration.GetResolvedPiperCatalogCachePath();
        try
        {
            if (!File.Exists(path))
                return;

            var file = JsonSerializer.Deserialize<PiperCatalogCacheFile>(File.ReadAllText(path), JsonOptions);
            if (file == null)
                return;

            catalogEntries = file.Entries ?? new List<PiperVoiceCatalogEntry>();
            if (file.RefreshedAtUtc != DateTime.MinValue)
                configuration.TtsPiperCatalogRefreshedAtUtc = file.RefreshedAtUtc;

            LastStatus = $"Loaded cached Piper catalog: {catalogEntries.Count} voice entries.";
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            log.Warning(ex, "[Dheacon] Failed to load cached Piper catalog.");
        }
    }

    private void SaveCatalogCache()
    {
        var path = configuration.GetResolvedPiperCatalogCachePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var file = new PiperCatalogCacheFile
        {
            RefreshedAtUtc = configuration.TtsPiperCatalogRefreshedAtUtc,
            Entries = catalogEntries,
        };

        File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOptions));
    }

    private void LoadInstalledManifest()
    {
        var path = configuration.GetResolvedPiperInstalledVoicesManifestPath();
        try
        {
            if (!File.Exists(path))
            {
                installedManifest = new PiperInstalledVoiceManifest();
                return;
            }

            installedManifest = JsonSerializer.Deserialize<PiperInstalledVoiceManifest>(File.ReadAllText(path), JsonOptions)
                ?? new PiperInstalledVoiceManifest();
            installedManifest.Voices.RemoveAll(voice =>
                string.IsNullOrWhiteSpace(voice.CatalogId) ||
                string.IsNullOrWhiteSpace(voice.ModelPath) ||
                string.IsNullOrWhiteSpace(voice.ConfigPath) ||
                !File.Exists(voice.ModelPath) ||
                !File.Exists(voice.ConfigPath));
        }
        catch (Exception ex)
        {
            installedManifest = new PiperInstalledVoiceManifest();
            LastError = ex.Message;
            log.Warning(ex, "[Dheacon] Failed to load Piper installed voices manifest.");
        }
    }

    private void SaveInstalledManifest()
    {
        var path = configuration.GetResolvedPiperInstalledVoicesManifestPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(installedManifest, JsonOptions));
    }

    private PiperVoiceCatalogEntry ApplyInstalledState(PiperVoiceCatalogEntry entry, PiperInstalledVoice installed)
        => entry with
        {
            Installed = true,
            InstalledModelPath = installed.ModelPath,
            InstalledConfigPath = installed.ConfigPath,
            InstalledDirectory = installed.InstallDirectory,
            InstalledModelSha256 = installed.ModelSha256,
            SizeBytes = installed.SizeBytes > 0 ? installed.SizeBytes : entry.SizeBytes,
        };

    private PiperVoiceCatalogEntry CreateEntryFromInstalledVoice(PiperInstalledVoice installed)
        => new()
        {
            CatalogId = installed.CatalogId,
            VoiceKey = installed.VoiceKey,
            DisplayName = installed.DisplayName,
            LanguageCode = installed.LanguageCode,
            LanguageName = installed.LanguageName,
            Gender = installed.Gender,
            Quality = installed.Quality,
            SizeBytes = installed.SizeBytes,
            License = installed.License,
            Source = installed.Source,
            SourceKey = installed.SourceKey,
            Version = installed.Version,
            ModelDigest = installed.ModelDigest,
            Installed = true,
            InstalledModelPath = installed.ModelPath,
            InstalledConfigPath = installed.ConfigPath,
            InstalledDirectory = installed.InstallDirectory,
            InstalledModelSha256 = installed.ModelSha256,
        };

    private string GetVoiceInstallDirectory(string catalogId)
        => Path.Combine(configuration.GetResolvedPiperVoiceDirectory(), SanitizePathSegment(catalogId));

    private void DeleteVoiceDirectoryIfSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        var root = Path.GetFullPath(configuration.GetResolvedPiperVoiceDirectory());
        var fullPath = Path.GetFullPath(path);
        if (!IsSubPathOf(fullPath, root))
            throw new InvalidOperationException($"Refusing to delete Piper voice folder outside managed voice root: {fullPath}");

        Directory.Delete(fullPath, recursive: true);
    }

    private void DeleteManagedRuntimeDirectoryIfSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        var root = Path.GetFullPath(configuration.GetResolvedPiperRootDirectory());
        var fullPath = Path.GetFullPath(path);
        if (!IsSubPathOf(fullPath, root))
            throw new InvalidOperationException($"Refusing to replace Piper runtime folder outside managed root: {fullPath}");

        Directory.Delete(fullPath, recursive: true);
    }

    private void SetBusy(string status, double progress)
    {
        IsBusy = true;
        LastStatus = status;
        OperationProgress = progress;
    }

    private void ClearBusy()
    {
        IsBusy = false;
        if (OperationProgress < 0d)
            OperationProgress = -1d;
    }

    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? destinationDirectory);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool IsSubPathOf(string path, string root)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup for temporary downloads.
        }
    }

    private static string EscapeHuggingFacePath(string relativePath)
        => string.Join("/", relativePath.Split('/').Select(Uri.EscapeDataString));

    private static string ParseLanguageCode(string voiceKey)
    {
        var hyphen = voiceKey.IndexOf('-');
        return hyphen > 0 ? voiceKey[..hyphen] : "unknown";
    }

    private static string ParseVoiceName(string voiceKey)
    {
        var parts = voiceKey.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : voiceKey;
    }

    private static string ParseQuality(string voiceKey)
    {
        var parts = voiceKey.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[^1] : string.Empty;
    }

    private static string CultureInfoInvariantTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        return string.Join(
            " ",
            value.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Length == 1 ? part.ToUpperInvariant() : char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) || ch == ':' ? '_' : ch).ToArray();
        return new string(chars);
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
            return string.Empty;

        return property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : property.ToString();
    }

    private static long GetLong(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined ||
            !element.TryGetProperty(propertyName, out var property))
            return 0L;

        return property.TryGetInt64(out var value) ? value : 0L;
    }

    private sealed record SwedishVoiceMetadata(string DisplayName, string Gender, string Dataset, string License, string Notes);

    private sealed class PiperCatalogCacheFile
    {
        public DateTime RefreshedAtUtc { get; set; }
        public List<PiperVoiceCatalogEntry>? Entries { get; set; } = new();
    }

    private sealed class PiperInstalledVoiceManifest
    {
        public int Version { get; set; } = 1;
        public List<PiperInstalledVoice> Voices { get; set; } = new();
    }
}
