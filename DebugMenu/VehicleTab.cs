using UnityEngine;
using System.Collections.Generic;
using HarmonyLib;
using System.Reflection;
using System;

namespace EasyRed2Mod
{
    // Handles vehicle modifications: flight physics, instant repairs, infinite ammo, and enemy vehicle destruction
    public static class VehicleTab
    {
        // Feature toggles
        public static bool FlyMode, UnlockAll;
        public static bool InfAmmoTanks;
        public static bool InfAmmoPlanes;
        public static bool AutoRepair;
        private static float _nextAutoRepair = 0f;
        private static bool isRepairing = false;

        // Main vehicle loop called every frame
        public static void Update(Soldier local)
        {
            if (local == null || Time.timeScale == 0) return;

            var veh = local.GetCurrentVehicle();
            if (veh == null) return;

            // Auto-repair vehicle every 0.5s if enabled
            if (AutoRepair && Time.time > _nextAutoRepair)
            {
                _nextAutoRepair = Time.time + 0.5f;
                FullRepair(veh);
            }

            try
            {
                // Apply specific cheats depending on vehicle type (Plane vs Tank/Ground)
                bool isPlane = veh.TryCast<VehiclePlane>() != null;
                if (isPlane && InfAmmoPlanes) ApplyPlaneCheatsAggressive(veh);
                else if (!isPlane && InfAmmoTanks) ApplyTankCheatsSmart(veh);

                HandleVehicleFly(veh);
            }
            catch { }
        }

        // Unlimited ammunition and no-overheat for aircraft
        private static void ApplyPlaneCheatsAggressive(Vehicle v)
        {
            try
            {
                // Refill internal bomb bays
                var plane = v.TryCast<VehiclePlane>();
                if (plane != null) plane.RefillBombBay();

                FillVehicleInventory(v);

                // Refill mounted machine guns / cannons and remove fire delay & overheat
                var guns = v.GetComponentsInChildren<GenericGun>(true);
                foreach (var g in guns)
                {
                    if (g == null) continue;
                    SetProtectedIntBySearching(g, 999);
                    g.SetAmmoCount(999);
                    var gTrav = Traverse.Create(g);
                    gTrav.Field("nextAvailableShot").SetValue(0f);
                    gTrav.Field("overheat_time").SetValue(0f);
                }

                // Refill secondary defensive gunner turrets
                var turrets = v.GetComponentsInChildren<TurretGun>(true);
                foreach (var t in turrets)
                {
                    if (t == null) continue;
                    foreach (var w in t.weapons)
                    {
                        if (w == null) continue;
                        SetProtectedIntBySearching(w, 100);
                        w.loadedAmmoCount = 100;
                    }
                }
            }
            catch { }
        }

        // Refill tank shells and coaxial machine guns
        private static void ApplyTankCheatsSmart(Vehicle v)
        {
            try
            {
                FillVehicleInventory(v);

                var turrets = v.GetComponentsInChildren<TurretGun>(true);
                foreach (var turret in turrets)
                {
                    if (turret == null) continue;
                    var activeWeapon = turret.selectedWeapon;

                    foreach (var weapon in turret.weapons)
                    {
                        if (weapon == null) continue;

                        // Machine guns (RPM > 100): High ammo reserve and zero overheat
                        if (weapon.roundPerMinute > 100)
                        {
                            SetProtectedIntBySearching(weapon, 500);
                            weapon.loadedAmmoCount = 500;
                            Traverse.Create(weapon).Field("overheat_time").SetValue(0f);
                        }
                        // Main cannon: Instant chamber reload when empty
                        else if (weapon == activeWeapon)
                        {
                            if (weapon.loadedAmmoCount == 0)
                            {
                                SetProtectedIntBySearching(weapon, 1);
                                weapon.loadedAmmoCount = 1;
                                var wTrav = Traverse.Create(weapon);
                                wTrav.Field("nextReloadEnd").SetValue(0f);
                            }
                            // Delay native ammo check to prevent game reload lockouts
                            Traverse.Create(turret).Field("next_ammo_check_time").SetValue(Time.time + 10f);
                        }
                    }
                }
            }
            catch { }
        }

        // Restocks cargo/inventory carried inside the vehicle
        private static void FillVehicleInventory(Vehicle v)
        {
            var invManager = v.GetInventory();
            if (invManager != null && invManager.inventory != null && invManager.inventory.items != null)
            {
                foreach (var item in invManager.inventory.items)
                {
                    if (item == null) continue;
                    SetProtectedIntBySearching(item, 50);
                    var trav = Traverse.Create(item);
                    if (trav.Field("stackCount").FieldExists())
                        trav.Field("stackCount").SetValue(50);
                }
            }
        }

        // Helper to bypass game's anti-cheat ProtectedInt wrapper via reflection
        private static void SetProtectedIntBySearching(object target, int val)
        {
            if (target == null) return;
            try
            {
                var fields = target.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var f in fields)
                {
                    if (f.FieldType.Name.Contains("ProtectedInt"))
                    {
                        object pInt = f.GetValue(target);
                        if (pInt == null) continue;
                        var prop = pInt.GetType().GetProperty("Value");
                        if (prop != null)
                        {
                            prop.SetValue(pInt, val);
                            f.SetValue(target, pInt);
                        }
                    }
                }
            }
            catch { }
        }

        // Fully repairs vehicle hull, tracks, engines, and cleans up visual bullet holes
        public static void FullRepair(Vehicle v)
        {
            if (v == null || isRepairing) return;
            isRepairing = true;

            try
            {
                var vTrav = Traverse.Create(v);

                // Repair internal subsystem modules (engine, transmission, tracks)
                var movable = v.TryCast<MovableVehicle>();
                if (movable != null)
                {
                    var parts = vTrav.Field("internalParts").GetValue<Il2CppSystem.Collections.Generic.List<VehicleInternalPart>>();

                    if (parts != null)
                    {
                        for (int i = 0; i < parts.Count; i++)
                        {
                            movable.RepairPart(parts[i], 100);
                        }
                    }

                    movable.RepairPart((VehicleInternalPart)1, 500);
                    movable.RepairPart((VehicleInternalPart)2, 500);
                }

                // Native repair calls
                v.Repair();
                v.OnRepaired();

                // Clear GUI damage indicators (red engine/hull warning icons)
                var gui = v.GetComponentInChildren<VehicleGUI>(true);
                if (gui != null)
                {
                    if (gui.hullDamage != null) gui.hullDamage.enabled = false;
                    if (gui.engineDamage != null) gui.engineDamage.enabled = false;
                    if (gui.fueltankDamage != null) gui.fueltankDamage.enabled = false;
                }

                // Hide visual damage decals and bullet impact meshes
                foreach (var t in v.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.ToLower();
                    if (n.Contains("hole") || n.Contains("impact")) t.gameObject.SetActive(false);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ER2-REPAIR] Error: {ex.Message}");
            }
            finally
            {
                isRepairing = false;
            }
        }

        // Finds and instantly detonates all vehicles belonging to the opposing faction
        public static void DestroyEnemyVehicles(Soldier local)
        {
            if (local == null) return;
            string myF = local.faction.Length >= 3 ? local.faction.Substring(0, 3) : local.faction;
            foreach (var v in UnityEngine.Object.FindObjectsOfType<Vehicle>())
            {
                if (v == null) continue;
                string vF = v.GetVehicleFaction();
                if (!string.IsNullOrEmpty(vF) && !vF.Contains(myF))
                {
                    v.DamageHull((short)9999);
                    v.Damage(9999f);
                }
            }
        }

        // Overrides physics to fly vehicles in any camera direction using WASD
        private static void HandleVehicleFly(Vehicle veh)
        {
            var rb = veh.GetComponent<Rigidbody>();
            if (rb == null) return;

            if (FlyMode)
            {
                rb.useGravity = false;

                // Shift for boost speed
                float speed = Input.GetKey(KeyCode.LeftShift) ? 150f : 60f;
                Vector3 move = Vector3.zero;
                Transform camT = Camera.main.transform;

                if (Input.GetKey(KeyCode.W)) move += camT.forward;
                if (Input.GetKey(KeyCode.S)) move -= camT.forward;
                if (Input.GetKey(KeyCode.A)) move -= camT.right;
                if (Input.GetKey(KeyCode.D)) move += camT.right;

                rb.velocity = move * speed;
                if (move == Vector3.zero) rb.velocity = Vector3.zero;
            }
            else
            {
                // Re-enable gravity and prevent aircraft stalls when turning fly mode off
                if (!rb.useGravity)
                {
                    rb.useGravity = true;

                    var plane = veh.TryCast<VehiclePlane>();
                    if (plane != null)
                    {
                        Traverse.Create(plane).Field("isGrounded").SetValue(false);

                        // Give initial forward boost to prevent nose diving
                        if (rb.velocity.magnitude < 10f)
                        {
                            rb.velocity = veh.transform.forward * 50f;
                        }
                    }
                }
            }
        }
    }
}