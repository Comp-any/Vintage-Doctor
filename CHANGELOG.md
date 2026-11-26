# Changelog

## 1.9.0

- **Network traffic.** The check-up and the performance page have a card for the network: what the server sends and receives (and for how many players), and what your game receives, averaged over the last minute, with the busiest second and the totals. It warns when the traffic stays over 1.5 MB/s for each player, which slow connections can't keep up with.
- **A network page of its own** (`/doctor network`, or the button on the card): the same with graphs of the last minute and the last ten minutes, sent and received, for the server and for your game.
- **Measure the network.** A button on the network page counts the traffic for 20 seconds, on the server and your game at once, and shows what it is made of: chunks, creatures, changed blocks, block entities by type, and mods' messages by mod (each mod's channel, with a button to its page). A mod that sends most of the traffic gets a warning card with a copy button for its author. The window's status line counts down while it measures, and the page opens again when it is done. It uses the game's own counters; the count by mod is done by two small patches on the server's sending that do nothing between measurements.
- **Graphs for other mods.** A page line can hold `{{graph|values|unit|title}}` (values comma separated, a second line after `;`, unit `bytes` for B/s): the window draws it, the console writes the newest, the average and the top. See docs/api.md.

## 1.8.0

- **A countdown while measuring.** When you press "Measure where the time goes", the window's status line now says what is being measured and how long is left ("Measuring the server: 14 s left", or your game, or both), counting down every half second; the page opens by itself when it is done. The server tells your game its seconds left with each page.
- The measuring card now says which side each button times (your game's frames, or the server's ticks) and that it runs for real seconds, so a lagging or overloaded game fits fewer ticks into it and its slow ticks show up as the time they took.

## 1.7.0

- **Lines that break the chat, and a button that clears it.** The game draws all of a chat tab's lines as one formatted text, so a single line with a tag it never closes (say a mod's message with `<font ...>` and no `</font>`) changes every line after it, and a stray closing tag or `>` makes it write an error each time the chat is drawn, until the line is gone. The check-up now reads the chat's lines on your game and says which ones do this, with **Clear the chat** (the same as typing `.clearchat`) and **Copy the lines**, to send to whoever wrote them.
- **Every card can be copied for a report.** Each card that isn't simply "passed" has a **Copy this for a report** button: the doctor's and the game's versions, the mod the card is under, and the card's lines with what was found, why, and what to do.

## 1.6.2

- **The doctor's own command help caused a formatting error.** The help of `.doctor` and `/doctor` said `mod <id>`, and the game shows command help as formatted text, so it read `<id>` as a tag and wrote "Found closing tag <font> ... but <id> should be closed first" to the log (found by the doctor itself, in a real game). The help and the doctor's other usage texts now write `[mod id]`, and the doctor's texts are checked for this when it is built.
- **Commands' help is checked too.** The formatting-tag card now also looks at every chat command's help, and at texts that open a tag and never close it (harmless alone, an error once the game puts the text inside its own formatting), and names them: "the help of the command .something".

## 1.6.1

- **The check-up's sections are coloured by what is in them,** which is also the order they are in: a mod with a problem has a red heading, one with a warning an orange one, the rest the usual gold. In the list of mods at the top, the same mods are marked [x] and [!].

## 1.6.0

- **A mod that closes the game on purpose can say so.** Vintage Auto Update restarts or quits the game to finish installing updates, and the doctor then reported "The game didn't close properly last time". A mod now tells the doctor just before (`vintagedoctor:closing`, with its name and why), and the next start says "Vintage Auto Update closed the game last time (time), on purpose: restarting to install 3 mod update(s)" as something to know, not a warning. Vintage Auto Update does this from its 0.9.1.
- For mod authors: see "Closing the game on purpose" in [docs/api.md](docs/api.md).

## 1.5.0

Whose texts make the game's "Found closing tag" errors, the doctor always opening at its main page, and folding sections.

- **Formatting tags out of place, and whose they are.** The game reads < and > in texts (chat, item descriptions, tooltips, the handbook) as formatting tags, and writes "Found closing tag char > but no tag was opened at 12..." to the log each time a text has a > on its own or a closing tag that doesn't match. The error never says whose text it was. The doctor now reads every mod's texts the way the game does, matches each error to the text that makes exactly that error, and says so on a card in that mod's section: "Some Mod's texts have formatting tags out of place: 14 error(s) about them in the log this session", with each text's key in the mod's lang file, what is wrong in it and where, and what its author should change. When the same mistake sits at another place (something was put in front of the text) it says "likely". It also reads the debug and chat logs for the text the game showed, when they have it.
- Mods whose texts would make such errors, with none in the log yet, get a card to know; errors no mod's text fits get one card of their own with the text shown and where the whole of it is.
- These errors are no longer counted among the log's other errors "no mod could be named for".
- **The doctor opens at its main page.** When another mod opens the doctor with nothing more said (Vintage-E's Ctrl+H or `/emc diagnose` on its own), the window opens at the check-up, where that mod's section has the button for its pages, instead of at the mod's page. Asked for something in particular (`/emc diagnose machines`), it still opens that.
- **Sections fold.** Each section of the check-up folds and unfolds from its heading (a triangle in front of it). A mod's section with no warnings or problems starts folded, with what is in it after the name ("4 check(s), nothing to look at"); one with something to look at starts open, and so does the game's. The list at the top that shows one mod alone shows it whole. What you fold or unfold stays so while the window is open.
- For mod authors: `vintagedoctor:open` with an empty argument opens the check-up (see [docs/api.md](docs/api.md)).
- Not seen in a real game by the session that made it: the folding in the window, and the window opening at the check-up from Vintage-E's key. The tests run a server, where the pages are checked as text; there the formatting-tag errors were made by the game's own parser and traced to the test mod's texts.

## 1.4.0

Logs open from where the doctor speaks of them, and the page about code patches says who shares what.

- **A button that opens the log, wherever a log is spoken of.** A card or a page that refers to the log has *Open client-main.log* (or *server-main.log* for the world's side of a single-player game); one that refers to crash reports has *Open client-crash.log*; where it is the logs as a whole, or a file that may not exist yet, *Open the Logs folder*. The file opens in whatever your computer opens text files with. The game's window may stay in front of it, and the doctor's window says so when it has opened one.
- That covers the pages of the log, of start-up and of crashes (the button is at the top), the cards for each mod's errors, a mod's page, the list of mods, an item's page, and any card from another mod that names a log by its file.
- In single player, where both sides found the same ("no errors in the log"), the one card has a button for each side's log.
- A server that is another computer can't offer its files to your computer, so its cards have no such button: what is in its log is on the doctor's own page of it, as before.
- **Which mods change the same place in the game's code, and what each does there.** The page about code patches used to say "2 shared with another mod" and leave it at that on the check-up. Now every set of mods that meet gets a card of its own: the mods in its title, and under it each place by name with what each mod does to it (runs before the game's code, runs after it, rewrites it, steps in when it ends or fails). On the check-up the card is in the sections of the mods it names. Two mods rewriting the same piece of code is a warning; the rest is to know.
- A mod's own page lists the places it shares first, each with who else is at it.
- For mod authors: `ctx.LogButton()` (API version 3) puts that button under a finding, `{{folder|logs|<file>|<label>}}` writes it into a line of a page, and a finding that names a log by its file gets it without asking. See [docs/api.md](docs/api.md).
- For whoever works on the doctor: a text that says "log" or "crash report" has to say in one table which file its button opens (`LogTexts` in `src/Checks/Pages.cs`); `tools/check_lang.py` fails until it does, so a new text can't be left without its button.
- Not seen in a real game by the session that made it: the buttons in the window, and the file opening on Windows, Linux and macOS. The tests run a server, where the pages and their buttons are checked as text.

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
