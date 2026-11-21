# Notes for Claude

Vintage Doctor: a Vintage Story mod (C#, .NET 10, game 1.22) that finds out what's wrong with the game and its mods
and fixes what it can. More is in the "vintage story mods" project doc `claude/vintage-doctor.md`.

- **Author in modinfo.json is always "demonic69"**, never comp-any. Never add unknown keys to modinfo.json (the Mod DB
  rejects the release); a Mod DB page number goes in `moddb.json` (`{ "page": N }`) next to it, and the build puts it in the zip.
- **No .NET or game in a session.** `.github/workflows/build.yml` compiles against the real server, runs a server with
  the mod, the two example mods and a few real ones, and the mod's self-test (`src/SelfTest.cs`). Job logs can't be read
  from a session, so everything is pushed to the branch `ci-results-<branch>` (build.log, selftest.txt, every page as
  text, the server's log, the zip). Read that branch after a run.
- **It runs on pushes to main, work/** and ci/**.** Work on a `work/...` branch, read its results, then merge into main.
- **Publishing:** bump `version` in modinfo.json and `<Version>` in VintageDoctor.csproj, add a `## <version>` section
  to CHANGELOG.md (it becomes the release notes and the Mod DB changelog), merge to main, then
  `gh workflow run build.yml -R Comp-any/Vintage-Doctor --ref main -f release=true`. Publish every version whose run passes.
- **Waiting for a run:** `timeout 30 gh run watch <id> -R Comp-any/Vintage-Doctor --interval 30 --exit-status`, repeated.
  Never the default interval (it uses up the account's GitHub limit) and never an `until` loop on `gh api`.
- **Texts** live in `assets/vintagedoctor/lang/en.json`; `python3 tools/check_lang.py` checks every text the code
  uses is there with the right number of `{n}` places. The workflow runs it first.
- **Game internals** the doctor relies on (the crash report's format, the mod loader's messages, the profiler's
  marks, the settings' names) were read from decompiled code with `.github/workflows/decompile.yml`. Run it again
  before trusting them on a new game version.
- **Nothing client-side has been seen by a session** (the window, the hotkey, the frame-rate and settings checks):
  CI only runs a server. Say so when reporting, and ask the user to look.
