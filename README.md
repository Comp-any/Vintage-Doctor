# Vintage Doctor

A Vintage Story mod that works out what's wrong with your game and your mods, says so in plain words, and fixes
what it can. Other mods can hand it their own checks and fixes.

For Vintage Story 1.22. Drop the zip into your `Mods` folder. It works in a player's game alone, on a server alone,
or on both, which is the most complete.

## Opening it

Press **Ctrl+Shift+D**, or type **/doctor**. (On a server that doesn't have the mod, `.doctor` and the key still open
your own game's half.)

You get a check-up: one card per check.

- **Green**: passed. **Grey**: something to know. **Yellow**: may affect play. **Red**: something isn't running
  properly, or could crash.
- Click a card to see what was found, why it happens and what to do.
- A button with green words does a fix there and then, and runs the checks again. The other buttons open a page.
- The four counters at the top hide and show each colour.
- The check-up runs every check each time, and is laid out **by mod**: first a section for the game itself (crashes,
  lag, the Mods folder, the world), then one for each mod with something against it or something to offer, worst
  first, then one card for the mods nothing was found against.
- The list next to *Run checks again* shows one mod's section alone.
- A mod's section holds the buttons that mod added (Vintage-E's *Something is wrong with EMC values*, say). The
  doctor's own are in the game's section: *The game crashed*, *The game is slow or stutters*, *A mod isn't working*,
  *Something broke after I added or updated mods*... Each opens a page that looks into just that.

What your own game found and what the server found are put together under each mod. On a multiplayer server, the
server's findings are shown to its admins only, under "On the server:".

## What it looks at

| | |
|---|---|
| **Crashes** | The game's crash reports: when, on what, and which mod the crash points at (the game's own naming, then the doctor's reading of the trace). What the error usually means, and whether that mod has been updated since. Also when the game didn't close properly and left no report. |
| **Start-up** | Mods that threw an error while starting (the game carries on with half the mod), mods left out for a missing or too-old requirement, mods on the Mod DB's block list, mods installed twice, JSON patches that didn't apply, recipes that name things that don't exist. |
| **The log** | Every error and warning of the session: the same message counted once, traced to its mod, explained where the doctor (or the mod itself) knows what it means. A message written hundreds of times is called out, since that alone slows the game. |
| **The Mods folder** | Zips that can't be opened, mods zipped inside a folder or inside another zip, zips with `\` in their file names (nothing in them loads on Linux and Mac), source code downloaded instead of a release, `.rar` and `.7z` files, downloads that didn't finish, mods made for a newer game. |
| **Config files** | Files in `ModConfig` that are empty or no longer valid JSON, and whose they are. |
| **Lag** | The server's tick times, your frame rate, what's loaded, memory. **Measure where the time goes** runs the game's own profiler for 20 seconds and shows the time by part of the game and by mod. |
| **Mods against each other** | Which mods change the game's code and where two change the same place; JSON patches from different mods on the same thing; files that two mods both replace (only one copy is used); recipes with the same ingredients that make different things; the same code library in two mods; mods that say they clash; your mods' versions against the server's. |
| **Mods against themselves** | Blocks and items that name code no loaded mod has (they exist but do nothing); two things added under the same name (only the first exists); files of a mod the game couldn't read; a mod whose blocks and items never arrived; code in a mod that calls itself a content mod (the game leaves it out); a `.dll` in a subfolder (the game refuses the mod); patches for files that are no longer there; mods that ship whole copies of the game's files from an older version; mods with their own copy of a library the game has; mods with code made for an older game that have errors now; things that show a code instead of a name. |
| **The world** | Free disk space where it is saved; blocks and items left by mods that are gone; time running at another pace. |
| **What your mods bring** | Checks, fixes and whole pages that other mods hand the doctor. A mod with a doctor of its own (Vintage-E, for its EMC values) shows it here instead of in a second window. |
| **One thing** | A page for any mod (`/doctor mod carryon`), for the item you hold or the block you look at, and for why something can't be crafted (a recipe only for another class, say). |
| **What changed** | Mods added, updated and removed since last time, and which of them have errors now. |
| **Keys** | Two controls on the same key. |

## What it can fix

- Repack a mod zip the right way (`\` in file names, or the mod one folder down).
- Take a mod out of a zip-inside-a-zip.
- Take a library the game already has out of a mod that brought its own copy.
- Move spare copies of a mod, files that aren't mods and broken downloads out of the Mods folder.
- Move a broken or outdated config file aside so the mod writes a fresh one.
- Switch a mod off from the next start.
- Remove the items lying on the ground when there are hundreds.
- Turn down the graphics settings that cost the most frames.
- Whatever other mods offer through the API.

**Nothing is ever deleted.** Files go to `VintagestoryData/ModData/vintagedoctor/backups/<date and time>/`, from where
you can put them back.

## Commands

`/doctor` opens the check-up. Every page is a command too, which is how it works from a server's console:

`/doctor crash`, `perf`, `mods`, `mod <mod id>`, `log [mod id]`, `startup`, `files`, `patches`, `clashes`, `changes`, `world`,
`item [code]`, `block`, `craft [code]`, `hotkeys`, `bugreport [mod id]`, `api`

`/doctor fix <id> [argument]` does a fix; each fix's command is shown next to it in the console's output.
`.doctor ...` is the same for your own game only.

## A report to post

**Put together a report to post** (or `/doctor bugreport`, or `/doctor bugreport <mod id>` for one mod) gathers
versions, the mod list, the findings, the errors and the latest crash, with addresses, player ids and user folders
taken out. **Copy this page** puts it on the clipboard; it is also saved to
`ModData/vintagedoctor/bug-report-server.txt` (or `-client.txt`).

## Settings

`ModConfig/vintagedoctor.json`:

| | |
|---|---|
| `ServerReportPrivilege` | The privilege a player needs to see the server's half and use its fixes. Default `controlserver`. |
| `TellAdminsOnJoin` | Tell admins in chat when they join if the check-up found a problem or the game crashed last time. Default on. |
| `ScanAtStartup` | Write one line to the server log after start-up with the number of problems and warnings. Default on. |
| `IgnoreLogMessages` | Log lines containing any of these are left out of the doctor's pages. |
| `KnownProblems` | Your own explanations for log lines, in the form mods use ([docs/api.md](docs/api.md)). |
| `AllowCommandFixes` | Let fix buttons that mods describe in JSON run a server command (the command is always shown). Default on. |

## For mod authors

Your mod can add its own cards, fixes and pages, and teach the doctor what your log messages mean, with a JSON file
(no code), the event bus (no reference to the doctor) or a few lines of C#. See **[docs/api.md](docs/api.md)** and
the two mods in [`example/`](example).

## What it can't do

- It runs inside a world. If a mod stops the game from getting that far, the doctor can only tell you about it the
  next time a world loads (it reads the crash report then).
- It can't tell why a mod misbehaves when that mod writes nothing to the log and breaks nothing the game notices,
  unless the mod hands the doctor a check of its own.
- Time is only traced to a mod where the game can tell the code apart (things that tick, windows, timers).
- Switching a mod off, and a repacked zip, take effect at the next start of the game.

## Where it comes from

The window, the cards, the fixes and the bug report began as the "EMC doctor" inside
[Vintage-E](https://github.com/Comp-any/Vintage-E), which only looked at Vintage-E itself.

## Building

`build/package.sh` with the .NET 10 SDK and `VINTAGE_STORY` pointing at a 1.22 install. The repository's workflow
does the same against the real server, then starts a server with the mod and lets it examine itself.
