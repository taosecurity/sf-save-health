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
