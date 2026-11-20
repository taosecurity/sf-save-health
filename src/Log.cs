namespace SaveHealth;

/// <summary>Troubleshooting log: what was read, what each rule decided and why something failed.
/// Written to --log &lt;file&gt; (appended), and to the error stream with --verbose.
/// With --redact, lines are held back until the save's character name is known (or the log closes), so a name that is
/// part of a folder name in the first lines (the command line, "reading ...") is hidden too.</summary>
public static class Log
{
    static StreamWriter? file;
    static bool verbose;
    static readonly List<(string Stamp, string Message)> pending = [];

    public static void Open(string? path, bool toConsole)
    {
        verbose = toConsole;
        if (path is null) return;
        file = new StreamWriter(path, append: true) { AutoFlush = true };
        Write($"sf-save-health {typeof(Log).Assembly.GetName().Version} started: {string.Join(' ', Environment.GetCommandLineArgs().Skip(1))}");
    }

    public static void Write(string message)
    {
        string stamp = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        if (Redact.On && !Redact.HasPlayer)
        {
            if (file is not null || verbose) pending.Add((stamp, message));
            return;
        }
        Flush();
        Emit(stamp, message);
    }

    public static void Close()
    {
        Flush();
        file?.Dispose();
        file = null;
        verbose = false;
    }

    static void Flush()
    {
        foreach (var (stamp, message) in pending) Emit(stamp, message);
        pending.Clear();
    }

    static void Emit(string stamp, string message)
    {
        string line = $"{stamp} {Redact.Apply(message)}";
        file?.WriteLine(line);
        if (verbose) Console.Error.WriteLine(line);
    }
}
