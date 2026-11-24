#!/usr/bin/env python3
"""Checks that every text the code asks for is in assets/vintagedoctor/lang/en.json with as many {n} places as the
code gives values, that no text sits there unused, that no text uses a character the game may not be able to
draw (tools/game-font-chars.txt: an arrow or a tick would show as an empty box), and that every text that speaks
of a log is in the table that gives it a button to open that log (LogTexts in src/Checks/Pages.cs). Run from the
repository's top folder; exits 1 on a problem. With --list, prints every key the code uses and how many values it gives."""
import glob, json, re, sys

def drawable():
    """The characters every font the game comes with has, from tools/game-font-chars.txt."""
    chars = set("\n\t")
    for line in open("tools/game-font-chars.txt", encoding="utf-8"):
        if line.startswith("#"): continue
        for word in line.split():
            first, _, last = word.partition("-")
            chars.update(chr(c) for c in range(int(first, 16), int(last or first, 16) + 1))
    return chars

def undrawable(text, ok):
    """The characters of a text that the game's fonts may lack, as "'x' U+XXXX"."""
    return sorted({"'%s' U+%04X" % (c, ord(c)) for c in text if c not in ok})

# Code whose special characters are never handed to the game as they are: the table that turns them into drawable
# ones, and the self-test, which feeds that table. Anywhere else, a line ending in a "font-ok: why" comment is let be.
FONT_EXEMPT = ("src/Core/GameFont.cs", "src/SelfTest.cs")

def literals(line):
    """The string and char literals on a line of C#, with \\uXXXX written out, stopping at a // comment."""
    found, i, n = [], 0, len(line)
    while i < n:
        ch = line[i]
        if ch == '/' and line[i:i + 2] == '//': break
        if ch in '"\'':
            quote, j, out = ch, i + 1, []
            while j < n and line[j] != quote:
                if line[j] == '\\' and j + 1 < n:
                    if line[j + 1] == 'u' and re.fullmatch(r'[0-9A-Fa-f]{4}', line[j + 2:j + 6] or ''):
                        out.append(chr(int(line[j + 2:j + 6], 16))); j += 6; continue
                    j += 2; continue
                out.append(line[j]); j += 1
            found.append(''.join(out)); i = j + 1; continue
        i += 1
    return found

def calls(source):
    """(key, number of values) for each T("key", ...), Lang.Get("vintagedoctor:key", ...) and Lang.GetL(lang, "vintagedoctor:key", ...)."""
    for m in re.finditer(r'(?:\bT\(\s*"([a-z0-9-]+)"|"vintagedoctor:([a-z0-9-]+)")', source):
        key = m.group(1) or m.group(2)
        i, depth, args, in_str, esc, seen = m.end(), 0, 0, False, False, False
        while i < len(source):
            ch = source[i]
            if in_str:
                if esc: esc = False
                elif ch == '\\': esc = True
                elif ch == '"': in_str = False
            elif ch == '"': in_str = True; seen = True
            elif ch in '([{': depth += 1; seen = True
            elif ch in ')]}':
                if depth == 0: break
                depth -= 1
            elif ch == ',' and depth == 0: args += 1; seen = False
            elif not ch.isspace(): seen = True
            i += 1
        yield key, args

WHY = ["why-sameassembly", "why-missingassembly", "why-version", "why-harmony", "why-format", "why-json", "why-memory", "why-diskfull",
       "why-savecorrupt", "why-fileaccess", "why-graphics", "why-network", "why-null", "why-bug", "why-native", "why-badimage", "why-duplicate", "why-packaging"]
NOT_TEXTS = {"collect", "fix", "topics", "page", "deliver", "clientfacts", "open", "about", "closing"}  # event names such as "vintagedoctor:collect"

def log_texts():
    """The table in src/Checks/Pages.cs of the texts that speak of a log and which one a button should open: {key: kind}."""
    source = open("src/Checks/Pages.cs", encoding="utf-8").read()
    start, end = source.find("// log-texts-begin"), source.find("// log-texts-end")
    if start < 0 or end < start: return None
    return dict(re.findall(r"""\{\s*"([a-z0-9-]+)"\s*,\s*'(.)'\s*\}""", source[start:end]))

def main():
    used = {}
    for path in glob.glob("src/**/*.cs", recursive=True):
        for key, args in calls(open(path, encoding="utf-8").read()):
            if key in NOT_TEXTS: continue
            used[key] = max(used.get(key, 0), args)
    for key in WHY: used.setdefault(key, 0)
    if "--list" in sys.argv:
        for key in sorted(used): print(key, used[key])
        return 0
    lang = json.load(open("assets/vintagedoctor/lang/en.json", encoding="utf-8"))
    problems = []
    for key, args in sorted(used.items()):
        text = lang.get(key)
        if text is None: problems.append(f"missing: {key} ({args} values)"); continue
        places = {int(n) for n in re.findall(r"\{(\d+)\}", text)}
        if places and max(places) >= args: problems.append(f"{key}: the text uses {{{max(places)}}} but the code gives {args} values")
        missing = set(range(args)) - places
        if missing: problems.append(f"{key}: the code gives {args} values but the text never uses {sorted('{%d}' % n for n in missing)}")
    for key in sorted(set(lang) - set(used)):
        problems.append(f"unused: {key}")
    # What the game can draw: every text, and every piece of text the code itself puts together.
    ok = drawable()
    for key, text in sorted(lang.items()):
        bad = undrawable(text, ok)
        if bad: problems.append(f"{key}: the game's font may not have {', '.join(bad)}")
    for path in sorted(glob.glob("src/**/*.cs", recursive=True) + glob.glob("example/**/*.cs", recursive=True)):
        if path.replace("\\", "/") in FONT_EXEMPT: continue
        for number, line in enumerate(open(path, encoding="utf-8"), 1):
            if "font-ok:" in line: continue
            bad = undrawable(''.join(literals(line.rstrip("\n"))), ok)
            if bad: problems.append(f"{path}:{number}: the game's font may not have {', '.join(bad)} (write it another way, or let GameFont.Drawable do it)")
    # A text that speaks of a log gets a button that opens it (Pages.LogButtons): the table there says which.
    table = log_texts()
    if not table: problems.append("src/Checks/Pages.cs: the table of the texts that speak of a log (log-texts-begin ... log-texts-end) is gone")
    else:
        for key, text in sorted(lang.items()):
            if re.search(r"(?i)\blogs?\b|\bcrash reports?\b", text) and key not in table:
                problems.append(f"{key}: the text speaks of a log; say in LogTexts (src/Checks/Pages.cs) which one its button opens: "
                                "'m' this side's log, 'c' its crash reports, 'f' the Logs folder, '-' nothing to open")
        for key, kind in sorted(table.items()):
            if key not in lang: problems.append(f"LogTexts (src/Checks/Pages.cs): {key} is not a text")
            if kind not in "mcf-": problems.append(f"LogTexts (src/Checks/Pages.cs): {key} has '{kind}', which is none of m, c, f and -")
    for p in problems: print(p)
    print(f"{len(used)} texts used, {len(lang)} in the file, {len(problems)} problems")
    return 1 if problems else 0

if __name__ == "__main__":
    sys.exit(main())
