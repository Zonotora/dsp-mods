# FoundationOneBrickNoSoil v1.1.0 (experimental)

A BepInEx/Harmony mod for Dyson Sphere Program, based on the user's earlier
customized FoundationOneBrickNoSoil v1.0.3. Inspired by
[aekoch's UnlimitedFoundations](https://github.com/aekoch/DSP-Mods/tree/main/UnlimitedFoundations).
It is not an official release from aekoch.

## Features

- **Unlimited Foundations** for normal placement and blueprint placement:
  the player's package reports a large virtual supply of Foundation (item 1131),
  and the mod prevents that package (and the player's hand) from consuming Foundation.
  No Foundations are added to the saved inventory.
- **No Soil Pile cost** for manual placement. Blueprint Soil affordability checks
  are patched where discovered, and the Soil-balance guard from v1.0.3 remains.
  **Note:** v1.0.3 blueprint behavior has not been confirmed against this user's game.
- **Recipe** remains 1 Stone Brick (1108) -> 1 Foundation (1131), despite
  the now-unlimited in-game placement.
- **Equator Foundations button** on the left side of the game screen while
  standing on a solid planet. Click it, set *North* and *South* Foundation-row
  widths independently, then click Fill Equator and Confirm Fill.
- An equator fill is **incremental** (up to 96 reform tiles per frame), uses the
  selected Foundation brush color, and can be cancelled.
- Inputs are saved under `[Equator]` in the BepInEx config for this plugin.

## Equator tool

Set `North=10`, `South=10` for ten complete Foundation rows north of the
planetary equator and ten complete rows south. A zero on one side leaves that
side untouched. Widths are clamped to the current planet's reform grid.

The painter uses `PlatformSystem.reformOffsets` to select whole latitude rows
and the same `PlanetFactory.BeginFlattenTerrain`, `FlattenTerrain`, and
`EndFlattenTerrain` sequence as the game's terrain tool. It skips terrain
flattening on tiles already reformed; existing colored tiles in the band
will be repainted to match the selected color.

**Important limitations**:

- This can permanently alter a large fraction of a planet. Back up your save.
- The action modifies terrain and reform decoration, but does not explicitly
  remove vegetation, move veins, demolish buildings, or clear Dark Fog bases.
  Some tiles may not be valid near those objects.
- The operation runs on the local game instance. Nebula multiplayer
  synchronization is **not tested**; do not run equator fills in multiplayer
  without confirming proper synchronization and backing up the world.
- No compiled DLL or in-game test is included with this source ZIP. This is an
  **experimental implementation** based on publicly available mod examples,
  not a verified working release. Game API changes may require source updates.

## Build on Linux and install through r2modman

Requirements: a current .NET SDK, Python 3, Dyson Sphere Program locally
installed, and BepInEx 5 available through the selected r2modman profile.

1. Extract the source ZIP and open a terminal in `FoundationOneBrickNoSoil`.
2. Run:

```bash
./build-linux.sh "/path/to/Dyson Sphere Program" "/path/to/r2modman/profile"
```

The script uses the game's `DSPGAME_Data/Managed/` assembly references and the
profile's `BepInEx/core/` assembly references **at build time only**. It will
create `bin/Release/net472/FoundationOneBrickNoSoil.dll` and
`Local-FoundationOneBrickNoSoil-1.1.0.zip` (a *different*, compiled package).

3. In r2modman, **disable v1.0.3 and aekoch's UnlimitedFoundations** (and any
   other mod that overrides Foundation costs) in the profile.
4. Choose **Settings > Profile > Import local mod** and import the generated
   `Local-FoundationOneBrickNoSoil-1.1.0.zip`.
5. Launch modded and test on a disposable save first.

If compiling fails, copy the *first* compiler error along with the game version.
If placement still consumes Foundations or Soil, provide the relevant lines
from `BepInEx/LogOutput.log`.

## Acknowledgments / implementation references

- [aekoch's UnlimitedFoundations](https://github.com/aekoch/DSP-Mods/tree/main/UnlimitedFoundations)
- [Bulldozer](https://github.com/mattsemar/dsp-bulldozer) for public examples of
  reform latitude-row offsets and planet surface operations
- [FoundationPainter](https://github.com/excelkobayashi/ExcelDsp) for public
  examples of the terrain FlattenTerrain invocation

These projects are referenced for API usage patterns; this plugin's new equator
implementation was written separately.
