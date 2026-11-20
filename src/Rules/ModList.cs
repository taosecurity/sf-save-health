using SfsReader;

namespace SaveHealth.Rules;

/// <summary>A plugins.txt (the game's own, or a Vortex export of it): plugin names in load order, active ones marked "*".</summary>
public sealed record PluginList(string Path, List<(string Name, bool Active)> Entries)
{
    public static PluginList Read(string path)
    {
        if (!File.Exists(path)) throw new UserError($"no plugins file {path}",
            "pass --plugins <plugins.txt>: the game's %LOCALAPPDATA%\\Starfield\\Plugins.txt, or a Vortex export of it");
        var entries = new List<(string, bool)>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            bool active = line.StartsWith('*');
            entries.Add((active ? line[1..].Trim() : line, active));
        }
        Log.Write($"plugins file {path}: {entries.Count} entries, {entries.Count(e => e.Item2)} active");
        var odd = entries.Where(e => !System.Text.RegularExpressions.Regex.IsMatch(e.Item1, @"\.es[mpl]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
        if (entries.Count == 0 || odd.Count > 0) throw new UserError(
            entries.Count == 0 ? $"{path} lists no plugins" : $"{path} isn't a plugins list (line \"{odd[0].Item1}\" is not a plugin name)",
            "check that it's a plugins.txt (one plugin per line, active ones start with *), not a modlist JSON");
        return new PluginList(path, entries);
    }
}

/// <summary>M1-M2: the save's plugin list against a plugins.txt. M1 = what the game warns about when you load the save
/// (a plugin the save was made with that this load order doesn't turn on); M2 = plugins the list turns on that aren't
/// installed. Official Bethesda plugins are loaded by the game without plugins.txt, so they count as on when installed,
/// unless the list names them without "*".</summary>
public static class ModList
{
    public static bool IsOfficial(string name) =>
        name.Equals("Starfield.esm", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Constellation.esm", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("OldMars.esm", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("ShatteredSpace.esm", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("BlueprintShips-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("SFBGS", StringComparison.OrdinalIgnoreCase);

    public static List<RuleResult> Run(SaveContext ctx, PluginList list)
    {
        var (g, body, game) = (ctx.Save, ctx.Body, ctx.Game);
        var listed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (n, a) in list.Entries) listed[n] = a;   // a later line wins, as in the game
        bool On(string p) => listed.TryGetValue(p, out var a) ? a : IsOfficial(p) && game.HasPlugin(p);

        // Data left behind per plugin: the plugin's own change records, and references to its forms inside records.
        var counts = body.Records.Select(r => (r.Form, Ref: false))
            .Concat(body.Records.SelectMany(r => r.References.Select(x => (Form: x, Ref: true))))
            .Where(u => !u.Form.IsNull && !u.Form.IsCreated)
            .GroupBy(u => u.Form.Plugin, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => (Records: x.Count(u => !u.Ref), Refs: x.Count(u => u.Ref)), StringComparer.OrdinalIgnoreCase);

        // M1: in the save, not turned on by the list.
        var off = g.Plugins.Where(p => !On(p.Name)).ToList();
        var m1 = new List<Finding>();
        int leftovers = 0, notInData = 0;
        foreach (var p in off)
        {
            var (records, refs) = counts.GetValueOrDefault(p.Name);
            bool installed = game.HasPlugin(p.Name);
            if (!installed) notInData++;
            if (records + refs > 0) leftovers++;
            string why = listed.ContainsKey(p.Name) ? "turned off in the list" : "not in the list";
            string label = p.CreationName is { Length: > 0 } c ? $"{p.Name} [{c}]" : p.Name;
            Log.Write($"M1 {p.Name}: {why}; installed {installed}; {records} change records, {refs} references");
            m1.Add(new Finding("M1", records + refs > 0 ? Level.Problem : Level.Note, Reliability.Exact, p.Name.ToLowerInvariant(),
                $"{p.Kind} plugin {why}: {label}",
                records + refs > 0
                    ? "The save was made with this plugin, but this load order doesn't load it, so the game warns that a mod is missing. The save still holds data for it. If you removed the mod on purpose, that data stays behind; if not, turn it back on before playing."
                    : "The save was made with this plugin, but this load order doesn't load it, so the game warns that a mod is missing. It left nothing behind in the change forms (its scripts are not checked yet).",
                [$"{records:N0} change records, {refs:N0} references inside records", installed ? "the file is still in the Data folder" : "the file is not in the Data folder either (S1)"]));
        }
        var inSave = g.Plugins.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = list.Entries.Where(e => e.Active && !inSave.Contains(e.Name)).Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var s1 = new List<string>
        {
            $"{off.Count} of {g.Plugins.Count} plugins in the save are not loaded by {System.IO.Path.GetFileName(list.Path)} ({leftovers} with data left behind, {notInData} also missing from Data)",
            added.Count == 0 ? "the list turns on no plugin the save lacks" : $"{added.Count} plugin(s) turned on in the list are new since the save (adding mods is normally safe): {string.Join(", ", added.Take(10))}{(added.Count > 10 ? ", ..." : "")}",
            OrderLine(g, list),
        };
        if (off.Count >= 5 && off.Count * 4 >= g.Plugins.Count)
            s1.Add("That's a lot: this list probably belongs to a different Vortex profile than the one this save was made with.");

        // M2: turned on in the list, file not installed (the game skips it without a word).
        var m2 = list.Entries.Where(e => e.Active && !game.HasPlugin(e.Name)).Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => new Finding("M2", Level.Warning, Reliability.Exact, n.ToLowerInvariant(), $"turned on but not installed: {n}",
                "The list turns this plugin on, but its file isn't in the Data folder, so the game skips it. Is the mod deployed, or is --data the right folder?", []))
            .ToList();
        foreach (var f in m2) Log.Write($"M2 {f.Key}: active in the list, not in Data");

        return [new RuleResult("M1", "Plugins in the save that the plugin list doesn't load", m1, s1),
                new RuleResult("M2", "Plugins the list turns on that aren't installed", m2, [$"{m2.Count} found"])];
    }

    /// <summary>How many plugins that are both in the save and on in the list sit in a different order: the count that
    /// would have to move (plugins minus the longest run kept in order). Official plugins are left out (the game places them).
    /// The save lists full, light and medium plugins in separate blocks, so the order is compared within each block.</summary>
    static string OrderLine(SaveGame g, PluginList list)
    {
        var pos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (n, a) in list.Entries) if (a && !IsOfficial(n)) pos.TryAdd(n, pos.Count);
        int total = 0, moved = 0;
        foreach (var block in g.Plugins.Where(p => pos.ContainsKey(p.Name)).GroupBy(p => p.Kind))
        {
            var tails = new List<int>();   // longest increasing subsequence, patience method
            foreach (var x in block.Select(p => pos[p.Name]))
            {
                int i = tails.BinarySearch(x);
                if (i < 0) i = ~i;
                if (i == tails.Count) tails.Add(x); else tails[i] = x;
            }
            total += block.Count();
            moved += block.Count() - tails.Count;
        }
        Log.Write($"order: {total} mod plugins in both, {moved} moved (compared within full, light and medium blocks)");
        return moved == 0 ? $"load order: the {total} mod plugins in both are in the same order"
            : $"load order: {moved} of the {total} mod plugins in both have moved (the game matches plugins by name, so this is usually harmless)";
    }
}
