using System.Text.Json;
using SaveHealth;
using Xunit;

namespace SfSaveHealth.Tests;

/// <summary>--redact: what it hides and what it must leave alone. Redact is static, so every test starts from Reset.</summary>
public class RedactTests : IDisposable
{
    public RedactTests() => Redact.Reset();
    public void Dispose() => Redact.Reset();

    [Fact]
    public void Off_changes_nothing()
    {
        Redact.AddPlayer("Kestrel");   // ignored while off
        const string text = "Save3_0000000AM4B657374726563_x.sfs: Kestrel";
        Assert.False(Redact.On);
        Assert.Equal(text, Redact.Apply(text));
        Assert.Equal(text, Redact.FileName(text));
        Assert.Equal("Kestrel", Redact.Name("Kestrel"));
    }

    [Fact]
    public void Player_name_is_hidden_as_a_whole_word_with_the_same_capitals()
    {
        Redact.Enable();
        Redact.AddPlayer("Solo");
        Assert.Equal("run sf-save-health solo on <player>'s save; Solostar and SOLO stay",
            Redact.Apply("run sf-save-health solo on Solo's save; Solostar and SOLO stay"));
    }

    [Fact]
    public void Short_names_are_hidden_only_where_printed_on_purpose()
    {
        Redact.Enable();
        Redact.AddPlayer("Jo");
        Assert.Equal("Jo and jo stay in text", Redact.Apply("Jo and jo stay in text"));
        Assert.Equal(Redact.Player, Redact.Name("Jo"));
    }

    [Theory]
    [InlineData("Save12_143C1F9EM536F6C6F_000123_Ship.sfs", "Save12_143C1F9EM<name>_000123_Ship.sfs")]
    [InlineData("Save3_143C1F9E_536F6C6F_000024_20261029193005_1_0_4.sfs", "Save3_143C1F9E_<name>_000024_20261029193005_1_0_4.sfs")]
    [InlineData("Autosave1_143C1F9E_536F6C6F_000218_x.sfs", "Autosave1_143C1F9E_<name>_000218_x.sfs")]
    // F is both the sex letter and a hex digit, so it goes with the hidden name; the name is hidden either way.
    [InlineData("Autosave5_0000000AF4B6573_x.sfs", "Autosave5_0000000A<name>_x.sfs")]
    [InlineData("Quicksave0_0000000A536F6C6F_x.sfs", "Quicksave0_0000000A<name>_x.sfs")]
    [InlineData("form 536F6C6F stays", "form 536F6C6F stays")]
    public void Hex_name_in_save_file_names_is_hidden(string text, string expected)
    {
        Redact.Enable();
        Assert.Equal(expected, Redact.Apply(text));
    }

    [Fact]
    public void Profile_folder_is_hidden_plain_json_escaped_and_with_forward_slashes()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile) || profile.Length <= 3) return;   // nothing to hide on this machine
        string home = OperatingSystem.IsWindows() ? "%USERPROFILE%" : "~";
        Redact.Enable();
        string file = Path.Combine(profile, "Saves", "x.sfs");
        Assert.Equal(Path.Combine(home, "Saves", "x.sfs"), Redact.Apply(file));
        Assert.Equal(JsonSerializer.Serialize(Path.Combine(home, "Saves", "x.sfs")), Redact.Apply(JsonSerializer.Serialize(file)));
        Assert.Equal(home + "/Saves/x.sfs", Redact.Apply(file.Replace('\\', '/')));
    }

    [Fact]
    public void FileName_makes_the_placeholders_safe()
    {
        Redact.Enable();
        Redact.AddPlayer("Kestrel");
        // "_" counts as part of a word (as in \w), so the plain name is placed after a "-" here.
        string name = Redact.FileName("Save3_0000000AM4B657374726563_x-Kestrel-solo.txt");
        Assert.Equal("Save3_0000000AM_name__x-_player_-solo.txt", name);
        Assert.True(name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }

    [Fact]
    public void Log_lines_written_before_the_name_is_known_are_redacted_too()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sfsh-test-{Guid.NewGuid():N}.log");
        try
        {
            Redact.Enable();
            Log.Open(path, toConsole: false);
            Log.Write(@"reading C:\Archive\Kestrel-0000000A\Save3_0000000A_4B657374726563_000024_x.sfs");
            Redact.AddPlayer("Kestrel");
            Log.Write("parsed: Kestrel level 1");
            Log.Close();
            string text = File.ReadAllText(path);
            Assert.DoesNotContain("Kestrel", text);
            Assert.Contains(@"reading C:\Archive\<player>-0000000A\Save3_0000000A_<name>_000024_x.sfs", text);
            Assert.Contains("parsed: <player> level 1", text);
        }
        finally { Log.Close(); File.Delete(path); }
    }

    [Fact]
    public void Log_lines_are_written_at_close_when_no_save_was_read()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sfsh-test-{Guid.NewGuid():N}.log");
        try
        {
            Redact.Enable();
            Log.Open(path, toConsole: false);
            Log.Write("error: no save file x.sfs");
            Log.Close();
            Assert.Contains("error: no save file x.sfs", File.ReadAllText(path));
        }
        finally { Log.Close(); File.Delete(path); }
    }

    [Fact]
    public void A_log_that_can_no_longer_be_written_never_throws()
    {
        // A full disk or a removed drive: the writer's stream is gone. Write and Close must carry on quietly, so
        // the check keeps its result and exit code (code review of 8dede78, 2026-11-20).
        string path = Path.Combine(Path.GetTempPath(), $"sfsh-test-{Guid.NewGuid():N}.log");
        try
        {
            Log.Open(path, toConsole: false);
            var field = typeof(Log).GetField("file", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var broken = new MemoryStream();
            var writer = new StreamWriter(broken) { AutoFlush = true };
            broken.Dispose();
            ((StreamWriter?)field.GetValue(null))?.Dispose();
            field.SetValue(null, writer);
            int before = Log.Failures;
            Log.Write("done: Healthy exit 0");
            Assert.Null(field.GetValue(null));             // logging stopped after the first failure
            Log.Write("a second line after the failure");  // no retry, no second note
            Log.Close();
            Assert.Equal(before + 1, Log.Failures);
        }
        finally { Log.Close(); File.Delete(path); }
    }

    [Fact]
    public void Names_are_hidden_in_the_json_report_too()
    {
        Redact.Enable();
        Redact.AddPlayer("Zo\u00eb");
        // System.Text.Json writes the name as Zo\u00EB and the apostrophe as \u0027.
        string json = JsonSerializer.Serialize("Zo\u00eb's ship");
        Assert.Equal("\"<player>\\u0027s ship\"", Redact.Apply(json));
    }

    [Fact]
    public void Solo_log_waits_for_the_second_character_name()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sfsh-test-{Guid.NewGuid():N}.log");
        try
        {
            Redact.Enable();
            Redact.ExpectPlayers(2);
            Log.Open(path, toConsole: false);
            Redact.AddPlayer("Kestrel");
            Log.Write(@"reading C:\Archive\Merganser\Save4.sfs");
            Redact.AddPlayer("Merganser");
            Log.Write("parsed: Merganser level 2");
            Log.Close();
            string text = File.ReadAllText(path);
            Assert.DoesNotContain("Merganser", text);
            Assert.Contains(@"reading C:\Archive\<player>\Save4.sfs", text);
        }
        finally { Log.Close(); File.Delete(path); }
    }

    [Fact]
    public void A_log_file_that_cannot_be_opened_is_a_usage_error()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sfsh-missing-{Guid.NewGuid():N}", "x.log");
        var e = Assert.Throws<UserError>(() => Log.Open(path, toConsole: false));
        Assert.Contains(path, e.Message);
        Log.Close();
    }

    [Fact]
    public void Reset_turns_redaction_off_and_forgets_the_rules()
    {
        Redact.Enable();
        Redact.AddPlayer("Kestrel");
        Redact.Reset();
        Assert.False(Redact.On);
        Redact.Enable();
        Assert.Equal("Kestrel", Redact.Apply("Kestrel"));
    }
}
