#!/usr/bin/env python3
"""Which characters a font can draw, read from the font file itself (its cmap table). No libraries needed.

The game draws every text with its own fonts and has no second font to fall back on, so a character the font
lacks is drawn as an empty box. This lists what each font file under the given folders has, so the texts can be
checked against it (tools/check_lang.py reads tools/game-font-chars.txt, which is made from this output).

    python3 tools/font_chars.py <folder or font file> [...]            what every font has, as ranges
    python3 tools/font_chars.py --common <folder or font file> [...]   only what ALL of them have
"""
import os
import struct
import sys

# The characters whose presence decides how the doctor writes things; each is reported by name for every font.
ASKED = "→←↑↓↔⇒…×·•–—‘’“”«»‹›°±√∞≈≠≤≥✓✔✗✘✖⚠⚒▸▾▶▼►◄▲■□●○◆★☆♥"


def fonts_in(data):
    """Offsets of the fonts in a file: one for .ttf/.otf, several for a .ttc collection."""
    if data[:4] == b"ttcf":
        count = struct.unpack_from(">L", data, 8)[0]
        return list(struct.unpack_from(">%dL" % count, data, 12))
    return [0]


def tables(data, offset):
    count = struct.unpack_from(">H", data, offset + 4)[0]
    found = {}
    for i in range(count):
        tag, _, start, length = struct.unpack_from(">4sLLL", data, offset + 12 + 16 * i)
        found[tag.decode("latin1")] = (start, length)
    return found


def names(data, start):
    """Family, style and full name from the name table."""
    _, count, strings = struct.unpack_from(">HHH", data, start)
    got = {}
    for i in range(count):
        platform, encoding, language, name_id, length, offset = struct.unpack_from(">6H", data, start + 6 + 12 * i)
        if name_id not in (1, 2, 4):
            continue
        raw = data[start + strings + offset: start + strings + offset + length]
        try:
            text = raw.decode("utf-16-be") if platform in (0, 3) else raw.decode("mac_roman")
        except Exception:
            continue
        # English names win; anything is better than nothing.
        english = (platform == 3 and language == 0x409) or (platform == 1 and language == 0)
        if name_id not in got or english:
            got[name_id] = text
    return got.get(1, "?"), got.get(2, "?"), got.get(4, "?")


def characters(data, start):
    """Every code point the cmap table gives a real glyph (glyph 0 is the empty box)."""
    _, count = struct.unpack_from(">HH", data, start)
    chars = set()
    for i in range(count):
        platform, encoding, offset = struct.unpack_from(">HHL", data, start + 4 + 8 * i)
        if not (platform == 0 or (platform == 3 and encoding in (1, 10))):
            continue
        sub = start + offset
        fmt = struct.unpack_from(">H", data, sub)[0]
        if fmt == 4:
            seg2 = struct.unpack_from(">H", data, sub + 6)[0]
            seg = seg2 // 2
            ends = struct.unpack_from(">%dH" % seg, data, sub + 14)
            starts = struct.unpack_from(">%dH" % seg, data, sub + 16 + seg2)
            deltas = struct.unpack_from(">%dh" % seg, data, sub + 16 + 2 * seg2)
            offsets_at = sub + 16 + 3 * seg2
            offsets = struct.unpack_from(">%dH" % seg, data, offsets_at)
            for s in range(seg):
                for c in range(starts[s], ends[s] + 1):
                    if c == 0xFFFF:
                        continue
                    if offsets[s] == 0:
                        glyph = (c + deltas[s]) & 0xFFFF
                    else:
                        at = offsets_at + 2 * s + offsets[s] + 2 * (c - starts[s])
                        if at + 2 > len(data):
                            continue
                        glyph = struct.unpack_from(">H", data, at)[0]
                        if glyph:
                            glyph = (glyph + deltas[s]) & 0xFFFF
                    if glyph:
                        chars.add(c)
        elif fmt == 12:
            groups = struct.unpack_from(">L", data, sub + 12)[0]
            for g in range(groups):
                first, last, glyph = struct.unpack_from(">LLL", data, sub + 16 + 12 * g)
                for c in range(first, last + 1):
                    if glyph + (c - first):
                        chars.add(c)
        elif fmt == 6:
            first, n = struct.unpack_from(">HH", data, sub + 6)
            for k, glyph in enumerate(struct.unpack_from(">%dH" % n, data, sub + 10)):
                if glyph:
                    chars.add(first + k)
        elif fmt == 0:
            for c, glyph in enumerate(struct.unpack_from(">256B", data, sub + 6)):
                if glyph:
                    chars.add(c)
    return chars


def ranges(chars):
    """Sorted code points as 'XXXX' and 'XXXX-YYYY' words."""
    out, run = [], []
    for c in sorted(chars):
        if run and c == run[-1] + 1:
            run.append(c)
            continue
        if run:
            out.append(run)
        run = [c]
    if run:
        out.append(run)
    return " ".join("%04X" % r[0] if len(r) == 1 else "%04X-%04X" % (r[0], r[-1]) for r in out)


def read(path):
    """[(family, style, full name, characters)] for each font in the file."""
    with open(path, "rb") as f:
        data = f.read()
    found = []
    for offset in fonts_in(data):
        t = tables(data, offset)
        if "cmap" not in t:
            continue
        family, style, full = names(data, t["name"][0]) if "name" in t else ("?", "?", "?")
        found.append((family, style, full, characters(data, t["cmap"][0])))
    return found


def font_files(places):
    for place in places:
        if os.path.isfile(place):
            yield place
            continue
        for folder, _, files in os.walk(place):
            for name in sorted(files):
                if name.lower().endswith((".ttf", ".otf", ".ttc")):
                    yield os.path.join(folder, name)


def main(argv):
    common_only = "--common" in argv
    places = [a for a in argv if not a.startswith("--")]
    if not places:
        print(__doc__)
        return 2
    everything = []
    for path in font_files(places):
        try:
            for family, style, full, chars in read(path):
                everything.append((path, family, style, full, chars))
        except Exception as e:  # a file that isn't a font after all
            print("# %s: can't be read (%s)" % (path, e))
    if not everything:
        print("# no font files found under: " + ", ".join(places))
        return 1
    common = set.intersection(*[e[4] for e in everything])
    if not common_only:
        for path, family, style, full, chars in everything:
            print("# %s: %s / %s (%s), %d characters" % (path, family, style, full, len(chars)))
            print("#   has:   " + (" ".join(c for c in ASKED if ord(c) in chars) or "none of the ones asked about"))
            print("#   lacks: " + (" ".join("%s U+%04X" % (c, ord(c)) for c in ASKED if ord(c) not in chars) or "none"))
            print(ranges(chars))
            print()
    print("# In every one of the %d fonts above (%d characters):" % (len(everything), len(common)))
    print(ranges(common))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
