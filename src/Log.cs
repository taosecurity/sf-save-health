namespace SaveHealth;

/// <summary>Troubleshooting log: what was read, what each rule decided and why something failed.
/// Written to --log &lt;file&gt; (appended), and to the error stream with --verbose.
/// With --redact, lines are held back until every save's character name is known (solo reads two saves; see
/// <see cref="Redact.ExpectPlayers"/>), or the log closes, so a name that is
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
        try { file = new StreamWriter(path, append: true) { AutoFlush = true }; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new UserError($"couldn't open the log file {path}: {e.Message}", "pass --log with a file in a folder that exists and that you can write to");
        }
        Write($"sf-save-health {typeof(Log).Assembly.GetName().Version?.ToString(3)} started: {string.Join(' ', Environment.GetCommandLineArgs().Skip(1))}");
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
        try { file?.Dispose(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Failed(e); }
        file = null;
        verbose = false;
    }

    static void Flush()
    {
        var lines = pending.ToList();
        pending.Clear();
        foreach (var (stamp, message) in lines) Emit(stamp, message);
    }

    // A log that can't be written (a full disk, a removed drive, a closed stream) must never change the result or the
    // exit code: logging stops after one note on the error stream, and the check carries on.
    static void Emit(string stamp, string message)
    {
        string line = $"{stamp} {Redact.Apply(message)}";
        try { file?.WriteLine(line); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException) { Failed(e); }
        if (verbose)
            try { Console.Error.WriteLine(line); }
            catch (Exception e) when (e is IOException or ObjectDisposedException) { verbose = false; }
    }

    static void Failed(Exception e)
    {
        try { file?.Dispose(); } catch (Exception) { }
        file = null;
        try { Console.Error.WriteLine(Redact.Apply($"note: the log file can't be written any more ({e.Message}), so logging stopped; the check itself goes on")); }
        catch (Exception) { }
    }
}
