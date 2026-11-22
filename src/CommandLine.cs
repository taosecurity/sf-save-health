namespace SaveHealth;

/// <summary>One command-line option: its name, the placeholder for its value (null for an on/off switch), and the
/// commands that take it.</summary>
internal sealed record OptionSpec(string Name, string? Value, bool Check, bool Solo, string Example = "<value>")
{
    public bool TakesValue => Value is not null;
    public bool For(string command) => command == "check" ? Check : Solo;
    /// <summary>The option as the usage text shows it, for example [--limit N].</summary>
    public string Usage => Value is null ? $"[{Name}]" : $"[{Name} {Value}]";
}

/// <summary>The command line's options. Every argument that starts with -- is an option; the word after a valued
/// option is its value (a value never starts with --).</summary>
internal sealed class CommandLine(string[] args)
{
    public string[] Args { get; } = args;

    /// <summary>Every option, in the order the usage text lists them. The parser, the usage text and the tests all
    /// read this one table, so an option can't be known to one and missing from another.</summary>
    public static readonly OptionSpec[] Options =
    [
        new("--data", "<Starfield Data folder>", Check: true, Solo: true, Example: "auto"),
        new("--plugins", "<plugins.txt>", Check: true, Solo: false, Example: "Plugins.txt"),
        new("--reports", "<folder>", Check: false, Solo: true, Example: "reports"),
        new("--json", null, Check: true, Solo: false),
        new("--limit", "N", Check: true, Solo: true, Example: "50"),
        new("--lang", "en", Check: true, Solo: true, Example: "en"),
        new("--redact", null, Check: true, Solo: true),
        new("--log", "<file>", Check: true, Solo: true, Example: "sf-save-health.log"),
        new("--verbose", null, Check: true, Solo: true),
        // solo's pair form: shown in its own usage line, not in solo's option list.
        new("--before", "<a.sfs>", Check: false, Solo: true, Example: "a.sfs"),
        new("--after", "<b.sfs>", Check: false, Solo: true, Example: "b.sfs"),
    ];

    /// <summary>The options that take a value. Every other option is an on/off switch.</summary>
    public static readonly string[] Valued = Options.Where(o => o.TakesValue).Select(o => o.Name).ToArray();

    static OptionSpec? Find(string name) => Array.Find(Options, o => o.Name == name);

    /// <summary>The usage text's option list for a command, built from the table.</summary>
    public static string UsageOptions(string command) =>
        string.Join(' ', Options.Where(o => o.For(command) && o.Name is not ("--before" or "--after")).Select(o => o.Usage));

    public bool Has(string name)
    {
        Known(name, valued: false);
        return Array.IndexOf(Args, name) >= 0;
    }

    /// <summary>The program may only ask about options in the table (and Option/Value only about valued ones), so a
    /// new option can't be read without being in the table that the check, the usage text and the tests use.</summary>
    static void Known(string name, bool valued)
    {
        var o = Find(name) ?? throw new OptionTableError($"{name} is not in CommandLine.Options");
        if (valued && !o.TakesValue) throw new OptionTableError($"{name} is a switch, not an option with a value");
    }

    /// <summary>Refuses a command line the command can't use, before anything runs: an option the command doesn't
    /// know (a mistyped one, or one that belongs to the other command) and a valued option with no value after it
    /// (last, or followed by another --option). Options are case-sensitive. Then the save or zip's place: check and
    /// solo need it right after the command, and solo takes either a zip or --before/--after, not both. Other words
    /// that don't start with -- (the values, extra words) aren't checked here.</summary>
    public void RefuseUnknown(string command)
    {
        string other = command == "check" ? "solo" : "check";
        for (int i = 1; i < Args.Length; i++)
        {
            string a = Args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal)) continue;
            var o = Find(a);
            if (o is null)
                throw new UserError($"{command} doesn't know {a}", Closest(a, command) is { } near
                    ? $"use {near} (options are case-sensitive; sf-save-health --help lists them)"
                    : Closest(a, other) is { } theirs
                        ? $"{theirs} works only with {other}; sf-save-health --help lists {command}'s options"
                        : "check the spelling; sf-save-health --help lists the options");
            if (!o.For(command))
                throw new UserError($"{command} doesn't know {a}",
                    $"{a} works only with {other}; sf-save-health --help lists {command}'s options");
            if (o.TakesValue)
            {
                if (i + 1 >= Args.Length || Args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw NeedsValue(o);
                i++;   // the value
            }
        }
        bool first = Args.Length > 1 && !Args[1].StartsWith("--", StringComparison.Ordinal);
        if (command == "check" && !first)
            throw new UserError("check needs the save right after the command", "put the save first, for example sf-save-health check Save3.sfs --json");
        if (command == "solo")
        {
            bool pair = Array.IndexOf(Args, "--before") >= 0 || Array.IndexOf(Args, "--after") >= 0;
            if (pair && first)
                throw new UserError($"solo got both {Args[1]} and --before/--after", "give either sf-log-collector's zip or --before <a.sfs> --after <b.sfs>, not both");
            if (!pair && !first)
                throw new UserError("solo needs the zip right after the command", "put the zip first, for example sf-save-health solo StarfieldLogs-1.zip, or pass --before <a.sfs> --after <b.sfs>");
        }
    }

    /// <summary>The command's option nearest to a mistyped one: the same name in other letter case, or one at most
    /// two letters away. Null when nothing is that close.</summary>
    internal static string? Closest(string given, string command)
    {
        var mine = Options.Where(o => o.For(command)).Select(o => o.Name).ToArray();
        if (Array.Find(mine, n => n.Equals(given, StringComparison.OrdinalIgnoreCase)) is { } sameName) return sameName;
        var best = mine.Select(n => (Name: n, Distance: Distance(given, n))).OrderBy(x => x.Distance).First();
        return best.Distance <= 2 ? best.Name : null;
    }

    /// <summary>Levenshtein distance: the number of single-letter edits from a to b.</summary>
    static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            int diag = row[0];
            row[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int up = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diag + (a[i - 1] == b[j - 1] ? 0 : 1));
                diag = up;
            }
        }
        return row[b.Length];
    }

    /// <summary>An option that takes a value, given more than once, is a usage error, so a second value is never
    /// silently ignored (before 0.7.1 the first one was used). A switch given twice (--verbose --verbose) means the
    /// same as once and is accepted.</summary>
    public void RefuseRepeats()
    {
        var repeated = Args.Where(a => Array.IndexOf(Valued, a) >= 0)
            .GroupBy(a => a, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (repeated is not null)
            throw new UserError($"{repeated.Key} is given {repeated.Count()} times", $"give {repeated.Key} only once");
    }

    /// <summary>The value after an option, or null when the option isn't given. An option given without a value (last,
    /// or followed by another --option) is a usage error. In the program RefuseUnknown has already refused that case,
    /// so this check only guards direct callers (the tests); its message matches RefuseUnknown's.</summary>
    public string? Option(string name)
    {
        Known(name, valued: true);
        if (!Has(name)) return null;
        return Value(name) ?? throw NeedsValue(Find(name)!);
    }

    /// <summary>The one "needs a value" error, so RefuseUnknown and Option can't drift apart.</summary>
    static UserError NeedsValue(OptionSpec o) => new($"{o.Name} needs a value", $"put the value right after {o.Name}, for example {o.Name} {o.Example}");

    /// <summary>The value after an option, or null when the option is missing or has no value.</summary>
    public string? Value(string name)
    {
        Known(name, valued: true);
        int i = Array.IndexOf(Args, name);
        return i >= 0 && i + 1 < Args.Length && !Args[i + 1].StartsWith("--", StringComparison.Ordinal) ? Args[i + 1] : null;
    }

    /// <summary>--limit N: how many examples each finding lists (default 10). Anything but a whole number from 0 up is
    /// a usage error (exit 3).</summary>
    public int Limit()
    {
        string? given = Option("--limit");
        if (given is null) return 10;
        if (!int.TryParse(given, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n))
            throw given.Length > 0 && given.All(char.IsAsciiDigit)
                ? new UserError($"--limit {given} is too large (at most {int.MaxValue})", "pass a smaller number, for example --limit 50")
                : new UserError($"--limit {given} is not a number from 0 up", "pass --limit with a number of examples, for example --limit 50");
        return n;
    }
}
