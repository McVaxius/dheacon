using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Dheacon.Services;

public sealed class DheaconPresetService
{
    public const string Schema = "dheacon-preset-v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly Dictionary<string, DheaconPreset> presets = new(StringComparer.OrdinalIgnoreCase);

    public DheaconPresetService(IDalamudPluginInterface pluginInterface, IPluginLog log, Configuration configuration)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
        this.configuration = configuration;
        UserPresetsDirectory = Path.Combine(pluginInterface.ConfigDirectory.FullName, "data", "presets");
        LoadPresets();
        EnsureActivePreset();
    }

    public string UserPresetsDirectory { get; }
    public string LastStatus { get; private set; } = "Presets not loaded.";
    public string LastError { get; private set; } = string.Empty;
    public IReadOnlyCollection<DheaconPreset> Presets => presets.Values.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase).ToList();
    public DheaconPreset ActivePreset => GetPreset(configuration.ActivePresetId) ?? GetPreset(DheaconPresetIds.ReadingRoegadyn) ?? presets.Values.First();

    public DheaconPreset? GetPreset(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
            return null;

        var trimmed = idOrName.Trim();
        if (presets.TryGetValue(trimmed, out var direct))
            return direct;

        return presets.Values.FirstOrDefault(preset =>
            string.Equals(preset.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public bool SetActivePreset(string idOrName, out string message)
    {
        var preset = GetPreset(idOrName);
        if (preset == null)
        {
            message = $"Preset '{idOrName}' was not found.";
            return false;
        }

        configuration.ActivePresetId = preset.Id;
        configuration.CommentaryMode = preset.Mode;
        ApplyBehaviorToConfiguration(preset.Behavior, save: false);
        configuration.Save();
        message = $"Active preset set to {preset.Name}.";
        return true;
    }

    public bool DeleteUserPreset(string idOrName, out string message)
    {
        var preset = GetPreset(idOrName);
        if (preset == null)
        {
            message = $"Preset '{idOrName}' was not found.";
            return false;
        }

        if (preset.Protected)
        {
            message = $"Preset '{preset.Name}' is protected and cannot be deleted.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(preset.SourcePath) || !File.Exists(preset.SourcePath))
        {
            message = $"Preset '{preset.Name}' is not a user preset file.";
            return false;
        }

        File.Delete(preset.SourcePath);
        presets.Remove(preset.Id);
        EnsureActivePreset();
        message = $"Deleted preset '{preset.Name}'.";
        return true;
    }

    public string ExportPresetBase64(string idOrName, Func<string, string?>? kranglerPresetExporter = null)
    {
        var preset = GetPreset(idOrName) ?? ActivePreset;
        var export = preset.CloneForExport();
        if (string.Equals(preset.Id, configuration.ActivePresetId, StringComparison.OrdinalIgnoreCase))
            export.Behavior = CaptureBehaviorFromConfiguration();

        if (export.ImaginaryFren?.Enabled == true &&
            !string.IsNullOrWhiteSpace(export.ImaginaryFren.PresetKey) &&
            kranglerPresetExporter != null)
        {
            export.ImaginaryFren.EmbeddedKranglerPresetBase64 = kranglerPresetExporter(export.ImaginaryFren.PresetKey) ?? string.Empty;
        }

        var json = JsonSerializer.Serialize(export, JsonOptions);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public bool ImportPresetBase64(string encoded, out DheaconPreset? imported, out string message)
    {
        imported = null;
        if (string.IsNullOrWhiteSpace(encoded))
        {
            message = "Preset import text was empty.";
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded.Trim()));
            var preset = JsonSerializer.Deserialize<DheaconPreset>(json, JsonOptions);
            if (preset == null || !string.Equals(preset.Schema, Schema, StringComparison.OrdinalIgnoreCase))
            {
                message = "Preset import schema was not recognized.";
                return false;
            }

            preset.Id = SanitizeId(preset.Id);
            if (string.IsNullOrWhiteSpace(preset.Id))
                preset.Id = SanitizeId(preset.Name);
            if (string.IsNullOrWhiteSpace(preset.Id))
                preset.Id = Guid.NewGuid().ToString("N");

            if (IsProtectedId(preset.Id) || presets.ContainsKey(preset.Id))
                preset.Id = $"{preset.Id}-import-{DateTime.UtcNow:yyyyMMddHHmmss}";

            preset.Name = string.IsNullOrWhiteSpace(preset.Name) ? preset.Id : preset.Name.Trim();
            preset.Protected = false;
            preset.Bundled = false;
            preset.SourcePath = string.Empty;
            preset.Behavior ??= DheaconPresetBehavior.FromConfiguration(configuration);

            Directory.CreateDirectory(UserPresetsDirectory);
            var fileName = SanitizeFileName(preset.Id);
            var target = Path.Combine(UserPresetsDirectory, $"{fileName}.json");
            var suffix = 2;
            while (File.Exists(target))
            {
                target = Path.Combine(UserPresetsDirectory, $"{fileName}-{suffix}.json");
                suffix++;
            }

            File.WriteAllText(target, JsonSerializer.Serialize(preset, JsonOptions), Encoding.UTF8);
            preset.SourcePath = target;
            presets[preset.Id] = preset;
            imported = preset;
            message = $"Imported preset '{preset.Name}'.";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            LastError = ex.Message;
            return false;
        }
    }

    public DheaconPresetBehavior CaptureBehaviorFromConfiguration()
        => DheaconPresetBehavior.FromConfiguration(configuration);

    public bool ApplyBehaviorToConfiguration(DheaconPresetBehavior behavior, bool save)
    {
        var changed = false;

        changed |= Set(configuration.SuppressTeleportAndReturnTransitions, behavior.SuppressTeleportAndReturnTransitions, value => configuration.SuppressTeleportAndReturnTransitions = value);
        changed |= Set(configuration.AlertSoundRelativePath, behavior.AlertSoundRelativePath, value => configuration.AlertSoundRelativePath = value);
        changed |= Set(configuration.TtsBackend, behavior.TtsBackend, value => configuration.TtsBackend = value);
        changed |= Set(configuration.TtsModernVoiceId, behavior.TtsModernVoiceId, value => configuration.TtsModernVoiceId = value);
        changed |= Set(configuration.TtsVoiceName, behavior.TtsVoiceName, value => configuration.TtsVoiceName = value);
        changed |= Set(configuration.TtsPiperVoiceId, behavior.TtsPiperVoiceId, value => configuration.TtsPiperVoiceId = value);
        changed |= Set(configuration.TtsPiperTextAdapterEnabled, behavior.TtsPiperTextAdapterEnabled, value => configuration.TtsPiperTextAdapterEnabled = value);
        changed |= Set(configuration.TtsPiperTextAdapterId, behavior.TtsPiperTextAdapterId, value => configuration.TtsPiperTextAdapterId = value);
        changed |= Set(configuration.TtsRate, behavior.TtsRate, value => configuration.TtsRate = value);
        changed |= Set(configuration.TtsVolume, behavior.TtsVolume, value => configuration.TtsVolume = value);
        changed |= Set(configuration.TtsPitch, behavior.TtsPitch, value => configuration.TtsPitch = value);
        changed |= Set(configuration.TtsOutputGainPercent, behavior.TtsOutputGainPercent, value => configuration.TtsOutputGainPercent = value);
        changed |= Set(configuration.TtsPiperLengthScale, behavior.TtsPiperLengthScale, value => configuration.TtsPiperLengthScale = value);
        changed |= Set(configuration.TtsPiperSentenceSilence, behavior.TtsPiperSentenceSilence, value => configuration.TtsPiperSentenceSilence = value);
        changed |= Set(configuration.TtsPiperPitchShiftSemitones, behavior.TtsPiperPitchShiftSemitones, value => configuration.TtsPiperPitchShiftSemitones = value);
        changed |= Set(configuration.LoginCommentaryEnabled, behavior.LoginCommentaryEnabled, value => configuration.LoginCommentaryEnabled = value);
        changed |= Set(configuration.TerritoryCommentaryEnabled, behavior.TerritoryCommentaryEnabled, value => configuration.TerritoryCommentaryEnabled = value);
        changed |= Set(configuration.IdleCommentaryEnabled, behavior.IdleCommentaryEnabled, value => configuration.IdleCommentaryEnabled = value);
        changed |= Set(configuration.CombatCommentaryEnabled, behavior.CombatCommentaryEnabled, value => configuration.CombatCommentaryEnabled = value);
        changed |= Set(configuration.BgmMachinationsCommentaryEnabled, behavior.BgmMachinationsCommentaryEnabled, value => configuration.BgmMachinationsCommentaryEnabled = value);
        changed |= Set(configuration.ExpandedEventCommentaryEnabled, behavior.ExpandedEventCommentaryEnabled, value => configuration.ExpandedEventCommentaryEnabled = value);
        changed |= Set(configuration.NearbyObservationCommentaryEnabled, behavior.NearbyObservationCommentaryEnabled, value => configuration.NearbyObservationCommentaryEnabled = value);
        changed |= Set(configuration.ReadingRoegadynTriggerChancePercent, Math.Clamp(behavior.TriggerChancePercent, 0, 100), value => configuration.ReadingRoegadynTriggerChancePercent = value);
        changed |= Set(configuration.TerritoryCommentaryCooldownSeconds, Math.Max(0, behavior.TerritoryCooldownSeconds), value => configuration.TerritoryCommentaryCooldownSeconds = value);
        changed |= Set(configuration.IdleCommentaryCooldownSeconds, Math.Max(30, behavior.IdleCooldownSeconds), value => configuration.IdleCommentaryCooldownSeconds = value);
        changed |= Set(configuration.CombatCommentaryCooldownSeconds, Math.Max(0, behavior.CombatCooldownSeconds), value => configuration.CombatCommentaryCooldownSeconds = value);
        changed |= Set(configuration.BgmCommentaryCooldownSeconds, Math.Max(0, behavior.BgmCooldownSeconds), value => configuration.BgmCommentaryCooldownSeconds = value);
        changed |= Set(configuration.ExpandedEventCooldownSeconds, Math.Max(0, behavior.ExpandedEventCooldownSeconds), value => configuration.ExpandedEventCooldownSeconds = value);
        changed |= Set(configuration.NearbyObservationCooldownSeconds, Math.Max(30, behavior.NearbyObservationCooldownSeconds), value => configuration.NearbyObservationCooldownSeconds = value);

        if (changed && save)
            configuration.Save();

        return changed;
    }

    private void LoadPresets()
    {
        presets.Clear();
        Directory.CreateDirectory(UserPresetsDirectory);

        var pluginDir = pluginInterface.AssemblyLocation.DirectoryName ?? string.Empty;
        var bundledDir = Path.Combine(pluginDir, "data", "presets");
        var bundledCount = LoadPresetDirectory(bundledDir, bundled: true);
        var userCount = LoadPresetDirectory(UserPresetsDirectory, bundled: false);

        LastStatus = $"Loaded {presets.Count} preset(s): {bundledCount} bundled, {userCount} user.";
    }

    private int LoadPresetDirectory(string directory, bool bundled)
    {
        if (!Directory.Exists(directory))
            return 0;

        var loaded = 0;
        foreach (var file in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var preset = JsonSerializer.Deserialize<DheaconPreset>(File.ReadAllText(file), JsonOptions);
                if (preset == null || !string.Equals(preset.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                    continue;

                preset.Id = SanitizeId(preset.Id);
                if (string.IsNullOrWhiteSpace(preset.Id))
                    continue;

                preset.Name = string.IsNullOrWhiteSpace(preset.Name) ? preset.Id : preset.Name.Trim();
                preset.Behavior ??= DheaconPresetBehavior.FromConfiguration(configuration);
                preset.Bundled = bundled;
                preset.Protected = bundled && IsProtectedId(preset.Id) || preset.Protected;
                preset.SourcePath = file;

                if (!bundled && presets.TryGetValue(preset.Id, out var existing) && existing.Protected)
                    continue;

                presets[preset.Id] = preset;
                loaded++;
            }
            catch (Exception ex)
            {
                log.Warning(ex, $"[Dheacon] Failed to load preset '{file}'.");
                LastError = ex.Message;
            }
        }

        return loaded;
    }

    private void EnsureActivePreset()
    {
        if (presets.Count == 0)
            presets[DheaconPresetIds.ReadingRoegadyn] = DheaconPreset.CreateFallbackReadingRoegadyn();

        var changed = false;
        if (GetPreset(configuration.ActivePresetId) == null)
        {
            configuration.ActivePresetId = presets.ContainsKey(DheaconPresetIds.ReadingRoegadyn)
                ? DheaconPresetIds.ReadingRoegadyn
                : presets.Keys.First();
            changed = true;
        }

        var active = ActivePreset;
        if (configuration.CommentaryMode != active.Mode)
        {
            configuration.CommentaryMode = active.Mode;
            changed = true;
        }

        if (changed)
            configuration.Save();
    }

    private static bool IsProtectedId(string id)
        => string.Equals(id, DheaconPresetIds.ReadingRoegadyn, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(id, DheaconPresetIds.Dheacon, StringComparison.OrdinalIgnoreCase);

    private static string SanitizeId(string value)
        => new((value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string((value ?? string.Empty)
            .Trim()
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray());
        return sanitized.Length > 96 ? sanitized[..96] : sanitized;
    }

    private static bool Set<T>(T current, T value, Action<T> assign)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
            return false;

        assign(value);
        return true;
    }
}

public static class DheaconPresetIds
{
    public const string ReadingRoegadyn = "reading-roegadyn";
    public const string Dheacon = "dheacon";
}

public sealed class DheaconPreset
{
    public string Schema { get; set; } = DheaconPresetService.Schema;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Protected { get; set; }
    public CommentaryMode Mode { get; set; } = CommentaryMode.ReadingRoegadyn;
    public string LinePackId { get; set; } = "reading-roegadyn";
    public Dictionary<string, List<LinePackEntry>> Lines { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DheaconPresetBehavior Behavior { get; set; } = new();
    public KranglerImaginaryFrenPreset? ImaginaryFren { get; set; }

    [JsonIgnore]
    public bool Bundled { get; set; }

    [JsonIgnore]
    public string SourcePath { get; set; } = string.Empty;

    public DheaconPreset CloneForExport()
        => new()
        {
            Schema = Schema,
            Id = Id,
            Name = Name,
            Description = Description,
            Protected = false,
            Mode = Mode,
            LinePackId = LinePackId,
            Lines = Lines.ToDictionary(pair => pair.Key, pair => pair.Value.Select(line => new LinePackEntry { Text = line.Text, Weight = line.Weight }).ToList(), StringComparer.OrdinalIgnoreCase),
            Behavior = Behavior.Clone(),
            ImaginaryFren = ImaginaryFren?.Clone(),
        };

    public static DheaconPreset CreateFallbackReadingRoegadyn()
        => new()
        {
            Id = DheaconPresetIds.ReadingRoegadyn,
            Name = "Reading Roegadyn",
            Protected = true,
            Mode = CommentaryMode.ReadingRoegadyn,
            Behavior = new DheaconPresetBehavior { TtsPiperPitchShiftSemitones = -3.8d },
            ImaginaryFren = new KranglerImaginaryFrenPreset(),
        };
}

public sealed class DheaconPresetBehavior
{
    public bool SuppressTeleportAndReturnTransitions { get; set; } = true;
    public string AlertSoundRelativePath { get; set; } = @"data\transition-alert.wav";
    public TtsBackend TtsBackend { get; set; } = TtsBackend.LegacySapi;
    public string TtsModernVoiceId { get; set; } = string.Empty;
    public string TtsVoiceName { get; set; } = string.Empty;
    public string TtsPiperVoiceId { get; set; } = string.Empty;
    public bool TtsPiperTextAdapterEnabled { get; set; } = true;
    public string TtsPiperTextAdapterId { get; set; } = "en_US-to-sv_SE";
    public int TtsRate { get; set; }
    public int TtsVolume { get; set; } = 100;
    public double TtsPitch { get; set; } = 0.75d;
    public int TtsOutputGainPercent { get; set; } = 200;
    public double TtsPiperLengthScale { get; set; } = 1.0d;
    public double TtsPiperSentenceSilence { get; set; } = 0.2d;
    public double TtsPiperPitchShiftSemitones { get; set; } = -3.8d;
    public bool LoginCommentaryEnabled { get; set; } = true;
    public bool TerritoryCommentaryEnabled { get; set; } = true;
    public bool IdleCommentaryEnabled { get; set; } = true;
    public bool CombatCommentaryEnabled { get; set; } = true;
    public bool BgmMachinationsCommentaryEnabled { get; set; } = true;
    public bool ExpandedEventCommentaryEnabled { get; set; } = true;
    public bool NearbyObservationCommentaryEnabled { get; set; } = true;
    public int TriggerChancePercent { get; set; } = 25;
    public int TerritoryCooldownSeconds { get; set; } = 8;
    public int IdleCooldownSeconds { get; set; } = 600;
    public int CombatCooldownSeconds { get; set; } = 20;
    public int BgmCooldownSeconds { get; set; } = 120;
    public int ExpandedEventCooldownSeconds { get; set; } = 45;
    public int NearbyObservationCooldownSeconds { get; set; } = 180;

    public DheaconPresetBehavior Clone()
        => (DheaconPresetBehavior)MemberwiseClone();

    public static DheaconPresetBehavior FromConfiguration(Configuration configuration)
        => new()
        {
            SuppressTeleportAndReturnTransitions = configuration.SuppressTeleportAndReturnTransitions,
            AlertSoundRelativePath = configuration.AlertSoundRelativePath,
            TtsBackend = configuration.TtsBackend,
            TtsModernVoiceId = configuration.TtsModernVoiceId,
            TtsVoiceName = configuration.TtsVoiceName,
            TtsPiperVoiceId = configuration.TtsPiperVoiceId,
            TtsPiperTextAdapterEnabled = configuration.TtsPiperTextAdapterEnabled,
            TtsPiperTextAdapterId = configuration.TtsPiperTextAdapterId,
            TtsRate = configuration.TtsRate,
            TtsVolume = configuration.TtsVolume,
            TtsPitch = configuration.TtsPitch,
            TtsOutputGainPercent = configuration.TtsOutputGainPercent,
            TtsPiperLengthScale = configuration.TtsPiperLengthScale,
            TtsPiperSentenceSilence = configuration.TtsPiperSentenceSilence,
            TtsPiperPitchShiftSemitones = configuration.TtsPiperPitchShiftSemitones,
            LoginCommentaryEnabled = configuration.LoginCommentaryEnabled,
            TerritoryCommentaryEnabled = configuration.TerritoryCommentaryEnabled,
            IdleCommentaryEnabled = configuration.IdleCommentaryEnabled,
            CombatCommentaryEnabled = configuration.CombatCommentaryEnabled,
            BgmMachinationsCommentaryEnabled = configuration.BgmMachinationsCommentaryEnabled,
            ExpandedEventCommentaryEnabled = configuration.ExpandedEventCommentaryEnabled,
            NearbyObservationCommentaryEnabled = configuration.NearbyObservationCommentaryEnabled,
            TriggerChancePercent = configuration.ReadingRoegadynTriggerChancePercent,
            TerritoryCooldownSeconds = configuration.TerritoryCommentaryCooldownSeconds,
            IdleCooldownSeconds = configuration.IdleCommentaryCooldownSeconds,
            CombatCooldownSeconds = configuration.CombatCommentaryCooldownSeconds,
            BgmCooldownSeconds = configuration.BgmCommentaryCooldownSeconds,
            ExpandedEventCooldownSeconds = configuration.ExpandedEventCooldownSeconds,
            NearbyObservationCooldownSeconds = configuration.NearbyObservationCooldownSeconds,
        };
}

public sealed class KranglerImaginaryFrenPreset
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "Golden Sven";
    public string PresetKey { get; set; } = "e97d1e17-9247-46aa-a9ad-b942ab905d31";
    public string EmbeddedKranglerPresetBase64 { get; set; } = string.Empty;

    public KranglerImaginaryFrenPreset Clone()
        => (KranglerImaginaryFrenPreset)MemberwiseClone();
}
