using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveHealth;

/// <summary>Turns rule results into a plain-text or JSON report, and the exit code.</summary>
public static class Report
{
    public static int ExitCode(IEnumerable<RuleResult> results)
    {
        var all = results.SelectMany(r => r.Findings).ToList();
        return all.Any(f => f.Level == Level.Problem) ? 2 : all.Count > 0 ? 1 : 0;
    }

    public static string Verdict(IEnumerable<RuleResult> results)
    {
        var all = results.SelectMany(r => r.Findings).ToList();
        int p = all.Count(f => f.Level == Level.Problem), w = all.Count(f => f.Level == Level.Warning), n = all.Count(f => f.Level == Level.Note);
        return p + w + n == 0 ? "Healthy: no problems found." : $"{p} problem(s), {w} warning(s), {n} note(s).";
    }

    public static string Text(SaveContext ctx, List<RuleResult> results)
    {
        var g = ctx.Save;
        var sb = new StringBuilder();
        sb.AppendLine($"sf-save-health {typeof(Report).Assembly.GetName().Version?.ToString(3)}: {Path.GetFileName(ctx.Path)}");
        sb.AppendLine($"  {Redact.Name(g.PlayerName)}, level {g.PlayerLevel}, {g.Location}, playtime {g.PlaytimeText}, saved {g.SaveTimeUtc:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"  {g.Plugins.Count} plugins in the save; checked against {ctx.Game.DataDir}");
        foreach (var r in results)
        {
            sb.AppendLine();
            sb.AppendLine($"{r.Rule}. {r.Name}: {string.Join("; ", r.Summary.Take(1))}");
            foreach (var s in r.Summary.Skip(1)) sb.AppendLine($"    {s}");
            foreach (var f in r.Findings.OrderByDescending(f => f.Level))
            {
                sb.AppendLine($"  {f.Level.ToString().ToUpperInvariant(),-7} {f.Title}{(f.Reliability == Reliability.Heuristic ? "  (heuristic)" : "")}");
                foreach (var e in f.Evidence) sb.AppendLine($"            {e}");
            }
            // One explanation per kind of finding, not per finding.
            foreach (var m in r.Findings.Select(f => (f.Level, f.Meaning)).Distinct().OrderByDescending(x => x.Level))
                sb.AppendLine($"    What it means ({m.Level.ToString().ToLowerInvariant()}): {m.Meaning}");
        }
        sb.AppendLine();
        sb.AppendLine(Verdict(results));
        sb.AppendLine("Not checked yet: Papyrus script data (orphaned scripts, stuck stacks).");
        // Missing plugins are S1's job, and missing string files only affect display names: both go to the log only.
        foreach (var w in ctx.Game.Warnings)
            if (w.Contains(" is not in ") || w.Contains("strings found")) Log.Write($"plugin data: {w}");
            else sb.AppendLine($"WARNING reading plugins: {w}");
        return sb.ToString();
    }

    public static string Json(SaveContext ctx, List<RuleResult> results) => JsonSerializer.Serialize(new
    {
        schema = "savehealth-report/1",
        version = typeof(Report).Assembly.GetName().Version?.ToString(3),
        save = new { file = Path.GetFileName(ctx.Path), ctx.Save.SaveVersion, ctx.Save.PlayerLevel, plugins = ctx.Save.Plugins.Count },
        data = ctx.Game.DataDir,
        verdict = Verdict(results),
        exitCode = ExitCode(results),
        rules = results,
    }, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });
}
