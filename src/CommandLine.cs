namespace SaveHealth;

/// <summary>The command line's options. Every argument that starts with -- is an option; the word after a valued
/// option is its value (a value never starts with --).</summary>
internal sealed class CommandLine(string[] args)
{
    public string[] Args { get; } = args;

    public bool Has(string name) => Array.IndexOf(Args, name) >= 0;

    /// <summary>The options that take a value. Every other option is an on/off switch.</summary>
    public static readonly string[] Valued = ["--log", "--limit", "--lang", "--plugins", "--reports", "--data", "--before", "--after"];

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
    /// or followed by another --option) is a usage error.</summary>
    public string? Option(string name)
    {
        if (!Has(name)) return null;
        return Value(name) ?? throw new UserError($"{name} needs a value", $"put the value right after {name}, for example {name} {Example(name)}");
    }

    /// <summary>The value after an option, or null when the option is missing or has no value.</summary>
    public string? Value(string name)
    {
        int i = Array.IndexOf(Args, name);
        return i >= 0 && i + 1 < Args.Length && !Args[i + 1].StartsWith("--", StringComparison.Ordinal) ? Args[i + 1] : null;
    }

    static string Example(string name) => name switch
    {
        "--log" => "sf-save-health.log",
        "--plugins" => "Plugins.txt",
        "--reports" => "reports",
        "--lang" => "en",
        "--limit" => "50",
        _ => "<value>",
    };

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
