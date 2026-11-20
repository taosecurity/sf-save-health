using SaveHealth;
using SaveHealth.Rules;

// sf-save-health: checks a Starfield save against the installed plugins and reports what looks broken. Read-only.
//   check <save.sfs> [--data <Data folder>|auto] [--plugins <plugins.txt>] [--json] [--limit N] [--lang en] [--redact] [--log <file>] [--verbose]
//   solo <StarfieldLogs-*.zip> | solo --before <a.sfs> --after <b.sfs>
//        [--data <Data folder>|auto] [--reports <folder>] [--limit N] [--lang en] [--redact] [--log <file>] [--verbose]
// Exit codes: 0 healthy, 1 warnings or notes only, 2 problems, 3 couldn't check (bad arguments, unreadable save,
// unsupported save version).

if (args.Length < 2 || args[0] is "-h" or "--help" or "help")
{
    Console.Error.WriteLine("usage: sf-save-health check <save.sfs> [--data <Starfield Data folder>] [--plugins <plugins.txt>] [--json] [--limit N] [--lang en] [--redact] [--log <file>] [--verbose]");
    Console.Error.WriteLine("         (--plugins: also check the save against a load order, the game's Plugins.txt or a Vortex export of it)");
    Console.Error.WriteLine("       sf-save-health solo <StarfieldLogs-*.zip> [--data <Starfield Data folder>] [--reports <folder>] [--limit N] [--lang en] [--redact] [--log <file>] [--verbose]");
    Console.Error.WriteLine("       sf-save-health solo --before <a.sfs> --after <b.sfs> [the same options]");
    Console.Error.WriteLine("         (a before/after pair, from sf-log-collector's zip or two saves: prints only what is new in the after save)");
    Console.Error.WriteLine("       --data defaults to auto: the STARFIELD_DATA environment variable, then every Steam library, then Game Pass");
    Console.Error.WriteLine("         (<drive>:\\XboxGames\\Starfield\\Content\\Data); the first folder that holds Starfield.esm is used");
    Console.Error.WriteLine("       --reports defaults to .\\sf-save-health-reports; --redact hides the character, user and computer names and the profile folder");
    return 3;
}

if (args.Contains("--redact")) Redact.Enable();
try
{
    Log.Open(Option("--log"), args.Contains("--verbose"));
    switch (args[0].ToLowerInvariant())
    {
        case "check":
        {
            int limit = Limit();
            var ctx = SaveContext.Open(args[1], DataFolder(), Option("--lang") ?? "en");
            var results = Structure.Run(ctx, limit);
            if (Option("--plugins") is { } pl) results.AddRange(ModList.Run(ctx, PluginList.Read(pl)));
            results.AddRange(QuestRules.Run(ctx, limit));
            results.AddRange(Bloat.Run(ctx, limit));
            Console.Write(Redact.Apply(args.Contains("--json") ? Report.Json(ctx, results) + "\n" : Report.Text(ctx, results)));
            int code = Report.ExitCode(results);
            Log.Write($"done: {Report.Verdict(results)} exit {code}");
            return code;
        }
        case "solo":
        {
            int limit = Limit();
            string? before = Value("--before"), after = Value("--after");
            bool pair = args.Contains("--before") || args.Contains("--after");
            if (pair && (before is null || after is null))
                throw new UserError("solo with saves needs both --before <a.sfs> and --after <b.sfs>", "pass the save made before the test and the one made after it");
            string dir = Option("--reports") ?? Solo.ReportsDir();
            // Two saves are read: under --redact the log waits for both character names.
            Redact.ExpectPlayers(2);
            var o = pair ? Solo.RunPair(before!, after!, DataFolder(), Option("--lang") ?? "en", limit)
                         : Solo.Run(args[1], DataFolder(), Option("--lang") ?? "en", limit);
            Console.Write(Redact.Apply(o.Text));
            string name = pair ? Path.GetFileNameWithoutExtension(after!) + "-solo" : Path.GetFileNameWithoutExtension(args[1]);
            string report = Path.Combine(dir, Redact.FileName(name) + ".txt");
            // The check ran and its report is printed above, so a failed saved copy is reported on stderr
            // but doesn't hide the verdict: the exit code stays 0, 1 or 2.
            // Only the save itself is guarded here, so a later failure (stdout or the log) isn't reported as a failed save.
            string? saveError = null;
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(report, Redact.Apply(o.FullText));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                saveError = e.Message;
            }
            if (saveError is null)
            {
                Console.WriteLine(Redact.Apply($"Report saved to {report}"));
                Log.Write($"done: {o.Verdict} exit {o.ExitCode}; report {report}");
            }
            else
            {
                Console.Error.WriteLine(Redact.Apply($"error: couldn't save the report to {report}: {saveError}\n  fix: pass --reports with a folder you can write to (the report above is complete; only the saved copy is missing)"));
                Log.Write($"done: {o.Verdict} exit {o.ExitCode}; report not saved: {saveError}");
            }
            return o.ExitCode;
        }
        default:
            Console.Error.WriteLine($"unknown command {args[0]} (try: sf-save-health check <save.sfs>, or sf-save-health solo <StarfieldLogs-*.zip>)");
            return 3;
    }
}
catch (UserError e)
{
    Console.Error.WriteLine(Redact.Apply($"error: {e.Message}\n  fix: {e.Fix}"));
    Log.Write($"error: {e.Message} (fix: {e.Fix})");
    return 3;
}
catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(Redact.Apply($"error reading {(args[1].StartsWith("--") ? "a save" : args[1])}: {e.Message}\n  fix: if the file is in use (the game or Vortex), close it and try again; if the save is damaged, try another save"));
    Log.Write($"error: {e}");
    return 3;
}
catch (Exception e)
{
    // Anything else (a plugin Mutagen can't read, a save the parser trips on): still exit 3 with a readable message.
    Console.Error.WriteLine(Redact.Apply($"error: couldn't check {(args[1].StartsWith("--") ? "the saves" : args[1])} ({e.GetType().Name}: {e.Message})\n" +
        "  fix: run again with --log <file> --verbose to see which file it was reading; if a plugin is damaged, reinstall that mod"));
    Log.Write($"error: {e}");
    return 3;
}
finally { Log.Close(); }

// The value after an option, or null when the option isn't given. An option given without a value (last, or followed by
// another --option) is a usage error.
string? Option(string name)
{
    int i = Array.IndexOf(args, name);
    if (i < 0) return null;
    return Value(name) ?? throw new UserError($"{name} needs a value", $"put the value right after {name}, for example {name} {Example(name)}");
}

// The value after an option, or null when the option is missing or has no value.
string? Value(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
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

// --limit N: how many examples each finding lists (default 10). Anything but a whole number from 0 up is a usage error (exit 3).
int Limit()
{
    string? given = Option("--limit");
    if (given is null) return 10;
    if (!int.TryParse(given, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n))
        throw given.Length > 0 && given.All(char.IsAsciiDigit)
            ? new UserError($"--limit {given} is too large (at most {int.MaxValue})", "pass a smaller number, for example --limit 50")
            : new UserError($"--limit {given} is not a number from 0 up", "pass --limit with a number of examples, for example --limit 50");
    return n;
}

// --data <folder> uses that folder; no --data, a bare --data (last, or followed by another option) or --data auto
// searches the usual places with SfsReader.DataFolder.Find.
string DataFolder()
{
    string? given = Value("--data");
    if (given is not null && !given.Equals("auto", StringComparison.OrdinalIgnoreCase)) return given;
    return SfsReader.DataFolder.Find(Log.Write) ?? throw new UserError("couldn't find the Starfield Data folder (no Starfield.esm in the Steam or Game Pass folders)",
        "pass --data <your Starfield\\Data folder>, for example \"C:\\Program Files (x86)\\Steam\\steamapps\\common\\Starfield\\Data\" (Steam) " +
        "or \"C:\\XboxGames\\Starfield\\Content\\Data\" (Game Pass), or set the STARFIELD_DATA environment variable");
}
