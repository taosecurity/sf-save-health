# AGENTS.md: sf-save-health

Instructions for AI assistants (and people) using sf-save-health. Every command below was run once before release.

## 1. What it is for

sf-save-health checks a Starfield `.sfs` save against the plugins (mods) installed in the game's Data folder, read-only, and reports what looks broken: plugins the save was made with that are missing (and whether they left data behind), forms that no longer exist in their plugin, quests whose done stages or objectives the installed plugins no longer define, running quests from removed mods, save bloat, and (with a load order file) plugins the load order doesn't turn on. `solo` compares a save from before a test with one from after it and reports only what is new.

Use it when a player asks whether a save is safe after changing mods, what a removed mod left behind, why a quest is stuck after a mod update, whether a save is bloated, or what one mod changed in a save.

Don't use it to edit, clean or repair a save: it never writes to a save.

## 2. Prerequisites

1. Windows 10 or 11 for the release zip. Linux works when built from source; there the automatic Data search checks only `~/.steam/steam`, so pass `--data`.
2. To run the release zip: the .NET 9 runtime or later. To build from source: the .NET SDK 9.0 or later (tested with 10.0.404, building for net9.0) and Git. Check with `dotnet --version`.
3. An installed copy of Starfield (Steam or Game Pass), or its Data folder.
4. A save written by Starfield 1.16.244 (header version 27, save version 153). Game Pass saves must be exported to `.sfs` first (README, "Where saves and plugins are").

## 3. Install

From the release: download `sf-save-health-<version>.zip` from https://github.com/taosecurity/sf-save-health/releases and unpack it into an empty folder. The program is `sf-save-health.exe` in the `sf-save-health-<version>` folder inside it.

From source:

```
git clone --recursive https://github.com/taosecurity/sf-save-health
cd sf-save-health
dotnet publish src/SaveHealth.csproj -c Release -o publish
```

The program is `publish\sf-save-health.exe` (Windows) or `dotnet publish/sf-save-health.dll` (Linux). The build also makes `bin` and `obj` folders in the clone, and `-o publish` overwrites the `publish` folder; git ignores all three. If the clone was made without `--recursive`, run `git submodule update --init` first.

How to start it. The commands below call it `sf-save-health`; replace that with the program's path:

- Command Prompt (cmd): `"C:\path\to\publish\sf-save-health.exe" --help`
- PowerShell: `& "C:\path\to\publish\sf-save-health.exe" --help` (from the clone folder, `.\publish\sf-save-health.exe --help` also works)
- Linux: `dotnet /path/to/publish/sf-save-health.dll --help`

To type plain `sf-save-health` instead, add the folder to PATH for the current window: `set PATH=%PATH%;C:\path\to\publish` (cmd) or `$env:PATH += ";C:\path\to\publish"` (PowerShell).

## 4. Prove the install works

```
sf-save-health --help
```

Expected: the usage text on stderr, starting `usage: sf-save-health check <save.sfs>`, and exit code 0 (since 0.7.4; 0.7.3 and older exit 3 here). To read only the version, run `sf-save-health --version`: it prints `sf-save-health 0.7.4` on stdout and exits 0. The version is also in the first line of every report, for example `sf-save-health 0.7.4: Save3_....sfs`.

To run the unit tests from a source clone (made-up files only, no game or saves needed): `dotnet test tests/SfSaveHealth.Tests -c Release` (expect all tests passed).

## 5. Common tasks

Steam saves are in `%USERPROFILE%\Documents\My Games\Starfield\Saves`. Quote every path. The Data folder is found automatically on Steam and Game Pass; add `--data "<Starfield\Data>"` when it isn't.

The paths below use Command Prompt (cmd) syntax. In PowerShell, write `%USERPROFILE%` as `$env:USERPROFILE` and `%LOCALAPPDATA%` as `$env:LOCALAPPDATA`, inside double quotes, for example `"$env:LOCALAPPDATA\Starfield\Plugins.txt"`.

What to expect: each command exits 0, 1 or 2 when the check ran (section 7). Exit 2 is normal for a save made with mods that are no longer installed; it means the report found problems, not that the tool failed. Only exit 3 means the check didn't run.

1. Check one save against the installed mods:
   `sf-save-health check "<save.sfs>"`
2. Also check it against the load order the game will use:
   `sf-save-health check "<save.sfs>" --plugins "%LOCALAPPDATA%\Starfield\Plugins.txt"`
   This adds rules M1 and M2 to the report, so it usually lists more problems and notes than task 1 for the same save.
3. The same report as JSON:
   `sf-save-health check "<save.sfs>" --json`
4. More examples per finding (default 10):
   `sf-save-health check "<save.sfs>" --limit 50`
   `--limit` takes a whole number from 0 up to 2147483647. Rules Q2 and Q3 list one finding per quest, up to 10 times the limit; the rest become one `... N more quest(s)` finding, so the exit code still counts them.
5. What changed between a save made before a test and one made after it:
   `sf-save-health solo --before "<a.sfs>" --after "<b.sfs>"`
6. The same from an sf-log-collector zip that holds `saves\before-*` and `saves\after-*`:
   `sf-save-health solo "<StarfieldLogs-*.zip>"`
7. Output that is safe to share (no character, user or computer name, no profile folder):
   add `--redact` to any command.
8. A log for troubleshooting:
   add `--verbose` (log to stderr) or `--log "<file.log>"` to any command.

## 6. Inputs and outputs

- Input: one `.sfs` file for `check`; two `.sfs` files (`--before`, `--after`) or one sf-log-collector zip for `solo`.
- `--data <folder>|auto`: the game's Data folder. With no `--data`, or with `--data auto`: the `STARFIELD_DATA` environment variable, then every Steam library, then `<drive>:\XboxGames\Starfield\Content\Data`. The first folder that holds `Starfield.esm` is used.
- `--plugins <file>` (check only): the game's `%LOCALAPPDATA%\Starfield\Plugins.txt` or a Vortex export of it. A modlist JSON is refused.
- Output: the text report on stdout; with `--json`, one JSON object (`schema` `savehealth-report/1`, `version`, `save`, `data`, `verdict`, `exitCode`, `rules`). Errors and `--verbose` lines go to stderr.
- Files written: `--log <file>` (appended to), and `solo`'s full report, `<name>.txt` in `--reports <folder>` (default `sf-save-health-reports` in the current folder; the folder, given or default, is created if needed). `solo` with a zip unpacks the two saves to a temporary folder and deletes it afterwards. Nothing else.

## 7. Exit codes

| Code | Meaning |
|---|---|
| 0 | Healthy: no findings. Also `--help`, `-h` or `help` anywhere on the line (the usage text; runs nothing) and `--version` anywhere on the line (prints the version; runs nothing). A word right after an option that takes a value is that value, so `--reports help` is no help request. |
| 1 | Only warnings or notes. |
| 2 | At least one problem. |
| 3 | Couldn't check: usage error (an extra word that is not an option, an option's value or the save or zip, such as a second save or a path with a space that isn't in quotes (write `check "C:\My Saves\x.sfs"`); an unknown command, such as `chek` alone or with `--help` or `--version`, which prints `unknown command chek` and the usage; a command with nothing after it (`check` alone), which prints the usage; an option the command doesn't know, such as `--plugin` or `--JSON` (options are case-sensitive; the `fix:` line names a close option when there is one); an option of the other command, such as `--json` with `solo` or `--reports` with `check`, mistyped or not; an option before the save or zip, such as `check --json Save3.sfs` (the save comes right after the command); `solo` with both a zip and `--before`/`--after`; an option that takes a value given without one, such as `--data` at the end of the line or before another option (a bare `--data` no longer means auto: write `--data auto`); an option that takes a value (`--data`, `--plugins`, `--limit`, `--lang`, `--log`, and solo's `--reports`, `--before`, `--after`; the rule covers all eight for every command) given more than once, or a `--limit` that isn't a number from 0 up or is larger than 2147483647), a missing or unreadable file, a file that isn't a save, an unsupported save version, a `--log` file that can't be opened, a report that couldn't be written to the output (for example a full disk behind a redirect), a plugin that can't be read, or no Data folder found. The `error:` line names the file and the `fix:` line says what to do. A switch (`--json`, `--redact`, `--verbose`) given twice is accepted. |

If `solo` can't save its full report into `--reports`, it prints an `error:` line on stderr but still exits 0, 1 or 2, because the check ran and the report was printed on stdout. `solo` saves that full report before it prints, so if the stdout write fails (exit 3), the saved copy in `--reports` is kept. A refused command line (any usage error) writes no log file.

## 8. Log

No log file unless asked for. If the `--log` file can't be written during a run (a full disk, a removed drive), logging stops with one `note:` line on stderr, and the result and exit code are unchanged. `--verbose` writes timestamped lines to stderr; `--log <file>` appends the same lines to that file, starting with the version and the command line. The log holds the save's size and header, the Data folder used, each rule's counts and decisions (each missing plugin, each quest rule's totals), plugin data warnings and parser warnings. With `--redact` the log is redacted too. Without it, the log holds the full command line and save paths, which can include the user's profile folder and a character name in a folder name; add `--redact` to any run whose log or report will be shared.

## 9. Limits

1. It never changes, moves or locks a save, never uploads anything and doesn't use the network.
2. Only saves written by Starfield 1.16.244 (header version 27, save version 153) are checked; others exit 3. Loading an old save in the current game and saving again makes a supported save.
3. It checks against one Data folder. Under Mod Organizer 2, run it from MO2 as an MO2 executable, or the mods' plugins are not seen and every mod looks missing.
4. Papyrus script data (orphaned scripts, stuck stacks) is not checked yet. Rules Q4, Q5 and B1 are heuristics and can raise false alarms; say so when you report them.
5. `--redact` hides names and paths, not plugin names, form IDs, quest names or locations.
6. Don't tell a player a save is "safe" from exit code 0 alone: report what was checked and what wasn't (the last lines of the report).
