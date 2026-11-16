using System.Text.RegularExpressions;
using SmaetRec.Core;
using Xunit;

namespace SmaetRec.Tests;

public class LocalizationUsageTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SmaetRec.sln")) && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }

    private static IEnumerable<string> Sources(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    private static int Placeholders(string text) =>
        Regex.Matches(text.Replace("{{", "").Replace("}}", ""), @"\{(\d+)[^}]*\}").Select(m => int.Parse(m.Groups[1].Value) + 1).DefaultIfEmpty(0).Max();

    [Fact]
    public void Every_key_used_in_code_exists()
    {
        var missing = new SortedSet<string>();
        foreach (var file in Sources("*.cs"))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"Loc\.T\(\s*""([A-Za-z0-9_]+)"""))
                if (!Loc.All.ContainsKey(m.Groups[1].Value)) missing.Add($"{m.Groups[1].Value} ({Path.GetFileName(file)})");
        Assert.True(missing.Count == 0, "Text keys used but not defined: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_key_used_in_windows_exists()
    {
        var missing = new SortedSet<string>();
        foreach (var file in Sources("*.xaml"))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\{Binding\s+(?:Path=)?\[([A-Za-z0-9_]+)\]"))
                if (!Loc.All.ContainsKey(m.Groups[1].Value)) missing.Add($"{m.Groups[1].Value} ({Path.GetFileName(file)})");
        Assert.True(missing.Count == 0, "Text keys used but not defined: " + string.Join(", ", missing));
    }

    [Fact]
    public void Both_languages_are_filled_in_and_take_the_same_values()
    {
        foreach (var (key, (ar, en)) in Loc.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(ar), key + " has no Arabic text");
            Assert.False(string.IsNullOrWhiteSpace(en), key + " has no English text");
            Assert.True(Placeholders(ar) == Placeholders(en), $"{key}: Arabic and English take a different number of values");
        }
    }

    [Fact]
    public void Calls_pass_as_many_values_as_the_text_needs()
    {
        var wrong = new SortedSet<string>();
        foreach (var file in Sources("*.cs"))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"Loc\.T\(\s*""([A-Za-z0-9_]+)""\s*\)"))
                if (Loc.All.TryGetValue(m.Groups[1].Value, out var v) && Placeholders(v.En) > 0)
                    wrong.Add($"{m.Groups[1].Value} ({Path.GetFileName(file)})");
        Assert.True(wrong.Count == 0, "Text needs values but is used without any: " + string.Join(", ", wrong));
    }

    [Fact]
    public void A_wrong_value_count_shows_the_text_instead_of_throwing()
    {
        string key = Loc.All.First(p => Placeholders(p.Value.En) >= 1).Key;
        string text = Loc.T(key, Array.Empty<object>());
        Assert.False(string.IsNullOrEmpty(text));
    }
}
