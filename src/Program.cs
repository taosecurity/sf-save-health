using SaveHealth;
using SaveHealth.Rules;

// sf-save-health: checks a Starfield save against the installed plugins and reports what looks broken. Read-only.
//   check <save.sfs> [--data <Data folder>|auto] [--plugins <plugins.txt>] [--json] [--limit N] [--lang en] [--redact] [--log <file>] [--verbose]
//   solo <StarfieldLogs-*.zip> | solo --before <a.sfs> --after <b.sfs>
//        [--data <Data folder>|auto] [--reports <folder>] [--limit N] [--lang en] [--redact] [--log <file>] [--verbose]
// Exit codes: 0 healthy (also --help and --version), 1 warnings or notes only, 2 problems, 3 couldn't check (bad
// arguments, unreadable save, unsupported save version).

// --help, -h or help anywhere (sf-save-health check x.sfs -h) shows the usage instead of running and exits 0 (since
// 0.7.4; 3 before). --version anywhere prints the version and exits 0. A line with no help word that is too short
// (a lone check or solo) or whose first word isn't a command (chek x.sfs --help) is still a usage error, exit 3.
bool wantsHelp = CommandLine.WantsHelp(args);
bool wantsVersion = !wantsHelp && CommandLine.WantsVersion(args);
if (args.Length < 2 || wantsHelp || wantsVersion)
{
    // An unknown first word (sf-save-health chek, or chek x.sfs --help) says so before the usage, as an unknown
    // command with more after it does. The first word is the user's own text (it can be a save path), so --redact
    // on the line hides it here too.
    if (Array.IndexOf(args, "--redact") >= 0) Redact.Enable();
    // Commands are case-insensitive, the help and version words are not (as options), so a lone --VERSION or HELP
    // is named as unknown before the usage instead of failing with the usage alone.
    bool unknownCommand = args.Length >= 1 && args[0].ToLowerInvariant() is not ("check" or "solo")
                          && args[0] is not ("-h" or "--help" or "help" or "--version");
    if (wantsVersion && !unknownCommand)
    {
        // The version is the only output, so a stdout that can't be written (a full disk behind a redirect) is
        // exit 3 with an error line, as for a report.
        try { Output.WriteReport($"sf-save-health {typeof(CommandLine).Assembly.GetName().Version?.ToString(3)}{Environment.NewLine}"); }
        catch (UserError e)
        {
            BestEffort(() => Console.Error.WriteLine($"error: {e.Message}\n  fix: {e.Fix}"));
            return 3;
        }
        return 0;
    }
    if (unknownCommand)
        BestEffort(() => Console.Error.WriteLine(Redact.Apply($"unknown command {args[0]}")));
    BestEffort(() =>
    {
        Console.Error.WriteLine($"usage: sf-save-health check <save.sfs> {CommandLine.UsageOptions("check")}");
        Console.Error.WriteLine("         (--plugins: also check the save against a load order, the game's Plugins.txt or a Vortex export of it)");
        Console.Error.WriteLine($"       sf-save-health solo <StarfieldLogs-*.zip> {CommandLine.UsageOptions("solo")}");
        Console.Error.WriteLine("       sf-save-health solo --before <a.sfs> --after <b.sfs> [the same options]");
        Console.Error.WriteLine("         (a before/after pair, from sf-log-collector's zip or two saves: prints only what is new in the after save)");
        Console.Error.WriteLine("       --data defaults to auto: the STARFIELD_DATA environment variable, then every Steam library, then Game Pass");
        Console.Error.WriteLine("         (<drive>:\\XboxGames\\Starfield\\Content\\Data); the first folder that holds Starfield.esm is used");
        Console.Error.WriteLine("       --reports defaults to .\\sf-save-health-reports; --redact hides the character, user and computer names and the profile folder");
        Console.Error.WriteLine("       (--help, -h or help anywhere on the line shows this text and exits 0; --version prints the version)");
    });
    return wantsHelp && !unknownCommand ? 0 : 3;
}

var cl = new CommandLine(args);
if (cl.Has("--redact")) Redact.Enable();
try
{
    // The command name first (an unknown command is the error to report), then options the command doesn't know or
    // that lack their value, then repeated options; all before --log is opened, so a refused run writes no log and a
    // second --log can't be silently ignored.
    string command = args[0].ToLowerInvariant();
    if (command is not ("check" or "solo"))
    {
        BestEffort(() => Console.Error.WriteLine(Redact.Apply($"unknown command {args[0]} (try: sf-save-health check <save.sfs>, or sf-save-health solo <StarfieldLogs-*.zip>)")));
        return 3;
    }
    cl.RefuseUnknown(command);
    cl.RefuseRepeats();
    Log.Open(cl.Option("--log"), cl.Has("--verbose"));
    switch (command)
    {
        case "check":
        {
            int limit = cl.Limit();
            var ctx = SaveContext.Open(args[1], DataFolder(), cl.Option("--lang") ?? "en");
            var results = Structure.Run(ctx, limit);
            if (cl.Option("--plugins") is { } pl) results.AddRange(ModList.Run(ctx, PluginList.Read(pl)));
            results.AddRange(QuestRules.Run(ctx, limit));
            results.AddRange(Bloat.Run(ctx, limit));
            Output.WriteReport(Redact.Apply(cl.Has("--json") ? Report.Json(ctx, results) + "\n" : Report.Text(ctx, results)));
            int code = Report.ExitCode(results);
            Log.Write($"done: {Report.Verdict(results)} exit {code}");
            return code;
        }
        case "solo":
        {
            int limit = cl.Limit();
            string? before = cl.Value("--before"), after = cl.Value("--after");
            bool pair = cl.Has("--before") || cl.Has("--after");
            if (pair && (before is null || after is null))
                throw new UserError("solo with saves needs both --before <a.sfs> and --after <b.sfs>", "pass the save made before the test and the one made after it");
            string dir = cl.Option("--reports") ?? Solo.ReportsDir();
            // Two saves are read: under --redact the log waits for both character names.
            Redact.ExpectPlayers(2);
            var o = pair ? Solo.RunPair(before!, after!, DataFolder(), cl.Option("--lang") ?? "en", limit)
                         : Solo.Run(args[1], DataFolder(), cl.Option("--lang") ?? "en", limit);
            string name = pair ? Path.GetFileNameWithoutExtension(after!) + "-solo" : Path.GetFileNameWithoutExtension(args[1]);
            return Output.FinishSolo(o, dir, Path.Combine(dir, Redact.FileName(name) + ".txt"));
        }
        default:
            return 3;   // not reached: an unknown command is refused above
    }
}
catch (UserError e)
{
    BestEffort(() => Console.Error.WriteLine(Redact.Apply($"error: {e.Message}\n  fix: {e.Fix}")));
    Log.Write($"error: {e.Message} (fix: {e.Fix})");
    return 3;
}
catch (OptionTableError e)
{
    // A programming slip (the code asks about an option the table lacks), not the save's fault: say so plainly.
    BestEffort(() => Console.Error.WriteLine($"error: internal error in sf-save-health: {e.Message}\n" +
        "  fix: this is a bug in sf-save-health, not in your save or mods; please report it with the command line you ran"));
    Log.Write($"error: {e}");
    return 3;
}
catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
{
    BestEffort(() => Console.Error.WriteLine(Redact.Apply($"error reading {(args[1].StartsWith("--") ? "a save" : args[1])}: {e.Message}\n  fix: if the file is in use (the game or Vortex), close it and try again; if the save is damaged, try another save")));
    Log.Write($"error: {e}");
    return 3;
}
catch (Exception e)
{
    // Anything else (a plugin Mutagen can't read, a save the parser trips on): still exit 3 with a readable message.
    BestEffort(() => Console.Error.WriteLine(Redact.Apply($"error: couldn't check {(args[1].StartsWith("--") ? "the saves" : args[1])} ({e.GetType().Name}: {e.Message})\n" +
        "  fix: run again with --log <file> --verbose to see which file it was reading; if a plugin is damaged, reinstall that mod")));
    Log.Write($"error: {e}");
    return 3;
}
finally { Log.Close(); }

// A console write that must not end the run or change its exit code: a closed or full stdout/stderr only loses
// that line. Used for the usage text, the error handlers' messages and solo's closing lines.
static void BestEffort(Action write) => Output.BestEffort(write);

// --data <folder> uses that folder; no --data or --data auto searches the usual places with SfsReader.DataFolder.Find.
// A bare --data (last, or followed by another option) is refused by RefuseUnknown before this runs (since 0.7.2;
// before, it meant auto).
string DataFolder()
{
    string? given = cl.Value("--data");
    if (given is not null && !given.Equals("auto", StringComparison.OrdinalIgnoreCase)) return given;
    return SfsReader.DataFolder.Find(Log.Write) ?? throw new UserError("couldn't find the Starfield Data folder (no Starfield.esm in the Steam or Game Pass folders)",
        "pass --data <your Starfield\\Data folder>, for example \"C:\\Program Files (x86)\\Steam\\steamapps\\common\\Starfield\\Data\" (Steam) " +
        "or \"C:\\XboxGames\\Starfield\\Content\\Data\" (Game Pass), or set the STARFIELD_DATA environment variable");
}

/// <summary>Prints the report on stdout. A failed write (a full disk behind a redirect, for example; .NET already
/// ignores a reader that has gone away) is the output's fault, not the save's, so it is a usage-style error (exit 3)
/// that says so instead of falling into the save-reading handler.</summary>
internal static class Output
{
    internal static void WriteReport(string text)
    {
        try { Console.Write(text); Console.Out.Flush(); }
        catch (Exception e) when (e is IOException or ObjectDisposedException or UnauthorizedAccessException)
        {
            throw new UserError($"couldn't write the report to the output: {e.Message}",
                "if you piped or redirected the output, make sure the program or file on the other end is still open, " +
                "that its drive has room and that you may write to it, then run again");
        }
    }

    /// <summary>The fix line for a solo report that couldn't be saved (a missing or read-only folder, no access
    /// rights, a full disk).</summary>
    internal const string SaveFix = "check that the folder's drive has room and that you may write there, or pass --reports with a folder you can write to " +
                                    "(the report above is complete; only the saved copy is missing)";

    /// <summary>solo's end: saves the full report to <paramref name="report"/> (in <paramref name="dir"/>) first, then prints the short one, so a
    /// failed stdout write (exit 3, from WriteReport) can't lose the saved copy. A failed save is reported on stderr
    /// but doesn't hide the verdict: the exit code stays 0, 1 or 2.</summary>
    internal static int FinishSolo(Solo.Outcome o, string dir, string report)
    {
        // Only the save itself is guarded here, so a later failure (stdout or the log) isn't reported as a failed save.
        string? saveError = null;
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(report, Redact.Apply(o.FullText));
            Log.Write($"solo: report saved to {report}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            saveError = e.Message;
            Log.Write($"solo: report not saved to {report}: {e.GetType().Name}: {e.Message}");
        }
        try { WriteReport(Redact.Apply(o.Text)); }
        catch (UserError e)
        {
            // The printed report is lost, so the error says where the full copy is (or that it couldn't be saved).
            throw new UserError(e.Message, e.Fix + (saveError is null
                ? $"; the full report was saved to {report}"
                : $"; the full report couldn't be saved to {report} either: {saveError}"));
        }
        // The check has finished and its report is printed, so nothing from here on may change the verdict's exit
        // code: Log.Write never throws (a failing log stops with a note), and the two console lines are best effort.
        if (saveError is null)
        {
            BestEffort(() => Console.WriteLine(Redact.Apply($"Report saved to {report}")));
            Log.Write($"done: {o.Verdict} exit {o.ExitCode}; report {report}");
        }
        else
        {
            BestEffort(() => Console.Error.WriteLine(Redact.Apply($"error: couldn't save the report to {report}: {saveError}\n  fix: {SaveFix}")));
            Log.Write($"done: {o.Verdict} exit {o.ExitCode}; report not saved: {saveError}");
        }
        return o.ExitCode;
    }

    /// <summary>A console write that must not end the run or change its exit code: a closed or full stdout/stderr
    /// only loses that line. The one copy, used by the usage text, the error handlers and solo's closing lines.</summary>
    internal static void BestEffort(Action write)
    {
        try { write(); }
        catch (Exception e) when (e is IOException or ObjectDisposedException or UnauthorizedAccessException) { }
    }
}
