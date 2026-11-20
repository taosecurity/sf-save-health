using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Starfield;
using Kit = BuildKit.GameData;

namespace SaveHealth.Rules;

/// <summary>One plugin's version of a quest: stage indexes with the OR of their log entries' flags (0x1 complete
/// quest, 0x2 fail quest), objective indexes, and whether the record is a partial form (record flag 0x4000: lists it
/// leaves out keep the earlier version's).</summary>
public sealed record QuestVersion(string Plugin, Dictionary<ushort, byte> Stages, HashSet<uint> Objectives, bool Partial = false);

/// <summary>Every installed plugin's version of each quest (the original and each override), from Mutagen's link cache.
/// Why every version and not just the winner: a QUST override can be a partial form (flag 0x4000) that leaves the stage
/// and objective lists out, meaning "keep the earlier ones". On 2026-11-10 the winning versions of Com_Companion_Barrett
/// and COM_Companion_Andreja had no stages for that reason, which gave false alarms; checked against the raw records with
/// a byte reader, Mutagen 0.54.4 read all 528 quest versions of the Baseline save exactly.</summary>
public sealed class QuestDefs
{
    /// <summary>Record header flag of a partial form (Starfield; seen on the 4 SFBGS050.esm quest overrides).</summary>
    const int PartialFormFlag = 0x4000;

    readonly Dictionary<FormKey, List<QuestVersion>> versions = new();

    /// <summary>All versions of the quest, earliest plugin first; empty if no installed plugin has it.</summary>
    public IReadOnlyList<QuestVersion> Versions(FormKey key) => versions.TryGetValue(key, out var v) ? v : [];

    public static QuestDefs Load(Kit kit, IEnumerable<FormKey> wanted)
    {
        var defs = new QuestDefs();
        int partial = 0;
        foreach (var key in wanted.Distinct())
        {
            // The link cache lists the winner first; reverse to earliest first.
            var list = kit.LinkCache.ResolveAllSimpleContexts<IQuestGetter>(key).Reverse()
                .Select(c => new QuestVersion(c.ModKey.FileName,
                    c.Record.Stages.GroupBy(s => s.Index).ToDictionary(g => g.Key,
                        g => (byte)g.SelectMany(s => s.LogEntries).Aggregate(0, (a, e) => a | (int)(e.Flags ?? 0))),
                    c.Record.Objectives.Select(o => (uint)o.Index).ToHashSet(),
                    (c.Record.MajorRecordFlagsRaw & PartialFormFlag) != 0))
                .ToList();
            if (list.Count == 0) continue;
            defs.versions[key] = list;
            if (list.Count > 1 && list[^1].Stages.Count == 0 && list.Any(v => v.Stages.Count > 0))
            {
                partial++;
                Log.Write($"quests: {key}: the winning version in {list[^1].Plugin} lists no stages ({(list[^1].Partial ? "partial form" : "full form")}); other versions with stages: {string.Join(", ", list.Where(v => v.Stages.Count > 0).Select(v => v.Plugin))}");
            }
        }
        Log.Write($"quests: {defs.versions.Count} quests found in the plugins, {defs.versions.Values.Sum(v => v.Count)} versions; {partial} winning version(s) without stages");
        return defs;
    }
}
