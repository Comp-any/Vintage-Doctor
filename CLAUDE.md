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
  before trusting them on a new game version. The content loader's messages (missing classes and behaviours, files it
  can't read, patches without a file, things registered twice) come from the public VSEssentials source and from what
  the sick server really logged: every one has a case in `tests/mods/doctorsick` or `tools/ci_sick_server.py`, so a
  game version that words them differently fails the sick-server test instead of going unnoticed.
- **A new kind of problem gets a sick-server case** (a broken file in `tests/mods/doctorsick`, or a zip made by
  `tools/ci_sick_server.py`) and an assertion in `SelfTest.SickServer` on what the page says. The first run shows what
  the game really writes; read `sick-server-main.log` on the results branch before guessing.
- **Characters the game can draw.** The game draws text with the computer's own default font (`defaultFontName` is
  "sans-serif"; only headings and big buttons use Lora, which it brings) and has no second font to fall back on: a
  character the font lacks is an empty box. Arrows, ticks, warning signs, hammers and small triangles can't be
  counted on (the user's Linux has none). `tools/game-font-chars.txt` is what is safe; `tools/check_lang.py` holds the
  lang file and every string in the code to it (`// font-ok: why` on a line lets it be). A report keeps its marks
  (`DoctorReport.OkMark`...): they are what the window, the other side and other mods read. Whatever is handed to the
  game to draw goes through `GameFont.Drawable` (the window's texts, button labels, chat), and the window draws its
  own triangle and arrows (`DrawCheckCard`). `decompile.yml` also lists what the game's own font files have.
- **The check-up is by mod** (1.3.0). Each side builds its cards flat, `Pages.BySection` tags every card with the mods
  it is about (`DoctorLine.Section`: tagged with `x.About`, named by a button, or named in its words), and
  `Pages.Arrange` puts the halves together: the game's section, a section per mod with its cards and its own buttons,
  then the mods nothing was found against. The window's list at the top shows one section alone. There is no
  "deeper" round: every check runs every time, so anything slow is done off the main thread or remembered
  (`ZipHealth`, `CountNames`, the patch targets). A check that finds nothing says so on the check-up (`Listed`).
- **Nothing client-side has been seen by a session** (the window, the hotkey, the frame-rate and settings checks):
  CI only runs a server. Say so when reporting, and ask the user to look.
