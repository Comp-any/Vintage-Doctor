#!/usr/bin/env python3
"""Checks that every text the code asks for is in assets/vintagedoctor/lang/en.json with as many {n} places as the
code gives values, and that no text sits there unused. Run from the repository's top folder; exits 1 on a problem.
With --list, prints every key the code uses and how many values it gives."""
import glob, json, re, sys

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
       "why-savecorrupt", "why-fileaccess", "why-graphics", "why-network", "why-null", "why-bug"]
NOT_TEXTS = {"collect", "fix"}  # the event names "vintagedoctor:collect" and "vintagedoctor:fix"

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
    for p in problems: print(p)
    print(f"{len(used)} texts used, {len(lang)} in the file, {len(problems)} problems")
    return 1 if problems else 0

if __name__ == "__main__":
    sys.exit(main())
