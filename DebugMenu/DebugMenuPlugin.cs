using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using System.Reflection;
using System;
using BepInEx.Configuration;

namespace EasyRed2Mod
{
    [BepInPlugin("pl.avene.easyred2.ultimate", "Easy Red 2 Ultimate Menu", "1.4.5")]
    public class DebugMenuPlugin : BepInEx.Unity.IL2CPP.BasePlugin
    {
        public static DebugMenuController Instance;
        public static DebugMenuPlugin PluginInstance;

        // --- CONFIGURATION (Saved automatically to BepInEx/config) ---
        public static ConfigEntry<KeyCode> ConfigNoclipKey;
        public static ConfigEntry<KeyCode> ConfigTeleportKey;
        public static ConfigEntry<KeyCode> ConfigVehFlyKey;
        public static ConfigEntry<KeyCode> ConfigMenuKey;
        public static ConfigEntry<KeyCode> ConfigAutoHealKey;
        public static ConfigEntry<bool> ConfigIncludeModdedWeapons;

        public override void Load()
        {
            PluginInstance = this;

            // --- CONFIG INITIALIZATION ---
            ConfigNoclipKey = Config.Bind("Keybinds", "NoclipKey", KeyCode.N, "Key for Noclip");
            ConfigTeleportKey = Config.Bind("Keybinds", "TeleportKey", KeyCode.B, "Key for Teleport");
            ConfigVehFlyKey = Config.Bind("Keybinds", "VehFlyKey", KeyCode.G, "Key for Vehicle Fly");
            ConfigMenuKey = Config.Bind("Keybinds", "MenuKey", KeyCode.Insert, "Menu opening key");
            ConfigAutoHealKey = Config.Bind("Keybinds", "AutoHealKey", KeyCode.J, "Key for Auto Healing");
            ConfigIncludeModdedWeapons = Config.Bind("Spawner", "IncludeModdedWeapons", false,
                "Include Workshop/modded weapons in the spawn menu. Disabled loads vanilla weapons only.");

            // Pass saved config values to UI variables in respective tabs
            SettingsTab.NoclipKey = ConfigNoclipKey.Value;
            SettingsTab.TeleportKey = ConfigTeleportKey.Value;
            SettingsTab.VehFlyKey = ConfigVehFlyKey.Value;
            SettingsTab.MenuKey = ConfigMenuKey.Value;
            SettingsTab.AutoHealKey = ConfigAutoHealKey.Value;
            Spawner.IncludeModdedWeapons = ConfigIncludeModdedWeapons.Value;

            try
            {
                // Register custom MonoBehaviour with IL2CPP runtime
                Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<DebugMenuController>();
                var harmony = new Harmony("pl.avene.easyred2.patch");

                harmony.PatchAll(Assembly.GetExecutingAssembly());

                // Hook Soldier.Update to reliably instantiate DebugMenuController in the game scene
                harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Soldier"), "Update"),
                    new HarmonyMethod(typeof(Patches), nameof(Patches.UpdatePostfix)));

                // Force unlock and show cursor when the menu is open
                harmony.Patch(AccessTools.PropertySetter(typeof(Cursor), "lockState"),
                    new HarmonyMethod(typeof(Patches), nameof(Patches.SetCursorLockPrefix)));

                harmony.Patch(AccessTools.PropertySetter(typeof(Cursor), "visible"),
                    new HarmonyMethod(typeof(Patches), nameof(Patches.SetCursorVisiblePrefix)));

                PatchNoRecoil(harmony);

                // --- ITEM WEIGHT PATCHES ---
                PatchWeightByName(harmony, "VirtualItem");
                PatchWeightByName(harmony, "VirtualItemStackable");
                PatchWeightByName(harmony, "VirtualMagazineItem");
            }
            catch (Exception e)
            {
                Log.LogError("Plugin loading error: " + e.Message);
            }
        }

        // Helper method to dynamically patch weight calculation on item classes
        private void PatchWeightByName(Harmony harmony, string className)
        {
            try
            {
                var type = AccessTools.TypeByName(className);
                if (type == null) return;

                var method = AccessTools.DeclaredMethod(type, "GetMass");
                if (method != null)
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(GenericWeightPatch), nameof(GenericWeightPatch.Prefix)));
                }
            }
            catch { }
        }

        // Hook both soldier recoil and FPS camera shake
        private void PatchNoRecoil(Harmony harmony)
        {
            try
            {
                var soldierRecoil = AccessTools.Method(typeof(Soldier), "GiveRecoil", new[] { typeof(float) });
                if (soldierRecoil != null)
                    harmony.Patch(soldierRecoil,
                        prefix: new HarmonyMethod(typeof(RecoilPatch), nameof(RecoilPatch.SoldierGiveRecoilPrefix)));

                var fpsRecoil = AccessTools.Method(typeof(FPSGunManager), "RecoilEffect",
                    new[] { typeof(Soldier), typeof(float), typeof(FPSAnimationCollection) });
                if (fpsRecoil != null)
                    harmony.Patch(fpsRecoil,
                        prefix: new HarmonyMethod(typeof(RecoilPatch), nameof(RecoilPatch.RecoilEffectPrefix)));
            }
            catch (Exception e)
            {
                Log.LogWarning("No Recoil patch error: " + e.Message);
            }
        }

        public static class Patches
        {
            // Spawn DebugMenuController upon the first active soldier update
            public static void UpdatePostfix(Component __instance)
            {
                if (Instance == null)
                {
                    GameObject obj = new GameObject("DebugMenuController");
                    UnityEngine.Object.DontDestroyOnLoad(obj);
                    Instance = obj.AddComponent<DebugMenuController>();
                }
                Instance.CachePlayer(__instance);
            }

            // Fix soldier animation glitch when leaving vehicles
            [HarmonyPatch(typeof(Vehicle), "GetOffVehicle")]
            public static class VehicleExitPatch
            {
                public static void Postfix(Soldier soldier)
                {
                    if (soldier != null)
                    {
                        soldier.ForceControllerEnabled();
                        soldier.SetAnimationsTPS();
                        soldier.RefreshAnimatorState();
                    }
                }
            }

            // Vehicle Godmode / Ignore Hull Damage
            [HarmonyPatch(typeof(Vehicle), "DamageHull")]
            public static class VehicleDamagePatch
            {
                public static bool Prefix(Vehicle __instance, short dam)
                {
                    if (VehicleTab.AutoRepair) return false; // Skip original damage method
                    return true;
                }
            }

            // Vehicle Godmode / Ignore General Damage
            [HarmonyPatch(typeof(Vehicle), "Damage")]
            public static class VehicleDamageFloatPatch
            {
                public static bool Prefix(Vehicle __instance, float dam)
                {
                    if (VehicleTab.AutoRepair) return false; // Skip original damage method
                    return true;
                }
            }

            // Eject soldier from vehicle upon death to prevent physics bugs
            [HarmonyPatch(typeof(Soldier), "Kill")]
            public static class KillPatch
            {
                public static void Prefix(Soldier __instance)
                {
                    if (__instance != null)
                    {
                        var veh = __instance.GetCurrentVehicle();
                        if (veh != null)
                        {
                            veh.GetOffVehicle(__instance);
                        }
                    }
                }
            }

            // Player speed multiplier (Third Person)
            [HarmonyPatch(typeof(Soldier), "Move")]
            public static class MovePatch
            {
                public static void Prefix(Soldier __instance, ref Vector3 move, bool sprint, float deltaTime)
                {
                    if (__instance != null && __instance.IsPlayer() && PlayerTab.MoveSpeedMultiplier != 1.0f)
                    {
                        move *= PlayerTab.MoveSpeedMultiplier;
                    }
                }
            }

            // Player speed multiplier (First Person)
            [HarmonyPatch(typeof(Soldier), "MoveFPS")]
            public static class MoveFPSPatch
            {
                public static void Prefix(Soldier __instance, ref Vector3 move, bool sprint)
                {
                    if (__instance != null && __instance.IsPlayer() && PlayerTab.MoveSpeedMultiplier != 1.0f)
                    {
                        move *= PlayerTab.MoveSpeedMultiplier;
                    }
                }
            }

            // Prevent helmets from being dropped or shot off
            [HarmonyPatch(typeof(Soldier), "DropHelmet")]
            public static class DropHelmetPatch
            {
                public static bool Prefix(Soldier __instance, ref GameObject __result)
                {
                    if (PlayerTab.IndestructibleHelmets && __instance != null)
                    {
                        if (__instance.IsPlayer() || (!string.IsNullOrEmpty(CombatTab.LocalFaction) && __instance.faction == CombatTab.LocalFaction))
                        {
                            __result = null;
                            return false; // Cancel helmet drop
                        }
                    }
                    return true;
                }
            }

            // Unlock and reveal the mouse cursor while menu is open
            public static bool SetCursorLockPrefix(ref CursorLockMode value)
            {
                if (Instance != null && Instance.showMenu) { value = CursorLockMode.None; return true; }
                return true;
            }

            public static bool SetCursorVisiblePrefix(ref bool value)
            {
                if (Instance != null && Instance.showMenu) { value = true; return true; }
                return true;
            }
        }

        // Override item weight to 0 kg
        public static class GenericWeightPatch
        {
            public static bool Prefix(ref float __result)
            {
                if (PlayerTab.UnlimitedWeight)
                {
                    __result = 0f;
                    return false;
                }
                return true;
            }
        }

        // Prevent movement speed penalties caused by encumbrance
        public static class SoldierSpeedPatch
        {
            public static bool Prefix(ref float __result)
            {
                if (PlayerTab.UnlimitedWeight) { __result = 1f; return false; }
                return true;
            }
        }

        public static class SoldierSlowedPatch
        {
            public static bool Prefix(ref bool __result)
            {
                if (PlayerTab.UnlimitedWeight) { __result = false; return false; }
                return true;
            }
        }

        // Allow sprinting regardless of carried weight
        public static class PCSprintPatch
        {
            public static bool Prefix(ref bool __result)
            {
                if (PlayerTab.UnlimitedWeight && Input.GetKey(KeyCode.LeftShift)) { __result = true; return false; }
                return true;
            }
        }

        // Cancel weapon recoil for the player
        public static class RecoilPatch
        {
            public static bool SoldierGiveRecoilPrefix(Soldier __instance)
            {
                if (CombatTab.NoRecoil && __instance != null && __instance.IsPlayer())
                    return false; // Cancel recoil
                return true;
            }

            public static bool RecoilEffectPrefix(Soldier __0)
            {
                if (CombatTab.NoRecoil && __0 != null && __0.IsPlayer())
                    return false; // Cancel camera kickback
                return true;
            }
        }
    }
}