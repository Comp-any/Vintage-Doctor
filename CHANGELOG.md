# Changelog

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
