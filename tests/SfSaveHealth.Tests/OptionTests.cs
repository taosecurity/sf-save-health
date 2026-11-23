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
    // The case-sensitive note comes only with a letter-case slip, not a spelling one.
    [InlineData("check", "--plugin", "use --plugins (sf-save-health --help lists them)")]
    [InlineData("check", "--JSON", "use --json (options are case-sensitive; sf-save-health --help lists them)")]
    [InlineData("check", "--verbos", "use --verbose (sf-save-health --help lists them)")]
    [InlineData("solo", "--report", "use --reports (sf-save-health --help lists them)")]
    [InlineData("solo", "--befor", "use --before (sf-save-health --help lists them)")]
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
        if (string.IsNullOrEmpty(profile) || profile.Length <= 3) return;   // nothing to hide on this machine
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
        if (string.IsNullOrEmpty(profile) || profile.Length <= 3) return;   // nothing to hide on this machine
        var (code, err) = RunMain([Path.Combine(profile, "x.sfs"), "--redact", "--help"]);
        Assert.Equal(3, code);
        Assert.StartsWith("unknown command", err);
        Assert.Contains("usage: sf-save-health check <save.sfs>", err);
        Assert.DoesNotContain(profile, err);
    }

    // An option of the other command, mistyped.

    [Theory]
    [InlineData("solo", "--JSON", "did you mean --json? it works only with check (options are case-sensitive")]
    [InlineData("solo", "--plugin", "did you mean --plugins? it works only with check (sf-save-health --help lists solo's options)")]
    [InlineData("check", "--report", "did you mean --reports? it works only with solo (sf-save-health --help lists check's options)")]
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

    // Extra words (since 0.7.3): a word that is no option, no value and not the save or zip is refused.

    [Theory]
    [InlineData("check|a.sfs|b.sfs", "check takes one save, but got an extra word \"b.sfs\"")]
    [InlineData("check|a.sfs|--json|b.sfs", "check takes one save, but got an extra word \"b.sfs\"")]
    [InlineData("check|a.sfs|--limit|5|b.sfs", "check takes one save, but got an extra word \"b.sfs\"")]
    [InlineData("check|a.sfs|b.sfs|--json", "check takes one save, but got an extra word \"b.sfs\"")]
    [InlineData("solo|x.zip|y.zip", "solo takes one zip, but got an extra word \"y.zip\"")]
    [InlineData("solo|--before|a.sfs|--after|b.sfs|extra", "solo with --before and --after takes no zip or other word, but got an extra word \"extra\"")]
    [InlineData("solo|--before|a.sfs|extra|--after|b.sfs", "solo with --before and --after takes no zip or other word, but got an extra word \"extra\"")]
    public void An_extra_word_exits_3_naming_it_before_any_log_is_opened(string line, string message)
    {
        using var d = new TempDir();
        string log = Path.Combine(d.Path, "x.log");
        var (code, err) = RunMain([.. line.Split('|'), "--log", log]);
        Assert.Equal(3, code);
        Assert.Contains($"error: {message}", err);
        Assert.Contains("a path with spaces needs quotes: sf-save-health", err);
        Assert.False(File.Exists(log));   // a refused run writes no log
    }

    [Theory]
    [InlineData("check", "C:\\My", "Saves\\x.sfs")]
    [InlineData("solo", "C:\\My", "Logs\\StarfieldLogs-1.zip")]
    public void An_unquoted_path_with_a_space_is_recognized_as_one_split_path(string command, string first, string second)
    {
        using var d = new TempDir();
        string log = Path.Combine(d.Path, "x.log");
        var (code, err) = RunMain([command, first, second, "--log", log]);
        Assert.Equal(3, code);
        Assert.Contains($"error: \"{first}\" and \"{second}\" look like one path with a space in it, split in two", err);
        Assert.Contains($"fix: a path with spaces needs quotes: sf-save-health {command} \"{first} {second}\"", err);
        Assert.False(File.Exists(log));
    }

    [Fact]
    public void A_split_path_after_an_options_value_is_recognized_too()
    {
        var (code, err) = RunMain(["solo", "x.zip", "--reports", "C:\\My", "Reports"]);
        Assert.Equal(3, code);
        Assert.Contains("error: \"C:\\My\" and \"Reports\" look like one path with a space in it, split in two", err);
        Assert.Contains("fix: a path with spaces needs quotes: put the whole path in quotes after its option, for example \"C:\\My Reports\"", err);
    }

    [Fact]
    public void An_extra_word_after_a_plain_name_is_not_called_a_split_path()
    {
        var (code, err) = RunMain(["check", "a.sfs", "b.sfs"]);
        Assert.Equal(3, code);
        Assert.DoesNotContain("look like one path", err);
    }

    [Theory]
    [InlineData("check", "C:\\Saves\\a.sfs", "C:\\Saves\\b.sfs", "check takes one save")]
    [InlineData("solo", "C:\\Logs\\a.zip", "C:\\Logs\\b.zip", "solo takes one zip")]
    public void Two_complete_files_are_not_called_one_split_path(string command, string first, string second, string what)
    {
        var (code, err) = RunMain([command, first, second]);
        Assert.Equal(3, code);
        Assert.DoesNotContain("look like one path", err);
        Assert.Contains($"error: {what}, but got an extra word \"{second}\"", err);
    }

    [Fact]
    public void An_option_error_still_comes_before_an_extra_word()
    {
        var (_, err) = RunMain(["check", "a.sfs", "b.sfs", "--plugin", "p"]);
        Assert.Contains("doesn't know --plugin", err);
        var (_, err2) = RunMain(["check", "--json", "a.sfs"]);
        Assert.Contains("check needs the save right after the command", err2);
    }

    [Fact]
    public void An_extra_word_message_is_redacted_under_redact()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile) || profile.Length <= 3) return;   // nothing to hide on this machine
        var (code, err) = RunMain(["check", Path.Combine(profile, "My"), Path.Combine("Saves", "x.sfs"), "--redact"]);
        Assert.Equal(3, code);
        Assert.Contains("split in two", err);
        Assert.DoesNotContain(profile, err);
    }

    [Fact]
    public void Values_are_not_extra_words()
    {
        new CommandLine(["check", "a.sfs", "--plugins", "p.txt", "--limit", "5", "--log", "l.log"]).RefuseUnknown("check");
        new CommandLine(["solo", "--before", "a.sfs", "--after", "b.sfs", "--reports", "r"]).RefuseUnknown("solo");
    }

    // -h and help anywhere (since 0.7.3), like --help.

    [Theory]
    [InlineData("check|x.sfs|-h")]
    [InlineData("check|x.sfs|help")]
    [InlineData("solo|x.zip|-h")]
    [InlineData("solo|--before|a.sfs|--after|b.sfs|help")]
    [InlineData("check|help")]
    [InlineData("check|-h")]
    [InlineData("check|x.sfs|--json|-h|--limit|5")]
    public void Dash_h_or_help_anywhere_shows_the_usage_and_runs_nothing(string line)
    {
        using var d = new TempDir();
        string log = Path.Combine(d.Path, "x.log");
        var (code, err) = RunMain([.. line.Split('|'), "--log", log]);
        Assert.Equal(3, code);
        Assert.StartsWith("usage: sf-save-health check <save.sfs>", err);
        Assert.False(File.Exists(log));
    }

    [Fact]
    public void An_unknown_command_with_dash_h_after_it_says_so_then_shows_the_usage()
    {
        var (code, err) = RunMain(["chek", "x.sfs", "-h"]);
        Assert.Equal(3, code);
        Assert.StartsWith("unknown command chek", err);
        Assert.Contains("usage: sf-save-health check <save.sfs>", err);
    }

    [Fact]
    public void Help_as_an_options_value_is_that_value_not_a_help_request()
    {
        Assert.False(CommandLine.WantsHelp(["check", "x.sfs", "--plugins", "help"]));
        Assert.False(CommandLine.WantsHelp(["solo", "x.zip", "--reports", "help"]));
        Assert.False(CommandLine.WantsHelp(["check", "x.sfs", "--log", "-h"]));
        var (_, err) = RunMain(["solo", "x.zip", "--reports", "help", "--json"]);
        Assert.DoesNotContain("usage:", err);   // refused for --json, not shown as a help request
        Assert.True(CommandLine.WantsHelp(["check", "x.sfs", "--json", "help"]));
        Assert.True(CommandLine.WantsHelp(["check", "x.sfs", "--reports", "x", "-h"]));
    }

    [Fact]
    public void The_usage_text_says_that_dash_h_and_help_show_it()
    {
        var (_, err) = RunMain(["--help"]);
        Assert.Contains("-h or help anywhere on the line shows this text", err);
    }

    [Fact]
    public void Help_is_a_help_request_never_an_extra_word()
    {
        var (_, err) = RunMain(["check", "a.sfs", "b.sfs", "help"]);
        Assert.StartsWith("usage:", err);
        Assert.DoesNotContain("extra word", err);
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
