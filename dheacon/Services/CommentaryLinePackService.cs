using System.Text.Json;
using Dalamud.Plugin.Services;

namespace Dheacon.Services;

public sealed class CommentaryLinePackService
{
    private const string LinePackRelativePath = @"data\reading-roegadyn-lines.json";

    private readonly IPluginLog log;
    private readonly Random random = new();
    private readonly object syncRoot = new();
    private readonly Dictionary<CommentaryCategory, List<CommentaryLine>> lines = new();
    private readonly Dictionary<CommentaryCategory, Queue<string>> recentLines = new();

    public CommentaryLinePackService(IPluginLog log)
    {
        this.log = log;
        Load();
    }

    public string LastLoadStatus { get; private set; } = "Line packs not loaded.";

    public string GetLine(CommentaryCategory category, CommentaryContext? context = null)
    {
        lock (syncRoot)
        {
            if (!lines.TryGetValue(category, out var categoryLines) || categoryLines.Count == 0)
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
            .Replace("{bgmId}", (context.BgmId ?? 0).ToString(), StringComparison.Ordinal);
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
