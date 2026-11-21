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
    public void Every_valued_option_is_in_the_list()
    {
        // Option() and Value() are only ever asked about these names; a new valued option must join the list.
        Assert.Equal(["--log", "--limit", "--lang", "--plugins", "--reports", "--data", "--before", "--after"], CommandLine.Valued);
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
    static (int Code, string Err) RunMain(string[] args, TextWriter? error = null)
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
