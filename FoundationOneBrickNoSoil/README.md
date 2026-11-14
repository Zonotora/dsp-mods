# FoundationOneBrickNoSoil for Dyson Sphere Program

A **source-code update/replacement** inspired by [aekoch's UnlimitedFoundations](https://github.com/aekoch/DSP-Mods/tree/main/UnlimitedFoundations).

This is **not an official update** by aekoch and is **not a compiled, ready-to-install release**.

## Behavior

- Crafting: **1 Stone Brick (item 1108) -> 1 Foundation (item 1131)**.
- **No Steel** or other ingredient required.
- Placing Foundations **uses actual Foundations from your inventory**. It does NOT fake 1,000 Foundations like the original mod.
- Raising/flattening terrain has **zero Soil Pile cost and zero Soil Pile gain**.
- The base game's Soil Pile counter may remain visible in the UI; the mod removes the cost/gain mechanic, not the UI element.
- Does NOT inject a million Soil Piles or modify saved Soil Pile balances.

## Important: replace, don't combine

**Disable/uninstall aekoch's UnlimitedFoundations before installing this mod.** That mod fakes the Foundation count, which would prevent the new recipe from actually mattering.

## Build and install (Windows)

Requirements:
- Dyson Sphere Program installed locally.
- BepInEx **5** for Dyson Sphere Program (via Thunderstore Mod Manager/r2modman or manual install).
- .NET SDK with .NET Framework **4.7.2 targeting/reference assemblies** available (typically Visual Studio Build Tools with the .NET Framework developer pack).

Open PowerShell in this source folder and run, adjusting the path to your game:

```powershell
.\build-and-install.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\Dyson Sphere Program"
```

Alternatively, compile in Visual Studio and copy the generated `bin\Release\net472\FoundationOneBrickNoSoil.dll` to `Dyson Sphere Program\BepInEx\plugins\FoundationOneBrickNoSoil\`.

If PowerShell blocks local scripts, launch it with the script execution enabled for that single process; do not change your machine-wide policy just for this mod.

## Testing

1. Start the game with BepInEx and **only this Foundation mod** enabled.
2. In `BepInEx\LogOutput.log`, confirm the recipe patch installed and logs `Updated 1 Foundation recipe(s)` (or the number of matching recipes).
3. Check that the Foundation recipe shows one Stone Brick, no Steel, and yields one Foundation.
4. Craft a few Foundations, place them in low terrain/ocean and verify the actual Foundation inventory decreases but the Soil Pile balance does not.
5. Save a backup before testing on an existing game. Machines already holding old ingredients may need their recipe reset.

## Implementation notes

This standalone Harmony/BepInEx plugin patches:
- `RecipeProto.InitRecipeItems` (Prefix): updates the Foundation recipe prototype before the game initializes crafting execution data.
- `BuildTool_Reform.GetNeedSandCountByInc` (Postfix): forces the cost used for both the Soil affordability check and deduction to zero on newer game versions.
- `PlanetFactory.ComputeFlattenTerrainReform` (Postfix): compatibility fallback for older game builds.

It intentionally does **not** patch `StorageComponent.GetItemCount` or `Player.sandCount`, unlike UnlimitedFoundations.

**Status:** Source only. Not built or tested against a live DSP game installation in this environment. Game API changes could require adjustments.

## Version 1.0.1 fix

The first package (1.0.0) incorrectly targeted only the legacy `ComputeFlattenTerrainReform` method. Newer Dyson Sphere Program builds calculate Foundation Soil costs through `BuildTool_Reform.GetNeedSandCountByInc` (including a newer 4-argument signature). This update patches all integer-returning overloads and forces the required Soil amount to zero.

**Disable/remove v1.0.0 and UnlimitedFoundations in r2modman** before enabling v1.0.1. Use a backup save to test that Foundation placement spends Foundations but not Soil, including on water/low-lying land. In the BepInEx log look for `Zero-Soil cost patch installed on 1 GetNeedSandCountByInc method(s).` If the method cannot be found or the count still decreases, report your game version and log. No compiled DLL is bundled.

## Build on Linux with r2modman (recommended)

1. Install the .NET SDK (`dotnet --version`) and Python 3 (`python3 --version`).
2. In Steam, right-click **Dyson Sphere Program > Properties > Installed Files > Browse**. Copy the **game folder** path (it contains `DSPGAME_Data`).
3. In r2modman, open your profile folder. It should contain `BepInEx/core/BepInEx.dll` after installing the xiaoye97 BepInEx package.
4. Run these commands from a terminal, replacing the two paths with your own:

```bash
unzip FoundationOneBrickNoSoil-1.0.3-linux-source.zip
cd FoundationOneBrickNoSoil
./build-linux.sh "/path/to/Dyson Sphere Program" "/path/to/r2modman/profile"
```

The script builds `bin/Release/net472/FoundationOneBrickNoSoil.dll` and also creates `Local-FoundationOneBrickNoSoil-1.0.3.zip` at the project root. This second ZIP is the ready-to-import r2modman package; **do not** import the source ZIP instead.

In r2modman, disable the original UnlimitedFoundations mod, then choose **Settings > Profile > Import local mod** and select `Local-FoundationOneBrickNoSoil-1.0.3.zip`. Start modded. Save backups and test because the gameplay patches have not been validated against the live game.

Linux builds work because the project uses the `Microsoft.NETFramework.ReferenceAssemblies` NuGet package to target .NET Framework 4.7.2 without a Windows installation. The build needs to restore that package from NuGet once (requires network access).

## Version 1.0.2: Foundation blueprints

Version 1.0.1 stopped manual Foundation placement from consuming Soil, but blueprint placement could still require and deduct Soil because it uses a different execution path.

- Adds a Harmony transpiler targeting `BuildTool_BlueprintPaste.DetermineReforms` to skip the blueprint **Soil affordability** check. The normal **Foundation item** checks are not altered.
- Adds a `Player.SetSandCount(long, ...)` prefix that keeps the active player's Soil balance unchanged during gameplay, so blueprint Soil deductions (and any soil gains) cannot modify it.
- Keeps the recipe and manual Foundation patches from v1.0.1.

**Important:** This version freezes all Soil increases and decreases during active gameplay, not just Foundation placement. A displayed Soil Pile counter may still exist. It doesn't grant extra Foundations, and Foundation items should still be consumed for blueprints. If other mods patch the blueprint placement method or sand-count setter, their behavior can conflict.

Build from the v1.0.2 source ZIP and import the generated `Local-FoundationOneBrickNoSoil-1.0.3.zip` into r2modman. Disable v1.0.1 and aekoch's original UnlimitedFoundations first.

Check `BepInEx/LogOutput.log` for `Blueprint Soil affordability check disabled` and `Soil balance guard installed`. If the blueprint transpiler reports a pattern not found, this game version may need a different patch.

**Build status:** source validated for structure only; not compiled or game-tested in this environment. Back up your save before testing with a zero-Soil balance.

## Version 1.0.3 (experimental blueprint fix)

Blueprint Foundation placement on newer DSP versions can bypass both the manual terrain
cost calculation and older blueprint-specific instruction patterns. v1.0.2's
`DetermineReforms` transpiler was dependent on a particular IL layout and could
fail silently if that layout changed.

v1.0.3 **replaces** that brittle pattern with a scan of the actual running game's
`BuildTool_BlueprintPaste` methods. It substitutes only their calls to
`Player.get_sandCount` with a temporary high balance, allowing the game's
blueprint Soil affordability checks to pass. The player's normal Soil balance,
the HUD, and Foundation item inventory are not given this artificial balance.

The existing `Player.SetSandCount` hook prevents actual Soil deductions or gains.
Additionally, when the game is running, a frame-end safeguard restores the
player's original Soil balance if a game update or mod bypasses the setter.
This deliberately freezes Soil across *all* gameplay, not just blueprints.
It does not change the number of Foundation items needed by blueprints.

**Testing:** Use a disposable backed-up save and a profile with other Foundation
mods disabled. Confirm manual placement, blueprint placement with zero Soil,
and that Foundation items are consumed. In the r2modman profile's
`BepInEx/LogOutput.log`, search for these phrases:

- `Blueprint Soil getter patch installed on` (needs >=1 method)
- `Soil balance guard installed on` (needs >=1 method)
- `Soil balance changed despite setter guard` (the fallback repaired a direct write)
- `Could not inspect/patch` or `No blueprint Soil getter calls patched` (report these if present)

If it still consumes Soil or cannot place a blueprint with no Soil, provide the
relevant LogOutput.log lines and your DSP game version; those will reveal which
method was missed. **This source is not compiled or game-tested here.**
