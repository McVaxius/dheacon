using System.Text.Json;
using Dalamud.Plugin.Services;

namespace Dheacon.Services;

public sealed class CommentaryLinePackService
{
    private const string LinePackRelativePath = @"data\reading-roegadyn-lines.json";

    private readonly IPluginLog log;
    private readonly DheaconPresetService presetService;
    private readonly Random random = new();
    private readonly object syncRoot = new();
    private readonly Dictionary<CommentaryCategory, List<CommentaryLine>> lines = new();
    private readonly Dictionary<CommentaryCategory, Queue<string>> recentLines = new();

    public CommentaryLinePackService(IPluginLog log, DheaconPresetService presetService)
    {
        this.log = log;
        this.presetService = presetService;
        Load();
    }

    public string LastLoadStatus { get; private set; } = "Line packs not loaded.";

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

    private void Load()
    {
        lines.Clear();

        var assemblyDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty;
        var path = Path.GetFullPath(Path.Combine(assemblyDirectory, LinePackRelativePath));

        try
        {
            if (File.Exists(path))
            {
                var file = JsonSerializer.Deserialize<LinePackFile>(File.ReadAllText(path), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });

                if (file != null)
                {
                    foreach (var category in file.Categories)
                    {
                        if (!Enum.TryParse<CommentaryCategory>(category.Category, true, out var parsed))
                            continue;

                        var loadedLines = category.Lines
                            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
                            .Select(line => new CommentaryLine(line.Text.Trim(), Math.Max(1, line.Weight)))
                            .ToList();

                        if (loadedLines.Count > 0)
                            lines[parsed] = loadedLines;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[Dheacon] Failed to load Reading Roegadyn line pack; using built-in fallback lines.");
        }

        LoadFallbacksForMissingCategories();
        LastLoadStatus = $"Loaded {lines.Sum(pair => pair.Value.Count)} Reading Roegadyn lines.";
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

    public Dictionary<string, int> GetActiveLineCounts()
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

        if (string.Equals(activePreset.LinePackId, "reading-roegadyn", StringComparison.OrdinalIgnoreCase) &&
            lines.TryGetValue(category, out var sharedLines))
        {
            return sharedLines;
        }

        return lines.GetValueOrDefault(category) ?? new List<CommentaryLine>();
    }

    private void LoadFallbacksForMissingCategories()
    {
        AddFallback(CommentaryCategory.ManualTest, "Reading Roegadyn reporting in. The local voice cache is ready.");
        AddFallback(CommentaryCategory.TerritoryChange, "We have moved from {from} to {to}.");
        AddFallback(CommentaryCategory.Login, "Welcome back. I have the route notes and a clean mug.");
        AddFallback(CommentaryCategory.Idle, "A rare quiet moment. Even the aetherytes are keeping their opinions to themselves.");
        AddFallback(CommentaryCategory.CombatStart, "Steel out. Commentary brief and useful.");
        AddFallback(CommentaryCategory.CombatEnd, "Combat ended. I will pretend that was all deliberate.");
        AddFallback(CommentaryCategory.BgmMachinations, "Machinations detected. Someone nearby is about to make paperwork violent.");
        AddFallback(CommentaryCategory.LevelChange, "{job} level {level}. Please update the forms before looking proud.");
        AddFallback(CommentaryCategory.ClassJobChange, "{job} selected. New badge, same weather.");
        AddFallback(CommentaryCategory.MountStart, "Mounted. The walking committee has been adjourned.");
        AddFallback(CommentaryCategory.MountEnd, "Dismounted. The floor has resumed jurisdiction.");
        AddFallback(CommentaryCategory.FlightStart, "Flight confirmed. Gravity has filed a complaint.");
        AddFallback(CommentaryCategory.FlightEnd, "Flight ended. Return the sky to its drawer.");
        AddFallback(CommentaryCategory.DutyQueueStart, "Duty queue joined. Waiting has acquired an official title.");
        AddFallback(CommentaryCategory.DutyQueueEnd, "Duty queue ended. The clipboard may unclench.");
        AddFallback(CommentaryCategory.DutyStart, "Duty commenced. Please keep heroics within posted guidelines.");
        AddFallback(CommentaryCategory.DutyEnd, "Duty concluded. Everyone pretend the report was tidy.");
        AddFallback(CommentaryCategory.CraftingStart, "Crafting started. The desk has requested incident coverage.");
        AddFallback(CommentaryCategory.CraftingEnd, "Crafting ended. No one mention the offcuts.");
        AddFallback(CommentaryCategory.GatheringStart, "Gathering started. The dirt is now a stakeholder.");
        AddFallback(CommentaryCategory.GatheringEnd, "Gathering ended. The land has been inconvenienced.");
        AddFallback(CommentaryCategory.FishingStart, "Fishing started. We will negotiate with water.");
        AddFallback(CommentaryCategory.FishingEnd, "Fishing ended. The fish have submitted mixed feedback.");
        AddFallback(CommentaryCategory.CutsceneStart, "Cutscene started. I will hold your place in reality.");
        AddFallback(CommentaryCategory.CutsceneEnd, "Cutscene ended. Reality has resumed billing.");
        AddFallback(CommentaryCategory.CutsceneStartDuty, "Duty cutscene started. The instance has taken narrative custody.");
        AddFallback(CommentaryCategory.CutsceneEndDuty, "Duty cutscene ended. The objective may continue being unreasonable.");
        AddFallback(CommentaryCategory.CutsceneStartNonDuty, "Cutscene started. I will hold your place in reality.");
        AddFallback(CommentaryCategory.CutsceneEndNonDuty, "Cutscene ended. Reality has resumed billing.");
        AddFallback(CommentaryCategory.CutsceneStartTreasureDungeon, "Treasure dungeon cutscene started. The loot room is being dramatic.");
        AddFallback(CommentaryCategory.CutsceneEndTreasureDungeon, "Treasure dungeon cutscene ended. Count your doors and your optimism.");
        AddFallback(CommentaryCategory.PerformanceStart, "Performance started. The arts have entered the ledger.");
        AddFallback(CommentaryCategory.PerformanceEnd, "Performance ended. Applause may be filed alphabetically.");
        AddFallback(CommentaryCategory.MinigameStart, "Mini-game started. Small stakes, full paperwork.");
        AddFallback(CommentaryCategory.MinigameEnd, "Mini-game ended. The ledger accepts tiny victories.");
        AddFallback(CommentaryCategory.SummoningBellStart, "Summoning bell engaged. Retainers, brace for questions.");
        AddFallback(CommentaryCategory.SummoningBellEnd, "Summoning bell released. Commerce may breathe again.");
        AddFallback(CommentaryCategory.PartyFinderStart, "Party Finder opened. Strangers will now self-categorize.");
        AddFallback(CommentaryCategory.PartyFinderEnd, "Party Finder closed. The social ledger is temporarily quiet.");
        AddFallback(CommentaryCategory.SwimmingStart, "Swimming started. Boots are no longer policy-compliant.");
        AddFallback(CommentaryCategory.SwimmingEnd, "Swimming ended. Drip responsibly.");
        AddFallback(CommentaryCategory.DivingStart, "Diving started. The ocean has accepted your application.");
        AddFallback(CommentaryCategory.DivingEnd, "Diving ended. Surface paperwork restored.");
        AddFallback(CommentaryCategory.Unconscious, "Unconscious. I will mark this as a training expense.");
        AddFallback(CommentaryCategory.Recovered, "Recovered. The floor has released its claim.");
        AddFallback(CommentaryCategory.PvpEnter, "PvP entered. Diplomacy has been given a weapon.");
        AddFallback(CommentaryCategory.PvpLeave, "PvP left. The scoreboard may return to sleep.");
        AddFallback(CommentaryCategory.NearbyPlayerObservation, "{nearbyPlayer} is nearby. I will pretend this was scheduled.");
        AddFallback(CommentaryCategory.NearbyCrowdObservation, "{nearbyCount} nearby adventurers detected. The pavement is negotiating for space.");
    }

    private void AddFallback(CommentaryCategory category, string text)
    {
        if (lines.ContainsKey(category))
            return;

        lines[category] = new List<CommentaryLine> { new(text, 1) };
    }

    private sealed record CommentaryLine(string Text, int Weight);

    private sealed class LinePackFile
    {
        public List<LinePackCategory> Categories { get; set; } = new();
    }

    private sealed class LinePackCategory
    {
        public string Category { get; set; } = string.Empty;
        public List<LinePackEntry> Lines { get; set; } = new();
    }

    private sealed class LinePackEntry
    {
        public string Text { get; set; } = string.Empty;
        public int Weight { get; set; } = 1;
    }
}
