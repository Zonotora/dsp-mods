#!/usr/bin/env bash
set -euo pipefail

if (( $# != 2 )); then
  echo 'Usage: ./build-linux.sh "/path/to/Dyson Sphere Program" "/path/to/r2modman/profile"' >&2
  echo 'Find the first path in Steam > Dyson Sphere Program > Properties > Installed Files > Browse.' >&2
  echo 'Find the second path using the r2modman profile folder option.' >&2
  exit 2
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo 'dotnet not found: install the .NET SDK (not just the runtime).' >&2
  exit 1
fi
if ! command -v python3 >/dev/null 2>&1; then
  echo 'python3 not found: install Python 3 to package the mod ZIP.' >&2
  exit 1
fi

GAME_DIR=$(realpath "$1")
PROFILE_DIR=$(realpath "$2")
if [[ -f "$PROFILE_DIR/BepInEx/core/BepInEx.dll" ]]; then
  CORE_DIR="$PROFILE_DIR/BepInEx/core"
elif [[ -f "$PROFILE_DIR/core/BepInEx.dll" ]]; then
  CORE_DIR="$PROFILE_DIR/core"
else
  echo "BepInEx/core/BepInEx.dll not found in profile: $PROFILE_DIR" >&2
  echo 'Install xiaoye97 BepInEx in r2modman, then provide your r2modman profile folder.' >&2
  exit 1
fi

for file in \
  "$GAME_DIR/DSPGAME_Data/Managed/Assembly-CSharp.dll" \
  "$GAME_DIR/DSPGAME_Data/Managed/UnityEngine.CoreModule.dll" \
  "$CORE_DIR/BepInEx.dll" \
  "$CORE_DIR/0Harmony.dll"; do
  if [[ ! -f "$file" ]]; then
    echo "Missing required DLL: $file" >&2
    exit 1
  fi
done

HERE=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
dotnet build "$HERE/FoundationOneBrickNoSoil.csproj" --configuration Release \
  "-p:DSPGameDir=$GAME_DIR" \
  "-p:BepInExCoreDir=$CORE_DIR"

DLL="$HERE/bin/Release/net472/FoundationOneBrickNoSoil.dll"
[[ -f "$DLL" ]] || { echo "DLL missing after build: $DLL" >&2; exit 1; }

python3 - "$HERE" "$DLL" <<'PY'
import pathlib, sys, zipfile
here = pathlib.Path(sys.argv[1])
dll = pathlib.Path(sys.argv[2])
package = here / 'Local-FoundationOneBrickNoSoil-1.0.3.zip'
with zipfile.ZipFile(package, 'w', zipfile.ZIP_DEFLATED) as z:
    for name in ('manifest.json', 'icon.png', 'README.md'):
        z.write(here / name, name)
    z.write(dll, 'FoundationOneBrickNoSoil.dll')
print(f'Compiled DLL: {dll}')
print(f'r2modman package: {package}')
PY

echo 'Disable the original UnlimitedFoundations mod, then use r2modman > Settings > Profile > Import local mod.'
