namespace SaveHealth.Rules;

/// <summary>B1: how many objects the game created during play (change records whose refid kind is 2, the FF… forms),
/// and which plugins the created placed objects and actors point to. The plugin breakdown repeats the logic of
/// an earlier research script, written again on top of the parser's decoded records: a created record's
/// reference list usually holds the base form it was made from, so the plugins it names hint at what spawned it (it also
/// names cells, keywords and so on, so this is a hint, not proof). A heuristic: never a PROBLEM.</summary>
public static class Bloat
{
    /// <summary>Thresholds from the saves seen so far (2026-11-09): normal saves hold 25,000-35,000 created records,
    /// bloated ones 288,000-295,000 (two long playthroughs shared by players).</summary>
    public const int NoteFrom = 60_000, WarningFrom = 150_000;

    /// <summary>Save form types of created placed objects (0) and created actors (1), as in the script.</summary>
    static readonly byte[] BreakdownTypes = [0, 1];

    /// <summary>What B1 measures in one save. ByPlugin: how many created objects and actors name each plugin, most first.</summary>
    public sealed record Counts(int Created, int Objects, int Actors, List<(string Plugin, int Count)> ByPlugin);

    public static Counts Measure(SaveContext ctx)
    {
        var created = ctx.Body.Records.Where(r => r.RefKind == 2).ToList();
        // Each created object or actor counts once for every plugin its reference list names.
        var byPlugin = created.Where(r => BreakdownTypes.Contains(r.FormType))
            .SelectMany(r => r.References.Where(x => !x.IsNull && !x.IsCreated).Select(x => x.Plugin)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(grp => (Plugin: grp.Key, Count: grp.Count()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Plugin, StringComparer.OrdinalIgnoreCase).ToList();
        return new Counts(created.Count, created.Count(r => r.FormType == 0), created.Count(r => r.FormType == 1), byPlugin);
    }

    public static List<RuleResult> Run(SaveContext ctx, int limit)
    {
        var m = Measure(ctx);
        var (created, objects, actors, byPlugin) = (m.Created, m.Objects, m.Actors, m.ByPlugin);
        long size = ctx.Container.FileSize;
        Log.Write($"B1: {created} created records ({objects} placed objects, {actors} actors) of {ctx.Body.Records.Count}; save {size} bytes");
        foreach (var (p, c) in byPlugin.Take(15)) Log.Write($"B1   {c,8} created objects/actors point to {p}");

        var level = created >= WarningFrom ? Level.Warning : created >= NoteFrom ? Level.Note : (Level?)null;
        string verdict = level switch
        {
            Level.Warning => "bloated",
            Level.Note => "growing",
            _ => "OK",
        };
        var top = byPlugin.Take(Math.Min(limit, 5)).Select(x => $"{x.Count,8:N0}  {x.Plugin}").ToList();
        var summary = new List<string>
        {
            $"{created:N0} objects created during play ({verdict}; normal 25,000-35,000, note from {NoteFrom:N0}, warning from {WarningFrom:N0}); save file {size / 1e6:N1} MB",
            $"{objects:N0} created placed objects, {actors:N0} created actors, {created - objects - actors:N0} other; {ctx.Body.Records.Count:N0} change records in all",
        };
        if (top.Count > 0)
        {
            summary.Add("plugins the created objects and actors point to most (a hint at what made them):");
            summary.AddRange(top.Select(t => "  " + t));
        }

        var findings = new List<Finding>();
        if (level is { } lv)
            findings.Add(new Finding("B1", lv, Reliability.Heuristic, "b1",
                $"{created:N0} objects created during play ({verdict})",
                lv == Level.Warning
                    ? "The save holds far more created objects than normal (mostly planet content and space-encounter ships and crews the game never cleaned up). Big saves load and save slowly and can lower fps. LarannKiar's Save Cleaner (an optional SFSE file on Starfield Engine Fixes) can remove them, but its author calls it experimental and unsafe: back up the save first and keep the backup."
                    : "The number of created objects is above normal and growing. Nothing to do yet; if it keeps growing past 150,000, see the advice for a bloated save.",
                [$"save file {size:N0} bytes; the plugin breakdown is in the summary above"]) { Count = created });
        return [new RuleResult("B1", "Objects created during play (bloat)", findings, summary)];
    }
}
