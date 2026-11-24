# Privacy

sf-save-health runs on your own computer. It collects no data and sends nothing anywhere.

## What it does not do

- It makes no network connections.
- It has no telemetry, analytics, crash reporting or update checks.
- It never changes a save, a plugin or any game file. It doesn't repair saves.
- It starts no other program.

## What it reads

- the save you name (`.sfs`), which it opens for reading only;
- the game's Data folder (`--data`, or the automatic search): the plugins, and the string tables and archives that hold
  the quests' names. The automatic search looks at the `STARFIELD_DATA` environment variable, Steam's
  `libraryfolders.vdf` and the Game Pass folders, only to find a folder that holds `Starfield.esm`;
- with `--plugins`, the load order file you give (the game's own `%LOCALAPPDATA%\Starfield\Plugins.txt` or a Vortex
  export of it);
- for `solo`, the saves you give with `--before` and `--after`, or the saves inside the sf-log-collector zip you give.

## What it writes

It prints its report to the console. `check --json` prints the report as JSON on the console. Nothing else is written
unless you ask:

- `solo` saves its full report as a text file in the folder you give with `--reports`, or in `sf-save-health-reports`
  in the current folder.
- `--log <file>` adds a troubleshooting log to the file you name. There is no log file unless you ask for one.
  `--verbose` prints the same lines on the console instead.
- `solo` with a zip unpacks the two saves into a temporary folder (`%TEMP%\sf-save-health-solo-...`) and removes it
  when it ends. A file that another program holds open can stay behind; the log says so.

The report, the JSON and the log name the save file, the character's name, the plugins in your load order, your
quests and the folders it used. They can also include your Windows user name and your computer name. Without
`--redact`, read them before you share them.

## `--redact`

`--redact` hides the character's name (in the text and as the hex code in a save's file name), your profile folder,
your Windows user name and your computer name in everything the tool writes: the reports, the JSON, the saved `solo`
report, the errors and the log. Plugin names, form IDs, quest names and locations stay, because they are what a report
is for.

## Personal data

sf-save-health handles none beyond what is in your own save and file paths, as described above. Nothing leaves your
computer unless you copy a report somewhere yourself.

## Downloading it

Downloading sf-save-health from GitHub is covered by GitHub's own
[privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement).
sf-save-health's author receives only the public download counts GitHub shows.

## Questions

Open an issue at https://github.com/taosecurity/sf-save-health/issues.
