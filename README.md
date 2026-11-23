# sf-save-health

sf-save-health checks a Starfield save (`.sfs`) against the mods you have installed now and tells you what looks broken. Each finding says what it means and what you can do about it.

Use it before you load an old save after changing your mod list, after you remove a mod, or when a save starts acting strangely. It finds plugins the save needs that are gone, data those mods left behind, quests that a mod update broke, and save bloat (the full list is under "What it checks"). With `solo` it compares a save from before a test with one from after it and reports only what changed, which shows what one mod did to a save.

It only reads. It never changes, moves or locks a save, and it never uploads anything.

Download the latest zip from [Releases](https://github.com/taosecurity/sf-save-health/releases).

## First command

Unpack `sf-save-health-<version>.zip` into a folder of its own, open a Command Prompt in the `sf-save-health-<version>` folder it makes (see Install), and run:

```
sf-save-health check "%USERPROFILE%\Documents\My Games\Starfield\Saves\Save12_....sfs"
```

Put the path in quotes, because save folders and names often have spaces. In PowerShell, write the path as `"$env:USERPROFILE\Documents\My Games\Starfield\Saves\Save12_....sfs"` and start the program as `.\sf-save-health`.

The tool finds the game's Data folder by itself on Steam and Game Pass installs (see `--data` below).

For AI assistants: step-by-step instructions are in [AGENTS.md](AGENTS.md).

## Example

This is a real save, checked with `--redact` so the character name is hidden. It is a healthy save, so every rule reports nothing to fix. A save with problems prints each finding under its rule and exits with code 2.

```
> sf-save-health check "Save3_....sfs" --redact
sf-save-health 0.7.3: Save3_143C1F9EM<name>_000038_20261111000630_8_0_4.sfs
  <player>, level 8, Frontier, playtime 0 days 0 hours 38 minutes, saved 2026-11-11 00:06 UTC
  13 plugins in the save; checked against C:\Program Files (x86)\Steam\steamapps\common\Starfield\Data

S1. Plugins in the save that are missing from the Data folder: 0 of 13 plugins in the save are missing from the Data folder (0 with data left behind)

S2. Form IDs that point past the save's plugin list: 0 found

S3. Forms not found in their plugin: 0 distinct (0 change records, 0 references)
    not counted: 104 engine form(s) below Starfield.esm:800, which live in the game itself

Q1. Running quests from plugins that are not installed: 558 quests in the save, 558 decoded exactly, 558 compared with their plugin versions

Q2. Done stages the quest no longer has: 0 quest(s)

Q3. Objectives the quest no longer has: 0 quest(s)

Q4. Quests at a completing stage but not marked complete (heuristic): 0 quest(s) (0 complete and still running, which is normal)

Q5. Quests caught starting or stopping, or complete with open objectives (heuristic): 0 starting or stopping, 0 complete with an open objective

B1. Objects created during play (bloat): 13,018 objects created during play (OK; normal 25,000-35,000, note from 60,000, warning from 150,000); save file 2.2 MB
    12,551 created placed objects, 197 created actors, 270 other; 27,724 change records in all
    plugins the created objects and actors point to most (a hint at what made them):
         9,340  Starfield.esm
         1,670  SFBGS00D.esm
           320  SFBGS003.esm
            98  SFBGS050.esm
            13  ShatteredSpace.esm

Healthy: no problems found.
Not checked yet: Papyrus script data (orphaned scripts, stuck stacks).
```

The exit code for this save is 0, because it found no problems.

## Install

**From the release zip (Windows):**

1. Download `sf-save-health-<version>.zip` from [Releases](https://github.com/taosecurity/sf-save-health/releases).
2. Right-click the zip, choose **Extract All**, and pick a new, empty folder.
3. Open the `sf-save-health-<version>` folder inside it in File Explorer, click the address bar at the top, type `cmd` and press Enter. A Command Prompt window opens in that folder, ready for the commands in this README.
4. Run the first command above.

The zip needs the .NET 9 runtime (or a newer .NET). If the program says .NET is missing, install the ".NET Runtime 9" from [dot.net](https://dotnet.microsoft.com/download/dotnet/9.0). Check the download with its `.sha256` file if you like:

```
certutil -hashfile sf-save-health-<version>.zip SHA256
```

**From source (Windows or Linux):** you need Git and the .NET SDK 9 or later. The save parser, [sf-save-reader](https://github.com/taosecurity/sf-save-reader), is a git submodule, so clone with `--recursive`:

```
git clone --recursive https://github.com/taosecurity/sf-save-health
cd sf-save-health
dotnet publish src/SaveHealth.csproj -c Release -o publish
```

The program is then `publish\sf-save-health.exe` on Windows, or `dotnet publish/sf-save-health.dll` on Linux. If you cloned without `--recursive`, run `git submodule update --init` before building.

## Words used here

- **Plugin:** a mod's data file (`.esm`, `.esp` or `.esl`). It adds or changes records in the game.
- **Record:** one thing in a plugin, such as a weapon, a quest, a location or a setting.
- **Form:** the game's word for a record once it is loaded. Each form has a Form ID.
- **Form ID:** a record's number, such as `0002B560`. The first two digits depend on the load order, so the report writes forms as `Plugin.esm:2C9C97` (the plugin, then the number inside it).
- **Change record:** an entry in a save file that holds what changed in one record during play. The game also calls these change forms.
- **Load order:** the order the game loads plugins in. When two plugins change the same record, the one loaded later wins.
- **Override:** a later plugin's copy of a record from an earlier one, which replaces it in the game.
- **Quest stage and objective:** a quest moves through numbered stages (10, 20, ...) and shows objectives on screen. The save remembers which stages are done and the state of each objective.
- **Data folder:** the game's `Data` folder, where the installed plugins are.
- **Papyrus:** Starfield's scripting language. Mods' scripts also leave data in a save, which this version doesn't check yet.

## Where saves and plugins are

- **Steam:** saves are in `%USERPROFILE%\Documents\My Games\Starfield\Saves`. The Data folder is `C:\Program Files (x86)\Steam\steamapps\common\Starfield\Data`, or the same path in the Steam library you installed the game to.
- **Game Pass / Xbox app:** the game is in `X:\XboxGames\Starfield\Content` (X is the drive you installed to), so the Data folder is `X:\XboxGames\Starfield\Content\Data`. The saves are kept inside the Xbox app's own container files, not as `.sfs` files. Export a save to `.sfs` first, for example with HarukaMa's [starfield-xgp-import](https://github.com/HarukaMa/starfield-xgp-import), then check the exported file.
- **Vortex:** Vortex deploys mods into the game's real Data folder, so the automatic `--data` sees your mods. Vortex can also export the load order as a plugins list; you can pass that file to `--plugins`.
- **Mod Organizer 2:** MO2 keeps mods in its own `mods` folder and shows them in Data only to programs it starts. Add `sf-save-health.exe` as an executable in MO2 (with arguments such as `check "<save>"`) and run it from MO2, so the mods' plugins are seen. Run outside MO2, only the plugins really in Data (the base game and Creations) are seen, and every mod looks "missing".
- **The load order (Plugins.txt):** the game reads the list of turned-on plugins from `%LOCALAPPDATA%\Starfield\Plugins.txt`. Pass it with `--plugins` to check the save against what the game will actually load.
- **Linux (Steam with Proton):** the automatic search looks only in `~/.steam/steam/steamapps/common/Starfield/Data`; for any other library pass `--data`. The saves and Plugins.txt are inside the Proton prefix: `~/.steam/steam/steamapps/compatdata/1716740/pfx/drive_c/users/steamuser/Documents/My Games/Starfield/Saves` and `.../pfx/drive_c/users/steamuser/AppData/Local/Starfield/Plugins.txt`.

## Commands

### `check`: one save against the installed mods

```
sf-save-health check <save.sfs> [--data <folder>|auto] [--plugins <plugins.txt>] [--json] [--limit N] [--lang <code>] [--redact] [--log <file>] [--verbose]
```

| Option | What it does |
|---|---|
| `--data <folder>` | The game's Data folder to check the save against. With no `--data`, or with `--data auto`, the tool looks in this order: the `STARFIELD_DATA` environment variable, every Steam library, then Game Pass (`<drive>:\XboxGames\Starfield\Content\Data` on each drive). The first folder that holds `Starfield.esm` is used. |
| `--plugins <file>` | Also check the save against a load order: the game's `Plugins.txt`, or a Vortex export of it (one plugin per line, turned-on plugins start with `*`). Adds rules M1 and M2. |
| `--json` | Writes the report as one JSON object (schema `savehealth-report/1`) instead of text. |
| `--limit N` | How many examples each finding lists (default 10). The rest are counted as "... N more". Q2 and Q3 list one finding per quest, up to 10 times N; any quests past that are counted in one more finding, so the verdict and exit code always include them. |
| `--lang <code>` | The language of names read from the plugins' string files (default `en`). |
| `--redact` | Hides the character name, the Windows user name, the computer name and your profile folder (below). |
| `--log <file>`, `--verbose` | A troubleshooting log (below). |

An option that takes a value (`--data`, `--plugins`, `--limit`, `--lang`, `--log`, and for `solo` also `--reports`, `--before` and `--after`) may be given once. Given twice, such as `--limit 5 --limit 50`, it is a usage error (exit code 3) that names the option. A switch such as `--json`, `--redact` or `--verbose` given twice means the same as once.

The command line is checked before anything runs, and these are usage errors (exit code 3) too. Each one prints an `error:` line and a `fix:` line, and writes no log:

- An option that takes a value given without one, such as `--data` at the end of the line or followed by another option. Write `--data auto` for the automatic search.
- An option the command doesn't know, such as `--plugin` or `--JSON`. Options are case-sensitive. When the name is close to a real option, the fix names it (and says that options are case-sensitive when only the letter case differs).
- An option of the other command, such as `--json` with `solo` or `--reports` with `check`. A mistyped one, such as `--JSON` with `solo`, is named as the other command's option too.
- An unknown command, such as `chek`. Given alone or with `--help`, it is followed by the usage text.
- An option before the save, such as `check --json Save3.sfs`: the save (or for `solo`, the zip) comes right after the command.
- `solo` with both a zip and `--before`/`--after`: give one or the other.
- An extra word that is not an option, not an option's value and not the save (or the zip for `solo`), such as a second save. A path with a space in it that isn't in quotes splits into two words and is refused too: write `check "C:\My Saves\x.sfs"`.
- `--help`, `-h` or `help` anywhere on the line, which shows the usage text and runs nothing. A word right after an option that takes a value is that value, so `--reports help` names a folder called `help`. A save or zip that is itself named `help` needs a path such as `.\help`.

For example:

```
> sf-save-health check "Save3_....sfs" --plugin Plugins.txt
error: check doesn't know --plugin
  fix: use --plugins (sf-save-health --help lists them)
```

### `solo`: what changed between a save before a test and one after it

```
sf-save-health solo --before <a.sfs> --after <b.sfs> [--data <folder>|auto] [--reports <folder>] [--limit N] [--lang <code>] [--redact] [--log <file>] [--verbose]
sf-save-health solo <StarfieldLogs-*.zip> [the same options]
```

`solo` runs every rule on both saves and prints only what is new in the after save: new findings, quests that started, moved on or stopped, and the change in created objects. Use it to see what one mod did: make a hard save (Save menu) before you try the mod and another one after.

- The two saves can be given with `--before` and `--after`, or as the zip that [sf-log-collector](https://github.com/taosecurity/sf-log-collector) makes with its `p` option (it holds `saves\before-*` and `saves\after-*`).
- It refuses exit saves (the game writes one every time you quit, so it isn't a fixed point), the same save twice, saves of two different characters, and an after save that is older than the before save.
- The full report, with every quest change, is saved to `--reports <folder>`, or by default to `sf-save-health-reports` in the current folder. The screen shows the short version and the report's path.

Here is a real `solo` run on a log-collector zip from a new character's test, with `--redact` (the zip and mod names are shortened, and lines marked `...` were left out):

```
> sf-save-health solo "StarfieldLogs-20261110-190752-....zip" --redact
sf-save-health 0.7.3 solo: StarfieldLogs-20261110-190752-....zip
  before: before-Save2_143C1F9EM<name>_000030_20261110235820_3_0_4.sfs  level 3, Jemison - The Lodge, playtime 0 days 0 hours 30 minutes, saved 2026-11-10 23:58 UTC
  after:  after-Save3_143C1F9EM<name>_000038_20261111000630_8_0_4.sfs  level 8, Frontier, playtime 0 days 0 hours 38 minutes, saved 2026-11-11 00:06 UTC
  <player>; checked against C:\Program Files (x86)\Steam\steamapps\common\Starfield\Data

Plugins:
  13 in the after save (13 before)
  in Data but not in the save: NewModA.esm, NewModB.esm, ... (not loaded by this game, or deployed later)

New findings in the after save: none (0 in the after save in all, 0 in the before save)

Objects created during play: 7,398 -> 13,018 (+5,620); placed objects 6,987 -> 12,551, actors 163 -> 197; save file 1.7 -> 2.2 MB
  plugins whose created objects and actors grew most:
      +4,629  Starfield.esm  (4,711 -> 9,340)
        +244  SFBGS003.esm  (76 -> 320)
         +83  SFBGS050.esm  (15 -> 98)
          +4  SFBGS00D.esm  (1,666 -> 1,670)
          +4  ShatteredSpace.esm  (9 -> 13)

Quests that changed: 55
  changed  DialogueUCTheDen (Starfield.esm:2A1049): running, stage - -> running, stage 50
  ...
  new      FFDenR01 (Starfield.esm:1E7942): running, stage 10
  ...
  changed  MQ101 (Starfield.esm:3448): running, completed, stage 1800 -> stopped, completed, stage 9000
  ...
  new      RI01 (Starfield.esm:2C9C97): stopped, completed, stage 10000
  ...
  ... 15 more in the saved report (or use --limit N)

Verdict: Healthy: nothing new to worry about in the after save.
Report saved to ...\sf-save-health-reports\StarfieldLogs-20261110-190752-....txt
```

The exit code is 0, because nothing new looks broken. "stage -" means that no stage of the quest is done yet.

## What it checks

Each finding is a PROBLEM, a WARNING or a NOTE, and says what it means. Rules marked "heuristic" make a judgment call and can raise false alarms; the others compare decoded save data with plugin data.

| Rule | What it finds |
|---|---|
| S1 | Plugins the save was made with that are missing from the Data folder. PROBLEM when the save still holds data for the plugin (its change records, or references to its forms inside other records), NOTE when it left nothing behind. When a quarter or more of the plugins are missing, it says the installed mods probably belong to a different load order (another Vortex profile). |
| S2 | Form IDs in the save that point to a plugin slot past the save's own plugin list. PROBLEM: the save may be damaged. |
| S3 | Forms the save refers to that no longer exist in their plugin, usually because a mod update removed or renumbered them. PROBLEM for mods; NOTE for Bethesda's own plugins, where long playthroughs keep harmless leftovers from earlier game updates. Engine forms below `Starfield.esm:800` are not counted. |
| M1 | With `--plugins`: plugins in the save that the load order doesn't turn on, so the game will warn that a mod is missing. PROBLEM when the save holds data for them, NOTE otherwise. It also says which turned-on plugins are new since the save and how many plugins changed order. |
| M2 | With `--plugins`: plugins the load order turns on whose file isn't in the Data folder, so the game skips them. WARNING. |
| Q1 | Quests that were running when the save was made, from plugins that aren't installed now. WARNING. |
| Q2 | Quests with done stages that the quest, as the installed plugins now define it, doesn't have. WARNING: a mod update or an override probably renumbered or removed them. |
| Q3 | Quests with saved objectives that the quest no longer has. WARNING: a shown objective may never clear. |
| Q4 | Running quests at a stage that should complete or fail them, but not marked complete. NOTE, heuristic. |
| Q5 | Quests caught halfway through starting or stopping when the game saved, and completed quests that still show an open objective. NOTE, heuristic. |
| B1 | Objects the game created during play (save bloat), with the plugins those objects point to most. Normal saves hold 25,000 to 35,000; NOTE from 60,000, WARNING from 150,000. Heuristic. |

For Q2 and Q3 the tool reads every version of each quest (the original and every mod that overrides it) in the save's plugin order, the way the game does. It doesn't check Papyrus script data yet (orphaned scripts, stuck script stacks); the report says so at the end.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Healthy: no findings. |
| 1 | Only warnings or notes. |
| 2 | At least one problem. |
| 3 | Couldn't check: a usage error (an unknown command, an option the command doesn't know, an option given without its value or given twice, an extra word such as a second save or an unquoted path with a space, or `--help`, `-h` or `help`; see Commands), a report that couldn't be written to the output (for example a full disk behind a redirect), a missing or unreadable file, a file that isn't a save, an unsupported save version, a `--log` file that can't be opened, a plugin that can't be read, or no Data folder found. The `error:` line names the file and the `fix:` line says what to do. |

If `solo` can't save its full report into the `--reports` folder, it prints an `error:` line but still exits 0, 1 or 2, because the check ran and the report was printed. The fix line asks you to check that the folder's drive has room and that you may write there, or to pass `--reports` with another folder.

`solo` saves its full report before it prints the short one. So if printing fails (exit code 3, for example a full disk behind a redirect), the saved report is still there.

## Privacy: `--redact`

Reports are made to be shared, for example when you ask for help with a broken save. `--redact` hides what could identify you in everything the tool writes (the text and JSON reports, the saved solo report, errors and the log):

- the character's name, in the text and as the hex code the game puts in save file names (`<player>`, `<name>`);
- your user profile folder (`%USERPROFILE%`, or `~` on Linux), your Windows user name (`<user>`) and your computer name (`<computer>`).

Plugin names, form IDs, quest names and locations stay, because they are what a report is for.

## Logging and troubleshooting

- `--verbose` writes what the tool read and decided to the error stream (stderr): the save's size and header, the Data folder found, each rule's counts, each missing plugin and why a finding was made, and parser warnings.
- `--log <file>` writes the same lines, with times, to a file. The file is added to, not replaced, so one log can hold several runs. There is no log file unless you ask for one.
- Errors name the file and say how to fix the problem, for example:

```
error: no save file Save99.sfs
  fix: check the path; saves end in .sfs
```

For a bug report, run the command again with `--log sf-save-health.log --redact` and attach the log and the report.

## JSON output

`check --json` writes one object with `schema` (`savehealth-report/1`), `version`, `save` (file name, save version, player level, plugin count), `data` (the Data folder used), `verdict`, `exitCode` and `rules`: one entry per rule with its `Findings` (rule, level, reliability, title, meaning, evidence and count) and `Summary` lines. A new field may be added without a new schema; a field that is renamed or removed raises it.

## Limits

- Read-only: it opens saves for reading only and writes only the files you ask for (`--log`, and `solo`'s report in `--reports`). It doesn't use the network.
- It reads saves written by Starfield 1.16.244 (header version 27, save version 153). Other save versions are refused with exit code 3, because their layout may differ; load an old save in the current game and save again, or look for a newer release.
- It checks against the plugins in one Data folder. Under Mod Organizer 2, run it from MO2 (above).
- It doesn't check Papyrus script data yet, and it doesn't run the game's scripts: a quest the save calls "running" may still be starting.
- It doesn't repair saves. To remove leftovers from a bloated save, see the advice in rule B1's finding, and always keep a backup of the save.

## Found a mistake?

Open an issue: https://github.com/taosecurity/sf-save-health/issues

## Credits

sf-save-health is made by Richard Bejtlich. It builds on:

- [sf-save-reader](https://github.com/taosecurity/sf-save-reader) reads the save (container, header, plugin list, change records and quest state). Its own credits list the work it builds on.
- [BuildKit](https://github.com/taosecurity/BuildKit) and [Mutagen](https://github.com/Mutagen-Modding/Mutagen) read the plugins: every version of each quest, with its stages and objectives.
- HarukaMa's [starfield-xgp-import](https://github.com/HarukaMa/starfield-xgp-import), for Game Pass saves.

## License

GPL-3.0, see [LICENSE](LICENSE). The parts by other people, and sf-save-reader's MIT license, are listed in [NOTICE](NOTICE).
