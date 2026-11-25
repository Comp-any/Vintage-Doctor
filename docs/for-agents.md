# Vintage Doctor: a guide for agents

What this mod is, how it works inside, how other mods hand it things, and what to watch out for when changing it
or hooking a mod into it. Written for an AI agent (or a person) picking this up cold. The full API reference is
`docs/api.md`; the changes by version are in `CHANGELOG.md`.

Current version when written: **1.7.0** (2026-11-25), game **1.22.7**, API version **3**.

---

## 1. What it is

Vintage Doctor (`vintagedoctor`, author **demonic69**) is a Vintage Story code mod (C#, .NET 10) that finds out what
is wrong with the game and its mods, says it in plain words, and fixes what it can. Where it can't fix something it
gives the player enough to fix it themselves or to report it: what was found, why it happens, what to do, and a
button that copies it all for a report.

- **Universal, required on neither side.** It works on the player's game alone, on a server alone, or both.
- **Opening it:** Ctrl+Shift+D, `.doctor` (the player's own game), `/doctor` (the server; works from the console).
- **Repo:** `Comp-any/Vintage-Doctor` (private). Releases are GitHub releases; the Mod DB page is 80941
  (`moddb.json`, never a key in `modinfo.json`).

## 2. What the player sees

- **A window of cards.** Each card has a level, shown by its colour, dot and counter:
  - **ok** (green): checked, fine.
  - **info** (grey): worth knowing.
  - **warning** (yellow/orange): may affect play.
  - **problem** (red): something isn't running or could crash.

  A card opens to its details, an explanation, advice ("what to do") and buttons.
- **The check-up** (the main page) runs every check each time and is laid out **by mod**:
  - first the game's own section;
  - then one section per mod with something to say, problems first, then warnings, then the rest;
  - last, one card listing the mods nothing was found against.

  Section headings are coloured by their worst card, and fold when clicked. Sections with nothing to look at start
  folded. A drop-down shows one mod's section alone (marked `[x]` / `[!]`).
- **Buttons on cards:**
  - a fix (green words; it runs, then the page is built again);
  - a link to another page;
  - open a log file or the Logs folder;
  - open a web page;
  - copy text. Every card that isn't "passed" has **Copy this for a report**: versions, the mod, and the card's lines.
- **Two sides.** The player's game checks its own log, crash reports, settings, keys, chat and Mods folder. The server
  checks its own log and the world. On the check-up the two halves are merged under each mod, and what both found is
  said once. The server's half needs the `controlserver` privilege.
- **Pages** (each also a command, `/doctor <page> [argument]`):
  - `crash`, `perf`, `mods`, `mod <id>`, `log [id]`, `startup`, `files`, `patches`, `clashes`, `changes`, `world`;
  - `item [code]`, `block`, `craft [code]`, `hotkeys`, `bugreport [id]`, `api`;
  - plus any page a mod adds (`emc`, `farrelief`).

  `/doctor fix <id> [argument]` runs a fix.
- **Nothing is deleted.** Fixes move files into `ModData/vintagedoctor/backups/<date-time>/`.

## 3. What it checks (short list)

- **Crashes:** `client-crash.log` / `server-crash.log`, the mod named, the trace read, the error explained. A
  session that didn't close properly. A mod can say it closed the game on purpose (see 5.5).
- **Start-up:**
  - mods that threw;
  - missing or too-old requirements;
  - a mod installed twice;
  - patches that didn't apply;
  - recipes naming things that don't exist.
- **The log:** every error and warning of the session, grouped, traced to its mod, with a card per mod on the
  check-up.
- **Mods folder and config files:**
  - damaged or cut-off zips;
  - wrong folder layout;
  - zip inside a zip, `\` in names, `.rar`/`.7z`, source code instead of a release;
  - a `.dll` in a subfolder;
  - broken or empty config files.
- **Lag:** tick times / frame rate, memory growth, a 20 s profile with the game's own profiler.
- **Mods against each other:**
  - classes and behaviours no mod has;
  - things registered twice;
  - files several mods replace;
  - copies of game files and of game libraries;
  - recipes with the same ingredients and different results;
  - Harmony patches by mod, and **which methods two mods patch, with what each does there**;
  - JSON patches on the same path;
  - declared conflicts;
  - versions against the server's.
- **Formatting tags:** the game's "Found closing tag..." log errors, traced to the mod text or command help that makes
  exactly that error. Also chat lines that break the chat window, with **Clear the chat**.
- **World:** disk space, blocks and items left by removed mods, time speed.
- **One thing:** a mod's page, the held item, the block looked at, why something can't be crafted.

## 4. How it is built inside (for changing it)

- **`src/VintageDoctorSystem*.cs`:** the mod system, split by side:
  - `.Client.cs`: window, hotkey, commands, button handling;
  - `.Server.cs`: commands, the start-up check-up file;
  - `.Guests.cs`: everything on the event bus from other mods.
- **`src/Core/`:** reading things.
  - `LogScan` (the log; sets tag errors aside), `CrashScan`, `FolderScan` (+ `ZipHealth`), `ConfigScan`, `Evidence`
    (all of it for one page), `ModSnapshot`.
  - `DoctorReport` and `DoctorLine`: a page is a list of lines with a style, an indent and a section.
  - `DoctorContext`: what a check writes through.
  - `TextTags`: mirrors the game's VTML parser word for word.
  - `GameFont`: makes text drawable.
- **`src/Checks/`:** the checks. `Pages.cs` builds each page and the check-up:
  - `BySection` tags cards with mods;
  - `Arrange` merges the two sides by mod;
  - `LogButtons` adds open-the-log buttons.

  `BuiltInFixes.cs` holds the doctor's own fixes (`doctor:repack`, `doctor:disable-mod`, `doctor:clear-chat`, ...).
- **`src/Gui/GuiDialogDoctor.cs`:** the window. It turns report lines into cards:
  - a Check line at indent 0 is a card;
  - a Note line or an indented line after it is that card's detail.
- **A report line's text** carries:
  - **marks:** ✓ ⚠ ✖ • for the level, → for advice;
  - **buttons as tokens:** `{{open|page|arg|label}}`, `{{fix|id|arg|label}}`, `{{url|address||label}}`,
    `{{copy|text||label}}`, `{{folder|logs|file|label}}`;
  - **references:** `[[item:code]]`, `[[block:code]]`.

  Labels and arguments can't contain `{ } |`.
- **Texts:** all in `assets/vintagedoctor/lang/en.json`.

## 5. How other mods hand it things

Four ways, all optional. **Never make Vintage Doctor a dependency.** A mod must load and work without it.

### 5.1 A JSON file (no code)

`assets/<modid>/config/vintagedoctor.json`:
- `knownProblems`: a log line pattern → a card with title, explain, advice, and a fix of type `resetConfig`,
  `command` or `url`;
- `conflicts`: another mod yours doesn't get on with;
- `recommends`: a mod worth adding;
- `configFiles`: which files in `ModConfig` are yours.

### 5.2 The event bus (no reference to the doctor's DLL)

This is the preferred way for the user's mods.

```csharp
api.Event.RegisterEventBusListener(OnCollect, 0.5, "vintagedoctor:collect");
api.Event.RegisterEventBusListener(OnFix, 0.5, "vintagedoctor:fix");

void OnCollect(string ev, ref EnumHandling h, IAttribute data)
{
    if (!(data is ITreeAttribute tree) || tree.GetString("side") != "server") return;   // answer on the side that knows
    string only = tree.GetString("mod"); if (only != null && only != "mymod") return;
    var r = tree.GetOrAddTreeAttribute("results").GetOrAddTreeAttribute("mymod:tanks");
    r.SetString("level", "warning");            // ok | info | warning | problem
    r.SetString("title", "2 tanks are leaking"); // don't start it with your mod's name: the doctor adds it
    r.SetString("details", "tank at 10 3 10\ntank at 12 3 10");
    r.SetString("advice", "Seal them with resin.");
    r.SetString("fix", "mymod:seal"); r.SetString("fixLabel", "Seal them"); r.SetString("fixArgument", "all");
}
void OnFix(string ev, ref EnumHandling h, IAttribute data)
{
    if (!(data is ITreeAttribute tree) || tree.GetString("id") != "mymod:seal") return;
    tree.SetString("result", "Sealed 2 tanks");  // or tree.SetBool("failed", true)
}
```

The tree the doctor sends has:
- `side`, `topic` (`overview` / `mod`), `argument`, `mod`, `language`, `player`;
- `held`, `heldCode`;
- `hasLookedAt` + `lookedAtX/Y/Z/Dim`;
- `compact`, `clientfacts`.

A result can also carry `link`, `linkArgument` and `linkLabel`. `vintagedoctor:about` does the same for the item,
block and craft pages (`subject` = `item:code` / `block:code`).

### 5.3 The C# API (reference `VintageDoctor.dll` with `<Private>false</Private>`)

`DoctorApi.Get(api)` returns `IDoctorApi`, or null when the doctor isn't installed. It has:
- `RegisterCheck`, `RegisterFix`, `RegisterTopic`, `RegisterKnownProblem`;
- `ReportIncident`, `Unregister`;
- (API 2) `RegisterClientFacts`, `OpenPage`.

A check gets an `IDoctorContext`:
- cards: `Ok`, `Info`, `Warn`, `Problem`;
- lines inside the last card: `Detail`, `Explain`, `Advice`;
- buttons: `FixButton`, `LinkButton`, `UrlButton`, `CopyButton`, (API 3) `LogButton`;
- `LogIssues(modId)`: the log's errors for a mod;
- who asked: `Player`, `Side`, `Subject`, `LookedAt`, `ClientFacts`...

Keep every line that names a doctor type in its own class, and only call it when
`api.ModLoader.IsModEnabled("vintagedoctor")`. Check `ApiVersion` before using newer members (2: doctor 1.2.0,
3: 1.4.0).

### 5.4 A whole doctor of your own (event bus, 1.2.0+)

Used by Vintage-E (its EMC doctor) and Far Relief.
- **`vintagedoctor:topics`:** add `topics[<id>] = {mod, question}`. It becomes `/doctor <id>` and a button in your
  mod's section.
- **`vintagedoctor:page`:** fill `texts`, `styles` (0 text, 1 heading, 2 total, 3 note, 4 warn, 5 card) and
  `indents`. Or set `later = true` and push `vintagedoctor:deliver` `{request, texts, styles, indents}` when done.
- **`vintagedoctor:fix`:** may also answer `later`.
- **`vintagedoctor:clientfacts`:** the player's game adds up to 64 KB under `facts[<modid>]`. It reaches your
  server-side page and fix handlers.
- **`vintagedoctor:open`** `{topic, argument, player}`: opens the window.
  - An empty argument opens the **check-up**, not your page (the doctor always starts at its main page).
  - Give an argument to open your page directly.

### 5.5 Saying you are closing the game on purpose (1.6.0+)

```csharp
var t = new TreeAttribute(); t.SetString("mod", "mymod"); t.SetString("reason", "restarting to install updates");
api.Event.PushEvent("vintagedoctor:closing", t);   // t.GetBool("noted") tells you a doctor heard it
```

Push it right before closing. The next start then says "My Mod closed the game last time, on purpose: ..." instead
of "didn't close properly". Vintage Auto Update does this (0.9.1+).

### 5.6 Compatibility rules (what keeps old and new versions working together)

- Nothing that exists is removed or renamed: event names, tree keys, API members, packet members.
- New things are new events, new optional keys, or new API members behind a higher `ApiVersion`.
- An old doctor ignores new events (nothing listens). A new doctor never hears from an old mod; its own checks still
  run. Neither side may throw because the other is missing or older.

## 6. What makes a good check (the user's own rules)

- **Find and report problems, fix them if possible, otherwise give the player enough to fix or report them.**
- **Copy buttons for what can't be fixed.** If something can't be fixed by the doctor, especially something the
  player should report to the mod's author, give it a **copy button** with the relevant information. Example: Vintage
  Doors losing track of a player, and the lines that led up to it from the server log.
- **Say what to do, not only what is.** "3 pipes aren't connected, at x y z" beats "invalid network state".
- **Levels:** keep `problem` for things really broken. Say what you checked even when fine (an `ok` card).
- **Fixes must be safe:** move, don't delete; say in the label what the button does.
- **Don't repeat the doctor.** It already reports your mod's log errors, failed start, failed patches and broken
  config files.
- **Checks run on the main thread every time the check-up opens.** Keep them to a few milliseconds; work slow things
  out once (or off the main thread) and remember them.

## 7. Pitfalls that have bitten before

- **Characters.** The game draws text with the computer's default font and has no fallback, so arrows, ticks,
  warning signs and triangles show as empty boxes.
  - Use letters, digits and ordinary punctuation. Safe beyond ASCII: Latin-1, `… • – — ‘ ’ “ ” « » × ·`.
  - `tools/check_lang.py` enforces `tools/game-font-chars.txt` on the lang file and every C# string (a line ending
    `// font-ok: why` is let be).
  - Report marks stay in reports. `GameFont.Drawable` writes them out before anything is drawn.
- **`<` and `>` in text.** The game reads them as formatting tags in chat, command help, tooltips and the handbook.
  - Never write placeholders like `<id>`; use `[id]`. Never a lone ` > `.
  - A tag left open in a chat line restyles the whole chat. Escape text bound for chat (`&gt;` / `&lt;`; the doctor's
    `ChatSafe` does it).
  - `check_lang.py` refuses these in texts and in `WithDescription`/`WithExamples`.
- **Logs.** A text that mentions "log" or "crash report" must be listed in `LogTexts` (`src/Checks/Pages.cs`), which
  says what its button opens. `check_lang.py` fails otherwise. Don't add log buttons by hand.
- **A Note line under a card is taken as that card's detail** by the window. A standalone line after a card must be
  style Text at indent 0 (`x.Aside`).
- **`Vintagestory.API.Common` has its own `Func<,>`.** Write `System.Func`; forgetting it has failed builds twice.
- **`\uXXXX` written through the Write tool arrives as the character itself.** Write such escapes from a script.

## 8. Using the doctor in a mod's smoke run (CI)

When a mod is built in CI, start a real server with the mod **and Vintage Doctor** and let the doctor say what is
wrong. It reads the same log, Mods folder and config files a player's game would, so it catches things a build can't:
- a mod that throws while starting, or a requirement missing or too old;
- patches that didn't apply, and recipes naming things that don't exist;
- classes or behaviours the game couldn't find, and things registered twice;
- broken files in the zip, and the mod's errors in the log;
- clashes with the other mods loaded;
- whatever the mod itself hands the doctor (its own sanity checks, its page and its fixes).

### Getting the doctor

The repo `Comp-any/Vintage-Doctor` is **public** (since 2026-11-25), so a run can download the newest release without
any secret:

```yaml
- name: Install Vintage Doctor
  env:
    GH_TOKEN: ${{ github.token }}     # the run's own token: not needed for a public repo, but it lifts GitHub's
                                      # limit of 60 anonymous API requests an hour, which shared runners can hit
  run: |
    tag=$(gh api repos/Comp-any/Vintage-Doctor/releases/latest --jq .tag_name)
    gh release download "$tag" -R Comp-any/Vintage-Doctor -p 'vintagedoctor_*.zip' -D "$DATA/Mods/"
    echo "Vintage Doctor $tag" > results/doctor-version.txt
```

Without `gh`, `curl -fsSL https://api.github.com/repos/Comp-any/Vintage-Doctor/releases/latest` gives the release,
and the zip's `browser_download_url` downloads with no token at all.

Write which version was tested into the results (`doctor-version.txt`) and check it when reading them.

The mods that test with the doctor (Vintage-E, Far Relief, Vintage Computer, Vintage Doors) share
`.github/scripts/get-doctor.sh`, which does this without any token. Copy it into a new mod:
- It follows `github.com/Comp-any/Vintage-Doctor/releases/latest` to the newest tag and downloads
  `vintagedoctor_<version>.zip` by its public link. That isn't the API, so GitHub's limit of 60 anonymous requests
  an hour doesn't apply.
- A token in `DOCTOR_TOKEN`, `GH_TOKEN` or `GITHUB_TOKEN`, if set, is used for the API instead.
- If the download fails, it falls back to the copy in `tools/vendor/vintagedoctor/`.
- It prints the zip's path, and writes which one it used to `$DOCTOR_NOTE` (default `ci/doctor-version.txt`).
  "saved copy" there means the download failed.

The `DOCTOR_TOKEN` organisation secret is no longer needed.

### What the doctor does on its own after start-up

About 4 seconds after the server reaches `RunGame`, the doctor runs the whole check-up and:
- writes **`<data>/ModData/vintagedoctor/startup-checkup.txt`**, the check-up laid out by mod, as text;
- writes one line to `server-main.log`:
  - `[vintagedoctor] Check-up after start-up: N problem(s), M warning(s). Type /doctor to see them.`, or
  - `[vintagedoctor] Check-up after start-up: nothing wrong (... ms ...)`.

So the smallest smoke run needs no commands:
1. Wait for that log line, without `sleep`: poll the log in a loop that gives up after a time limit.
2. Read your mod's section of `startup-checkup.txt`.
3. Stop the server.

The file looks like this:

```
== The game (Vintage Story 1.22.7) ==
✓ All 11 mod(s) loaded and started without the game reporting a problem.
...
== My Mod 1.2.3 ==
⚠ 2 tanks are leaking
  tank at 10 3 10
  → Seal them with resin.
  Seal them (/doctor fix mymod:seal all)
...
== The other mods ==
```

- **Marks:** `✖` a problem, `⚠` a warning, `•` to know, `✓` passed, `→` advice. A button shows as its words with
  the command it runs, in brackets.
- **Your section** starts at `== <your mod's name> <version> ==` and ends at the next line starting with `== `. A mod
  the game didn't load gets `== <name> (not loaded) ==`.
- **Since 1.3.0 a card under its mod's own heading doesn't start with the mod's name.** Accept both forms if the
  run may meet an older doctor.

A useful rule for failing the build:
- any `✖` line in your section;
- `(not loaded)` after your mod's name;
- any `✖` in the game's section that names your mod id or name.

Treat `⚠` as something to read, or fail on it too if your mod should be clean.

```bash
# wait for the check-up (polling a local file; give up after 3 minutes)
timeout 180 bash -c 'until grep -q "\[vintagedoctor\] Check-up after start-up" "$DATA/Logs/server-main.log"; do sleep 2; done'
up="$DATA/ModData/vintagedoctor/startup-checkup.txt"
mine=$(awk -v h="== My Mod " 'index($0,h)==1{on=1;print;next} /^== /{on=0} on' "$up")
echo "$mine" > results/doctor-my-mod.txt
if echo "$mine" | grep -q "^✖\|(not loaded)"; then echo "Vintage Doctor found a problem with My Mod"; exit 1; fi
```

### Asking for more pages

Every page is a server command, and the server builds it as text. Each page built is also saved to
**`<data>/ModData/vintagedoctor/last-report-server.txt`**, replaced each time.

Useful pages:
- `/doctor`: the check-up again, later (after the world ran a while);
- `/doctor mod <modid>`: everything about your mod;
- `/doctor log <modid>`: its errors and warnings, grouped;
- `/doctor startup`, `/doctor clashes`, `/doctor patches`;
- `/doctor <your page>` and `/doctor <your page> <argument>`, if your mod adds one (5.4);
- `/doctor fix <fix id> [argument]`: runs a fix.
  - The result is written to the log as `[vintagedoctor] /doctor fix <id>`, with what happened on the next line.
  - A fix that answers `later` writes the same when it is done.

There are two ways to send those commands:
- **Through the server's console input.** Start the server with its standard input on a pipe you can write to,
  and write the command followed by a newline.
- **From your mod's own self-test** (a test command or config switch of yours). Run the command with the game's
  `api.ChatCommands.ExecuteUnparsed("/doctor mod mymod", args)` with a console caller (`EnumCallerType.Console`,
  privileges `*`), then read the reply or `last-report-server.txt`. Vintage-E and Vintage Computer do it this way.

After each command, wait for the file to change before reading it. A page a mod builds later (`later = true`) is
written when it is delivered.

### Testing your mod's handover to the doctor

If your mod hands the doctor checks, a page or fixes (section 5), test them in the same run:
- Your findings must appear in your section of the check-up.
- `/doctor <your page>` must return your page.
- Each fix must say it worked in the log.
- Test without the doctor too: your mod must start and behave the same when `vintagedoctor` isn't installed. Run
  the same smoke test once without the zip, or test your bus handlers directly.

### Keep what the doctor said

Copy these into the run's results (a results branch, since a session can't read job logs):
- `startup-checkup.txt` and `last-report-server.txt`;
- `doctor-version.txt`;
- the server's `server-main.log`.

That way whoever reads the results sees exactly what the doctor saw.

### What a smoke run can't show

A server has no player's game, so nothing on the player's side is checked:
- the window, the hotkey;
- the frame rate and graphics settings;
- the client log and the chat;
- client-side fixes.

Say so when reporting, and ask the user to look in a real game. A dedicated server is also "another computer" to
its players, so its pages have no buttons that open log files.

## 9. Mods that use it

- **Vintage-E:** hands over its whole EMC doctor (page `emc`), its check-up card, item-page cards ("Worth N EMC")
  and client facts. Copy buttons since its 1.26.7.
- **Far Relief** (`farrelief`): sanity checks, a page with `here`, `perf` and `deep`, 15 fixes, a block-page card.
- **Vintage Auto Update:** a JSON file of known problems, and `vintagedoctor:closing` before it restarts or quits
  the game.
- **The repo's examples:** `example/DoctorExample` (C# API and event bus, soft dependency) and
  `example/doctorexamplejson` (JSON only).
