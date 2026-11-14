using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace FoundationOneBrickNoSoil
{
    /// <summary>
    /// Replaces UnlimitedFoundations with a craftable, soil-free Foundation.
    /// 1 Stone Brick -> 1 Foundation; terrain work has no soil cost or gain.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.community.dsp.FoundationOneBrickNoSoil";
        public const string PluginName = "FoundationOneBrickNoSoil";
        public const string PluginVersion = "1.0.3";

        private const int FoundationItemId = 1131;
        private const int StoneBrickItemId = 1108;

        private Harmony harmony;
        private static ManualLogSource log;
        // Last-known local player Soil balance, used only as a fallback if a new
        // game version bypasses Player.SetSandCount for blueprint placement.
        private Player trackedPlayer;
        private long stableSandCount;
        private int restorationWarningCount;
        private static bool restoringSoil;
        private static MethodInfo restoreSetter;
        private static readonly MethodInfo BlueprintGetterReplacement =
            AccessTools.Method(typeof(Plugin), nameof(GetBlueprintSandCount));

        private void Awake()
        {
            log = Logger;
            harmony = new Harmony(PluginGuid);

            // Modify recipe prototypes BEFORE the game derives its crafting data.
            // Patching this method avoids adding a dependency on LDBTool.
            try
            {
                var initRecipes = AccessTools.Method(typeof(RecipeProto), "InitRecipeItems");
                if (initRecipes == null)
                {
                    Logger.LogError("RecipeProto.InitRecipeItems was not found; Foundation recipe was NOT changed.");
                }
                else
                {
                    harmony.Patch(
                        initRecipes,
                        prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeInitRecipeItems)));
                    Logger.LogInfo("Foundation recipe patch installed.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not patch Foundation recipe: " + ex);
            }

            // Modern DSP (0.10.34+) calculates the actual Soil Pile cost here,
            // after computing the raw terrain height change. The old
            // PlanetFactory.ComputeFlattenTerrainReform patch below is NOT
            // sufficient on these versions. By returning zero from this
            // conversion method, both the affordability test and the actual
            // soil deduction use zero, while Foundation items remain required.
            try
            {
                int patched = 0;
                foreach (var method in AccessTools.GetDeclaredMethods(typeof(BuildTool_Reform)))
                {
                    if (method.Name != "GetNeedSandCountByInc" || method.ReturnType != typeof(int))
                        continue;

                    harmony.Patch(
                        method,
                        postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterGetNeedSandCountByInc)));
                    patched++;
                }

                if (patched == 0)
                    Logger.LogError("BuildTool_Reform.GetNeedSandCountByInc returning int was not found; Soil cost may still apply.");
                else
                    Logger.LogInfo("Zero-Soil cost patch installed on " + patched + " GetNeedSandCountByInc method(s).");
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not patch modern Soil cost calculation: " + ex);
            }

            // Blueprint terrain work is not performed by BuildTool_Reform.
            // Earlier versions tried to match one specific IL branch in
            // DetermineReforms; that brittle match often fails on new DSP builds.
            // Instead, replace calls to the Player Soil getter across the
            // blueprint paste tool with a high temporary value, so Soil cannot
            // block blueprint reform. The actual balance remains unchanged.
            InstallBlueprintGetterPatches();

            // The actual balance may be adjusted by a different path when
            // pasting a blueprint. Freeze gameplay Soil balance at its existing
            // value instead of giving a fake large Soil count. This covers
            // manual placement, blueprints, and other terrain tools without
            // interfering with the real Foundation item inventory.
            try
            {
                int patched = 0;
                foreach (var method in AccessTools.GetDeclaredMethods(typeof(Player)))
                {
                    if (method.Name != "SetSandCount")
                        continue;
                    var parameters = method.GetParameters();
                    if (parameters.Length == 0 || parameters[0].ParameterType != typeof(long))
                        continue;
                    harmony.Patch(
                        method,
                        prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeSetSandCount)));
                    patched++;
                }

                if (patched == 0)
                    Logger.LogError("Player.SetSandCount(long, ...) not found; blueprint Soil balance may change.");
                else
                    Logger.LogInfo("Soil balance guard installed on " + patched + " Player.SetSandCount method(s).");
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not patch Soil balance updates: " + ex);
            }

            // Compatibility fallback for older DSP versions: suppress both
            // positive Soil gains and negative Soil costs. This old method may
            // no longer exist; that's normal if the modern patch above works.
            try
            {
                var computeSoil = AccessTools.Method(typeof(PlanetFactory), "ComputeFlattenTerrainReform");
                if (computeSoil == null)
                {
                    Logger.LogInfo("Legacy PlanetFactory.ComputeFlattenTerrainReform is absent (normal on newer game versions).");
                }
                else
                {
                    harmony.Patch(
                        computeSoil,
                        postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterComputeFlattenTerrainReform)));
                    Logger.LogInfo("Legacy zero-soil terrain patch installed.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not patch terrain soil cost: " + ex);
            }
        }

        // RecipeProto.InitRecipeItems builds recipe execute data from the LDB.
        // A Prefix updates the source arrays before that happens.
        private static void BeforeInitRecipeItems()
        {
            var recipes = LDB.recipes == null ? null : LDB.recipes.dataArray;
            if (recipes == null)
            {
                log.LogError("Recipe database is unavailable; Foundation recipe was NOT changed.");
                return;
            }

            int changed = 0;
            foreach (var recipe in recipes)
            {
                if (recipe == null || recipe.Results == null || recipe.Results.Length != 1 ||
                    recipe.Results[0] != FoundationItemId)
                {
                    continue;
                }

                // Also removes Steel: only one input remains.
                recipe.Items = new[] { StoneBrickItemId };
                recipe.ItemCounts = new[] { 1 };
                recipe.ResultCounts = new[] { 1 };
                changed++;
            }

            if (changed == 0)
            {
                log.LogWarning("No single-output Foundation recipes found; recipe unchanged.");
            }
            else
            {
                log.LogInfo("Updated " + changed + " Foundation recipe(s): 1 Stone Brick -> 1 Foundation.");
            }
        }

        // Modern Foundation placement uses this number both to check whether
        // the player has enough Soil and to subtract Soil from their balance.
        private static void AfterGetNeedSandCountByInc(ref int __result)
        {
            __result = 0;
        }

        // Only replace direct uses of Player.sandCount inside blueprint paste
        // methods. Other gameplay and HUD reads keep the actual Soil balance.
        private static long GetBlueprintSandCount(Player player)
        {
            // Safe even if a game-version-specific check converts to int.
            return 2000000000L;
        }

        private void InstallBlueprintGetterPatches()
        {
            var getter = AccessTools.PropertyGetter(typeof(Player), "sandCount");
            if (getter == null || getter.ReturnType != typeof(long))
            {
                log.LogError("Blueprint patch: Player.get_sandCount() returning long not found.");
                return;
            }

            int patched = 0;
            int readSites = 0;
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(BuildTool_BlueprintPaste)))
            {
                try
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null)
                        continue;

                    int sites = 0;
                    // Scan original method body before patching so we only
                    // touch methods which actually read the Soil balance.
                    foreach (var instr in PatchProcessor.GetOriginalInstructions(method))
                    {
                        if ((instr.opcode == OpCodes.Call || instr.opcode == OpCodes.Callvirt) &&
                            Equals(instr.operand, getter))
                            sites++;
                    }
                    if (sites == 0)
                        continue;
                    harmony.Patch(method,
                        transpiler: new HarmonyMethod(typeof(Plugin), nameof(ReplaceBlueprintSandReads)));
                    patched++;
                    readSites += sites;
                    log.LogInfo("Blueprint Soil getter patch: " + method.Name + " (" + sites + " read(s)).");
                }
                catch (Exception ex)
                {
                    log.LogWarning("Could not inspect/patch BuildTool_BlueprintPaste." + method.Name + ": " + ex);
                }
            }
            if (patched == 0)
                log.LogError("No blueprint Soil getter calls patched. Please provide BepInEx/LogOutput.log and the game version.");
            else
                log.LogInfo("Blueprint Soil getter patch installed on " + patched + " method(s), " + readSites + " call(s).");
        }

        private static IEnumerable<CodeInstruction> ReplaceBlueprintSandReads(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var getter = AccessTools.PropertyGetter(typeof(Player), "sandCount");
            int count = 0;
            foreach (var instr in instructions)
            {
                if ((instr.opcode == OpCodes.Call || instr.opcode == OpCodes.Callvirt) &&
                    Equals(instr.operand, getter))
                {
                    // Same stack effect: a Player reference -> a long.
                    instr.opcode = OpCodes.Call;
                    instr.operand = BlueprintGetterReplacement;
                    count++;
                }
                yield return instr;
            }
            if (count > 0)
                log.LogInfo("Replaced " + count + " blueprint Soil getter(s) in " + __originalMethod.Name);
        }

        // The active player's Soil balance may still be written directly by
        // blueprint reform code in certain builds. A frame-end reconciliation
        // protects the saved balance when that occurs. It also gives us a log
        // showing whether the SetSandCount hook was bypassed.
        private void Update()
        {
            // Do not capture or restore Soil during a save loading/unloading.
            if (!GameMain.isRunning)
            {
                trackedPlayer = null;
                return;
            }
            var player = GameMain.mainPlayer;
            if (!ReferenceEquals(trackedPlayer, player))
            {
                trackedPlayer = player;
                if (player != null)
                {
                    stableSandCount = player.sandCount;
                    restorationWarningCount = 0;
                    log.LogInfo("Tracking active player's Soil balance: " + stableSandCount);
                }
            }
        }

        private void LateUpdate()
        {
            if (!GameMain.isRunning)
                return;
            var player = GameMain.mainPlayer;
            if (player == null || !ReferenceEquals(trackedPlayer, player))
                return;
            long current = player.sandCount;
            if (current == stableSandCount)
                return;

            if (restorationWarningCount++ < 5)
                log.LogWarning("Soil balance changed despite setter guard (" +
                    stableSandCount + " -> " + current + "); restoring original amount.");
            try
            {
                if (restoreSetter == null)
                {
                    foreach (var m in AccessTools.GetDeclaredMethods(typeof(Player)))
                    {
                        if (m.Name != "SetSandCount") continue;
                        var args = m.GetParameters();
                        if (args.Length >= 1 && args.Length <= 2 && args[0].ParameterType == typeof(long))
                        {
                            restoreSetter = m;
                            break;
                        }
                    }
                }
                if (restoreSetter == null)
                {
                    log.LogError("Cannot restore Soil balance: SetSandCount overload was not found.");
                    return;
                }
                var pars = restoreSetter.GetParameters();
                var argsForCall = pars.Length == 1
                    ? new object[] { stableSandCount }
                    : new object[] { stableSandCount, Enum.ToObject(pars[1].ParameterType, 0) };
                restoringSoil = true;
                restoreSetter.Invoke(player, argsForCall);
                if (player.sandCount != stableSandCount)
                    log.LogError("Soil balance restoration failed (another mod may be overriding it).");
            }
            catch (Exception ex)
            {
                log.LogError("Soil restoration error: " + ex);
            }
            finally
            {
                restoringSoil = false;
            }
        }

        // The original number is normally the player's current Soil balance.
        // Let the game's setter execute with an unchanged number (rather than
        // skipping it entirely) to preserve notifications and event flow.
        // Do not intercept values for nonlocal players or initial load before
        // GameMain.mainPlayer is bound.
        private static void BeforeSetSandCount(Player __instance, ref long __0)
        {
            if (GameMain.isRunning && !restoringSoil && __instance != null && object.ReferenceEquals(GameMain.mainPlayer, __instance))
                __0 = __instance.sandCount;
        }

        // Older versions calculate the raw soil delta through this method.
        private static void AfterComputeFlattenTerrainReform(ref int __result)
        {
            __result = 0;
        }

        private void OnDestroy()
        {
            if (harmony != null)
            {
                harmony.UnpatchSelf();
            }
        }
    }
}
