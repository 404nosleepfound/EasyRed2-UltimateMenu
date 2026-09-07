using UnityEngine;
using BepInEx.Configuration;
using System;
using HarmonyLib;

namespace EasyRed2Mod
{
    // Handles hotkey remapping, configuration saving, global game time scale (slow motion), and visual color customization
    public static class SettingsTab
    {
        // Default hotkeys
        public static KeyCode NoclipKey = KeyCode.N;
        public static KeyCode TeleportKey = KeyCode.B;
        public static KeyCode VehFlyKey = KeyCode.G;
        public static KeyCode MenuKey = KeyCode.Insert;
        public static KeyCode AutoHealKey = KeyCode.J;

        // State flags indicating whether the mod is currently waiting for a keypress to rebind
        public static bool IsBindingNoclip, IsBindingTeleport, IsBindingVehFly, IsBindingMenu, IsBindingAutoHeal;

        // Game speed multiplier (1.0 = normal speed, 0.3 = slow motion)
        public static float TimeScale = 1.0f;
        public static int colorIdx = 0;
        private static bool slowMotionWasApplied = false;

        // Main settings and keybind listener loop called every frame
        public static void Update(Soldier local)
        {
            // Check if any keybinding mode is active and capture input
            if (IsBindingNoclip) ListenForKey(ref NoclipKey, ref IsBindingNoclip, DebugMenuPlugin.ConfigNoclipKey);
            else if (IsBindingTeleport) ListenForKey(ref TeleportKey, ref IsBindingTeleport, DebugMenuPlugin.ConfigTeleportKey);
            else if (IsBindingVehFly) ListenForKey(ref VehFlyKey, ref IsBindingVehFly, DebugMenuPlugin.ConfigVehFlyKey);
            else if (IsBindingMenu) ListenForKey(ref MenuKey, ref IsBindingMenu, DebugMenuPlugin.ConfigMenuKey);
            else if (IsBindingAutoHeal) ListenForKey(ref AutoHealKey, ref IsBindingAutoHeal, DebugMenuPlugin.ConfigAutoHealKey);

            // Execute shortcuts only when NOT currently remapping keys
            if (!IsBindingNoclip && !IsBindingTeleport && !IsBindingVehFly && !IsBindingMenu)
            {
                if (Input.GetKeyDown(TeleportKey)) CombatTab.TeleportToCrosshair(local);
                if (Input.GetKeyDown(NoclipKey)) PlayerTab.Noclip = !PlayerTab.Noclip;
                if (Input.GetKeyDown(VehFlyKey)) VehicleTab.FlyMode = !VehicleTab.FlyMode;
            }

            // Time scale manipulation (do not override if the game is natively paused at Time.timeScale = 0)
            if (Time.timeScale == 0f) return;

            // Apply slow motion
            if (TimeScale < 1.0f)
            {
                if (Time.timeScale != TimeScale) Time.timeScale = TimeScale;
                slowMotionWasApplied = true;
            }
            // Restore normal game speed once slow motion is turned off
            else if (slowMotionWasApplied)
            {
                Time.timeScale = 1.0f;
                slowMotionWasApplied = false;
            }
        }

        // Listens for the next keyboard/mouse press, updates the keybind, and writes changes directly to the BepInEx config file
        private static void ListenForKey(ref KeyCode target, ref bool flag, ConfigEntry<KeyCode> config)
        {
            if (!Input.anyKey) return;

            foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
            {
                if (Input.GetKeyDown(k))
                {
                    // Pressing Escape cancels the rebind operation without changing the key
                    if (k == KeyCode.Escape)
                    {
                        flag = false;
                        return;
                    }

                    // Assign new key and save to .cfg
                    target = k;
                    if (config != null)
                    {
                        config.Value = k;
                        if (DebugMenuPlugin.PluginInstance != null)
                            DebugMenuPlugin.PluginInstance.Config.Save();
                    }

                    flag = false;
                    break;
                }
            }
        }

        // Helper to get a clean string representation of the KeyCode for UI labels
        public static string GetKeyName(KeyCode key)
        {
            return key.ToString();
        }

        // Cycles through preset colors for ESP bounding boxes, snaplines, and chams
        public static void CycleColor()
        {
            colorIdx++;
            if (colorIdx > 3) colorIdx = 0;
            switch (colorIdx)
            {
                case 0: VisualsTab.VisibleColor = new Color(0.8f, 0.1f, 0.1f, 0.8f); break; // Red
                case 1: VisualsTab.VisibleColor = new Color(0.9f, 0.8f, 0.1f, 0.8f); break; // Yellow
                case 2: VisualsTab.VisibleColor = new Color(0.1f, 0.5f, 0.9f, 0.8f); break; // Blue
                case 3: VisualsTab.VisibleColor = new Color(1f, 1f, 1f, 0.8f); break;       // White
            }
        }
    }
}