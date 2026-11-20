using SfsReader;

namespace SaveHealth;

/// <summary>One save, parsed, with the game's Data folder to check it against.</summary>
public sealed class SaveContext
{
    /// <summary>Header versions the parser has been verified on (game 1.16.244 writes header version 27, save
    /// version 153, in every sample so far). Others are refused.</summary>
    public static readonly (uint Header, byte Save)[] SupportedVersions = [(27, 153)];

    public required string Path { get; init; }
    public required SfsContainer Container { get; init; }
    public required SaveGame Save { get; init; }
    public required SaveBody Body { get; init; }
    public required GameData Game { get; init; }

    public static SaveContext Open(string path, string dataDir, string language)
    {
        if (!File.Exists(path)) throw new UserError($"no save file {path}", "check the path; saves end in .sfs");
        if (!Directory.Exists(dataDir)) throw new UserError($"no Data folder {dataDir}", "pass --data <your Starfield\\Data folder>");
        Log.Write($"reading {path} ({new FileInfo(path).Length:N0} bytes)");
        var (c, g) = SaveFile.Open(path);
        if (!SupportedVersions.Contains((g.EngineVersion, g.SaveVersion)))
            throw new UserError($"{System.IO.Path.GetFileName(path)} has header version {g.EngineVersion}, save version {g.SaveVersion} " +
                                $"(game {g.CurrentGameVersion}); Save Health supports {string.Join(", ", SupportedVersions.Select(v => $"{v.Header}/{v.Save}"))}",
                "the game has probably been updated; this save can't be checked until Save Health supports the new version");
        Redact.AddPlayer(g.PlayerName);
        var body = SaveBody.Parse(c.Inner, g);
        foreach (var w in g.Warnings.Concat(body.Warnings)) Log.Write($"parser warning: {w}");
        Log.Write($"parsed: save {g.SaveNumber}, {Redact.Name(g.PlayerName)} level {g.PlayerLevel}, {g.Plugins.Count} plugins, {body.Records.Count} change records");
        return new SaveContext { Path = path, Container = c, Save = g, Body = body, Game = new GameData(dataDir, language) };
    }
}

/// <summary>An error the user can fix: says what went wrong and what to do, without a stack trace.</summary>
public sealed class UserError(string message, string fix) : Exception(message)
{
    public string Fix { get; } = fix;
}
