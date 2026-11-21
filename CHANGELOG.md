# Changelog

## 1.3.0

The check-up is one page by mod, and nothing on it needs a character the game can't draw.

- **Symbols that showed as empty boxes are gone.** The game draws text with your computer's own default font, and on many computers that font has no arrows, ticks, warning signs or small triangles. The window now draws its own arrow before a line that says what to do and its own triangle on a card that opens, a fix button has green words instead of a hammer, and in chat a page's marks are written `[ok]`, `[!]`, `[x]` and `->`. Whatever a mod hands the doctor gets the same treatment before the game draws it.
- **The check-up lists every check, each time.** There is no second round of "deeper checks" any more, and no button for it. What it used to hold is on the check-up: every mod zip read to its end (off the game's main thread, and each file only once until it changes), patches of two mods on the same thing, things that show a code instead of a name, recipes with the same ingredients, and every mod's own slower checks. A check that found nothing says so, so you can see what was looked at. `/doctor deep` opens the check-up.
- **Laid out by mod.** A section for the game itself (crashes, lag, the Mods folder, the world), then a section for each mod with something against it or something to offer, the ones with problems first, then one card naming the mods nothing was found against. What your game found and what the server found about a mod are together under its heading.
- **Show one mod alone.** Where the deeper checks button was, a list of the mods on the page: pick one and only its section is shown (and counted, and copied).
- **Each mod's errors in the log get a card of their own,** in that mod's section, with a button to all of them.
- **A mod's buttons are in the mod's own section,** not in one list at the bottom: its pages, and one for everything the doctor knows about it. The doctor's own pages are buttons in the game's section.
- For mod authors: `DoctorCheck.Deep` no longer does anything (the field stays, so older mods load); a check runs every time the check-up is opened, so a slow one should remember its answer. Nothing else changes: your findings and your pages move under your mod's heading by themselves. See [docs/api.md](docs/api.md).
- Not seen in a real game by the session that made it: the window's new drawing, the list of mods and the green fix buttons. The tests run a server, where the pages are checked as text.

## 1.2.0

For mods that already have a doctor of their own: they can now hand the whole thing over, so there is one window instead of two. Vintage-E is the first to do it (from its version 1.26.0): with Vintage Doctor installed, its EMC check-up, its pages and its fixes show up here, under "Something is wrong with EMC values". Nothing changes for players who don't have such a mod.

- **A mod's own pages, written by the mod.** A mod can offer whole pages line by line (headings, cards, buttons), opened like any other page and listed among the questions. One page id can be a whole set of pages.
- **Pages and fixes that take a while.** A mod can say "this needs a moment" and deliver its page or its fix's result a few seconds later. The window keeps waiting and says a mod is working on it; from the server console the result is written out when it comes. A mod that never answers leaves a warning instead of a page that hangs.
- **What a mod on your own game knows travels with the request,** so its checks on the server can compare the two sides (is the same version on both, did its data arrive).
- **Cards from mods on the pages about one thing:** an item, the block you look at, why something can't be crafted.
- **A mod can open the doctor's window at its page** from its own key or command.
- A page that only the server (or only your game) has no longer makes the other side answer with the check-up.
- For mod authors: all of it works over the event bus, with no reference to the doctor, and most of it from the C# API too (API version 2). See [docs/api.md](docs/api.md), section 4; the example mod uses every part, and the test run checks it on a real server.

## 1.1.0

The ways mods break themselves and each other. A new page, **Are any mods getting in each other's way?** (`/doctor clashes`), and what matters from it on the check-up.

- **Blocks and items whose code is missing:** a block or item that names code no loaded mod has (its class, its data class or a behaviour) is still in the game, but does nothing (machines don't open, tools don't work). The doctor lists them by mod with the code each needs, and says when it is because the mod's code failed to start.
- **Blocks and items the game refused to add,** which is what happens when two mods, or two files of one mod, add a thing with the same name.
- **Block and item files the game couldn't read,** by mod, and **a mod whose blocks and items never arrived** at all (with the repack button when it is a zip packed with `\`).
- **Code the game leaves out:** a mod with a `.dll` that calls itself a content mod, and a zip with a `.dll` in a subfolder, which the game refuses to load whole.
- **Patches for files that aren't there:** told apart into the harmless kind (a patch for a mod you don't have, which is how mods support each other) and the kind that has stopped working (the game's file or an installed mod's file was renamed or removed).
- **Files replaced by more than one mod:** the game uses one copy and drops the other without a word. Data and shaders are a warning; textures, shapes and sounds a note.
- **Mods that ship whole copies of the game's own files,** and a warning when such a mod was made for an older game: everything the game changed in those files since is undone.
- **Libraries brought along:** a mod with its own copy of a library the game has (Harmony, Newtonsoft.Json...), or the same library in two mods. A warning when the mod has errors, a note when it doesn't, and a fix that takes the game's libraries out of the zip (the original is kept).
- **Recipes with the same ingredients that make different things:** only one of them can ever be crafted. On the clashes page, and on an item's "why can't I craft it" page.
- **Mods with code made for an older game that have errors now:** the ones to update first.
- **Memory that keeps growing:** the doctor notes memory use once a minute and tells "uses a lot" from "uses more every minute".
- **Things without a name** (full check-up): a mod whose blocks and items show a code instead of a name.
- **More errors explained:** a name registered twice, a library made for another system, a damaged code file, a code mod packed the wrong way, two mods with a `.dll` of the same name.
- A mod's own page now says which of the game's files it replaces and which libraries it brings.

## 1.0.0

The first version. The doctor from Vintage-E (its window, its cards, its fixes and its bug report) taken out into a mod of its own that looks at the whole game and every mod, not at EMC.

- **The window (Ctrl+Shift+D, or /doctor):** a check-up of cards, green for passed, yellow for worth a look, red for a problem, grey for information. Click a card for what was found, why, and what to do; green ⚒ buttons do a fix and run the checks again. Under the cards, questions ("The game crashed", "The game is slow", "A mod isn't working"...) open a page for just that.
- **Works on either side or both:** your own game checks its log, crash reports, settings, keys and Mods folder; the server checks its own and the world. In single player you get both. On a server without the mod you still get your own game's half; a server with it answers admins.
- **Crashes:** reads the game's crash reports, names the mod the crash points at (the game's own naming, then the doctor's reading of the trace), says in plain words what the error usually means, and whether that mod has been updated since. Also notices when the game didn't close properly and left no report.
- **Start-up:** mods that threw an error while starting, mods left out for a missing or too-old requirement (with a link to find it), mods on the Mod DB's block list, mods installed twice, JSON patches that didn't apply and recipes that name things that don't exist, each by mod.
- **The log:** every error and warning of the session, the same message counted once, traced to its mod (by its tag, its files, its code in the trace or its name) and explained where the doctor knows the message.
- **The Mods folder and config files:** zips that can't be opened, mods zipped inside a folder or inside another zip, zips with `\` in their file names (which load nothing on Linux and Mac), source code downloaded instead of a release, mods made for a newer game, config files that are empty or broken. Fixes repack a zip the right way, take the inner zip out, move spare copies and broken files aside, and let a mod write its config afresh. Nothing is deleted: everything goes to ModData/vintagedoctor/backups.
- **Lag:** the server's tick times and the game's frame rate, what is loaded, memory, and a measuring button that uses the game's own profiler for 20 seconds and shows where the time went, by part of the game and by mod.
- **Also:** which mods change the game's code and where two change the same place; JSON patches from different mods on the same thing; blocks and items left in the world by mods that are gone; free disk space; keys shared by two controls; your mods' versions against the server's; what changed in the mod list since last time; a page for any mod, block or item; why something can't be crafted.
- **A report to post:** versions, the mod list, the findings, the errors and the latest crash, with addresses, player ids and user folders taken out.
- **For other mods:** an API to hand the doctor checks, fixes, pages and explanations for log messages: a few lines of C#, the event bus (no reference to the doctor), or a JSON file with no code at all. See docs/api.md.
