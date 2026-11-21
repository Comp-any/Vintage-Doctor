#!/usr/bin/env python3
"""Fills a data folder with the things that go wrong in real Mods folders, for the doctor's "sick server" test run:
zips packed the wrong way, a zip that isn't a mod, a damaged one, a mod installed twice, mods that bring libraries the
game already has, two mods that replace the same file of the game's, a config file cut short and a crash report from an
earlier session. The broken mods themselves are in tests/mods (copy them into <data folder>/Mods first).

usage: ci_sick_server.py <data folder> [<game folder>]
"""
import glob, json, os, sys, zipfile

data = sys.argv[1]
game = sys.argv[2] if len(sys.argv) > 2 else None
mods, config, logs = (os.path.join(data, d) for d in ("Mods", "ModConfig", "Logs"))
for d in (mods, config, logs): os.makedirs(d, exist_ok=True)

def info(modid, version="1.0.0", made_for="1.22.0"):
    return json.dumps({"type": "content", "modid": modid, "name": modid, "version": version, "authors": ["demonic69"], "dependencies": {"game": made_for}})

def write_zip(name, entries):
    path = os.path.join(mods, name)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        for entry, text in entries: z.writestr(entry, text)
    return path

# File names with "\" instead of "/": nothing in it loads on Linux.
write_zip("doctor-sick-backslash.zip", [("modinfo.json", info("doctorsickslash")), ("assets\\doctorsickslash\\lang\\en.json", "{}"),
                                        ("assets\\doctorsickslash\\itemtypes\\thing.json", '{ "code": "thing" }')])
# The mod's files one folder down, as when a folder is zipped instead of its contents.
write_zip("doctor-sick-nested.zip", [("Nested-main/modinfo.json", info("doctorsicknested")), ("Nested-main/assets/doctorsicknested/lang/en.json", "{}")])
write_zip("doctor-sick-notamod.zip", [("readme.txt", "not a mod")])
inner = write_zip("doctorwrapped_1.0.0.zip", [("modinfo.json", info("doctorwrapped"))])
with zipfile.ZipFile(os.path.join(mods, "doctor-sick-wrapped.zip"), "w") as z: z.write(inner, "doctorwrapped_1.0.0.zip")
os.remove(inner)
with open(os.path.join(mods, "doctor-sick-corrupt.zip"), "wb") as f: f.write(b"this is not a zip file at all, only text pretending to be one " * 4)
with open(os.path.join(mods, "doctor-sick-packed.rar"), "wb") as f: f.write(b"Rar!\x1a\x07\x00 not really")
write_zip("doctordup_1.0.0.zip", [("modinfo.json", info("doctordup", "1.0.0"))])
write_zip("doctordup_1.1.0.zip", [("modinfo.json", info("doctordup", "1.1.0"))])


# A .dll in a subfolder: the game refuses the whole mod.
write_zip("doctor-sick-straydll.zip", [("modinfo.json", info("doctorsickstray")), ("lib/Helper.dll", "not really code")])
# Mods that bring libraries along: one the game already has, and one that is in two mods. Both call themselves
# content mods, so the game leaves the (made-up) code alone and says so.
write_zip("doctor-sick-bundled.zip", [("modinfo.json", info("doctorsickbundled")), ("0Harmony.dll", "not really code"), ("SickShared.dll", "not really code")])
write_zip("doctor-sick-shared.zip", [("modinfo.json", info("doctorsickshared")), ("SickShared.dll", "not really code")])

# Two mods that replace the same file of the game's, one of them made for an older game. The file is a real one of the
# game's, copied as it is, so nothing in the game changes.
override = None
if game:
    found = sorted(glob.glob(os.path.join(game, "assets", "survival", "recipes", "grid", "*.json")))
    if found:
        override = "recipes/grid/" + os.path.basename(found[0])
        with open(found[0], "rb") as f: original = f.read()
        old = [("modinfo.json", info("doctorsickold", made_for="1.19.0")), ("assets/game/" + override, original),
               ("assets/doctorsick/config/note.json", '{ "note": "replaces the file of the same name in Doctor Sick" }')]
        write_zip("doctor-sick-old.zip", old)
        sick = os.path.join(mods, "doctorsick")
        if os.path.isdir(sick):
            target = os.path.join(sick, "assets", "game", *override.split("/"))
            os.makedirs(os.path.dirname(target), exist_ok=True)
            with open(target, "wb") as f: f.write(original)
with open(os.path.join(config, "doctor-sick-override.txt"), "w") as f: f.write(override or "")
print("the game file both sick mods replace:", override)

with open(os.path.join(config, "doctorcrashy.json"), "w") as f: f.write('{ "enabled": tru')
with open(os.path.join(config, "vintagedoctor.json"), "w") as f: f.write('{ "SelfTestOnStart": true }')
with open(os.path.join(config, "doctor-sick-server.marker"), "w") as f: f.write("the doctor's self-test runs its sick-server checks when this file is here")

# A real crash report: the one the game wrote when an early build of the doctor switched the tick profiler on mid-tick.
with open(os.path.join(logs, "server-crash.log"), "w") as f:
    f.write("""Game Version: v1.22.7 (Stable)
11/21/2026 05:56:18: Critical error occurred
Loaded Mods: 
System.NullReferenceException: Object reference not set to an instance of an object.
   at Vintagestory.API.Common.FrameProfilerUtil.End() in VintagestoryApi\\Common\\FrameProfilerUtil.cs:line 213
   at Vintagestory.Server.ServerMain.Process() in VintagestoryLib\\Server\\ServerMain.cs:line 941
   at Vintagestory.Server.ServerProgram.Main() in VintagestoryLib\\Server\\ServerProgram.cs:line 156
   at Vintagestory.ClientNative.CrashReporter.Start(ThreadStart start) in VintagestoryLib\\Client\\ClientPlatform\\ClientNative\\CrashReporter.cs:line 95
""")
print("sick server set up in", data, "-", sorted(os.listdir(mods)))
