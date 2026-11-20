using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using SfsReader;
using Kit = BuildKit.GameData;

namespace SaveHealth.Rules;

/// <summary>Q1-Q5: each quest's saved state compared with what the installed plugins define for it: every version of the
/// QUST record (the original and each override) in the save's own plugin order, opened with Mutagen through BuildKit.
/// Plugins missing from Data are skipped here (S1 reports them).</summary>
public static class QuestRules
{
    /// <summary>Save form type of every QUST change record in the samples (seen in file, 13 + 3 saves).</summary>
    const byte QuestFormType = 8;

    /// <summary>What the game uses for a quest: the stage list and the objective list of the winning version, except that
    /// a partial form (record flag 0x4000) with an empty list leaves that list out, so it comes from the next earlier
    /// version that has it (the 4 SFBGS050.esm overrides, 2026-11-10). A full override replaces the lists, so a stage or
    /// objective it drops is gone even if the original plugin has it (until 2026-11-10 the lists of all versions were
    /// joined, which hid that case; found by an offline test against real saves). Each stage's log-entry flags
    /// come from the latest version that has that stage.</summary>
    sealed class Def(IReadOnlyList<QuestVersion> versions)
    {
        static QuestVersion? Effective(IReadOnlyList<QuestVersion> vs, Func<QuestVersion, int> count) =>
            vs.Reverse().FirstOrDefault(v => !v.Partial || count(v) > 0) ?? vs.Reverse().FirstOrDefault(v => count(v) > 0);

        readonly QuestVersion? stageSource = Effective(versions, v => v.Stages.Count);
        readonly QuestVersion? objectiveSource = Effective(versions, v => v.Objectives.Count);
        public HashSet<ushort> Stages { get; } = Effective(versions, v => v.Stages.Count)?.Stages.Keys.ToHashSet() ?? [];
        public HashSet<uint> Objectives { get; } = Effective(versions, v => v.Objectives.Count)?.Objectives ?? [];
        public string StagePlugin => stageSource?.Plugin ?? versions[^1].Plugin;
        public string ObjectivePlugin => objectiveSource?.Plugin ?? versions[^1].Plugin;
        public byte StageFlags(ushort stage) => versions.LastOrDefault(v => v.Stages.ContainsKey(stage))?.Stages[stage] ?? 0;
        public string Plugins => string.Join(" + ", versions.Select(v => v.Plugin));
    }

    sealed record SavedQuest(ChangeRecord Record, QuestState State, Def? Final, string Label);

    /// <summary>One quest's saved state in short, for before/after reports (solo).</summary>
    public sealed record Snapshot(uint FormId, string Label, string Status, ushort? Stage, bool Completed);

    /// <summary>Runs Q1-Q5. When <paramref name="snapshots"/> is given, it is filled with every decoded quest.</summary>
    public static List<RuleResult> Run(SaveContext ctx, int limit, List<Snapshot>? snapshots = null)
    {
        var (g, body, game) = (ctx.Save, ctx.Body, ctx.Game);
        var records = body.Records.Where(r => r.FormType == QuestFormType && !r.Form.IsNull && !r.Form.IsCreated).ToList();

        var states = new List<(ChangeRecord Rec, QuestState State)>();
        int failed = 0;
        foreach (var r in records)
            try { states.Add((r, QuestState.Decode(ctx.Container.Inner, r))); }
            catch (InvalidDataException e) { failed++; Log.Write($"quest {r.Form} not decoded: {e.Message}"); }
        Log.Write($"quests: {records.Count} change records of form type {QuestFormType}, {states.Count} decoded, {failed} not decoded");

        // The plugin versions: open the installed plugins in the save's order (each plugin's masters come first).
        using var kit = new Kit(game.DataDir);
        var installed = g.Plugins.Where(p => game.HasPlugin(p.Name)).ToList();
        foreach (var p in installed)
            if (kit.TryOpen(p.Name) is null) Log.Write($"quests: {p.Name} (or one of its masters) could not be opened with Mutagen");
        Log.Write($"quests: Mutagen load order {string.Join(", ", kit.LoadOrder.Select(m => m.ModKey.FileName.String))}");
        var cache = kit.LinkCache;
        var opened = kit.LoadOrder.Select(m => m.ModKey.FileName.String).ToHashSet(StringComparer.OrdinalIgnoreCase);

        FormKey KeyOf(ChangeRecord r) => new(ModKey.FromFileName(r.Form.Plugin), r.Form.LocalId);
        var keys = states.Where(s => opened.Contains(s.Rec.Form.Plugin)).Select(s => KeyOf(s.Rec)).ToList();
        var defs = QuestDefs.Load(kit, keys);

        var quests = new List<SavedQuest>();
        foreach (var (rec, st) in states)
        {
            Def? final = null;
            string? edid = null;
            if (opened.Contains(rec.Form.Plugin))
            {
                var key = KeyOf(rec);
                if (defs.Versions(key) is { Count: > 0 } v) { final = new Def(v); edid = cache.TryResolve<IQuestGetter>(key, out var q) ? q.EditorID : null; }
                else Log.Write($"quests: {rec.Form} is form type {QuestFormType} but not a quest in the plugins (S3 reports forms that are gone)");
            }
            quests.Add(new SavedQuest(rec, st, final, edid is { Length: > 0 } e ? $"{e} ({rec.Form.Plugin}:{rec.Form.LocalId:X})" : $"{rec.Form.Plugin}:{rec.Form.LocalId:X}"));
        }

        snapshots?.AddRange(quests.Select(q => new Snapshot(q.Record.Form.FormId, q.Label, q.State.Status, q.State.CurrentStage, q.State.Completed)));
        var summary = $"{records.Count:N0} quests in the save, {states.Count:N0} decoded exactly, {quests.Count(q => q.Final != null):N0} compared with their plugin versions";
        string saveType = SaveType(ctx.Path);
        return [Q1(quests, game, limit, summary, failed), Q2(quests, limit), Q3(quests, limit), Q4(quests, limit), Q5(quests, limit, saveType)];
    }

    static bool Running(QuestState s) => s.Enabled;

    static RuleResult Q1(List<SavedQuest> quests, SfsReader.GameData game, int limit, string summary, int failed)
    {
        var findings = new List<Finding>();
        foreach (var grp in quests.Where(q => !game.HasPlugin(q.Record.Form.Plugin) && Running(q.State))
                     .GroupBy(q => q.Record.Form.Plugin, StringComparer.OrdinalIgnoreCase))
            findings.Add(new Finding("Q1", Level.Warning, Reliability.Exact, grp.Key.ToLowerInvariant(),
                $"{grp.Key}: {grp.Count()} quest(s) still running, but the plugin is not installed",
                "These quests were running when the save was made, and the mod that defines them is gone. The game drops them on load; if the mod was removed by mistake, reinstall it.",
                Examples(grp, limit, q => $"{q.Label}  {q.State.Status}, stage {Stage(q)}")) { Count = grp.Count() });
        var lines = new List<string> { summary };
        if (failed > 0) lines.Add($"{failed} quest record(s) could not be decoded, so they were not checked (details in the log)");
        return new RuleResult("Q1", "Running quests from plugins that are not installed", findings, lines);
    }

    static RuleResult Q2(List<SavedQuest> quests, int limit)
    {
        var bad = new List<(SavedQuest Q, List<ushort> Missing)>();
        foreach (var q in quests.Where(q => q.Final != null && q.State.Stages != null))
        {
            var missing = q.State.Stages!.Where(s => s.Done && !q.Final!.Stages.Contains(s.Index)).Select(s => s.Index).ToList();
            if (missing.Count > 0) bad.Add((q, missing));
        }
        Log.Write($"Q2: {bad.Count} quests with done stages the quest no longer has");
        var findings = bad.Select(b => new Finding("Q2", Level.Warning, Reliability.Exact, b.Q.Record.Form.FormId.ToString("X8"),
            $"{b.Q.Label}: done stage(s) {string.Join(", ", b.Missing)} no longer exist",
            "The save says these stages were done, but the quest as the installed plugins now define it has no such stage. A mod update (or a mod that overrides the quest) probably renumbered or removed them; the quest may not continue as expected.",
            [$"{b.Q.State.Status}, stage {Stage(b.Q)}; the stage list in use comes from {b.Q.Final!.StagePlugin} ({b.Q.Final.Stages.Count} stages; versions: {b.Q.Final.Plugins})"])).ToList();
        Cap(findings, limit, "Q2", "done stages the quest no longer has");
        return new RuleResult("Q2", "Done stages the quest no longer has", findings, [$"{bad.Count:N0} quest(s)"]);
    }

    static RuleResult Q3(List<SavedQuest> quests, int limit)
    {
        var bad = new List<(SavedQuest Q, List<uint> Missing)>();
        foreach (var q in quests.Where(q => q.Final != null && q.State.Objectives != null))
        {
            var missing = q.State.Objectives!.Where(o => !q.Final!.Objectives.Contains(o.Index)).Select(o => o.Index).ToList();
            if (missing.Count > 0) bad.Add((q, missing));
        }
        Log.Write($"Q3: {bad.Count} quests with objectives the quest no longer has");
        var findings = bad.Select(b => new Finding("Q3", Level.Warning, Reliability.Exact, b.Q.Record.Form.FormId.ToString("X8"),
            $"{b.Q.Label}: objective(s) {string.Join(", ", b.Missing)} no longer exist",
            "The save holds state for these objectives, but the quest as the installed plugins now define it has no such objective. A mod update (or a mod that overrides the quest) probably removed them; a shown objective may never clear.",
            [$"{b.Q.State.Status}; the objective list in use comes from {b.Q.Final!.ObjectivePlugin} ({b.Q.Final.Objectives.Count} objectives; versions: {b.Q.Final.Plugins})"])).ToList();
        Cap(findings, limit, "Q3", "objectives the quest no longer has");
        return new RuleResult("Q3", "Objectives the quest no longer has", findings, [$"{bad.Count:N0} quest(s)"]);
    }

    static RuleResult Q4(List<SavedQuest> quests, int limit)
    {
        var bad = new List<(SavedQuest Q, string Kind)>();
        int completedRunning = 0;
        // A running quest that is complete is normal (post-quest dialogue: MQ101 in every save). Only a quest whose
        // highest done stage should have completed or failed it, but whose completed flag is NOT set, is reported.
        foreach (var q in quests.Where(q => q.Final != null && Running(q.State) && q.State.CurrentStage is > 0))
        {
            var flags = (QuestLogEntry.Flag)q.Final!.StageFlags(q.State.CurrentStage!.Value);
            if ((flags & (QuestLogEntry.Flag.CompleteQuest | QuestLogEntry.Flag.FailQuest)) == 0) continue;
            if (q.State.Completed) { completedRunning++; continue; }
            bad.Add((q, (flags & QuestLogEntry.Flag.CompleteQuest) != 0 ? "completes" : "fails"));
        }
        Log.Write($"Q4: {bad.Count} running quests at a completing stage without the completed flag ({completedRunning} complete and still running, not reported)");
        var findings = new List<Finding>();
        if (bad.Count > 0)
            findings.Add(new Finding("Q4", Level.Note, Reliability.Heuristic, "q4",
                $"{bad.Count} running quest(s) at a stage that should complete them, but not marked complete",
                "These quests reached a stage whose journal entry completes (or fails) the quest, yet the save doesn't mark them complete. The entry has conditions, so this can be normal; it's worth a look if one of these quests seems stuck.",
                Examples(bad.Select(b => b.Q), limit, q => $"{q.Label}  stage {q.State.CurrentStage} {bad.First(b => b.Q == q).Kind} the quest")) { Count = bad.Count });
        return new RuleResult("Q4", "Quests at a completing stage but not marked complete (heuristic)", findings,
            [$"{bad.Count:N0} quest(s) ({completedRunning:N0} complete and still running, which is normal)"]);
    }

    static RuleResult Q5(List<SavedQuest> quests, int limit, string saveType)
    {
        var pending = quests.Where(q => q.State.Pending).ToList();
        var doneShown = quests.Where(q => !q.State.Pending && q.State.Completed && Running(q.State) &&
                                          q.State.Objectives?.Any(o => QuestState.ObjectiveShown(o.State) && !QuestState.ObjectiveCompleted(o.State)) == true).ToList();
        Log.Write($"Q5: {pending.Count} quests starting/stopping, {doneShown.Count} completed but running with open objectives ({saveType})");
        var findings = new List<Finding>();
        if (pending.Count > 0)
            findings.Add(new Finding("Q5", Level.Note, Reliability.Heuristic, "q5-pending",
                $"{pending.Count} quest(s) were starting or stopping when the game saved ({saveType})",
                "A quest caught halfway through starting or stopping. Usually it finishes after loading; if the same quest shows here in save after save, it may be stuck.",
                Examples(pending, limit, q => $"{q.Label}  {q.State.Status}")) { Count = pending.Count });
        if (doneShown.Count > 0)
            findings.Add(new Finding("Q5", Level.Note, Reliability.Heuristic, "q5-done-open",
                $"{doneShown.Count} completed quest(s) still running with an open objective",
                "These quests are marked complete but still show an objective that isn't done. That can be a leftover marker in the quest log.",
                Examples(doneShown, limit, q => $"{q.Label}  stage {Stage(q)}")) { Count = doneShown.Count });
        return new RuleResult("Q5", "Quests caught starting or stopping, or complete with open objectives (heuristic)", findings,
            [$"{pending.Count:N0} starting or stopping, {doneShown.Count:N0} complete with an open objective"]);
    }

    /// <summary>The quest's current stage, or "-" when it has none (as sf-save-reader prints it).</summary>
    static string Stage(SavedQuest q) => q.State.CurrentStage?.ToString() ?? "-";

    /// <summary>Q2 and Q3 list one finding per quest. Past limit x 10 quests the rest become one finding that counts
    /// them, so the verdict and exit code still see every quest (with --limit 0, only that finding).</summary>
    internal static void Cap(List<Finding> findings, int limit, string rule, string what)
    {
        int keep = limit * 10;
        if (findings.Count <= keep) return;
        int more = findings.Count - keep;
        findings.RemoveRange(keep, more);
        findings.Add(new Finding(rule, Level.Warning, Reliability.Exact, rule.ToLowerInvariant() + "-more",
            $"... {more:N0} more quest(s) with {what} (use --limit N to list them)",
            "Each of these quests is like the ones listed above; raise --limit to see them one by one.", []) { Count = more });
    }

    static List<string> Examples(IEnumerable<SavedQuest> list, int limit, Func<SavedQuest, string> line)
    {
        var all = list.ToList();
        var lines = all.Take(limit).Select(line).ToList();
        if (all.Count > limit) lines.Add($"... {all.Count - limit:N0} more (use --limit N)");
        return lines;
    }

    /// <summary>The kind of save, from the game's file name (Save12_..., Autosave1_..., Quicksave0_..., Exitsave0_...).</summary>
    public static string SaveType(string path)
    {
        string n = Path.GetFileName(path);
        int dash = n.IndexOf('-');
        if (dash >= 0 && dash < 7) n = n[(dash + 1)..]; // the log collector's before-/after- prefix
        return n.StartsWith("Autosave", StringComparison.OrdinalIgnoreCase) ? "autosave"
            : n.StartsWith("Quicksave", StringComparison.OrdinalIgnoreCase) ? "quicksave"
            : n.StartsWith("Exitsave", StringComparison.OrdinalIgnoreCase) ? "exit save"
            : n.StartsWith("Save", StringComparison.OrdinalIgnoreCase) ? "hard save" : "unknown save type";
    }
}
