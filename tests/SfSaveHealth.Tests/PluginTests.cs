using System.Text;
using SaveHealth;
using SaveHealth.Rules;
using Xunit;

namespace SfSaveHealth.Tests;

/// <summary>Plugin files in the Data folder (any case), plugins.txt parsing, and which plugins count as official.</summary>
public class PluginTests
{
    [Fact]
    public void DataMasters_finds_esm_files_in_any_case()
    {
        using var d = new TempDir();
        d.File("Alpha.esm");
        d.File("Beta.ESM");
        d.File("Gamma.Esm");
        d.File("Delta.esp");
        d.File("Readme.txt");
        d.File("Epsilon.esm.bak");
        Directory.CreateDirectory(Path.Combine(d.Path, "Sub.esm"));
        Assert.Equal(["Alpha.esm", "Beta.ESM", "Gamma.Esm"], Solo.DataMasters(d.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Parser_HasPlugin_matches_any_spelling()
    {
        using var d = new TempDir();
        d.File("MyMod.ESM");
        var game = new SfsReader.GameData(d.Path);
        Assert.True(game.HasPlugin("mymod.esm"));
        Assert.True(game.HasPlugin("MyMod.esm"));
        Assert.False(game.HasPlugin("Other.esm"));
    }

    static readonly string Body = "# This file is used by Starfield to keep track of your downloaded content.\n" +
                                  "*Starfield.esm\n\n*ModA.esm\nModB.esp\n  *  ModC.esl  \n# a comment\n";

    static (string, bool)[] Expected =>
        [("Starfield.esm", true), ("ModA.esm", true), ("ModB.esp", false), ("ModC.esl", true)];

    [Theory]
    [InlineData("lf")]
    [InlineData("crlf")]
    [InlineData("bom-lf")]
    [InlineData("bom-crlf")]
    [InlineData("utf16")]
    public void PluginList_reads_every_line_ending_and_bom_the_same(string kind)
    {
        using var d = new TempDir();
        string text = kind.Contains("crlf") ? Body.Replace("\n", "\r\n") : Body;
        byte[] bytes = kind switch
        {
            "utf16" => Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray(),
            _ when kind.StartsWith("bom") => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray(),
            _ => Encoding.UTF8.GetBytes(text),
        };
        var list = PluginList.Read(d.File("Plugins.txt", bytes));
        Assert.Equal(Expected, list.Entries.ToArray());
    }

    [Fact]
    public void PluginList_refuses_a_file_that_is_not_a_plugin_list()
    {
        using var d = new TempDir();
        var e = Assert.Throws<UserError>(() => PluginList.Read(d.File("modlist.json", Encoding.UTF8.GetBytes("{\n  \"mods\": []\n}\n"))));
        Assert.Contains("isn't a plugins list", e.Message);
        Assert.Throws<UserError>(() => PluginList.Read(d.File("empty.txt", Encoding.UTF8.GetBytes("# nothing\n\n"))));
        Assert.Throws<UserError>(() => PluginList.Read(Path.Combine(d.Path, "missing.txt")));
    }

    [Theory]
    [InlineData("Starfield.esm", true)]
    [InlineData("starfield.ESM", true)]
    [InlineData("Constellation.esm", true)]
    [InlineData("OldMars.esm", true)]
    [InlineData("ShatteredSpace.esm", true)]
    [InlineData("BlueprintShips-Starfield.esm", true)]
    [InlineData("SFBGS003.esm", true)]
    [InlineData("sfbgs00a_a.esm", true)]
    [InlineData("MyMod.esm", false)]
    [InlineData("MyMod-ShatteredSpaceAddon.esm", false)]
    [InlineData("StarfieldPlus.esm", false)]
    [InlineData("MySFBGS.esm", false)]
    public void IsOfficial_knows_the_official_plugins(string name, bool official) =>
        Assert.Equal(official, ModList.IsOfficial(name));
}
