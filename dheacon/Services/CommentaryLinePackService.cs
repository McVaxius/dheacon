using System.Text.Json;
using Dalamud.Plugin.Services;

namespace Dheacon.Services;

public sealed class CommentaryLinePackService
{
    public const string ReadingRoegadynLinePackId = "reading-roegadyn-lines";

    private const string ReadingRoegadynLegacyAliasId = "reading-roegadyn";
    private const string LegacyLinePackRelativePath = @"data\reading-roegadyn-lines.json";
    private const string BundledLinePackRelativeDirectory = @"data\line-packs";
    private const string UserLinePackRelativeDirectory = @"data\line-packs";

    private readonly IPluginLog log;
    private readonly DheaconPresetService presetService;
    private readonly Random random = new();
    private readonly object syncRoot = new();
    private readonly Dictionary<string, Dictionary<CommentaryCategory, List<CommentaryLine>>> linePacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LinePackInfo> linePackInfos = new();
    private readonly Dictionary<CommentaryCategory, Queue<string>> recentLines = new();

    public CommentaryLinePackService(IPluginLog log, DheaconPresetService presetService)
    {
        this.log = log;
        this.presetService = presetService;
        Load();
    }

    public string LastLoadStatus { get; private set; } = "Line packs not loaded.";

    public IReadOnlyList<LinePackInfo> LinePacks
    {
        get
        {
            lock (syncRoot)
            {
                return linePackInfos
                    .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(info => info.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
    }

    public string CanonicalizeLinePackId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return string.Empty;

        var trimmed = id.Trim();
        if (string.Equals(trimmed, ReadingRoegadynLegacyAliasId, StringComparison.OrdinalIgnoreCase))
            return ReadingRoegadynLinePackId;

        return SanitizeLinePackId(trimmed);
    }

    public LinePackInfo? GetLinePackInfo(string? id)
    {
        var canonicalId = CanonicalizeLinePackId(id);
        if (string.IsNullOrWhiteSpace(canonicalId))
            return null;

        lock (syncRoot)
        {
            return linePackInfos.FirstOrDefault(info => string.Equals(info.Id, canonicalId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public string GetLine(CommentaryCategory category, CommentaryContext? context = null)
    {
        lock (syncRoot)
        {
            var categoryLines = ResolveLines(category);
            if (categoryLines.Count == 0)
            {
                LastLoadStatus = $"No lines found for {category}; used fallback text.";
                return ApplyContext("The Reading Roegadyn has no notes for this moment.", context);
            }

            var recent = recentLines.GetValueOrDefault(category);
            var suppressed = recent is { Count: > 0 }
                ? categoryLines.Where(line => !recent.Contains(line.Text)).ToList()
                : categoryLines;

            var candidates = suppressed.Count > 0 ? suppressed : categoryLines;
            var selected = SelectWeighted(candidates);
            Remember(category, selected.Text, categoryLines.Count);
            return ApplyContext(selected.Text, context);
        }
    }

    public Dictionary<string, int> GetActiveLineCounts()
    {
        lock (syncRoot)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in Enum.GetValues<CommentaryCategory>())
            {
                var count = ResolveLines(category).Count;
                if (count > 0)
                    result[category.ToString()] = count;
            }

            return result;
        }
    }

    private void Load()
    {
        lock (syncRoot)
        {
            linePacks.Clear();
            linePackInfos.Clear();

            var assemblyDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty;
            var legacyPath = Path.GetFullPath(Path.Combine(assemblyDirectory, LegacyLinePackRelativePath));
            var loadedLegacy = LoadLinePackFile(
                legacyPath,
                bundled: true,
                fallbackId: ReadingRoegadynLinePackId,
                fallbackName: "Reading Roegadyn",
                fallbackDescription: "Default Reading Roegadyn commentary lines.");

            var bundledDirectory = Path.GetFullPath(Path.Combine(assemblyDirectory, BundledLinePackRelativeDirectory));
            var loadedBundled = LoadLinePackDirectory(bundledDirectory, bundled: true);

            var userDirectory = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, UserLinePackRelativeDirectory);
            Directory.CreateDirectory(userDirectory);
            var loadedUser = LoadLinePackDirectory(userDirectory, bundled: false);

            if (!linePacks.TryGetValue(ReadingRoegadynLinePackId, out var readingLines))
            {
                readingLines = new Dictionary<CommentaryCategory, List<CommentaryLine>>();
                linePacks[ReadingRoegadynLinePackId] = readingLines;
            }

            LoadFallbacksForMissingCategories(readingLines);
            if (!linePackInfos.Any(info => string.Equals(info.Id, ReadingRoegadynLinePackId, StringComparison.OrdinalIgnoreCase)))
            {
                EnsureLinePackInfo(
                    new LinePackInfo(
                        ReadingRoegadynLinePackId,
                        "Reading Roegadyn",
                        "Default Reading Roegadyn commentary lines.",
                        legacyPath,
                        true));
            }

            var totalLines = linePacks.Values.Sum(pack => pack.Values.Sum(categoryLines => categoryLines.Count));
            LastLoadStatus = $"Loaded {linePacks.Count} line pack(s), {totalLines} line(s): {(loadedLegacy ? 1 : 0) + loadedBundled} bundled, {loadedUser} user.";
        }
    }

    private int LoadLinePackDirectory(string directory, bool bundled)
    {
        if (!Directory.Exists(directory))
            return 0;

        var loaded = 0;
        foreach (var file in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (LoadLinePackFile(file, bundled, fallbackId: Path.GetFileNameWithoutExtension(file), fallbackName: null, fallbackDescription: null))
                loaded++;
        }

        return loaded;
    }

    private bool LoadLinePackFile(string path, bool bundled, string? fallbackId, string? fallbackName, string? fallbackDescription)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            var file = JsonSerializer.Deserialize<LinePackFile>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (file == null)
                return false;

            var id = CanonicalizeLinePackId(string.IsNullOrWhiteSpace(file.Id) ? fallbackId : file.Id);
            if (string.IsNullOrWhiteSpace(id))
                return false;

            var loadedCategories = new Dictionary<CommentaryCategory, List<CommentaryLine>>();
            foreach (var category in file.Categories ?? new List<LinePackCategory>())
            {
                if (!Enum.TryParse<CommentaryCategory>(category.Category, true, out var parsed))
                    continue;

                var loadedLines = (category.Lines ?? new List<LinePackEntry>())
                    .Where(line => !string.IsNullOrWhiteSpace(line.Text))
                    .Select(line => new CommentaryLine(line.Text.Trim(), Math.Max(1, line.Weight)))
                    .ToList();

                if (loadedLines.Count > 0)
                    loadedCategories[parsed] = loadedLines;
            }

            if (loadedCategories.Count == 0)
                return false;

            linePacks[id] = loadedCategories;
            EnsureLinePackInfo(new LinePackInfo(
                id,
                string.IsNullOrWhiteSpace(file.Name) ? fallbackName ?? TitleFromId(id) : file.Name.Trim(),
                string.IsNullOrWhiteSpace(file.Description) ? fallbackDescription ?? string.Empty : file.Description.Trim(),
                path,
                bundled));
            return true;
        }
        catch (Exception ex)
        {
            log.Warning(ex, $"[Dheacon] Failed to load line pack '{path}'.");
            return false;
        }
    }

    private void EnsureLinePackInfo(LinePackInfo info)
    {
        linePackInfos.RemoveAll(existing => string.Equals(existing.Id, info.Id, StringComparison.OrdinalIgnoreCase));
        linePackInfos.Add(info);
    }

    private List<CommentaryLine> ResolveLines(CommentaryCategory category)
    {
        var activePreset = presetService.ActivePreset;
        if (activePreset.Lines.TryGetValue(category.ToString(), out var presetLines))
        {
            var converted = presetLines
                .Where(line => !string.IsNullOrWhiteSpace(line.Text))
                .Select(line => new CommentaryLine(line.Text.Trim(), Math.Max(1, line.Weight)))
                .ToList();
            if (converted.Count > 0)
                return converted;
        }

        var selectedLinePackId = CanonicalizeLinePackId(activePreset.LinePackId);
        if (!string.IsNullOrWhiteSpace(selectedLinePackId) &&
            linePacks.TryGetValue(selectedLinePackId, out var selectedLinePack) &&
            selectedLinePack.TryGetValue(category, out var selectedLines) &&
            selectedLines.Count > 0)
        {
            return selectedLines;
        }

        if (linePacks.TryGetValue(ReadingRoegadynLinePackId, out var readingLinePack) &&
            readingLinePack.TryGetValue(category, out var readingLines) &&
            readingLines.Count > 0)
        {
            return readingLines;
        }

        return new List<CommentaryLine>();
    }

    private CommentaryLine SelectWeighted(IReadOnlyList<CommentaryLine> candidates)
    {
        var totalWeight = candidates.Sum(line => Math.Max(1, line.Weight));
        var roll = random.Next(1, totalWeight + 1);
        var cursor = 0;

        foreach (var line in candidates)
        {
            cursor += Math.Max(1, line.Weight);
            if (roll <= cursor)
                return line;
        }

        return candidates[^1];
    }

    private void Remember(CommentaryCategory category, string text, int categoryLineCount)
    {
        var keepCount = Math.Clamp(categoryLineCount / 2, 1, 4);
        if (!recentLines.TryGetValue(category, out var recent))
        {
            recent = new Queue<string>();
            recentLines[category] = recent;
        }

        recent.Enqueue(text);
        while (recent.Count > keepCount)
            recent.Dequeue();
    }

    private static string ApplyContext(string text, CommentaryContext? context)
    {
        if (context == null)
            return text;

        return text
            .Replace("{from}", context.FromTerritoryName ?? $"territory {context.FromTerritoryId ?? 0}", StringComparison.Ordinal)
            .Replace("{to}", context.ToTerritoryName ?? $"territory {context.ToTerritoryId ?? 0}", StringComparison.Ordinal)
            .Replace("{fromId}", (context.FromTerritoryId ?? 0).ToString(), StringComparison.Ordinal)
            .Replace("{toId}", (context.ToTerritoryId ?? 0).ToString(), StringComparison.Ordinal)
            .Replace("{bgmId}", (context.BgmId ?? 0).ToString(), StringComparison.Ordinal)
            .Replace("{job}", context.Job ?? "adventurer", StringComparison.Ordinal)
            .Replace("{level}", (context.Level ?? 0).ToString(), StringComparison.Ordinal)
            .Replace("{event}", context.Event ?? "event", StringComparison.Ordinal)
            .Replace("{nearbyPlayer}", context.NearbyPlayerName ?? "nearby adventurer", StringComparison.Ordinal)
            .Replace("{nearbyCount}", (context.NearbyPlayerCount ?? 0).ToString(), StringComparison.Ordinal)
            .Replace("{cutsceneContext}", context.CutsceneContext ?? "scene", StringComparison.Ordinal);
    }

    private void LoadFallbacksForMissingCategories(Dictionary<CommentaryCategory, List<CommentaryLine>> target)
    {
        AddFallback(target, CommentaryCategory.ManualTest, "Reading Roegadyn reporting in. The local voice cache is ready.");
        AddFallback(target, CommentaryCategory.TerritoryChange, "We have moved from {from} to {to}.");
        AddFallback(target, CommentaryCategory.Login, "Welcome back. I have the route notes and a clean mug.");
        AddFallback(target, CommentaryCategory.Idle, "A rare quiet moment. Even the aetherytes are keeping their opinions to themselves.");
        AddFallback(target, CommentaryCategory.CombatStart, "Steel out. Commentary brief and useful.");
        AddFallback(target, CommentaryCategory.CombatEnd, "Combat ended. I will pretend that was all deliberate.");
        AddFallback(target, CommentaryCategory.BgmMachinations, "Machinations detected. Someone nearby is about to make paperwork violent.");
        AddFallback(target, CommentaryCategory.LevelChange, "{job} level {level}. Please update the forms before looking proud.");
        AddFallback(target, CommentaryCategory.ClassJobChange, "{job} selected. New badge, same weather.");
        AddFallback(target, CommentaryCategory.MountStart, "Mounted. The walking committee has been adjourned.");
        AddFallback(target, CommentaryCategory.MountEnd, "Dismounted. The floor has resumed jurisdiction.");
        AddFallback(target, CommentaryCategory.FlightStart, "Flight confirmed. Gravity has filed a complaint.");
        AddFallback(target, CommentaryCategory.FlightEnd, "Flight ended. Return the sky to its drawer.");
        AddFallback(target, CommentaryCategory.DutyQueueStart, "Duty queue joined. Waiting has acquired an official title.");
        AddFallback(target, CommentaryCategory.DutyQueueEnd, "Duty queue ended. The clipboard may unclench.");
        AddFallback(target, CommentaryCategory.DutyStart, "Duty commenced. Please keep heroics within posted guidelines.");
        AddFallback(target, CommentaryCategory.DutyEnd, "Duty concluded. Everyone pretend the report was tidy.");
        AddFallback(target, CommentaryCategory.CraftingStart, "Crafting started. The desk has requested incident coverage.");
        AddFallback(target, CommentaryCategory.CraftingEnd, "Crafting ended. No one mention the offcuts.");
        AddFallback(target, CommentaryCategory.GatheringStart, "Gathering started. The dirt is now a stakeholder.");
        AddFallback(target, CommentaryCategory.GatheringEnd, "Gathering ended. The land has been inconvenienced.");
        AddFallback(target, CommentaryCategory.FishingStart, "Fishing started. We will negotiate with water.");
        AddFallback(target, CommentaryCategory.FishingEnd, "Fishing ended. The fish have submitted mixed feedback.");
        AddFallback(target, CommentaryCategory.CutsceneStart, "Cutscene started. I will hold your place in reality.");
        AddFallback(target, CommentaryCategory.CutsceneEnd, "Cutscene ended. Reality has resumed billing.");
        AddFallback(target, CommentaryCategory.CutsceneStartDuty, "Duty cutscene started. The instance has taken narrative custody.");
        AddFallback(target, CommentaryCategory.CutsceneEndDuty, "Duty cutscene ended. The objective may continue being unreasonable.");
        AddFallback(target, CommentaryCategory.CutsceneStartNonDuty, "Cutscene started. I will hold your place in reality.");
        AddFallback(target, CommentaryCategory.CutsceneEndNonDuty, "Cutscene ended. Reality has resumed billing.");
        AddFallback(target, CommentaryCategory.CutsceneStartTreasureDungeon, "Treasure dungeon cutscene started. The loot room is being dramatic.");
        AddFallback(target, CommentaryCategory.CutsceneEndTreasureDungeon, "Treasure dungeon cutscene ended. Count your doors and your optimism.");
        AddFallback(target, CommentaryCategory.PerformanceStart, "Performance started. The arts have entered the ledger.");
        AddFallback(target, CommentaryCategory.PerformanceEnd, "Performance ended. Applause may be filed alphabetically.");
        AddFallback(target, CommentaryCategory.MinigameStart, "Mini-game started. Small stakes, full paperwork.");
        AddFallback(target, CommentaryCategory.MinigameEnd, "Mini-game ended. The ledger accepts tiny victories.");
        AddFallback(target, CommentaryCategory.SummoningBellStart, "Summoning bell engaged. Retainers, brace for questions.");
        AddFallback(target, CommentaryCategory.SummoningBellEnd, "Summoning bell released. Commerce may breathe again.");
        AddFallback(target, CommentaryCategory.PartyFinderStart, "Party Finder opened. Strangers will now self-categorize.");
        AddFallback(target, CommentaryCategory.PartyFinderEnd, "Party Finder closed. The social ledger is temporarily quiet.");
        AddFallback(target, CommentaryCategory.SwimmingStart, "Swimming started. Boots are no longer policy-compliant.");
        AddFallback(target, CommentaryCategory.SwimmingEnd, "Swimming ended. Drip responsibly.");
        AddFallback(target, CommentaryCategory.DivingStart, "Diving started. The ocean has accepted your application.");
        AddFallback(target, CommentaryCategory.DivingEnd, "Diving ended. Surface paperwork restored.");
        AddFallback(target, CommentaryCategory.Unconscious, "Unconscious. I will mark this as a training expense.");
        AddFallback(target, CommentaryCategory.Recovered, "Recovered. The floor has released its claim.");
        AddFallback(target, CommentaryCategory.PvpEnter, "PvP entered. Diplomacy has been given a weapon.");
        AddFallback(target, CommentaryCategory.PvpLeave, "PvP left. The scoreboard may return to sleep.");
        AddFallback(target, CommentaryCategory.NearbyPlayerObservation, "{nearbyPlayer} is nearby. I will pretend this was scheduled.");
        AddFallback(target, CommentaryCategory.NearbyCrowdObservation, "{nearbyCount} nearby adventurers detected. The pavement is negotiating for space.");
    }

    private static void AddFallback(Dictionary<CommentaryCategory, List<CommentaryLine>> target, CommentaryCategory category, string text)
    {
        if (target.ContainsKey(category))
            return;

        target[category] = new List<CommentaryLine> { new(text, 1) };
    }

    private static string SanitizeLinePackId(string value)
        => new((value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());

    private static string TitleFromId(string id)
        => string.Join(" ", id
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private sealed record CommentaryLine(string Text, int Weight);

    private sealed class LinePackFile
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<LinePackCategory> Categories { get; set; } = new();
    }

    private sealed class LinePackCategory
    {
        public string Category { get; set; } = string.Empty;
        public List<LinePackEntry> Lines { get; set; } = new();
    }
}

public sealed record LinePackInfo(
    string Id,
    string Name,
    string Description,
    string SourcePath,
    bool Bundled);
