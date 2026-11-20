using System.IO.Compression;
using System.Text;
using SaveHealth.Rules;

namespace SaveHealth;

/// <summary>sf-save-health solo: the before and after saves of a solo test, read from the log collector's zip
/// (saves\before-* and saves\after-*). Runs every rule on both and reports only what is new in the after save.</summary>
public static class Solo
{
    sealed record Side(SaveContext Ctx, List<RuleResult> Results, List<QuestRules.Snapshot> Quests, Bloat.Counts Bloat);

    /// <summary>Text = what the screen shows; FullText = the saved report (every quest change).</summary>
    public sealed record Outcome(string Text, string FullText, int ExitCode, string Verdict);

    public static Outcome Run(string zipPath, string dataDir, string language, int limit)
    {
        if (!File.Exists(zipPath)) throw new UserError($"no zip {zipPath}",
            "pass the zip that sf-log-collector made with its p option (StarfieldLogs-*.zip), or two saves with --before <a.sfs> --after <b.sfs>");
        string tmp = Path.Combine(Path.GetTempPath(), "sf-save-health-solo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            var (before, after) = Extract(zipPath, tmp);
            var a = Check(before, dataDir, language, limit);
            var b = Check(after, dataDir, language, limit);
            return Compare(Path.GetFileName(zipPath), a, b, limit);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (IOException e) { Log.Write($"could not remove {tmp}: {e.Message}"); }
        }
    }

    /// <summary>solo --before &lt;a.sfs&gt; --after &lt;b.sfs&gt;: the same comparison for two saves given directly, with the
    /// same refusals as the zip (no exit saves, not the same save twice, same character, after not older).
    /// The saves are only read.</summary>
    public static Outcome RunPair(string beforePath, string afterPath, string dataDir, string language, int limit)
    {
        foreach (var (p, which) in new[] { (beforePath, "--before"), (afterPath, "--after") })
            if (!File.Exists(p)) throw new UserError($"no save file {p} ({which})", "check the path; saves end in .sfs");
        string nb = Path.GetFileName(beforePath), na = Path.GetFileName(afterPath);
        Log.Write($"solo: before {beforePath}, after {afterPath}");
        foreach (var n in new[] { nb, na })
            if (QuestRules.SaveType(n) == "exit save")
                throw new UserError($"{n} is an exit save; solo compares two hard saves made on purpose",
                    "the game writes an exit save every time you quit, so it isn't a fixed 'before' or 'after' point. Make a hard save (Save menu) before and after the test, and pass those two");
        // Paths differ only in case on Windows and macOS for the same file; on Linux they are two files.
        var paths = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (Path.GetFullPath(beforePath).Equals(Path.GetFullPath(afterPath), paths) || SameBytes(beforePath, afterPath))
            throw new UserError($"the before and after saves are the same save ({nb})", "make a new hard save after the test, and pass it with --after");
        var a = Check(beforePath, dataDir, language, limit);
        var b = Check(afterPath, dataDir, language, limit);
        return Compare($"{nb} -> {na}", a, b, limit);
    }

    static bool SameBytes(string x, string y)
    {
        if (new FileInfo(x).Length != new FileInfo(y).Length) return false;
        using var fx = File.OpenRead(x);
        using var fy = File.OpenRead(y);
        var (bx, by) = (new byte[1 << 16], new byte[1 << 16]);
        while (true)
        {
            int n = fx.ReadAtLeast(bx, bx.Length, throwOnEndOfStream: false);
            int m = fy.ReadAtLeast(by, by.Length, throwOnEndOfStream: false);
            if (n != m || !bx.AsSpan(0, n).SequenceEqual(by.AsSpan(0, m))) return false;
            if (n == 0) return true;
        }
    }

    /// <summary>Copies the two saves out of the zip into a temporary folder (the zip is only read). The collector writes
    /// zip paths with backslashes, so both separators are accepted.</summary>
    static (string Before, string After) Extract(string zipPath, string tmp)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        List<ZipArchiveEntry> Find(string prefix) => zip.Entries.Where(e =>
        {
            string n = e.FullName.Replace('\\', '/');
            return n.StartsWith("saves/" + prefix, StringComparison.OrdinalIgnoreCase) && n.EndsWith(".sfs", StringComparison.OrdinalIgnoreCase);
        }).ToList();
        var before = Find("before-");
        var after = Find("after-");
        Log.Write($"solo: {zipPath}: {zip.Entries.Count} entries, {before.Count} before save(s), {after.Count} after save(s)");
        if (before.Count != 1 || after.Count != 1)
            throw new UserError($"{Path.GetFileName(zipPath)} holds {before.Count} before save(s) and {after.Count} after save(s) under saves\\; solo needs exactly one of each",
                "make the log package with the collector's p option (a before/after pair), not y (newest save only)");
        string nb = Name(before[0]), na = Name(after[0]);
        foreach (var n in new[] { nb, na })
            if (QuestRules.SaveType(n) == "exit save")
                throw new UserError($"{n} is an exit save; solo compares two hard saves made on purpose",
                    "the game writes an exit save every time you quit, so it isn't a fixed 'before' or 'after' point. Make a hard save (Save menu) before and after the test, then run the collector's p option again");
        if (nb.Equals(na, StringComparison.OrdinalIgnoreCase) || before[0].Crc32 == after[0].Crc32 && before[0].Length == after[0].Length)
            throw new UserError($"the before and after saves are the same save ({nb})", "make a new hard save after the test, then run the collector's p option again");
        string Out(ZipArchiveEntry e) { string p = Path.Combine(tmp, Path.GetFileName(e.FullName.Replace('\\', '/'))); e.ExtractToFile(p); return p; }
        return (Out(before[0]), Out(after[0]));
    }

    static string Name(ZipArchiveEntry e) => Path.GetFileName(e.FullName.Replace('\\', '/'));

    static Side Check(string path, string dataDir, string language, int limit)
    {
        var ctx = SaveContext.Open(path, dataDir, language);
        var quests = new List<QuestRules.Snapshot>();
        var results = Structure.Run(ctx, limit);
        results.AddRange(QuestRules.Run(ctx, limit, quests));
        results.AddRange(Rules.Bloat.Run(ctx, limit));
        return new Side(ctx, results, quests, Rules.Bloat.Measure(ctx));
    }

    static Outcome Compare(string label, Side a, Side b, int limit)
    {
        var (ga, gb) = (a.Ctx.Save, b.Ctx.Save);
        if (!string.Equals(ga.PlayerName, gb.PlayerName, StringComparison.Ordinal))
            throw new UserError(Redact.On ? "the before and after saves are different characters" : $"the before save is {ga.PlayerName}'s and the after save is {gb.PlayerName}'s",
                "both saves must be the same character; make the pair again");
        if (gb.SaveTimeUtc < ga.SaveTimeUtc)
            throw new UserError($"the after save ({gb.SaveTimeUtc:yyyy-MM-dd HH:mm} UTC) is older than the before save ({ga.SaveTimeUtc:yyyy-MM-dd HH:mm} UTC)", "check which save is which and make the pair again");

        var sb = new StringBuilder();
        sb.AppendLine($"sf-save-health {typeof(Solo).Assembly.GetName().Version?.ToString(3)} solo: {label}");
        sb.AppendLine($"  before: {Path.GetFileName(a.Ctx.Path)}  level {ga.PlayerLevel}, {ga.Location}, playtime {ga.PlaytimeText}, saved {ga.SaveTimeUtc:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"  after:  {Path.GetFileName(b.Ctx.Path)}  level {gb.PlayerLevel}, {gb.Location}, playtime {gb.PlaytimeText}, saved {gb.SaveTimeUtc:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"  {Redact.Name(gb.PlayerName)}; checked against {b.Ctx.Game.DataDir}");

        // Profile check first, so a wrong Vortex profile isn't mistaken for a broken save.
        sb.AppendLine();
        sb.AppendLine("Plugins:");
        var namesA = ga.Plugins.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var namesB = gb.Plugins.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = gb.Plugins.Where(p => !namesA.Contains(p.Name)).Select(p => p.Name).ToList();
        var removed = ga.Plugins.Where(p => !namesB.Contains(p.Name)).Select(p => p.Name).ToList();
        var missing = gb.Plugins.Where(p => !b.Ctx.Game.HasPlugin(p.Name)).Select(p => p.Name).ToList();
        var extra = DataMasters(b.Ctx.Game.DataDir)
            .Where(n => !namesB.Contains(n)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        sb.AppendLine($"  {gb.Plugins.Count} in the after save ({ga.Plugins.Count} before)");
        if (added.Count > 0) sb.AppendLine($"  new since the before save: {string.Join(", ", added)}");
        if (removed.Count > 0) sb.AppendLine($"  gone since the before save: {string.Join(", ", removed)}");
        if (missing.Count > 0) sb.AppendLine($"  in the save but not in Data: {string.Join(", ", missing)}");
        if (extra.Count > 0) sb.AppendLine($"  in Data but not in the save: {string.Join(", ", extra)} (not loaded by this game, or deployed later)");
        if (missing.Count == 0 && extra.Count == 0) sb.AppendLine("  the Data folder matches the after save's plugin list");
        else if (missing.Count > 0) sb.AppendLine("  The Data folder doesn't match the save. Is Vortex on the profile you tested with? Findings about missing plugins below may come from that, not from the save.");

        // Findings new in the after save, or grown since the before save.
        var old = a.Results.SelectMany(r => r.Findings).GroupBy(f => (f.Rule, f.Key)).ToDictionary(g => g.Key, g => g.Sum(f => f.Count));
        var fresh = new List<(Finding F, int? Was)>();
        foreach (var f in b.Results.SelectMany(r => r.Findings))
            if (!old.TryGetValue((f.Rule, f.Key), out int was)) fresh.Add((f, null));
            else if (f.Count > was) fresh.Add((f, was));
        sb.AppendLine();
        sb.AppendLine($"New findings in the after save: {(fresh.Count == 0 ? "none" : fresh.Count.ToString())}" +
                      $" ({b.Results.Sum(r => r.Findings.Count)} in the after save in all, {a.Results.Sum(r => r.Findings.Count)} in the before save)");
        foreach (var (f, was) in fresh.OrderByDescending(x => x.F.Level))
        {
            sb.AppendLine($"  {f.Level.ToString().ToUpperInvariant(),-7} {f.Rule} {f.Title}{(was is { } w ? $"  (was {w:N0})" : "")}{(f.Reliability == Reliability.Heuristic ? "  (heuristic)" : "")}");
            foreach (var e in f.Evidence) sb.AppendLine($"            {e}");
        }
        foreach (var m in fresh.Select(x => (x.F.Rule, x.F.Meaning)).Distinct())
            sb.AppendLine($"    {m.Rule}: {m.Meaning}");

        // Created objects: before -> after, and which plugins gained the most.
        sb.AppendLine();
        var (ba, bb) = (a.Bloat, b.Bloat);
        sb.AppendLine($"Objects created during play: {ba.Created:N0} -> {bb.Created:N0} ({bb.Created - ba.Created:+#,0;-#,0;0}); " +
                      $"placed objects {ba.Objects:N0} -> {bb.Objects:N0}, actors {ba.Actors:N0} -> {bb.Actors:N0}; save file {a.Ctx.Container.FileSize / 1e6:N1} -> {b.Ctx.Container.FileSize / 1e6:N1} MB");
        var before = ba.ByPlugin.ToDictionary(x => x.Plugin, x => x.Count, StringComparer.OrdinalIgnoreCase);
        var grew = bb.ByPlugin.Select(x => (x.Plugin, Was: before.GetValueOrDefault(x.Plugin), Now: x.Count))
            .Where(x => x.Now > x.Was).OrderByDescending(x => x.Now - x.Was).Take(5).ToList();
        if (grew.Count > 0)
        {
            sb.AppendLine("  plugins whose created objects and actors grew most:");
            foreach (var (p, was, now) in grew) sb.AppendLine($"    {now - was,8:+#,0}  {p}  ({was:N0} -> {now:N0})");
        }

        // Quests that changed state.
        var qa = a.Quests.GroupBy(q => q.FormId).ToDictionary(g => g.Key, g => g.First());
        var changes = new List<string>();
        foreach (var q in b.Quests.OrderBy(q => q.Label, StringComparer.OrdinalIgnoreCase))
        {
            string now = Describe(q);
            if (!qa.TryGetValue(q.FormId, out var p)) changes.Add($"  new      {q.Label}: {now}");
            else if (Describe(p) is var then && then != now) changes.Add($"  changed  {q.Label}: {then} -> {now}");
        }
        foreach (var p in a.Quests.Where(p => b.Quests.All(q => q.FormId != p.FormId)))
            changes.Add($"  gone     {p.Label} (was {Describe(p)}; the game dropped its change record)");
        int show = (int)Math.Min(Math.Max((long)limit * 4, 40), int.MaxValue); // long math: a huge --limit can't overflow
        sb.AppendLine();
        sb.AppendLine($"Quests that changed: {changes.Count}");
        const string Mark = "\u0001QUESTS\u0001";
        sb.AppendLine(Mark);

        int p2 = fresh.Count(x => x.F.Level == Level.Problem), w2 = fresh.Count(x => x.F.Level == Level.Warning), n2 = fresh.Count(x => x.F.Level == Level.Note);
        string verdict = p2 > 0 ? $"{p2} new problem(s){(w2 + n2 > 0 ? $", {w2} new warning(s), {n2} new note(s)" : "")}: read them before playing on."
            : w2 > 0 ? $"{w2} new warning(s){(n2 > 0 ? $", {n2} new note(s)" : "")}: worth a look."
            : n2 > 0 ? $"Healthy, with {n2} new note(s)."
            : "Healthy: nothing new to worry about in the after save.";
        int code = p2 > 0 ? 2 : w2 + n2 > 0 ? 1 : 0;
        sb.AppendLine();
        sb.AppendLine("Verdict: " + verdict);
        // The screen shows the first quest changes; the saved report has all of them.
        var shortList = changes.Take(show).ToList();
        if (changes.Count > show) shortList.Add($"  ... {changes.Count - show} more in the saved report (or use --limit N)");
        string text = sb.ToString();
        return new Outcome(text.Replace(Mark + Environment.NewLine, Lines(shortList)), text.Replace(Mark + Environment.NewLine, Lines(changes)), code, verdict);
    }

    /// <summary>The .esm file names in a Data folder, with the extension matched in any case: on Linux and Proton the
    /// file system is case-sensitive, and a file may be called Name.ESM. The other options are those of the plain
    /// two-argument EnumerateFiles, so the list on Windows doesn't change.</summary>
    internal static List<string> DataMasters(string dataDir) =>
        Directory.EnumerateFiles(dataDir, "*.esm", new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive,
            MatchType = MatchType.Win32,
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            RecurseSubdirectories = false,
        }).Select(Path.GetFileName).OfType<string>().ToList();

    static string Lines(IEnumerable<string> lines) => string.Concat(lines.Select(l => l + Environment.NewLine));

    static string Describe(QuestRules.Snapshot q) => $"{q.Status}{(q.Completed ? ", completed" : "")}, stage {q.Stage?.ToString() ?? "-"}";

    /// <summary>Where solo reports go without --reports: sf-save-health-reports in the current folder.</summary>
    public static string ReportsDir() => Path.Combine(Environment.CurrentDirectory, "sf-save-health-reports");
}
