using System.Text.RegularExpressions;

namespace SaveHealth;

/// <summary>--redact: hides what could identify the player in everything the tool writes (the text and JSON reports, the
/// saved solo report, error messages and the log), so a report can be posted publicly. It hides the character's name
/// (as text, and as the hex code the game puts in save file names), the Windows user name, the computer name and the
/// user profile folder. Finding counts, plugin names, form IDs and quest names stay, because they are what a report
/// is for.</summary>
public static class Redact
{
    public const string Player = "<player>";

    static bool on;
    static readonly List<(Regex Pattern, string With)> rules = [];

    /// <summary>Starfield save names: Save12_143C1F9E M 536F6C6F _ ... (player ID, sex, the name in hex, then the rest).
    /// Some saves have "_" instead of the sex letter (Save3_143C1F9E_536F6C6F_...). The hex name is hidden even before
    /// the save is read.</summary>
    static readonly Regex SaveNameHex = new(@"(?<=(?:Save|Autosave|Quicksave|Exitsave)\d*_[0-9A-Fa-f]{8}[MFmf_]?)[0-9A-Fa-f]{2,}(?=_)",
        RegexOptions.CultureInvariant);

    public static bool On => on;

    /// <summary>True once a character name has been read from a save (<see cref="AddPlayer"/>); the log waits for it.</summary>
    public static bool HasPlayer { get; private set; }

    /// <summary>Turns redaction on. Call before the log opens, so the log's first line is redacted too.</summary>
    public static void Enable()
    {
        on = true;
        foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable("HOME") })
            if (dir is { Length: > 3 })
            {
                string d = dir.TrimEnd('\\', '/');
                // As written, with JSON's doubled backslashes, and with forward slashes.
                foreach (var form in new[] { d, d.Replace("\\", "\\\\"), d.Replace('\\', '/') }.Distinct())
                    AddRule(new Regex(Regex.Escape(form), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                        OperatingSystem.IsWindows() ? "%USERPROFILE%" : "~");
            }
        AddWord(Environment.UserName, "<user>");
        AddWord(Environment.MachineName, "<computer>");
    }

    /// <summary>Hides a character name read from a save wherever it appears as a whole word with the same capitals
    /// (names of 3 or more characters). Shorter names are hidden only where the tool prints the name on purpose
    /// (<see cref="Name"/>), because replacing them everywhere would also hit ordinary words.</summary>
    public static void AddPlayer(string? name)
    {
        if (!on || string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        // Case-sensitive: a character called "Solo" must not turn the command word "solo" into <player>.
        // The name's hex code is hidden only inside save file names (SaveNameHex), so a form ID that happens to spell
        // the same hex digits stays.
        if (name.Length >= 3) AddWord(name, Player, RegexOptions.None);
        HasPlayer = true;
    }

    /// <summary>The name as the report prints it: <see cref="Player"/> when redacting.</summary>
    public static string Name(string name) => on ? Player : name;

    /// <summary>Applies every rule to <paramref name="text"/> (no change when redaction is off).</summary>
    public static string Apply(string text)
    {
        if (!on || string.IsNullOrEmpty(text)) return text;
        foreach (var (pattern, with) in rules) text = pattern.Replace(text, with);
        return SaveNameHex.Replace(text, "<name>");
    }

    /// <summary>A file name with redaction applied and the &lt;&gt; of the placeholders made safe for every file system.</summary>
    public static string FileName(string name)
    {
        if (!on) return name;
        var bad = Path.GetInvalidFileNameChars().Concat(['<', '>']).ToHashSet();
        return new string(Apply(name).Select(c => bad.Contains(c) ? '_' : c).ToArray());
    }

    /// <summary>Turns redaction off and forgets every rule (for the unit tests, which share this static state).</summary>
    internal static void Reset()
    {
        lock (rules) { rules.Clear(); on = false; HasPlayer = false; }
    }

    static void AddWord(string? word, string with, RegexOptions caseOption = RegexOptions.IgnoreCase)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Length < 2) return;
        AddRule(new Regex(@"(?<![\p{L}\p{N}_])" + Regex.Escape(word) + @"(?![\p{L}\p{N}_])", caseOption | RegexOptions.CultureInvariant), with);
    }

    static void AddRule(Regex pattern, string with)
    {
        lock (rules)
        {
            if (rules.Any(r => r.Pattern.ToString() == pattern.ToString())) return;
            // Applied in the order added: Enable adds the profile folders before the user name, so a whole profile
            // path becomes %USERPROFILE% rather than C:\Users\<user>.
            rules.Add((pattern, with));
        }
    }
}
