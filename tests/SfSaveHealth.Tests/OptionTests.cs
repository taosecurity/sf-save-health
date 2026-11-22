using SaveHealth;
using Xunit;
using static SfSaveHealth.Tests.CommandLineTests;

namespace SfSaveHealth.Tests;

/// <summary>Since 0.7.2 a command line the command can't use is refused (exit 3) before anything runs and before
/// --log is opened: a valued option with no value, an option the command doesn't know, and a lone unknown command.</summary>
public class OptionTests : IDisposable
{
    public OptionTests() => Redact.Reset();
    public void Dispose() { Log.Close(); Redact.Reset(); }

    // A valued option with no value: last, or followed by another option. Every valued option, both commands.

    public static TheoryData<string, string, string> BareValuedOptions()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (name, example) in new[] { ("--data", "auto"), ("--plugins", "Plugins.txt"), ("--limit", "50"), ("--lang", "en"), ("--log", "sf-save-health.log") })
            data.Add("check", name, example);
        foreach (var (name, example) in new[] { ("--data", "auto"), ("--reports", "reports"), ("--limit", "50"), ("--lang", "en"), ("--log", "sf-save-health.log"), ("--before", "a.sfs"), ("--after", "b.sfs") })
            data.Add("solo", name, example);
        return data;
    }

    [Theory]
    [MemberData(nameof(BareValuedOptions))]
    public void A_valued_option_at_the_end_with_no_value_exits_3(string command, string name, string example)
    {
        using var d = new TempDir();
        string log = Path.Combine(d.Path, "x.log");
        string[] head = name == "--log" ? [command, "x.sfs"] : [command, "x.sfs", "--log", log];
        var (code, err) = RunMain([.. head, name]);
        Assert.Equal(3, code);
        Assert.Contains($"error: {name} needs a value", err);
        Assert.Contains($"fix: put the value right after {name}, for example {name} {example}", err);
        Assert.False(File.Exists(log));   // a refused run writes no log
    }

    [Theory]
    [MemberData(nameof(BareValuedOptions))]
    public void A_valued_option_followed_by_another_option_exits_3(string command, string name, string example)
    {
        using var d = new TempDir();
        string log = Path.Combine(d.Path, "x.log");
        string[] args = name == "--log" ? [command, "x.sfs", name, "--verbose"] : [command, "x.sfs", name, "--verbose", "--log", log];
        var (code, err) = RunMain(args);
        Assert.Equal(3, code);
        Assert.Contains($"error: {name} needs a value", err);
        Assert.Contains($"for example {name} {example}", err);
        Assert.False(File.Exists(log));
    }

    [Fact]
    public void A_bare_data_no_longer_means_auto()
    {
        // Before 0.7.2 "--data" with nothing after it searched for the Data folder; now it's a usage error.
        var (code, err) = RunMain(["check", "no-such-save.sfs", "--data"]);
        Assert.Equal(3, code);
        Assert.Contains("error: --data needs a value", err);
        Assert.DoesNotContain("couldn't find the Starfield Data folder", err);
        Assert.DoesNotContain("no-such-save", err);   // refused before the save is opened
    }

    // An option the command doesn't know.

    [Theory]
    [InlineData("check", "--plugin", "use --plugins")]
    [InlineData("check", "--JSON", "use --json")]
    [InlineData("check", "--verbos", "use --verbose")]
    [InlineData("solo", "--report", "use --reports")]
    [InlineData("solo", "--befor", "use --before")]
    [InlineData("check", "--frobnicate", "check the spelling; sf-save-health --help lists the options")]
    public void A_mistyped_option_exits_3_naming_it_and_the_closest_option(string command, string given, string fix)
    {
        using var d = new TempDir();
        string log = Path.Combine(d.Path, "x.log");
        var (code, err) = RunMain([command, "x.sfs", given, "--log", log]);
        Assert.Equal(3, code);
        Assert.Contains($"error: {command} doesn't know {given}", err);
        Assert.Contains($"fix: {fix}", err);
        Assert.False(File.Exists(log));
    }

    [Theory]
    [InlineData("check", "--reports", "solo")]
    [InlineData("check", "--before", "solo")]
    [InlineData("check", "--after", "solo")]
    [InlineData("solo", "--json", "check")]
    [InlineData("solo", "--plugins", "check")]
    public void An_option_of_the_other_command_exits_3(string command, string given, string other)
    {
        using var d = new TempDir();
        string log = Path.Combine(d.Path, "x.log");
        string[] args = given == "--json" ? [command, "x.zip", given, "--log", log] : [command, "x.sfs", given, "v", "--log", log];
        var (code, err) = RunMain(args);
        Assert.Equal(3, code);
        Assert.Contains($"error: {command} doesn't know {given}", err);
        Assert.Contains($"fix: {given} works only with {other}; sf-save-health --help lists {command}'s options", err);
        Assert.False(File.Exists(log));
    }

    [Fact]
    public void The_command_word_is_not_case_sensitive_but_options_are()
    {
        var (code, err) = RunMain(["CHECK", "x.sfs", "--Json"]);
        Assert.Equal(3, code);
        Assert.Contains("error: check doesn't know --Json", err);
        Assert.Contains("fix: use --json", err);
    }

    [Fact]
    public void Every_option_in_the_table_is_accepted_by_its_commands()
    {
        foreach (var o in CommandLine.Options)
            foreach (var command in new[] { "check", "solo" }.Where(o.For))
            {
                // solo's pair form takes no zip (a zip and a pair together are refused).
                string[] args = o.Name is "--before" or "--after" ? [command, "--before", "a.sfs", "--after", "b.sfs"]
                    : o.TakesValue ? [command, "x.sfs", o.Name, o.Example] : [command, "x.sfs", o.Name];
                new CommandLine(args).RefuseUnknown(command);
            }
    }

    [Fact]
    public void Values_and_the_save_are_not_mistaken_for_options()
    {
        // Words that don't start with -- (the save, values, and a value such as -1 for --limit) aren't options.
        new CommandLine(["check", "x.sfs", "--limit", "-1", "--lang", "en", "--json"]).RefuseUnknown("check");
        new CommandLine(["solo", "--before", "a.sfs", "--after", "b.sfs", "--reports", "r"]).RefuseUnknown("solo");
    }

    [Fact]
    public void An_unknown_option_is_reported_before_a_repeated_one()
    {
        var (code, err) = RunMain(["check", "x.sfs", "--plugin", "a", "--lang", "en", "--lang", "de"]);
        Assert.Equal(3, code);
        Assert.Contains("doesn't know --plugin", err);
        Assert.DoesNotContain("given 2 times", err);
    }

    [Fact]
    public void A_mistyped_option_message_is_redacted()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var (code, err) = RunMain(["check", "x.sfs", "--redact", "--" + Path.Combine(profile, "x")]);
        Assert.Equal(3, code);
        Assert.Contains("doesn't know", err);
        Assert.DoesNotContain(profile, err);
    }

    // The command word.

    [Fact]
    public void A_lone_unknown_command_says_so_and_shows_the_usage()
    {
        var (code, err) = RunMain(["chek"]);
        Assert.Equal(3, code);
        Assert.StartsWith("unknown command chek", err);
        Assert.Contains("usage: sf-save-health check <save.sfs>", err);
    }

    [Theory]
    [InlineData("check")]
    [InlineData("solo")]
    [InlineData("--help")]
    [InlineData("help")]
    [InlineData("-h")]
    public void A_lone_known_word_shows_only_the_usage(string word)
    {
        var (code, err) = RunMain([word]);
        Assert.Equal(3, code);
        Assert.StartsWith("usage: sf-save-health check <save.sfs>", err);
        Assert.DoesNotContain("unknown command", err);
    }

    [Fact]
    public void An_unknown_command_with_help_after_it_still_says_unknown_command()
    {
        var (code, err) = RunMain(["chek", "x.sfs", "--help"]);
        Assert.Equal(3, code);
        Assert.StartsWith("unknown command chek", err);
        Assert.Contains("usage: sf-save-health check <save.sfs>", err);
    }

    [Fact]
    public void An_unknown_command_with_help_after_it_is_redacted()
    {
        // The usage path names the first word too: under --redact a profile folder in it is hidden there as well.
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var (code, err) = RunMain([Path.Combine(profile, "x.sfs"), "--redact", "--help"]);
        Assert.Equal(3, code);
        Assert.StartsWith("unknown command", err);
        Assert.Contains("usage: sf-save-health check <save.sfs>", err);
        Assert.DoesNotContain(profile, err);
    }

    // An option of the other command, mistyped.

    [Theory]
    [InlineData("solo", "--JSON", "did you mean --json? it works only with check (options are case-sensitive")]
    [InlineData("solo", "--plugin", "did you mean --plugins? it works only with check (options are case-sensitive")]
    [InlineData("check", "--report", "did you mean --reports? it works only with solo (options are case-sensitive")]
    public void A_mistyped_option_of_the_other_command_names_that_command(string command, string given, string fix)
    {
        var (code, err) = RunMain([command, "x.zip", given]);
        Assert.Equal(3, code);
        Assert.Contains($"error: {command} doesn't know {given}", err);
        Assert.Contains($"fix: {fix}", err);
    }

    // The save or zip's place.

    [Theory]
    [InlineData("check", "--json", "x.sfs")]
    [InlineData("check", "--limit", "5", "x.sfs")]
    public void Check_with_an_option_before_the_save_exits_3(params string[] rest)
    {
        using var d = new TempDir();
        string log = Path.Combine(d.Path, "x.log");
        var (code, err) = RunMain([.. rest, "--log", log]);
        Assert.Equal(3, code);
        Assert.Contains("error: check needs the save right after the command", err);
        Assert.False(File.Exists(log));
    }

    [Fact]
    public void Solo_with_both_a_zip_and_a_pair_exits_3()
    {
        var (code, err) = RunMain(["solo", "x.zip", "--before", "a.sfs", "--after", "b.sfs"]);
        Assert.Equal(3, code);
        Assert.Contains("error: solo got both x.zip and --before/--after", err);
    }

    [Fact]
    public void Solo_with_an_option_before_the_zip_exits_3()
    {
        var (code, err) = RunMain(["solo", "--limit", "5", "x.zip"]);
        Assert.Equal(3, code);
        Assert.Contains("error: solo needs the zip right after the command", err);
    }

    [Fact]
    public void Help_after_a_command_shows_the_usage_and_runs_nothing()
    {
        using var d = new TempDir();
        string log = Path.Combine(d.Path, "x.log");
        var (code, err) = RunMain(["check", "x.sfs", "--log", log, "--help"]);
        Assert.Equal(3, code);
        Assert.StartsWith("usage: sf-save-health check <save.sfs>", err);
        Assert.False(File.Exists(log));
    }
}
