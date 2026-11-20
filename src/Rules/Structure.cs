using SfsReader;

namespace SaveHealth.Rules;

/// <summary>S1-S3: leftovers of removed mods and forms that no longer exist (moved from sfs-reader's check command,
/// 2026-11-10; same counts). Looks at every change record's own form and every plugin form it references.</summary>
public static class Structure
{
    sealed record Use(FormRef Form, ChangeRecord Record, bool IsReference);

    public static List<RuleResult> Run(SaveContext ctx, int limit)
    {
        var (g, body, game) = (ctx.Save, ctx.Body, ctx.Game);
        var uses = body.Records.Select(r => new Use(r.Form, r, false))
            .Concat(body.Records.SelectMany(r => r.References.Select(x => new Use(x, r, true))))
            .Where(u => !u.Form.IsNull && !u.Form.IsCreated).ToList();
        Log.Write($"structure: {body.Records.Count} change records, {uses.Count(u => u.IsReference)} references to plugin forms, {g.Plugins.Count} plugins");
        return [S1(g, game, uses), S2(game, uses, limit), S3(game, uses, limit)];
    }

    static RuleResult S1(SaveGame g, GameData game, List<Use> uses)
    {
        var missing = g.Plugins.Where(p => !game.HasPlugin(p.Name)).ToList();
        var findings = new List<Finding>();
        int leftovers = 0;
        foreach (var p in missing)
        {
            var mine = uses.Where(u => u.Form.Plugin.Equals(p.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            int records = mine.Count(u => !u.IsReference), refs = mine.Count(u => u.IsReference);
            string label = p.CreationName is { Length: > 0 } c ? $"{p.Name} [{c}]" : p.Name;
            Log.Write($"S1 {p.Name}: not in Data; {records} change records, {refs} references");
            if (records + refs > 0) leftovers++;
            findings.Add(new Finding("S1", records + refs > 0 ? Level.Problem : Level.Note, Reliability.Exact, p.Name.ToLowerInvariant(),
                $"{p.Kind} plugin missing: {label}",
                records + refs > 0
                    ? "The save was made with this plugin and still holds data for it, but it isn't installed now. If you removed the mod on purpose, that data stays behind; if not, reinstall it before playing."
                    : "The save was made with this plugin, which isn't installed now. It left nothing behind in the change forms (its scripts are not checked yet).",
                [$"{records:N0} change records, {refs:N0} references inside records"]));
        }
        var summary = new List<string> { $"{missing.Count} of {g.Plugins.Count} plugins in the save are missing from the Data folder ({leftovers} with data left behind)" };
        if (missing.Count >= 5 && missing.Count * 4 >= g.Plugins.Count)
            summary.Add("That's a lot: the installed mods probably belong to a different load order (another Vortex profile?) than the one this save was made with. Switch to that profile, or point --data at its Data folder, before trusting S1.");
        return new RuleResult("S1", "Plugins in the save that are missing from the Data folder", findings, summary);
    }

    static RuleResult S2(GameData game, List<Use> uses, int limit)
    {
        var unresolved = uses.Where(u => u.Form.Plugin.StartsWith('?')).ToList();
        Log.Write($"S2: {unresolved.Count} form IDs past the plugin list");
        var findings = new List<Finding>();
        if (unresolved.Count > 0)
            findings.Add(new Finding("S2", Level.Problem, Reliability.Exact, "s2", $"{unresolved.Count:N0} form IDs point past the save's plugin list",
                "These form IDs name a plugin slot the save's own plugin list doesn't have. The save may be damaged.",
                Examples(unresolved, game, limit)) { Count = unresolved.Count });
        return new RuleResult("S2", "Form IDs that point past the save's plugin list", findings, [$"{unresolved.Count:N0} found"]);
    }

    static RuleResult S3(GameData game, List<Use> uses, int limit)
    {
        foreach (var grp in uses.Where(u => game.HasPlugin(u.Form.Plugin)).GroupBy(u => u.Form.Plugin, StringComparer.OrdinalIgnoreCase))
            game.Load(grp.Key, grp.Select(u => u.Form.LocalId).Distinct(), warnMissing: false);
        var gone = uses.Where(u => game.HasPlugin(u.Form.Plugin) && game.Get(u.Form) is null).ToList();
        // Engine forms (low IDs such as 3A6) exist in the game itself, not in Starfield.esm (seen in the ships work).
        var engine = gone.Where(u => u.Form.Plugin.Equals("Starfield.esm", StringComparison.OrdinalIgnoreCase) && u.Form.LocalId < 0x800).ToList();
        gone = gone.Except(engine).ToList();
        var findings = new List<Finding>();
        foreach (var grp in gone.GroupBy(u => u.Form.Plugin, StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x.Count()))
        {
            int distinct = grp.Select(u => u.Form.FormId).Distinct().Count();
            bool official = IsOfficial(grp.Key);
            Log.Write($"S3 {grp.Key}: {distinct} distinct forms not found{(official ? " (official plugin: note)" : "")}");
            findings.Add(new Finding("S3", official ? Level.Note : Level.Problem, Reliability.Exact, grp.Key.ToLowerInvariant(),
                $"{grp.Key}: {distinct:N0} forms no longer exist in the plugin",
                official
                    ? "The save refers to forms this official Bethesda plugin doesn't have any more. In long playthroughs these are left over from earlier game updates (seen in 8 Xbox saves made since 2024, 2026-11-10); the game ignores them."
                    : "The save refers to forms this plugin doesn't have any more (the mod was probably updated and removed or renumbered them). Things tied to them may be missing or broken.",
                Examples(grp.ToList(), game, limit)) { Count = distinct });
        }
        int total = gone.Select(u => u.Form.FormId).Distinct().Count();
        var summary = new List<string> { $"{total:N0} distinct ({gone.Count(u => !u.IsReference):N0} change records, {gone.Count(u => u.IsReference):N0} references)" };
        if (engine.Count > 0)
            summary.Add($"not counted: {engine.Select(u => u.Form.FormId).Distinct().Count()} engine form(s) below Starfield.esm:800, which live in the game itself");
        return new RuleResult("S3", "Forms not found in their plugin", findings, summary);
    }

    /// <summary>Bethesda's own plugins: the game, its DLC, the edition extras, ship blueprints and Bethesda Creations
    /// (SFBGSxxx, Trackers Alliance sfta01-06).</summary>
    public static bool IsOfficial(string plugin) => System.Text.RegularExpressions.Regex.IsMatch(plugin,
        @"^(Starfield|ShatteredSpace|Constellation|OldMars|BlueprintShips-[^.]+|SFBGS[0-9A-F]{3}|sfta\d\d)\.esm$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    static List<string> Examples(List<Use> list, GameData game, int limit)
    {
        var lines = new List<string>();
        foreach (var u in list.GroupBy(u => u.Form.FormId).Take(limit))
        {
            var first = u.First();
            string owner = game.Describe(first.Record.Form) is { Length: > 0 } d ? d : $"save form type {first.Record.FormType}";
            string where = first.IsReference
                ? $"referenced by {first.Record.Form} ({owner})"
                : $"change record #{first.Record.Index}, save form type {first.Record.FormType}";
            lines.Add($"{first.Form,-36} {where}{(u.Count() > 1 ? $"  (+{u.Count() - 1} more uses)" : "")}");
        }
        int more = list.Select(u => u.Form.FormId).Distinct().Count() - limit;
        if (more > 0) lines.Add($"... {more:N0} more (use --limit N)");
        return lines;
    }
}
