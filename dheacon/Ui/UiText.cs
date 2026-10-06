using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Resources;
using System.Text.RegularExpressions;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dheacon.Services;

namespace Dheacon.Ui;

internal sealed class UiText : IDisposable
{
    [ThreadStatic] private static UiText? current;
    internal static UiText Current => current ?? throw new InvalidOperationException("Enter the Dheacon UI frame before drawing.");
    internal static readonly (string Code,string Name)[] Languages=[("en","English"),("de","Deutsch"),("fr","Français"),
        ("es","Español"),("it","Italiano"),("ru","Русский"),("ja","日本語"),("ko","한국어"),("zh-Hans","简体中文"),
        ("vi","Tiếng Việt"),("pt-BR","Português (Brasil)"),("id","Bahasa Indonesia"),("pl","Polski"),("tr","Türkçe"),("hi","हिन्दी")];
    internal const string NativeSymbols="—…↗·+";
    internal static IEnumerable<string> CjkLanguages(string selected) => new[]{"ja","ko","zh-Hans"}.OrderBy(code=>code==selected?0:1);
    private readonly ResourceManager manager;
    internal ResourceSet Resources { get; }
    internal IReadOnlyList<string> RequiredText { get; }
    internal CultureInfo Culture { get; }
    internal string Language { get; }
    private readonly Func<UiFontRole,IDisposable> pushFont;
    private readonly (Regex Pattern, string Key, int ArgumentCount)[] messageTemplates;
    internal UiText(string language, Func<UiFontRole,IDisposable> pushFont)
    {
        Language=Languages.Any(l=>l.Code==language)?language:"en";
        Culture=CultureInfo.GetCultureInfo(Language);
        manager=new ResourceManager("Dheacon.Localization.Strings_"+Language.Replace('-','_'),typeof(UiText).Assembly);
        Resources=manager.GetResourceSet(CultureInfo.InvariantCulture,true,false) ?? throw new MissingManifestResourceException(Language);
        this.pushFont=pushFont;
        var english=new ResourceManager("Dheacon.Localization.Strings_en",typeof(UiText).Assembly);
        try
        {
            var fallback=english.GetResourceSet(CultureInfo.InvariantCulture,true,false) ?? throw new MissingManifestResourceException("en");
            RequiredText=Values(Resources).Concat(Values(fallback)).Concat(Languages.Select(l=>l.Name)).Append(NativeSymbols).Distinct().ToArray();
        }
        finally { english.ReleaseAllResources(); }
        // Service messages remain English in logs; only their UI copies are localized.
        var parameter = new Regex(@"\{(\d+)(?::([^}]+))?\}");
        messageTemplates = Resources.Cast<DictionaryEntry>().Select(entry => (string)entry.Key)
            .Where(key => parameter.IsMatch(key) && (!key.TrimStart().StartsWith('{') || key is "{0} speech failed; used Legacy SAPI fallback: {1}" or "{0} failed; used Legacy SAPI fallback: {1}" or "{0} is already installed."))
            .OrderByDescending(key => parameter.Replace(key, "").Length).Select(key =>
            {
                var pattern = "^";
                var offset = 0;
                var holes = parameter.Matches(key);
                foreach (Match hole in holes)
                {
                    var backend = key is "{0} speech failed; used Legacy SAPI fallback: {1}" or "{0} failed; used Legacy SAPI fallback: {1}" && hole.Groups[1].Value == "0";
                    pattern += Regex.Escape(key[offset..hole.Index]) + $"(?<arg{hole.Groups[1].Value}>" + (backend ? "ModernWindows|LegacySapi|PiperLocal" : ".*?") + ")";
                    offset = hole.Index + hole.Length;
                }
                pattern += Regex.Escape(key[offset..]) + "$";
                return (new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(20)), key,
                    holes.Cast<Match>().Max(hole => int.Parse(hole.Groups[1].Value, CultureInfo.InvariantCulture)) + 1);
            }).ToArray();
    }
    internal static string T(string english)
    {
        if (Current.Resources.GetString(english, true) is { } exact) return exact;
        var trimmed = english.TrimEnd();
        if (trimmed.Length != english.Length && Current.Resources.GetString(trimmed, true) is { } label)
            return label + english[trimmed.Length..];
        foreach (var template in Current.messageTemplates)
        {
            var match = template.Pattern.Match(english);
            if (!match.Success) continue;
            var args = Enumerable.Range(0, template.ArgumentCount).Select(index =>
            {
                var value = match.Groups[$"arg{index}"].Value;
                if (template.Key == "Quick Setup complete. Dheacon is {0}." && index == 0) return (object)T(value);
                if (index == 0 && template.Key is "Ignored {0} because the player is not ready." or "Ignored {0} because expanded event commentary is disabled." or "Suppressed {0} by expanded event cooldown." or "Skipped {0}: speech is already busy." or "Skipped {0}: automatic trigger chance is 0%." or "Skipped {0}: automatic trigger chance roll missed at {1}%." or "Queued {0}: {1}." or "Failed to queue {0}: {1}." or "Queued {0}: {1}" or "Preparing {0}: {1}" or "Spoke {0}: {1}" or "Failed to speak {0}." or "No lines found for {0}; used fallback text.")
                    return (object)(Enum.TryParse<CommentaryCategory>(value, out _) ? T(value) : value);
                if (index == 1 && template.Key is "Queued {0}: {1}." or "Failed to queue {0}: {1}." or "Queued {0}: {1}" or "Preparing {0}: {1}" or "Spoke {0}: {1}")
                    return (object)CommentaryReason(match.Groups["arg0"].Value, value);
                if (index == 0 && template.Key is "Generated {0}: {1}" or "Not applicable for {0}." or "{0} speech failed; used Legacy SAPI fallback: {1}" or "{0} failed; used Legacy SAPI fallback: {1}")
                    return (object)(Enum.TryParse<TtsBackend>(value, out _) ? T(value) : value);
                if (template.Key == "Cache hit: {0}" && index == 0 || template.Key == "Generated {0}: {1}" && index == 1)
                    return (object)CacheFile(value);
                if (index == 0 && template.Key.StartsWith("Downloading Piper {0}: ", StringComparison.Ordinal))
                    return (object)(value is "runtime" or "model" or "config" or "model card" or "package" ? T(value) : value);
                if (index == 1 && template.Key == "Piper pitch shift failed ({0} semitones): {1}. Using unshifted WAV.")
                    return (object)WavWarning(value);
                return (object)value;
            }).ToArray();
            return string.Format(Current.Culture, Current.Resources.GetString(template.Key, false)!, args);
        }
        return english; // Names, game data and raw diagnostic values are consumer data.
    }
    internal static string F(string english,params object?[] args) => string.Format(Current.Culture,T(english),args);
    internal static string CommentaryReason(string category, string reason)
    {
        if (!Enum.TryParse<CommentaryCategory>(category, out var kind) || kind == CommentaryCategory.NearbyPlayerObservation) return reason;
        if (kind == CommentaryCategory.NearbyCrowdObservation)
        {
            var crowd = Regex.Match(reason, @"^(\d+) nearby players$");
            return crowd.Success ? F("{0} nearby players", crowd.Groups[1].Value) : reason;
        }
        if (kind == CommentaryCategory.LevelChange)
        {
            var level = Regex.Match(reason, @"^(.+) level (\d+)$");
            return level.Success ? F("{0} level {1}", level.Groups[1].Value, level.Groups[2].Value) : reason;
        }
        // Remaining producer reasons have authored leading words; names and numeric captures stay raw.
        return T(reason);
    }
    private static string CacheFile(string value)
    {
        var suffix = Regex.Match(value, @"^(.*?)( \(pitch [+-]?\d+(?:\.\d+)? st(?: (?:skipped|applied|warning))?\))$");
        return suffix.Success ? suffix.Groups[1].Value + T(suffix.Groups[2].Value) : value;
    }
    internal static string VoiceLabel(string value)
    {
        if (value is "Windows default" or "No Piper voice selected") return T(value);
        const string missing = " (not installed)";
        return value.EndsWith(missing, StringComparison.Ordinal) ? F("{0} (not installed)", value[..^missing.Length]) : value;
    }
    internal static string FollowerStatus(string value)
        => value is "Krangler follower IPC idle." or "Krangler preset list not loaded." or "Krangler preset list IPC returned a failure."
            or "Krangler preset list unavailable; free text fallback is active." or "Krangler preset export unavailable."
            or "No embedded Krangler preset was present." or "Imported embedded Krangler preset." or "Krangler rejected embedded preset import."
            or "Krangler preset import unavailable." or "Krangler Imaginary Fren IPC unavailable; speech continues without follower."
            or "Krangler Imaginary Fren IPC unavailable while disabling follower." or "Krangler follower status received."
            || Regex.IsMatch(value, @"^Loaded \d+ Krangler preset\(s\)\.$") ? T(value) : value;
    internal static string SpeechWarning(string value)
    {
        // This getter can also contain raw exception text. Translate only its authored envelopes.
        var combined = Regex.Match(value, @"^(Piper pitch shift failed \([+-]?\d+(?:\.\d+)? semitones\): .*\. Using unshifted WAV\.)(?: (.*))?$", RegexOptions.Singleline);
        if (combined.Success)
            return T(combined.Groups[1].Value) + (combined.Groups[2].Success ? " " + WavWarning(combined.Groups[2].Value) : "");
        return value.StartsWith("ModernWindows speech failed;", StringComparison.Ordinal)
            || value.StartsWith("PiperLocal speech failed;", StringComparison.Ordinal)
            || value.StartsWith("LegacySapi speech failed;", StringComparison.Ordinal) ? T(value) : WavWarning(value);
    }
    private static string WavWarning(string value)
    {
        foreach (var prefix in new[] { "original WAV is not usable: ", "shifted WAV is not usable: " })
            if (value.StartsWith(prefix, StringComparison.Ordinal)) return F(prefix + "{0}", WavWarning(value[prefix.Length..]));
        return value is "not a RIFF/WAVE file" or "fmt chunk is too small" or "missing fmt chunk" or "invalid fmt chunk" or "missing data chunk"
                or "16-bit PCM data is not sample aligned" or "24-bit PCM data is not sample aligned" or "32-bit PCM data is not sample aligned" or "float WAV data is not sample aligned"
                or "pitch writer produced no samples" or "shifted WAV was not created" or "shifted audio data did not differ from the original"
            || value.StartsWith("invalid WAV chunk '", StringComparison.Ordinal)
            || value.StartsWith("unsupported WAV format tag ", StringComparison.Ordinal)
            || value.StartsWith("unsupported PCM bit depth ", StringComparison.Ordinal)
            || value.StartsWith("unsupported IEEE float bit depth ", StringComparison.Ordinal)
            || value.StartsWith("shifted duration drifted from ", StringComparison.Ordinal) ? T(value) : value;
    }
    internal static string Interpolated(FormattableString text) => string.Format(Current.Culture, T(text.Format), text.GetArguments().Select(argument => argument is bool flag ? T(flag ? "Yes" : "No") : argument).ToArray());
    internal string Format(string key, params object?[] args) => string.Format(Culture, Resources.GetString(key, false) ?? key, args);
    internal string Label(string key) => Resources.GetString(key, false) ?? key;
    internal static IDisposable Font(UiFontRole role) => Current.pushFont(role);
    internal Scope Enter() => new(this);
    internal readonly struct Scope : IDisposable
    {
        private readonly UiText? previous;
        internal Scope(UiText value) { previous=current; current=value; }
        public void Dispose() => current=previous;
    }
    internal ushort[] GlyphRanges()
    {
        var chars=RequiredText.SelectMany(MaterialText.NativeGlyphText).Where(c=>!char.IsControl(c))
            .Concat(Enumerable.Range(0x20,0x024F-0x20+1).Select(i=>(char)i))
            .Concat(Enumerable.Range(0x0400,0x052F-0x0400+1).Select(i=>(char)i)).Concat("—").Distinct().Order().ToArray();
        var result=new List<ushort>();
        for(var index=0;index<chars.Length;index++)
        {
            var first=chars[index]; var last=first;
            while(index+1<chars.Length && chars[index+1]==last+1) last=chars[++index];
            result.Add(first); result.Add(last);
        }
        result.Add(0); return result.ToArray();
    }
    internal static IEnumerable<string> Values(ResourceSet set) => set.Cast<DictionaryEntry>().Select(e=>(string)e.Value!);
    public void Dispose() => manager.ReleaseAllResources();
}
