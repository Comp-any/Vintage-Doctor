#!/usr/bin/env bash
# Builds Vintage Doctor and zips it into release/vintagedoctor_<version>.zip, ready to drop into a Mods folder.
# Needs the .NET 10 SDK and VINTAGE_STORY pointing at a Vintage Story 1.22 install.
set -euo pipefail
cd "$(dirname "$0")/.."

if [ -z "${VINTAGE_STORY:-}" ]; then
  echo "Set VINTAGE_STORY to your Vintage Story install folder (the one with VintagestoryAPI.dll)." >&2
  exit 1
fi

python3 tools/check_lang.py
dotnet build VintageDoctor.csproj -c Release

version=$(python3 -c "import json;print(json.load(open('modinfo.json'))['version'])")
out="release/vintagedoctor_${version}.zip"
mkdir -p release
rm -f "$out"
files="modinfo.json modicon.png VintageDoctor.dll VintageDoctor.xml assets"
[ -f bin/Release/Mods/vintagedoctor/moddb.json ] && files="$files moddb.json"
(cd bin/Release/Mods/vintagedoctor && zip -qr "../../../../$out" $files)
echo "Built $out"
