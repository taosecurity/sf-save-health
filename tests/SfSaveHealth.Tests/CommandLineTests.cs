using System.Reflection;
using System.Text;
using SaveHealth;
using Xunit;

namespace SfSaveHealth.Tests;

/// <summary>Command-line options (--limit, repeated options), the log's close path, and the error handlers' console
/// writes. Redact and Log are static, so every test starts and ends from a reset.</summary>
public class CommandLineTests : IDisposable
{
    public CommandLineTests() => Redact.Reset();
    public void Dispose() { Log.Close(); Redact.Reset(); }

    static CommandLine Cl(params string[] args) => new(args);

    // --limit

    [Fact]
    public void Limit_defaults_to_10()
    {
        Assert.Equal(10, Cl("check", "x.sfs").Limit());
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("50", 50)]
    [InlineData("007", 7)]
    [InlineData("2147483647", int.MaxValue)]
    public void Limit_takes_a_whole_number_from_0_up(string given, int expected)
    {
        Assert.Equal(expected, Cl("check", "x.sfs", "--limit", given).Limit());
    }

    [Theory]
    [InlineData("2147483648")]
    [InlineData("99999999999999999999")]
    public void Limit_too_large_says_so(string given)
    {
        var e = Assert.Throws<UserError>(() => Cl("check", "x.sfs", "--limit", given).Limit());
        Assert.Equal($"--limit {given} is too large (at most 2147483647)", e.Message);
        Assert.Contains("--limit 50", e.Fix);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData("ten")]
    [InlineData("1.5")]
    [InlineData("1,000")]
    [InlineData(" 5")]
    [InlineData("")]
    [InlineData("٥")]   // ARABIC-INDIC DIGIT FIVE: a digit, but not an ASCII one
    public void Limit_that_is_not_a_number_from_0_up_says_so(string given)
    {
        var e = Assert.Throws<UserError>(() => Cl("check", "x.sfs", "--limit", given).Limit());
        Assert.Equal($"--limit {given} is not a number from 0 up", e.Message);
        Assert.Contains("--limit 50", e.Fix);
    }

    [Theory]
    [InlineData("check", "x.sfs", "--limit")]
    [InlineData("check", "x.sfs", "--limit", "--json")]
    public void Limit_without_a_value_is_a_usage_error(params string[] args)
    {
        var e = Assert.Throws<UserError>(() => Cl(args).Limit());
        Assert.Equal("--limit needs a value", e.Message);
        Assert.Equal("put the value right after --limit, for example --limit 50", e.Fix);
    }

    // Repeated options

    [Theory]
    [InlineData("check", "x.sfs", "--limit", "5", "--limit", "50")]
    [InlineData("check", "x.sfs", "--log", "a.log", "--json", "--log", "b.log")]
    [InlineData("check", "x.sfs", "--lang", "en", "--lang", "de")]
    [InlineData("check", "x.sfs", "--plugins", "a.txt", "--plugins", "b.txt")]
    [InlineData("check", "x.sfs", "--data", "D", "--data", "E")]
    [InlineData("solo", "x.zip", "--reports", "a", "--reports", "b")]
    [InlineData("solo", "--before", "a.sfs", "--after", "b.sfs", "--before", "c.sfs")]
    [InlineData("solo", "--before", "a.sfs", "--after", "b.sfs", "--after", "c.sfs")]
    public void An_option_with_a_value_given_twice_is_a_usage_error(params string[] args)
    {
        var e = Assert.Throws<UserError>(() => Cl(args).RefuseRepeats());
        string name = args.GroupBy(a => a).First(g => g.Key.StartsWith("--") && g.Count() > 1).Key;
        Assert.Equal($"{name} is given 2 times", e.Message);
        Assert.Equal($"give {name} only once", e.Fix);
    }

    [Theory]
    [InlineData("check", "x.sfs", "--json", "--json")]
    [InlineData("check", "x.sfs", "--verbose", "--verbose")]
    [InlineData("check", "x.sfs", "--redact", "--json", "--redact")]
    public void A_switch_given_twice_is_accepted(params string[] args)
    {
        // An on/off switch has no value to lose, so giving it twice means the same as once.
        Cl(args).RefuseRepeats();
    }

    [Fact]
    public void The_valued_list_is_the_tables_valued_options()
    {
        // Valued is built from the one option table, so it can't lack a valued option the table has.
        Assert.Equal(["--data", "--plugins", "--reports", "--limit", "--lang", "--log", "--before", "--after"], CommandLine.Valued);
        Assert.Equal(CommandLine.Options.Where(o => o.TakesValue).Select(o => o.Name), CommandLine.Valued);
    }

    [Fact]
    public void Every_option_the_program_reads_is_in_the_table()
    {
        // A scan of the source: every Has/Option/Value call names an option in the table, and Option/Value only
        // valued ones. A new option read anywhere without joining the table fails here (and at run time, below).
        string src = Path.Combine(RepoRoot(), "src");
        var calls = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "\\.(Has|Option|Value)\\(\"(--[a-z]+)\"\\)").Cast<System.Text.RegularExpressions.Match>())
            .Select(m => (Kind: m.Groups[1].Value, Name: m.Groups[2].Value)).ToList();
        Assert.True(calls.Count >= 10, $"only {calls.Count} option reads found; the scan's pattern no longer matches the code");
        foreach (var (kind, name) in calls)
        {
            var spec = CommandLine.Options.SingleOrDefault(o => o.Name == name);
            Assert.True(spec is not null, $"{name} is read in src but isn't in CommandLine.Options");
            if (kind != "Has") Assert.True(spec!.TakesValue, $"{name} is read with {kind}() but the table has it as a switch");
        }
    }

    [Fact]
    public void Reading_an_option_that_is_not_in_the_table_throws()
    {
        Assert.Throws<OptionTableError>(() => Cl("check", "x.sfs").Has("--nosuch"));
        Assert.Throws<OptionTableError>(() => Cl("check", "x.sfs").Value("--nosuch"));
        Assert.Throws<OptionTableError>(() => Cl("check", "x.sfs").Option("--json"));   // a switch has no value
    }

    [Fact]
    public void The_option_table_slip_is_an_internal_error_not_a_save_problem()
    {
        // Program.cs catches OptionTableError before its general handler, which would blame a damaged plugin.
        Assert.IsAssignableFrom<InvalidOperationException>(new OptionTableError("x"));
        string program = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Program.cs"));
        int table = program.IndexOf("catch (OptionTableError", StringComparison.Ordinal);
        // Every catch of plain Exception (with or without a when filter, a comment or another variable name) must come
        // after it, so the first one found is the one to compare.
        var general = System.Text.RegularExpressions.Regex.Match(program, @"catch\s*\(\s*Exception\b");
        Assert.True(general.Success, "Program.cs has no catch (Exception ...)");
        Assert.True(table > 0 && general.Index > table, "catch (OptionTableError) must come before every catch (Exception ...)");
        Assert.Contains("internal error in sf-save-health", program);
    }

    [Fact]
    public void The_usage_lists_each_commands_options_as_0_7_1_did()
    {
        Assert.Equal("[--data <Starfield Data folder>] [--plugins <plugins.txt>] [--json] [--limit N] [--lang en] [--redact] [--log <file>] [--verbose]",
            CommandLine.UsageOptions("check"));
        Assert.Equal("[--data <Starfield Data folder>] [--reports <folder>] [--limit N] [--lang en] [--redact] [--log <file>] [--verbose]",
            CommandLine.UsageOptions("solo"));
    }

    [Fact]
    public void Every_option_in_the_usage_text_is_valued_or_a_switch_as_the_table_says()
    {
        var (_, err) = RunMain(["--help"]);
        var shown = System.Text.RegularExpressions.Regex.Matches(err, "\\[(--[a-z]+)( [^\\]]+)?\\]").Cast<System.Text.RegularExpressions.Match>().ToList();
        Assert.NotEmpty(shown);
        foreach (var m in shown)
        {
            var spec = CommandLine.Options.Single(o => o.Name == m.Groups[1].Value);
            Assert.Equal(spec.TakesValue, m.Groups[2].Success);
        }
        foreach (var o in CommandLine.Options) Assert.Contains(o.Name, err);
    }

    static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "src", "Program.cs"))) return d.FullName;
        throw new InvalidOperationException("couldn't find the repo's src folder above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Different_options_and_repeated_values_are_fine()
    {
        // The same value twice (two options pointing at one folder) is not a repeated option.
        Cl("solo", "--before", "a.sfs", "--after", "b.sfs", "--data", "D", "--reports", "D", "--redact", "--verbose").RefuseRepeats();
        Cl("check", "x.sfs").RefuseRepeats();
    }

    [Fact]
    public void Options_are_matched_exactly()
    {
        // --JSON is not --json (options are case-sensitive, as before), so it is not a repeat of it.
        Cl("check", "x.sfs", "--json", "--JSON").RefuseRepeats();
        Assert.False(Cl("check", "x.sfs", "--JSON").Has("--json"));
    }

    [Fact]
    public void A_repeated_option_exits_3_with_its_message_and_writes_no_log()
    {
        using var d = new TempDir();
        string first = Path.Combine(d.Path, "first.log");
        var (code, err) = RunMain(["check", "x.sfs", "--log", first, "--log", Path.Combine(d.Path, "second.log")]);
        Assert.Equal(3, code);
        Assert.Contains("error: --log is given 2 times", err);
        Assert.Contains("fix: give --log only once", err);
        Assert.False(File.Exists(first));
    }

    [Fact]
    public void An_unknown_command_is_reported_before_a_repeated_option()
    {
        using var d = new TempDir();
        string log = Path.Combine(d.Path, "x.log");
        var (code, err) = RunMain(["chek", "save.sfs", "--data", "D", "--data", "E", "--log", log]);
        Assert.Equal(3, code);
        Assert.StartsWith("unknown command chek", err);
        Assert.DoesNotContain("given 2 times", err);
        Assert.False(File.Exists(log));   // a refused run writes no log
    }

    [Fact]
    public void An_unknown_command_message_is_redacted()
    {
        // The command word is the user's own text: under --redact a profile folder in it is hidden like anywhere else.
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var (code, err) = RunMain([Path.Combine(profile, "x.sfs"), "check", "--redact"]);
        Assert.Equal(3, code);
        Assert.StartsWith("unknown command", err);
        Assert.DoesNotContain(profile, err);
    }

    [Fact]
    public void A_repeated_switch_runs_as_before()
    {
        // --json twice is not refused: the run goes on to the save, which doesn't exist here.
        var (code, err) = RunMain(["check", "no-such-save.sfs", "--json", "--json", "--data", "."]);
        Assert.Equal(3, code);
        Assert.DoesNotContain("given 2 times", err);
    }

    [Fact]
    public void A_report_that_cannot_be_written_blames_the_output_not_the_save()
    {
        var old = Console.Out;
        Console.SetOut(new ClosedWriter());
        try
        {
            var e = Assert.Throws<UserError>(() => Output.WriteReport("a report"));
            Assert.Equal("couldn't write the report to the output: The pipe has been ended.", e.Message);
            Assert.Contains("piped or redirected", e.Fix);
            Assert.DoesNotContain("save", e.Message + e.Fix);
        }
        finally { Console.SetOut(old); }
    }

    [Fact]
    public void A_bad_limit_exits_3_with_its_message()
    {
        var (code, err) = RunMain(["check", "x.sfs", "--limit", "-1"]);
        Assert.Equal(3, code);
        Assert.Contains("error: --limit -1 is not a number from 0 up", err);
        (code, err) = RunMain(["check", "x.sfs", "--limit", "2147483648"]);
        Assert.Equal(3, code);
        Assert.Contains("error: --limit 2147483648 is too large (at most 2147483647)", err);
    }

    // Log.Close when the last buffered lines can't be written

    [Fact]
    public void Log_Close_that_cannot_flush_never_throws_and_notes_it_once()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sfsh-test-{Guid.NewGuid():N}.log");
        var err = new StringWriter();
        var old = Console.Error;
        try
        {
            Log.Open(path, toConsole: false);
            var field = typeof(Log).GetField("file", BindingFlags.NonPublic | BindingFlags.Static)!;
            ((StreamWriter?)field.GetValue(null))?.Dispose();
            // AutoFlush off: the line stays in the writer's buffer until Close disposes it, and that flush fails.
            var writer = new StreamWriter(new FullDiskStream()) { AutoFlush = false };
            writer.Write("a buffered line");
            field.SetValue(null, writer);
            Console.SetError(err);
            int before = Log.Failures;
            Log.Close();
            Assert.Equal(before + 1, Log.Failures);
            Assert.Null(field.GetValue(null));
            string note = err.ToString();
            Assert.Contains("note: the log file couldn't be written (IOException 0x80070070)", note);
            Assert.DoesNotContain(path, note);   // the note never names the log's path
            Log.Close();                           // a second Close has nothing left to fail on
            Assert.Equal(before + 1, Log.Failures);
        }
        finally { Console.SetError(old); Log.Close(); File.Delete(path); }
    }

    // The error handlers' console writes are best effort: a closed stderr can't turn exit 3 into a crash.

    [Theory]
    [InlineData("--help", "x")]                         // the usage text
    [InlineData("frob", "x.sfs")]                       // an unknown command
    [InlineData("check", "x.sfs", "--limit", "ten")]    // a UserError
    [InlineData("check", "x.sfs", "--log", "a", "--log", "b")]  // a repeated option
    public void A_closed_error_stream_keeps_exit_3(params string[] args)
    {
        var (code, _) = RunMain(args, new ClosedWriter());
        Assert.Equal(3, code);
    }

    [Fact]
    public void A_closed_error_stream_keeps_exit_3_when_a_save_cant_be_read()
    {
        using var d = new TempDir();
        string save = d.File("Save1_0000000A_4B657374726563_000001_x.sfs", Encoding.ASCII.GetBytes("this is not a save file at all"));
        // With a working stderr the save is refused with an error line.
        var (code, err) = RunMain(["check", save, "--data", d.Path]);
        Assert.Equal(3, code);
        Assert.StartsWith("error", err);
        // With a closed stderr the message is lost, but the run still ends with exit 3 instead of a crash.
        (code, _) = RunMain(["check", save, "--data", d.Path], new ClosedWriter());
        Assert.Equal(3, code);
    }

    /// <summary>Runs the program's entry point with stderr sent to <paramref name="error"/> (or captured).</summary>
    internal static (int Code, string Err) RunMain(string[] args, TextWriter? error = null)
    {
        var captured = new StringWriter();
        var old = Console.Error;
        Console.SetError(error ?? captured);
        try
        {
            var main = typeof(Log).Assembly.EntryPoint!;
            object? result;
            try { result = main.Invoke(null, [args]); }
            catch (TargetInvocationException e) when (e.InnerException is not null) { throw e.InnerException; }
            return ((int)result!, captured.ToString());
        }
        finally
        {
            Console.SetError(old);
            Redact.Reset();
        }
    }

    /// <summary>A stream on a full disk: every write fails with the Windows "disk full" HResult.</summary>
    sealed class FullDiskStream : MemoryStream
    {
        static IOException Full() => new("There is not enough space on the disk.", unchecked((int)0x80070070));
        public override void Write(byte[] buffer, int offset, int count) => throw Full();
        public override void Write(ReadOnlySpan<byte> buffer) => throw Full();
        public override void Flush() { }
    }

    /// <summary>A console stream whose reader went away (a closed pipe): every write fails.</summary>
    sealed class ClosedWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) => throw new IOException("The pipe has been ended.");
        public override void Write(string? value) => throw new IOException("The pipe has been ended.");
        public override void Write(char[] buffer, int index, int count) => throw new IOException("The pipe has been ended.");
        public override void WriteLine(string? value) => throw new IOException("The pipe has been ended.");
    }
}
