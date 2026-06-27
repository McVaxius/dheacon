using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;

namespace Dheacon.Services;

public sealed record SpokenTextAdapterInfo(
    string Id,
    string SourceLanguage,
    string TargetLanguage,
    string Version,
    string ContentHash,
    string Status);

public sealed record SpokenTextAdaptation(
    string Original,
    string Adapted,
    bool WasAdapted,
    string AdapterId,
    string AdapterVersion,
    string AdapterContentHash,
    string Status);

public sealed partial class SpokenTextAdapterService
{
    public const string DefaultAdapterId = "en_US-to-sv_SE";

    private const string AdapterDirectoryRelativePath = @"data\text-adapters";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly IReadOnlyDictionary<char, string> EnglishLetterNames =
        new Dictionary<char, string>
        {
            ['A'] = "ay",
            ['B'] = "bee",
            ['C'] = "see",
            ['D'] = "dee",
            ['E'] = "ee",
            ['F'] = "eff",
            ['G'] = "gee",
            ['H'] = "aitch",
            ['I'] = "eye",
            ['J'] = "jay",
            ['K'] = "kay",
            ['L'] = "ell",
            ['M'] = "em",
            ['N'] = "en",
            ['O'] = "oh",
            ['P'] = "pee",
            ['Q'] = "cue",
            ['R'] = "ar",
            ['S'] = "ess",
            ['T'] = "tee",
            ['U'] = "you",
            ['V'] = "vee",
            ['W'] = "double you",
            ['X'] = "ex",
            ['Y'] = "why",
            ['Z'] = "zed",
        };

    private readonly IPluginLog log;
    private readonly Dictionary<string, AdapterDefinition> adapters = new(StringComparer.OrdinalIgnoreCase);

    public SpokenTextAdapterService(IPluginLog log)
    {
        this.log = log;
        LoadAdapters();
    }

    public string LastStatus { get; private set; } = "No spoken text adapters loaded.";
    public string LastError { get; private set; } = string.Empty;

    public IReadOnlyList<SpokenTextAdapterInfo> GetAdapters()
        => adapters.Values
            .OrderBy(adapter => adapter.Id, StringComparer.OrdinalIgnoreCase)
            .Select(adapter => adapter.Info)
            .ToList();

    public SpokenTextAdapterInfo? GetAdapterInfo(string adapterId)
        => ResolveAdapter(adapterId, string.Empty)?.Info;

    public SpokenTextAdaptation AdaptForTarget(string adapterId, string targetLanguage, string text)
    {
        var normalized = NormalizeText(text);
        if (string.IsNullOrWhiteSpace(normalized))
            return new SpokenTextAdaptation(string.Empty, string.Empty, false, string.Empty, string.Empty, string.Empty, "No text.");

        var adapter = ResolveAdapter(adapterId, targetLanguage);
        if (adapter == null)
            return new SpokenTextAdaptation(normalized, normalized, false, string.Empty, string.Empty, string.Empty, "No matching adapter.");

        var adapted = AdaptWith(adapter, normalized);
        return new SpokenTextAdaptation(
            normalized,
            adapted,
            !string.Equals(normalized, adapted, StringComparison.Ordinal),
            adapter.Id,
            adapter.Version,
            adapter.ContentHash,
            adapter.Info.Status);
    }

    public string ResolveAdapterId(string adapterId, string targetLanguage)
        => ResolveAdapter(adapterId, targetLanguage)?.Id ?? string.Empty;

    private AdapterDefinition? ResolveAdapter(string adapterId, string targetLanguage)
    {
        if (!string.IsNullOrWhiteSpace(adapterId) &&
            adapters.TryGetValue(adapterId.Trim(), out var configured) &&
            AdapterMatchesTarget(configured, targetLanguage))
        {
            return configured;
        }

        if (adapters.TryGetValue(DefaultAdapterId, out var defaultAdapter) &&
            AdapterMatchesTarget(defaultAdapter, targetLanguage))
        {
            return defaultAdapter;
        }

        if (string.IsNullOrWhiteSpace(targetLanguage))
            return adapters.Values.FirstOrDefault();

        return adapters.Values.FirstOrDefault(adapter => AdapterMatchesTarget(adapter, targetLanguage));
    }

    private static bool AdapterMatchesTarget(AdapterDefinition adapter, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(targetLanguage))
            return true;

        var adapterTarget = NormalizeLanguage(adapter.TargetLanguage);
        var target = NormalizeLanguage(targetLanguage);
        return target.Equals(adapterTarget, StringComparison.OrdinalIgnoreCase) ||
               target.StartsWith(adapterTarget + "-", StringComparison.OrdinalIgnoreCase) ||
               adapterTarget.StartsWith(target + "-", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLanguage(string language)
        => language.Trim().Replace('_', '-');

    private void LoadAdapters()
    {
        adapters.Clear();

        var assemblyDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty;
        var adapterDirectory = Path.GetFullPath(Path.Combine(assemblyDirectory, AdapterDirectoryRelativePath));

        try
        {
            if (Directory.Exists(adapterDirectory))
            {
                foreach (var path in Directory.EnumerateFiles(adapterDirectory, "*.json", SearchOption.TopDirectoryOnly))
                    TryLoadAdapter(path);
            }

            if (!adapters.ContainsKey(DefaultAdapterId))
                AddFallbackDefaultAdapter();

            LastStatus = $"Loaded {adapters.Count} spoken text adapter(s).";
            LastError = string.Empty;
        }
        catch (Exception ex)
        {
            adapters.Clear();
            AddFallbackDefaultAdapter();
            LastStatus = "Loaded fallback spoken text adapter.";
            LastError = ex.Message;
            log.Warning(ex, "[Dheacon] Failed to load spoken text adapters.");
        }
    }

    private void TryLoadAdapter(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var file = JsonSerializer.Deserialize<AdapterFile>(bytes, JsonOptions);
            if (file == null)
                return;

            var id = string.IsNullOrWhiteSpace(file.Id)
                ? Path.GetFileNameWithoutExtension(path)
                : file.Id.Trim();
            if (string.IsNullOrWhiteSpace(id))
                return;

            var contentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var adapter = CreateAdapterDefinition(file, id, contentHash, $"Loaded from {Path.GetFileName(path)}.");
            adapters[adapter.Id] = adapter;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            log.Warning(ex, $"[Dheacon] Failed to load spoken text adapter '{path}'.");
        }
    }

    private void AddFallbackDefaultAdapter()
    {
        var file = new AdapterFile
        {
            Id = DefaultAdapterId,
            SourceLanguage = "en_US",
            TargetLanguage = "sv_SE",
            Version = "fallback-v1",
            PhraseLexicon =
            {
                new LexiconEntry { Source = "Final Fantasy XIV", Replacement = "Final Fantasy fourteen" },
                new LexiconEntry { Source = "Final Fantasy 14", Replacement = "Final Fantasy fourteen" },
            },
            WordLexicon =
            {
                new LexiconEntry { Source = "Roegadyn", Replacement = "Roe ga din" },
                new LexiconEntry { Source = "aetheryte", Replacement = "etherite" },
                new LexiconEntry { Source = "aetherytes", Replacement = "etherites" },
            },
            AcronymExpansions =
            {
                new LexiconEntry { Source = "BGM", Replacement = "bee gee em" },
                new LexiconEntry { Source = "DTR", Replacement = "dee tee ar" },
                new LexiconEntry { Source = "FFXIV", Replacement = "eff eff fourteen" },
                new LexiconEntry { Source = "SAPI", Replacement = "sap ee" },
                new LexiconEntry { Source = "TTS", Replacement = "tee tee ess" },
                new LexiconEntry { Source = "UTC", Replacement = "you tee see" },
                new LexiconEntry { Source = "WAV", Replacement = "wave" },
            },
        };

        var fallbackHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DefaultAdapterId + file.Version))).ToLowerInvariant();
        var adapter = CreateAdapterDefinition(file, DefaultAdapterId, fallbackHash, "Fallback adapter.");
        adapters[adapter.Id] = adapter;
    }

    private static AdapterDefinition CreateAdapterDefinition(AdapterFile file, string id, string contentHash, string status)
    {
        var sourceLanguage = string.IsNullOrWhiteSpace(file.SourceLanguage) ? "unknown" : file.SourceLanguage.Trim();
        var targetLanguage = string.IsNullOrWhiteSpace(file.TargetLanguage) ? "unknown" : file.TargetLanguage.Trim();
        var version = string.IsNullOrWhiteSpace(file.Version) ? "unversioned" : file.Version.Trim();

        var phrases = NormalizeEntries(file.PhraseLexicon);
        var words = NormalizeEntries(file.WordLexicon);
        var acronyms = NormalizeEntries(file.AcronymExpansions);
        var rawOverrides = NormalizeEntries(file.RawPhonemeOverrides);
        var cleanupRules = file.RegexCleanupRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern))
            .ToList();

        return new AdapterDefinition(
            id,
            sourceLanguage,
            targetLanguage,
            version,
            contentHash,
            phrases,
            words,
            acronyms,
            rawOverrides,
            cleanupRules,
            new SpokenTextAdapterInfo(id, sourceLanguage, targetLanguage, version, contentHash, status));
    }

    private static List<LexiconEntry> NormalizeEntries(IEnumerable<LexiconEntry> entries)
        => entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Source))
            .Select(entry => new LexiconEntry
            {
                Source = entry.Source.Trim(),
                Replacement = (entry.Replacement ?? string.Empty).Trim(),
                IgnoreCase = entry.IgnoreCase,
                WholeWord = entry.WholeWord,
            })
            .OrderByDescending(entry => entry.Source.Length)
            .ThenBy(entry => entry.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string AdaptWith(AdapterDefinition adapter, string text)
    {
        var protectedSegments = new List<string>();
        var adapted = NormalizeForAdapter(text);
        adapted = ProtectSegments(adapted, protectedSegments);

        adapted = ApplyLexicon(adapted, adapter.PhraseLexicon);
        adapted = ProtectSegments(adapted, protectedSegments);

        adapted = ApplyAcronyms(adapted, adapter);
        adapted = ExpandNumbers(adapted);

        adapted = ApplyLexicon(adapted, adapter.RawPhonemeOverrides);
        adapted = ProtectSegments(adapted, protectedSegments);

        adapted = ApplyLexicon(adapted, adapter.WordLexicon);
        adapted = ProtectSegments(adapted, protectedSegments);

        adapted = ApplyCleanupRules(adapted, adapter.RegexCleanupRules);
        adapted = FinalCleanup(adapted);
        adapted = RestoreSegments(adapted, protectedSegments);
        return FinalCleanup(adapted);
    }

    private static string ApplyLexicon(string text, IReadOnlyList<LexiconEntry> entries)
    {
        var adapted = text;
        foreach (var entry in entries)
        {
            var pattern = BuildLexiconPattern(entry.Source, entry.WholeWord);
            var options = RegexOptions.CultureInvariant;
            if (entry.IgnoreCase)
                options |= RegexOptions.IgnoreCase;

            adapted = Regex.Replace(adapted, pattern, entry.Replacement, options, TimeSpan.FromMilliseconds(100));
        }

        return adapted;
    }

    private static string ApplyAcronyms(string text, AdapterDefinition adapter)
    {
        var adapted = ApplyLexicon(text, adapter.AcronymExpansions);
        return AcronymRegex().Replace(adapted, match =>
        {
            var value = match.Value;
            if (value.Length < 2 || value.Length > 8)
                return value;

            var parts = new List<string>();
            foreach (var ch in value)
            {
                if (EnglishLetterNames.TryGetValue(ch, out var name))
                    parts.Add(name);
            }

            return parts.Count == value.Length ? string.Join(" ", parts) : value;
        });
    }

    private static string ExpandNumbers(string text)
        => NumberRegex().Replace(text, match => NumberToWords(match.Value));

    private static string NumberToWords(string value)
    {
        if (value.Contains('.', StringComparison.Ordinal))
        {
            var parts = value.Split('.', 2);
            var left = NumberToWords(parts[0]);
            var right = string.Join(" ", parts[1].Where(char.IsDigit).Select(DigitToWord));
            return string.IsNullOrWhiteSpace(right) ? left : $"{left} point {right}";
        }

        var cleaned = value.TrimStart('0');
        if (cleaned.Length == 0)
            return "zero";

        if (!int.TryParse(cleaned, out var number))
            return string.Join(" ", value.Where(char.IsDigit).Select(DigitToWord));

        if (number < 10000)
            return IntegerToWords(number);

        return string.Join(" ", value.Where(char.IsDigit).Select(DigitToWord));
    }

    private static string IntegerToWords(int number)
    {
        string[] ones =
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
            "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
        };
        string[] tens =
        {
            "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety",
        };

        if (number < 20)
            return ones[number];

        if (number < 100)
            return number % 10 == 0 ? tens[number / 10] : $"{tens[number / 10]} {ones[number % 10]}";

        if (number < 1000)
            return number % 100 == 0 ? $"{ones[number / 100]} hundred" : $"{ones[number / 100]} hundred {IntegerToWords(number % 100)}";

        return number % 1000 == 0 ? $"{ones[number / 1000]} thousand" : $"{ones[number / 1000]} thousand {IntegerToWords(number % 1000)}";
    }

    private static string DigitToWord(char digit)
        => digit switch
        {
            '0' => "zero",
            '1' => "one",
            '2' => "two",
            '3' => "three",
            '4' => "four",
            '5' => "five",
            '6' => "six",
            '7' => "seven",
            '8' => "eight",
            '9' => "nine",
            _ => string.Empty,
        };

    private static string ProtectSegments(string text, List<string> protectedSegments)
        => ProtectedSegmentRegex().Replace(text, match =>
        {
            var token = $"@@{protectedSegments.Count}@@";
            protectedSegments.Add(match.Value);
            return token;
        });

    private static string RestoreSegments(string text, IReadOnlyList<string> protectedSegments)
    {
        var restored = text;
        for (var i = protectedSegments.Count - 1; i >= 0; i--)
            restored = restored.Replace($"@@{i}@@", protectedSegments[i], StringComparison.Ordinal);

        return restored;
    }

    private static string ApplyCleanupRules(string text, IReadOnlyList<RegexCleanupRule> cleanupRules)
    {
        var adapted = text;
        foreach (var rule in cleanupRules)
        {
            var options = RegexOptions.CultureInvariant;
            if (rule.IgnoreCase)
                options |= RegexOptions.IgnoreCase;

            adapted = Regex.Replace(adapted, rule.Pattern, rule.Replacement ?? string.Empty, options, TimeSpan.FromMilliseconds(100));
        }

        return adapted;
    }

    private static string BuildLexiconPattern(string source, bool wholeWord)
    {
        var escaped = string.Join(
            @"\s+",
            WhitespaceRegex().Split(source.Trim())
                .Where(part => part.Length > 0)
                .Select(Regex.Escape));
        if (!wholeWord)
            return escaped;

        return $@"(?<![\p{{L}}\p{{N}}]){escaped}(?![\p{{L}}\p{{N}}])";
    }

    private static string NormalizeText(string text)
        => FinalCleanup(NormalizeForAdapter(text));

    private static string NormalizeForAdapter(string text)
    {
        var normalized = text.Trim()
            .Replace('\u2018', '\'')
            .Replace('\u2019', '\'')
            .Replace('\u201c', '"')
            .Replace('\u201d', '"')
            .Replace('\u2013', '-')
            .Replace('\u2014', ',')
            .Replace('\u00a0', ' ');

        normalized = Regex.Replace(normalized, @"(?<=\d),(?=\d{3}\b)", string.Empty, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return normalized;
    }

    private static string FinalCleanup(string text)
    {
        var cleaned = WhitespaceRegex().Replace(text.Trim(), " ");
        cleaned = Regex.Replace(cleaned, @"\s+([,.;:!?])", "$1", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        cleaned = Regex.Replace(cleaned, @"([,.;:!?])(?=[^\s\]\)])", "$1 ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return WhitespaceRegex().Replace(cleaned.Trim(), " ");
    }

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\b[A-Z]{2,8}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex AcronymRegex();

    [GeneratedRegex(@"(?<![\p{L}\p{N}@])\d+(?:\.\d+)?(?![\p{L}\p{N}@])", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"(\[\[[\s\S]*?\]\]|\{[^{}]{1,100}\}|<[^<>]{1,100}>|%[A-Z0-9_]{1,64}%)", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex ProtectedSegmentRegex();

    private sealed record AdapterDefinition(
        string Id,
        string SourceLanguage,
        string TargetLanguage,
        string Version,
        string ContentHash,
        IReadOnlyList<LexiconEntry> PhraseLexicon,
        IReadOnlyList<LexiconEntry> WordLexicon,
        IReadOnlyList<LexiconEntry> AcronymExpansions,
        IReadOnlyList<LexiconEntry> RawPhonemeOverrides,
        IReadOnlyList<RegexCleanupRule> RegexCleanupRules,
        SpokenTextAdapterInfo Info);

    private sealed class AdapterFile
    {
        public string Id { get; set; } = string.Empty;
        public string SourceLanguage { get; set; } = string.Empty;
        public string TargetLanguage { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public List<LexiconEntry> PhraseLexicon { get; set; } = new();
        public List<LexiconEntry> WordLexicon { get; set; } = new();
        public List<LexiconEntry> AcronymExpansions { get; set; } = new();
        public List<LexiconEntry> RawPhonemeOverrides { get; set; } = new();
        public List<RegexCleanupRule> RegexCleanupRules { get; set; } = new();
    }

    private sealed class LexiconEntry
    {
        public string Source { get; set; } = string.Empty;
        public string Replacement { get; set; } = string.Empty;
        public bool WholeWord { get; set; } = true;
        public bool IgnoreCase { get; set; } = true;
    }

    private sealed class RegexCleanupRule
    {
        public string Pattern { get; set; } = string.Empty;
        public string Replacement { get; set; } = string.Empty;
        public bool IgnoreCase { get; set; }
    }
}
