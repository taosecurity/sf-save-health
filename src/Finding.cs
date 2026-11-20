namespace SaveHealth;

public enum Level { Note, Warning, Problem }

/// <summary>How far a rule can be trusted: Exact = decoded data compared with plugin data, no guessing;
/// Heuristic = a judgment call that can raise false alarms.</summary>
public enum Reliability { Exact, Heuristic }

/// <summary>One thing a rule found. Key identifies the same finding in another save (for before/after reports).</summary>
public sealed record Finding(string Rule, Level Level, Reliability Reliability, string Key, string Title,
    string Meaning, List<string> Evidence)
{
    public int Count { get; init; } = 1;
}

/// <summary>The result of one rule: its findings plus the counts it looked at (shown even when nothing is found).</summary>
public sealed record RuleResult(string Rule, string Name, List<Finding> Findings, List<string> Summary);
