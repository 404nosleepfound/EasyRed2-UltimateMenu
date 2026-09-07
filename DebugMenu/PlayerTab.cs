using UnityEngine;
using HarmonyLib;
using System;

namespace EasyRed2Mod
{
    /// Handles player-specific cheats and mechanics:
    /// God Mode, Super Jump, Noclip (Flight), Infinite Weight/Stamina, Quick Auto-Heal, and Squad Invincibility.
    public static class PlayerTab
    {
        // Feature toggles
        public static bool GodMode, SuperJump, Noclip, UnlimitedWeight;
        public static float NoclipSpeed = 50f;
        public static float MoveSpeedMultiplier = 1.0f;
        public static float JumpMultiplier = 1.0f;
        public static bool GodModeSquad = false;
        public static bool IndestructibleHelmets = false;

        // Cached Traverse instance to reduce reflection overhead on private fields
        private static Traverse _playerTraverse;
        private static Soldier _lastLocal;
        private static float _nextSquadScan = 0f;

        /// Main player loop called every frame while the player is alive.
        public static void Update(Soldier local)
        {
            if (local == null || local.life_total.Value <= 0) return;

            try
            {
                // Re-cache Traverse whenever the local player instance changes (e.g. after respawn)
                if (local != _lastLocal)
                {
                    _playerTraverse = Traverse.Create(local);
                    _lastLocal = local;
                }

                // --- GOD MODE ---
                // Refresh invincibility timer and lock health to maximum (100 HP)
                if (GodMode)
                {
                    local.SetInvincible(10f);
                    var hp = local.life_total;
                    hp.Value = 100;
                    local.life_total = hp;
                }

                // --- SUPER JUMP ---
                // Base jump power in Easy Red 2 is ~3.3f
                local.m_JumpPower = 3.3f * JumpMultiplier;

                // --- UNLIMITED WEIGHT & STAMINA ---
                if (UnlimitedWeight)
                {
                    if (_playerTraverse != null)
                    {
                        // Reset stamina and clear exhaustion slowdown timers
                        _playerTraverse.Field("staminaCount").SetValue(100f);
                        _playerTraverse.Field("out_of_stamina").SetValue(false);
                        _playerTraverse.Field("slowDownMovement_time").SetValue(0f);
                    }

                    // Prevent weapon aim while sprinting, boost movement speed when sprinting
                    if (local.IsAiming)
                    {
                        local.isSprinting = false;
                    }
                    else
                    {
                        if (local.isSprinting)
                        {
                            local.m_movement *= 1.5f;
                        }
                    }
                }

                // --- NOCLIP (FREE FLIGHT) ---
                var cc = local.m_controller;
                if (Noclip)
                {
                    // Disable Unity CharacterController to ignore collision and gravity
                    if (cc != null) cc.enabled = false;

                    Vector3 move = Vector3.zero;
                    Transform camT = Camera.main.transform;

                    // Directional movement relative to where the camera is looking
                    if (Input.GetKey(KeyCode.W)) move += camT.forward;
                    if (Input.GetKey(KeyCode.S)) move -= camT.forward;
                    if (Input.GetKey(KeyCode.D)) move += camT.right;
                    if (Input.GetKey(KeyCode.A)) move -= camT.right;
                    if (Input.GetKey(KeyCode.Space)) move += Vector3.up;
                    if (Input.GetKey(KeyCode.LeftControl)) move -= Vector3.up;

                    local.transform.position += move * NoclipSpeed * Time.deltaTime;
                }
                else if (cc != null && !cc.enabled)
                {
                    // Slightly offset vertical position on toggle-off to prevent falling through terrain/floors
                    if (local.GetCurrentVehicle() == null)
                    {
                        local.transform.position += Vector3.up * 1.0f;
                    }

                    // Re-enable character collision
                    cc.enabled = true;
                }
            }
            catch { }
        }

        /// Instantly searches inventory for medical supplies/food and consumes them via reflection.
        public static void UpdateAutoHeal(Soldier local)
        {
            if (local == null || local.life_total.Value <= 0) return;

            if (Input.GetKeyDown(SettingsTab.AutoHealKey))
            {
                if (local.inventory == null || local.inventory.inventory == null) return;

                var itemsList = local.inventory.inventory.items;

                for (int i = 0; i < itemsList.Count; i++)
                {
                    var vItem = itemsList[i];
                    if (vItem == null) continue;

                    string id = vItem.item_id.ToLower();

                    // Check for consumable health restoration items
                    if (id.Contains("aspirine") || id.Contains("tuna_can"))
                    {
                        var interactions = vItem.GetInventoryInteractions(local.inventory, local.inventory);

                        // Index 1 corresponds to the "Use / Consume" interaction option in the item context menu
                        if (interactions != null && interactions.Count > 1)
                        {
                            var consumeAction = interactions[1];
                            if (consumeAction != null)
                            {
                                // Invoke the consumption method via Traverse reflection
                                HarmonyLib.Traverse.Create(consumeAction).Method("Call").GetValue();

                                // Sync inventory state and force an internal health UI update
                                local.inventory.OnNetInventoryEdited();
                                local.Damage(0f);

                                break; // Only consume one item per keypress
                            }
                        }
                    }
                }
            }
        }

        /// Finds all friendly soldiers assigned to the player's squad and grants them continuous invincibility.
        /// Throttled to once per second to preserve CPU performance.
        public static void UpdateSquadGodMode(Soldier local)
        {
            if (!GodModeSquad || Time.time < _nextSquadScan) return;
            _nextSquadScan = Time.time + 1.0f; // Scan interval

            if (!GodModeSquad || local == null) return;

            try
            {
                var allSoldiers = UnityEngine.Object.FindObjectsOfType<Soldier>();

                foreach (var member in allSoldiers)
                {
                    // Check if soldier is in the same squad and not the local player
                    if (member != null && member != local && member.joinedSquad == local.joinedSquad)
                    {
                        member.SetInvincible(10f);

                        // Force soldier HP to 100
                        var hp = member.life_total;
                        var valProp = hp.GetType().GetProperty("Value");
                        if (valProp != null) valProp.SetValue(hp, (short)100, null);
                        member.life_total = hp;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[SQUAD-GODMODE] Error: {e.Message}");
            }
        }
    }
}