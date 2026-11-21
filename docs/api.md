# Handing Vintage Doctor your own checks

Vintage Doctor shows players a check-up of their game: cards saying what passed, what's worth a look and what's a
problem, with the reason, what to do, and buttons that fix things. Your mod can add its own cards, fixes and pages,
and teach the doctor what your log messages mean.

There are three ways in. Use whichever suits your mod; they can be mixed.

| | You write | Your mod needs |
|---|---|---|
| [A JSON file](#1-a-json-file-no-code) | `assets/<modid>/config/vintagedoctor.json` | nothing |
| [The event bus](#2-the-event-bus-no-reference) | two event listeners | nothing |
| [The C# API](#3-the-c-api) | a few calls | a reference to `VintageDoctor.dll` (not a requirement for players) |

Everything here is tried on a real server at every build: the two mods in [`example/`](../example) use all three ways,
and the doctor's own test loads them and checks their cards, fixes and pages show up.

Open **/doctor api** in game to see what the doctor has taken from your mod, and any mistake it found in your JSON.

## What a finding is

- **A level:** `ok` (green, a test that passed), `info` (grey, worth knowing), `warning` (yellow, may affect play),
  `problem` (red, something isn't running properly or could crash). Keep `problem` for things that are really broken.
- **A title:** one line in plain words: "3 pumps are jammed".
- **Details** (optional): shown when the card is opened.
- **An explanation** and **advice** (optional): why it happens, and what to do.
- **A fix button** (optional): something your mod can do about it there and then.

Say what you checked even when it's fine: an `ok` card tells the player your mod was looked at.

## 1. A JSON file (no code)

Put `vintagedoctor.json` in your mod's `assets/<modid>/config/` folder:

```json
{
  "knownProblems": [
    {
      "id": "old-config",
      "match": "MyMod: config version is outdated",
      "level": "warning",
      "title": "My Mod's settings are from an older version",
      "explain": "Settings that were renamed are back at their defaults.",
      "advice": "Check your settings, or start the file afresh.",
      "fix": { "type": "resetConfig", "file": "mymod.json", "label": "Start mymod.json afresh" }
    },
    {
      "id": "pump-jammed",
      "match": "pump \\d+ jammed",
      "regex": true,
      "side": "server",
      "level": "problem",
      "title": "A pump jammed",
      "fix": { "type": "command", "command": "/mymod unjam" }
    }
  ],
  "conflicts": [
    { "with": "othermod", "level": "warning", "why": "Both replace the firepit; use one of the two." }
  ],
  "recommends": [
    { "mod": "configlib", "why": "For changing My Mod's settings in game." }
  ],
  "configFiles": [ "mymod.json", "mymod/machines.json" ]
}
```

**knownProblems** teach the doctor a line of the game's log. When an error or warning containing `match` is in
this session's log, the doctor shows your card instead of the raw line.

| Key | |
|---|---|
| `match` | Text the log line must contain (case doesn't matter). With `"regex": true`, a regular expression. Required. |
| `title` | The card's line. Required. |
| `id` | A name for it, unique in your file. |
| `level` | `info`, `warning` (the default) or `problem`. `info` ones only show on the log's own page, not in the check-up. |
| `side` | `server`, `client` or `both` (the default): which log to look in. |
| `matchStack` | Text that must also be in the stack trace under the line. |
| `explain`, `advice` | Shown in the card. |
| `mod` | The mod the message is about, if not yours (a compatibility mod explaining another mod's message). |
| `fix` | A button, see below. |

`title`, `explain`, `advice` and a fix's `label` can be lang keys (`"mymod:doctor-oldconfig"`) for translation.

**A fix** in JSON is one of:

- `{ "type": "resetConfig", "file": "mymod.json" }`: moves that file out of `ModConfig` into the doctor's backups,
  so your mod writes a fresh one at the next start. Only files inside `ModConfig`.
- `{ "type": "command", "command": "/mymod repair" }`: runs the command as the server console. The command is
  always shown to the admin, and a server owner can switch these off (`AllowCommandFixes`).
- `{ "type": "url", "url": "https://..." }`: a button that opens a page in the player's browser.

Add `"label"` for the button's words.

**conflicts** say your mod doesn't get on with another. When both are loaded the doctor shows a card (at `level`)
with your `why`. **recommends** shows on your mod's page when the other mod isn't installed. **configFiles** tells
the doctor which files in `ModConfig` are yours, so a broken one is shown as yours.

Server owners can add `knownProblems` of their own, in the same form, to `ModConfig/vintagedoctor.json`.

## 2. The event bus (no reference)

When the doctor builds a page it pushes the event `vintagedoctor:collect` with a tree. Add a tree per finding under
`results`:

```csharp
api.Event.RegisterEventBusListener(OnDoctorCollects, 0.5, "vintagedoctor:collect");
api.Event.RegisterEventBusListener(OnDoctorFixes, 0.5, "vintagedoctor:fix");

void OnDoctorCollects(string eventName, ref EnumHandling handling, IAttribute data)
{
    if (!(data is ITreeAttribute tree) || tree.GetString("side") != "server") return;
    string onlyMod = tree.GetString("mod");               // set when one mod's page is being built
    if (onlyMod != null && onlyMod != "mymod") return;

    ITreeAttribute result = tree.GetOrAddTreeAttribute("results").GetOrAddTreeAttribute("mymod:tanks");
    result.SetString("level", "warning");                 // ok, info, warning or problem
    result.SetString("title", "2 tanks are leaking");
    result.SetString("details", "tank at 10 3 10\ntank at 12 3 10");   // one line each
    result.SetString("advice", "Seal them with resin.");
    result.SetString("fix", "mymod:seal");                // optional: a button
    result.SetString("fixLabel", "Seal them");
    result.SetString("fixArgument", "all");
}

void OnDoctorFixes(string eventName, ref EnumHandling handling, IAttribute data)
{
    if (!(data is ITreeAttribute tree) || tree.GetString("id") != "mymod:seal") return;
    // ... do it ...
    tree.SetString("result", "Sealed 2 tanks");           // one line, shown at the top of the window
    // tree.SetBool("failed", true);                      // when it didn't work
}
```

The name of each result tree should start with your mod id and a colon (`mymod:tanks`); the doctor shows your mod's
name before the title and lists the finding on your mod's page.

What the doctor puts in the tree it sends:

| Key | |
|---|---|
| `side` | `"server"` or `"client"`. Each side of the game asks separately; answer on the side that knows. |
| `topic` | `"overview"` for the check-up, `"mod"` for a mod's page, `"deep"` for the deeper checks. |
| `argument` | What followed the topic (a mod id on a mod's page). |
| `mod` | Set when only one mod's findings are wanted. |
| `language` | The asking player's language code. |
| `player` | The asking player's uid; empty for the console and the start-up check. |

The fix event's tree has `id`, `argument`, `side`, `language` and `player`. On the server a fix only runs for
players allowed to see the server's report (admins, by default) and for the console.

If Vintage Doctor isn't installed, nobody pushes these events and your listeners simply never run.

## 3. The C# API

Reference `VintageDoctor.dll` (from the mod's zip; `VintageDoctor.xml` next to it has the documentation for your
editor) with `<Private>false</Private>` so it isn't copied into your mod.

```csharp
using VintageDoctor.API;

IDoctorApi doctor = DoctorApi.Get(api);      // null when Vintage Doctor isn't installed

doctor.RegisterCheck(new DoctorCheck("mymod", "mymod:pumps", ctx =>
{
    int jammed = CountJammedPumps();
    if (jammed == 0) { ctx.Ok("All pumps are running"); return; }
    ctx.Warn(jammed + " pumps are jammed");
    ctx.Detail("pump at 10 5 10");
    ctx.Explain("A pump jams when its outlet is blocked.");
    ctx.Advice("Clear the outlets, or let the doctor restart the pumps.");
    ctx.FixButton("mymod:unjam", "all", "Restart the pumps");
}));

doctor.RegisterFix(new DoctorFix("mymod", "mymod:unjam", ctx =>
{
    if (ctx.Argument != "all") return ctx.Failed("Which pump?");
    return "Restarted " + RestartPumps() + " pumps";
}));
```

Each side of the game has its own doctor: register server checks in `StartServerSide` and client checks in
`StartClientSide`.

### Without making it a requirement

Your mod loads fine without Vintage Doctor as long as no code that names its types runs. Keep everything that does
in a class of its own, and only call into it when the doctor is there:

```csharp
public override void StartServerSide(ICoreServerAPI api)
{
    if (api.ModLoader.IsModEnabled("vintagedoctor")) WithDoctor.Register(api);
}

static class WithDoctor   // nothing else in your mod names VintageDoctor types
{
    public static void Register(ICoreAPI api) { IDoctorApi doctor = DoctorApi.Get(api); /* ... */ }
}
```

Don't list `vintagedoctor` under `dependencies` in your modinfo.json unless you want it to be required.

### What there is

**`IDoctorApi`**

| | |
|---|---|
| `RegisterCheck(DoctorCheck)` | A check for the check-up and for your mod's page. Set `Deep = true` for anything slow: it then only runs when the player asks for the deeper checks. |
| `RegisterFix(DoctorFix)` | What a fix button does. Return one line saying what happened, or `ctx.Failed("why")`. |
| `RegisterTopic(DoctorTopic)` | A page of your own, opened with `/doctor <id>` and listed under "What are you seeing?" with your question. |
| `RegisterKnownProblem(KnownProblem)` | The same as a JSON known problem, from code. |
| `ReportIncident(modId, level, title, details)` | Something that went wrong just now (a machine jammed, a save failed). Kept for the session and shown under your mod's name; the same title again raises a count. Safe from any thread. |
| `Unregister(modId)` | Takes everything of your mod away again. |
| `DoctorVersion`, `ApiVersion` | `ApiVersion` is 1. It only goes up when something is added; nothing is removed. |

**`IDoctorContext`** (what a check or page gets)

| | |
|---|---|
| `Ok`, `Info`, `Warn`, `Problem`, `Result(level, text)` | A card. |
| `Detail`, `Explain`, `Advice` | Lines inside the last card. `Explain` and `Advice` are left out of bug reports. |
| `FixButton(fixId, argument, label)` | A button that runs a registered fix, then shows the page again. |
| `LinkButton(topic, argument, label)` | A button to another page: `"mod"` with a mod id, `"item"` with a code, `"log"`, a topic of yours... |
| `UrlButton(url, label)`, `CopyButton(text, label)` | Open a web page; copy text to the clipboard. |
| `Section(heading)` | A heading (in a page of your own). |
| `ItemRef(collectible)` | Text that shows as the block's or item's icon and name, and opens its page when clicked. |
| `Translate(key, args)` | A text from your lang file in the asking player's language. |
| `LogIssues(modId)` | This session's errors and warnings from the log, grouped; with a mod id, only that mod's. |
| `Api`, `Side`, `Player`, `LanguageCode`, `Topic`, `Argument`, `HeldItem`, `Compact` | Who asked and for what. `Player` is null for the console and the start-up check. |

A check runs on the game's main thread, so it may read the world. Keep it quick (a few milliseconds); set `Deep`
for the rest. If a check throws, the doctor shows a card saying so with your check's name, and carries on.

## Good checks

- **Check what players actually get wrong:** a machine without power, a network that isn't connected, a setting
  that switches your mod's main feature off, a config from an older version, a mod yours needs a newer version of.
- **Say what to do, not only what is.** "3 pipes aren't connected" plus where they are beats "invalid network state".
- **Offer a fix only when it is safe.** A fix runs when an admin presses its button; say in the label what it does.
  Don't delete anything: move it, or ask.
- **Don't repeat the doctor.** It already reports your mod's errors in the log, a failed start, your patches that
  didn't apply and your broken config files, and puts them on your mod's page.
