# Changelog

## 0.7.2 (unreleased)

### What's new

This release stops quietly ignoring mistakes on the command line. A mistyped option, an option of the other command or an option with its value missing is now refused with a clear error, instead of the run going on without it. `solo` also keeps its saved report when printing fails.

### Example

    > sf-save-health check "Save3_....sfs" --plugin Plugins.txt
    error: check doesn't know --plugin
      fix: use --plugins (options are case-sensitive; sf-save-health --help lists them)

The run ends with exit code 3. The output was checked on 2026-11-22 with the 0.7.2 build.

### Behavior change

- A bare `--data` (at the end of the line, or followed by another option) no longer means the automatic search. It is now a usage error (exit code 3). Write `--data auto` instead.

### Fixes

- Every option that takes a value (`--data`, `--plugins`, `--limit`, `--lang`, `--log`, `--reports`, `--before`, `--after`), given without one, is a usage error (exit code 3) that names the option.
- An option the command doesn't know, such as `--plugin` or `--JSON`, is a usage error (exit code 3). Options stay case-sensitive. When the name is close to a real option, the fix line names it.
- An option of the other command, such as `--json` with `solo` or `--reports` with `check`, is a usage error that says which command the option works with.
- A mistyped option of the other command, such as `--JSON` with `solo`, now asks whether you meant that option (`--json`) and says which command it works with.
- An unknown command given alone or with `--help`, such as `sf-save-health chek`, now says `unknown command chek` before the usage text. With `--redact` on the line, a save path given in its place is hidden there too.
- An option before the save, such as `check --json Save3.sfs`, is a usage error that says to put the save first. Before, the option was read as the save's name.
- `solo` with both a zip and `--before`/`--after` is a usage error. Before, the zip was silently ignored.
- When `solo` can't print its short report (exit code 3), the error now says where the full report was saved, or that it couldn't be saved either.
- A slip in the program's own option table is now reported as an internal error, not as a damaged plugin.
- `--help` anywhere on the line shows the usage and runs nothing. Before, it was ignored when it wasn't first.
- `solo` saves its full report in `--reports` before it prints the short one, so a failed write to the output (exit code 3) no longer loses the saved copy.
- The fix line for a report that can't be written now also names room on the drive and access rights.
- `solo`'s cleanup of its temporary folder no longer stops on an access-denied error. It stays best-effort and writes a log line.
- A refused command line still writes no log file.
- The parser, the usage text and the tests now read one table of options, so a new option can't be left out of the checks.
- New tests cover each of these cases.

## 0.7.1 (2026-11-20)

### What's new

This release tightens the command line and the error messages. An option that takes a value, given twice, is now refused instead of quietly using the first value, and a few error paths now say what really went wrong.

### Example

    > sf-save-health check "Save3_....sfs" --limit 5 --limit 50
    error: --limit is given 2 times
      fix: give --limit only once

The run ends with exit code 3. The output was checked on 2026-11-20 with the 0.7.1 build.

### Fixes

- An option that takes a value (`--data`, `--plugins`, `--limit`, `--lang`, `--log`, `--reports`, `--before`, `--after`), given twice, is now a usage error (exit code 3) that names the option. Before, the first value was used and the second was silently ignored.
- A switch given twice (`--json --json`, `--redact --redact`, `--verbose --verbose`) means the same as once and runs as before.
- An unknown command is reported as an unknown command, even when the rest of the command line also has a repeated option. A refused run writes no log.
- If the report itself can't be written to the output (for example a full disk behind a redirect), the error now says so instead of saying the save couldn't be read.
- The usage text and error messages are written on a best-effort basis: if a write to the error stream fails, that line is skipped and the run still ends with exit code 3.
- A comment about the log's failure note now says which error codes come from the system on Linux and macOS.
- New tests cover `--limit`, repeated options and switches, an unknown command with a repeated option, a report that can't be written, a log that can't be flushed when it closes, and a failing error stream.

### Download

Download `sf-save-health-0.7.1.zip` and its `.sha256` file below, and follow the Install steps in the README.

## 0.7.0 (2026-11-20)

### What's new

This is the first public release of sf-save-health, a read-only checker for Starfield saves.

It compares a save with the mods you have installed now and reports what looks broken: plugins the save needs that are gone, data those mods left behind, forms a mod update removed, quests whose stages or objectives no longer exist, quests from removed mods that are still running, and save bloat. With `solo`, it compares a save from before a test with one from after it and reports only what changed.

### Example

A real save, checked with `--redact` after the player removed some mods (mod names changed, and lines marked `...` were left out):

    > sf-save-health check "Save3_....sfs" --redact
    sf-save-health 0.7.0: Save3_C875E8D7_<name>_000024_20261029193005_1_0_4.sfs
      <player>, level 1, Jemison - The Lodge, playtime 0 days 0 hours 24 minutes, saved 2026-10-29 19:30 UTC
      78 plugins in the save; checked against C:\Program Files (x86)\Steam\steamapps\common\Starfield\Data

    S1. Plugins in the save that are missing from the Data folder: 18 of 78 plugins in the save are missing from the Data folder (10 with data left behind)
      PROBLEM full plugin missing: NewModA.esm [New Mod A]
                2 change records, 3 references inside records
    ...
    B1. Objects created during play (bloat): 6,378 objects created during play (OK; normal 25,000-35,000, note from 60,000, warning from 150,000); save file 1.9 MB
    ...
    11 problem(s), 6 warning(s), 8 note(s).
    Not checked yet: Papyrus script data (orphaned scripts, stuck stacks).

### How it works

- It finds the game's Data folder by itself on Steam and Game Pass installs, and on Linux with Proton.
- `--plugins` also checks the save against a load order (the game's Plugins.txt or a Vortex export of it).
- `--redact` hides the character name, your user and computer names and the profile folder, so you can share a report.
- `--log <file>` and `--verbose` write a troubleshooting log.
- It never changes, moves or locks a save, and it never uploads anything.
- It reads saves with [sf-save-reader](https://github.com/taosecurity/sf-save-reader) 0.1.2 and plugins with [BuildKit](https://github.com/taosecurity/BuildKit) 0.4.1 and Mutagen 0.54.4.

### Download

Download `sf-save-health-0.7.0.zip`, unpack it into a folder of its own, and run `sf-save-health check "<your save>.sfs"`. It needs the .NET 9 runtime. Check the download with `sf-save-health-0.7.0.zip.sha256`. The README has the details, and AGENTS.md has step-by-step instructions for AI assistants.

## Earlier versions

Versions before 0.7.0 were private test builds and were not released.
