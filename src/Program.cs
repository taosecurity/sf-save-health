using SaveHealth;
using SaveHealth.Rules;

// sf-save-health: checks a Starfield save against the installed plugins and reports what looks broken. Read-only.
//   check <save.sfs> [--data <Data folder>|auto] [--plugins <plugins.txt>] [--json] [--limit N] [--lang en] [--redact] [--log <file>] [--verbose]
//   solo <StarfieldLogs-*.zip> | solo --before <a.sfs> --after <b.sfs>
//        [--data <Data folder>|auto] [--reports <folder>] [--limit N] [--redact] [--log <file>] [--verbose]
// Exit codes: 0 healthy, 1 warnings or notes only, 2 problems, 3 couldn't check (bad arguments, unreadable save,
// unsupported save version).

if (args.Length < 2 || args[0] is "-h" or "--help" or "help")
{
    Console.Error.WriteLine("usage: sf-save-health check <save.sfs> [--data <Starfield Data folder>] [--plugins <plugins.txt>] [--json] [--limit N] [--redact] [--log <file>] [--verbose]");
    Console.Error.WriteLine("         (--plugins: also check the save against a load order, the game's Plugins.txt or a Vortex export of it)");
    Console.Error.WriteLine("       sf-save-health solo <StarfieldLogs-*.zip> [--data <Starfield Data folder>] [--reports <folder>] [--limit N] [--redact] [--log <file>] [--verbose]");
    Console.Error.WriteLine("       sf-save-health solo --before <a.sfs> --after <b.sfs> [the same options]");
    Console.Error.WriteLine("         (a before/after pair, from sf-log-collector's zip or two saves: prints only what is new in the after save)");
    Console.Error.WriteLine("       --data defaults to auto: the STARFIELD_DATA environment variable, then every Steam library, then Game Pass");
    Console.Error.WriteLine("         (<drive>:\\XboxGames\\Starfield\\Content\\Data); the first folder that holds Starfield.esm is used");
    Console.Error.WriteLine("       --reports defaults to .\\sf-save-health-reports; --redact hides the character, user and computer names and the profile folder");
    return 3;
}

if (args.Contains("--redact")) Redact.Enable();
Log.Open(Option("--log"), args.Contains("--verbose"));
try
{
    switch (args[0].ToLowerInvariant())
    {
        case "check":
        {
            var ctx = SaveContext.Open(args[1], DataFolder(), Option("--lang") ?? "en");
            int limit = Option("--limit") is { } l ? int.Parse(l) : 10;
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
            int limit = Option("--limit") is { } l ? int.Parse(l) : 10;
            string? before = Option("--before"), after = Option("--after");
            bool pair = args.Contains("--before") || args.Contains("--after");
            if (pair && (before is null || after is null || before.StartsWith("--") || after.StartsWith("--")))
                throw new UserError("solo with saves needs both --before <a.sfs> and --after <b.sfs>", "pass the save made before the test and the one made after it");
            var o = pair ? Solo.RunPair(before!, after!, DataFolder(), Option("--lang") ?? "en", limit)
                         : Solo.Run(args[1], DataFolder(), Option("--lang") ?? "en", limit);
            Console.Write(Redact.Apply(o.Text));
            string dir = Option("--reports") ?? Solo.ReportsDir();
            Directory.CreateDirectory(dir);
            string name = pair ? Path.GetFileNameWithoutExtension(after!) + "-solo" : Path.GetFileNameWithoutExtension(args[1]);
            string report = Path.Combine(dir, Redact.FileName(name) + ".txt");
            File.WriteAllText(report, Redact.Apply(o.FullText));
            Console.WriteLine(Redact.Apply($"Report saved to {report}"));
            Log.Write($"done: {o.Verdict} exit {o.ExitCode}; report {report}");
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
    Console.Error.WriteLine(Redact.Apply($"error reading {(args[1].StartsWith("--") ? "a save" : args[1])}:{e.Message}\n  fix: if the file is in use (the game or Vortex), close it and try again; if the save is damaged, try another save"));
    Log.Write($"error: {e}");
    return 3;
}
finally { Log.Close(); }

string? Option(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// --data <folder> uses that folder; no --data, a bare --data (last, or followed by another option) or --data auto
// searches the usual places with SfsReader.DataFolder.Find.
string DataFolder()
{
    string? given = Option("--data");
    if (given is not null && !given.StartsWith("--") && !given.Equals("auto", StringComparison.OrdinalIgnoreCase)) return given;
    return SfsReader.DataFolder.Find(Log.Write) ?? throw new UserError("couldn't find the Starfield Data folder (no Starfield.esm in the Steam or Game Pass folders)",
        "pass --data <your Starfield\\Data folder>, for example \"C:\\Program Files (x86)\\Steam\\steamapps\\common\\Starfield\\Data\" (Steam) " +
        "or \"C:\\XboxGames\\Starfield\\Content\\Data\" (Game Pass), or set the STARFIELD_DATA environment variable");
}
