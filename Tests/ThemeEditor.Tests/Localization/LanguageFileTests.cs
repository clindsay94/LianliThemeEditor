using System.Text.Json;
using System.Text.RegularExpressions;

namespace ThemeEditorCSharp.Tests.Localization;

public sealed partial class LanguageFileTests
{
    // Missing keys fall back to English at runtime, so they're tolerated but may only shrink.
    // Lower these numbers when translations land; never raise them.
    private static readonly Dictionary<string, int> MissingKeyBaseline = new()
    {
        ["de"] = 69,
        ["fr"] = 69,
        ["ko"] = 69,
        ["ru"] = 69,
        ["tr"] = 16,
        ["zh"] = 69,
    };

    private static readonly string LangDirectory = Path.Combine(AppContext.BaseDirectory, "lang");

    public static TheoryData<string> Locales()
    {
        var data = new TheoryData<string>();
        foreach (var locale in MissingKeyBaseline.Keys) data.Add(locale);
        return data;
    }

    [Fact]
    public void Every_locale_file_has_a_baseline_entry()
    {
        var onDisk = Directory.GetFiles(LangDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name != "en")
            .Order();

        Assert.Equal(MissingKeyBaseline.Keys.Order(), onDisk);
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public void Locale_has_no_keys_that_english_lacks(string locale)
    {
        var english = Load("en");
        var orphans = Load(locale).Keys.Where(key => !english.ContainsKey(key)).ToList();

        Assert.Empty(orphans);
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public void Locale_placeholders_match_english(string locale)
    {
        var english = Load("en");
        var mismatches = Load(locale)
            .Where(pair => english.TryGetValue(pair.Key, out var en) && Placeholders(en) != Placeholders(pair.Value))
            .Select(pair => $"{pair.Key}: en={Placeholders(english[pair.Key])} {locale}={Placeholders(pair.Value)}")
            .ToList();

        Assert.Empty(mismatches);
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public void Locale_missing_key_count_does_not_grow(string locale)
    {
        var translated = Load(locale);
        var missing = Load("en").Keys.Count(key => !translated.ContainsKey(key));

        Assert.True(missing <= MissingKeyBaseline[locale],
            $"{locale}.json is missing {missing} keys, baseline is {MissingKeyBaseline[locale]}. Translate the new keys.");
    }

    private static Dictionary<string, string> Load(string locale)
    {
        using var stream = File.OpenRead(Path.Combine(LangDirectory, $"{locale}.json"));
        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        Flatten(document.RootElement, "", result);
        return result;
    }

    private static void Flatten(JsonElement element, string prefix, Dictionary<string, string> into)
    {
        foreach (var property in element.EnumerateObject())
        {
            var key = prefix + property.Name;
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                Flatten(property.Value, key + ".", into);
            }
            else
            {
                Assert.Equal(JsonValueKind.String, property.Value.ValueKind);
                into[key] = property.Value.GetString()!;
            }
        }
    }

    private static string Placeholders(string text) =>
        string.Join(",", PlaceholderPattern().Matches(text).Select(m => m.Groups[1].Value).Distinct().Order());

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();
}
