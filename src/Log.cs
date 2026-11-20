namespace SaveHealth;

/// <summary>Troubleshooting log: what was read, what each rule decided and why something failed.
/// Written to --log &lt;file&gt; (appended), and to the error stream with --verbose.</summary>
public static class Log
{
    static StreamWriter? file;
    static bool verbose;

    public static void Open(string? path, bool toConsole)
    {
        verbose = toConsole;
        if (path is null) return;
        file = new StreamWriter(path, append: true) { AutoFlush = true };
        Write($"sf-save-health {typeof(Log).Assembly.GetName().Version} started: {string.Join(' ', Environment.GetCommandLineArgs().Skip(1))}");
    }

    public static void Write(string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {Redact.Apply(message)}";
        file?.WriteLine(line);
        if (verbose) Console.Error.WriteLine(line);
    }

    public static void Close() { file?.Dispose(); file = null; }
}
