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

A mod that already has a doctor of its own can hand the whole thing over: see
[A whole doctor of your own](#4-a-whole-doctor-of-your-own).

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

**Where it shows.** The check-up is laid out by mod: a section for the game, then one for each mod with something
to say. Your findings sit under your mod's own heading, with the doctor's findings about your mod (its errors in the
log, a failed start, its broken files) and a button for each page you added. The list at the top of the window shows
one mod's section alone.

**Which characters to use.** The game draws text with the computer's own default font and has no second font to
fall back on, so a character that font lacks is an empty box. Arrows, ticks, warning signs and other symbols can't be
counted on; letters, digits and ordinary punctuation can. The doctor writes symbols out in what you hand it before the
game draws it (an arrow becomes `->`, a tick `[ok]`), so nothing shows as a box, but your text reads best when you
don't use them.

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
| `topic` | `"overview"` for the check-up, `"mod"` for a mod's page. (Up to 1.2.0 there was also `"deep"`, a second, slower round; the check-up runs everything now.) |
| `argument` | What followed the topic (a mod id on a mod's page). |
| `mod` | Set when only one mod's findings are wanted. |
| `language` | The asking player's language code. |
| `player` | The asking player's uid; empty for the console and the start-up check. |

Since 1.2.0 it also has `held` and `heldCode` (what the player holds: the item stack as bytes, and
`item:game:ingot-copper`), `hasLookedAt` with `lookedAtX`, `lookedAtY`, `lookedAtZ`, `lookedAtDim` (the block they look
at), `compact` (true for a bug report: facts only) and `clientfacts` (see section 4).

A result can also carry a link to another page: `link` (a page id, yours or one of the doctor's), `linkArgument` and
`linkLabel`.

The fix event's tree has `id`, `argument`, `side`, `language` and `player`. On the server a fix only runs for
players allowed to see the server's report (admins, by default) and for the console.

**A card on the page about one item or block (1.2.0).** On the doctor's pages about one thing (`/doctor item`,
`/doctor block`, `/doctor craft`) it pushes `vintagedoctor:about` instead, with the same tree plus `subject`
(`item:game:ingot-copper` or `block:game:anvil-copper`); `topic` is `item`, `block` or `craft`. Answer the same way
as for `vintagedoctor:collect`, about that thing only.

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
| `RegisterCheck(DoctorCheck)` | A check for the check-up and for your mod's page. It runs every time the check-up is opened, so a slow one should remember its answer. |
| `RegisterFix(DoctorFix)` | What a fix button does. Return one line saying what happened, or `ctx.Failed("why")`. |
| `RegisterTopic(DoctorTopic)` | A page of your own, opened with `/doctor <id>` and shown as a button, with your question on it, in your mod's section of the check-up. |
| `RegisterKnownProblem(KnownProblem)` | The same as a JSON known problem, from code. |
| `ReportIncident(modId, level, title, details)` | Something that went wrong just now (a machine jammed, a save failed). Kept for the session and shown under your mod's name; the same title again raises a count. Safe from any thread. |
| `Unregister(modId)` | Takes everything of your mod away again. |
| `RegisterClientFacts(modId, collect)` | (API 2, on the player's side) Something your server-side checks need to know about this player's game. `collect(topic, argument)` returns bytes (64 KB at most); they travel with each request and your check reads them with `ctx.ClientFacts(modId)`. |
| `OpenPage(topic, argument, player)` | (API 2) Opens the doctor's window at a page. On the server it asks that player's game; false when their game doesn't have the doctor. |
| `DoctorVersion`, `ApiVersion` | `ApiVersion` is 3 since Vintage Doctor 1.4.0 (2 since 1.2.0, 1 before). It only goes up when something is added; nothing is removed. |

Everything marked "API 2" needs Vintage Doctor 1.2.0, and "API 3" needs 1.4.0. If your mod should also work with an
older doctor, check `ApiVersion >= 2` (or `>= 3`) and keep the code that uses those members in a class of its own, as
the example mod does (`WithDoctor2`, `WithDoctor3`).

**`DoctorCheck`** has, since API 2, `Pages`: the pages the check runs on. (`Deep` is still there so older mods load, but
does nothing since 1.3.0: there is no second round of checks any more.) Left empty it runs on the
check-up and your mod's page; set to any of `"overview"`, `"mod"`, `"item"`, `"block"`, `"craft"` it runs only there.
On the last three it is about one thing, found in `ctx.Subject`.

**`DoctorTopic`** has, since API 2, `OwnHeadings`: the page writes its own headings, so the doctor doesn't put the
question above it as one.

**`IDoctorContext`** (what a check or page gets)

| | |
|---|---|
| `Ok`, `Info`, `Warn`, `Problem`, `Result(level, text)` | A card. |
| `Detail`, `Explain`, `Advice` | Lines inside the last card. `Explain` and `Advice` are left out of bug reports. |
| `FixButton(fixId, argument, label)` | A button that runs a registered fix, then shows the page again. |
| `LinkButton(topic, argument, label)` | A button to another page: `"mod"` with a mod id, `"item"` with a code, `"log"`, a topic of yours... |
| `UrlButton(url, label)`, `CopyButton(text, label)` | Open a web page; copy text to the clipboard. |
| `LogButton(file = null)` | (API 3) A button that opens a log on the player's computer, in whatever opens text files there: this side's own log (`client-main.log` or `server-main.log`) when no name is given, another file of the game's Logs folder by its name (`"server-debug.log"`), or the folder itself for `""`. On a server that is another computer no button is made: the player couldn't open its files. |
| `Section(heading)` | A heading (in a page of your own). |
| `ItemRef(collectible)` | Text that shows as the block's or item's icon and name, and opens its page when clicked. |
| `Translate(key, args)` | A text from your lang file in the asking player's language. |
| `LogIssues(modId)` | This session's errors and warnings from the log, grouped; with a mod id, only that mod's. |
| `Api`, `Side`, `Player`, `LanguageCode`, `Topic`, `Argument`, `HeldItem`, `Compact` | Who asked and for what. `Player` is null for the console and the start-up check. |
| `LookedAt`, `Subject` | (API 2) The block the asker looks at; on the item, block and craft pages, the thing the page is about. |
| `ClientFacts(modId)` | (API 2) What your mod on the asker's own game sent along (`RegisterClientFacts`); null when nothing came. |
| `Write(text, indent)`, `LinkText(...)`, `FixText(...)` | (API 2) A plain line outside any card, and buttons as text to put into it, several on one line. |

A check runs on the game's main thread, so it may read the world, and it runs every time the check-up is opened.
Keep it quick (a few milliseconds); work anything slower out once and remember it. If a check throws, the doctor
shows a card saying so with your check's name, and carries on.

## 4. A whole doctor of your own

For a mod that already has its own "what's wrong" window or command and wants Vintage Doctor to show it instead
when the doctor is installed. Everything here is on the event bus, so your mod needs no reference to the doctor and
loses nothing when it isn't there: keep your own window for that case, and hand over when
`api.ModLoader.IsModEnabled("vintagedoctor")` (and its version is 1.2.0 or newer; an older doctor never pushes
these events). Vintage-E does exactly this.

**Your pages.** The doctor asks which pages you have with `vintagedoctor:topics`, and asks for one with
`vintagedoctor:page`:

```csharp
api.Event.RegisterEventBusListener(OnTopics, 0.5, "vintagedoctor:topics");
api.Event.RegisterEventBusListener(OnPage, 0.5, "vintagedoctor:page");

void OnTopics(string eventName, ref EnumHandling handling, IAttribute data)
{
    if (!(data is ITreeAttribute tree) || tree.GetString("side") != "server") return;
    ITreeAttribute page = tree.GetOrAddTreeAttribute("topics").GetOrAddTreeAttribute("mymod");   // /doctor mymod
    page.SetString("mod", "mymod");
    page.SetString("question", "Something is wrong with my machines");   // the line among the doctor's questions
    page.SetBool("ownHeadings", true);                                   // your page starts with its own heading
}

void OnPage(string eventName, ref EnumHandling handling, IAttribute data)
{
    if (!(data is ITreeAttribute tree) || tree.GetString("topic") != "mymod") return;
    string argument = tree.GetString("argument");      // whatever followed: one page id can be a whole set of pages
    tree["texts"] = new StringArrayAttribute(new[] { "My machines", "⚠ 2 machines have no power", "at 10 5 10",
        "{{fix|mymod:power|all|Power them}} {{open|mymod|list|All machines}}" });
    tree.SetBytes("styles", new byte[] { 1, 5, 3, 0 });
    tree.SetBytes("indents", new byte[] { 0, 0, 1, 1 });
}
```

A page is lines of text, each with a style and an indent:

| Style | |
|---|---|
| 0 | Text. |
| 1 | A heading. |
| 2 | A result line (what a fix did). |
| 3 | A quiet note. |
| 4 | A warning line. |
| 5 | A card. It starts with `✓` (passed), `•` (to know), `⚠` (may affect play) or `✖` (a problem); the indented lines after it are its details, shown when the card is opened. A line starting with `→` is advice. These five marks say what a line is: the doctor never hands them to the game as text (it colours the card and draws the arrow itself), so they are safe to use although the game's font may not have them. |

Inside a line:

- `{{open|<page>|<argument>|<label>}}` is a button to a page: yours (`mymod`, with any argument) or one of the
  doctor's (`mod` with a mod id, `log` with a mod id, `item` with a code, `startup`, `files`...).
- `{{fix|<fix id>|<argument>|<label>}}` is a button that pushes `vintagedoctor:fix` with that id on the side that
  built the page, then shows the page again with the result at the top. Write `{{fix|c:bus:<fix id>|...}}` for a fix
  that has to run on the player's side (opening a window of yours, say).
- `{{url|<address>||<label>}}` opens a web page, `{{copy|<text>||<label>}}` copies text.
- `{{folder|logs|<file>|<label>}}` (1.4.0) opens a file of the game's Logs folder on the player's computer
  (`server-main.log`, `client-debug.log`; only a plain `.log` or `.txt` name), and `{{folder|logs||<label>}}` the
  folder. A doctor before 1.4.0 opens the folder for both. You rarely need to write it: naming the file in the
  line is enough (below), and then the button is only there where the file is on the player's computer.
- `[[item:game:ingot-copper]]` or `[[block:...]]` shows the thing's icon and name.

Labels and arguments can't contain `{`, `}` or `|`.

**Logs get a button by themselves (1.4.0).** A card or line that names a log by its file (`server-main.log`,
`client-debug.log`, `server-crash.log`), in a finding, a page or a known problem's words, gets a button that opens
that file, whichever way it was handed over. Nothing to do for it, and nothing happens for other words (a wooden log
stays a log). It is only added where the file is on the player's computer.

**Pages and fixes that take a while.** A page handler runs on the game's main thread and must return quickly. When
the page needs more (reading files, working something out on another thread), say so and deliver later:

```csharp
string request = tree.GetString("request");
tree.SetBool("later", true);
tree.SetInt("seconds", 30);          // how long the doctor waits before giving up: 5 to 300, 60 if left out
Task.Run(() =>
{
    string[] lines = WorkItOut();
    api.Event.EnqueueMainThreadTask(() =>
    {
        var answer = new TreeAttribute();
        answer.SetString("request", request);
        answer["texts"] = new StringArrayAttribute(lines);
        api.Event.PushEvent("vintagedoctor:deliver", answer);   // from the main thread
    }, "mymod-doctor");
});
```

The doctor tells the player's window to keep waiting, and puts your lines where they belong when they come. A fix
works the same way: set `later` in the fix event's tree and push `vintagedoctor:deliver` with `request` and `result`
(and `failed`) when it is done. From the server console, where a command has to answer at once, the page or result
is written to the console when it comes.

**What your mod on the player's game knows.** A server-side page often needs to know something about the asking
player's game: which version of your mod it has, whether your data arrived there. On the player's side, answer
`vintagedoctor:clientfacts`; whatever bytes you add travel with every request to the server:

```csharp
capi.Event.RegisterEventBusListener((string n, ref EnumHandling h, IAttribute data) =>
{
    if (data is ITreeAttribute tree && tree["facts"] is ITreeAttribute facts) facts.SetBytes("mymod", MyFactsAsBytes());
}, 0.5, "vintagedoctor:clientfacts");
```

On the server they are in every tree the doctor sends (`vintagedoctor:page`, `collect`, `about`, `fix`) as
`clientfacts`: `(tree["clientfacts"] as ITreeAttribute)?.GetBytes("mymod")`. 64 KB per mod at most. Nothing is there
when the console asked or the player's game doesn't have your mod.

**Opening the doctor at your page.** From your own hotkey or command, instead of your own window:

```csharp
var tree = new TreeAttribute();
tree.SetString("topic", "mymod");
tree.SetString("argument", "list");
// tree.SetString("player", player.PlayerUID);   // on the server: whose window
api.Event.PushEvent("vintagedoctor:open", tree);
bool opened = tree.GetBool("opened");            // false: fall back to your own window
```

On the server this only works for players whose own game has the doctor too; `opened` tells you.

**Which side.** The server's doctor and the one in each player's game ask separately, and the window shows both
halves. Offer your page on the side that knows (usually the server) and leave the other side silent: the doctor
doesn't put anything in its place.

## Good checks

- **Check what players actually get wrong:** a machine without power, a network that isn't connected, a setting
  that switches your mod's main feature off, a config from an older version, a mod yours needs a newer version of.
- **Say what to do, not only what is.** "3 pipes aren't connected" plus where they are beats "invalid network state".
- **Offer a fix only when it is safe.** A fix runs when an admin presses its button; say in the label what it does.
  Don't delete anything: move it, or ask.
- **Don't repeat the doctor.** It already reports your mod's errors in the log, a failed start, your patches that
  didn't apply and your broken config files, and puts them on your mod's page.
