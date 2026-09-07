using UnityEngine;
using System.Collections.Generic;
using HarmonyLib;
using System.Reflection;
using System;

namespace EasyRed2Mod
{
    // Handles weapon modifications, shooting mechanics, and combat cheats
    public static class CombatTab
    {
        // Feature toggles
        public static bool NoRecoil, InfiniteAmmo, RapidFire, ExplosiveBullets, RealisticAmmo;
        public static float DamageMultiplier = 1.0f;
        private static float nextExplosionTime = 0f;
        private static float nextRefillTime = 0f;
        public static string LocalFaction = "";

        // Dictionaries to store original weapon data so changes can be reverted cleanly
        private static Dictionary<int, float> originalDamage = new Dictionary<int, float>();
        private static Dictionary<int, RecoilStats> originalRecoilStats = new Dictionary<int, RecoilStats>();
        private static bool noRecoilWasEnabled = false;

        private static Dictionary<int, WeaponStats> originalWeaponStats = new Dictionary<int, WeaponStats>();
        private struct WeaponStats { public bool auto; public int rpm; public bool bolt; public bool manual; public bool boltActionBefore; }
        private struct RecoilStats
        {
            public GenericGun gun;
            public float recoilIntensity;
            public float bulletDispersion;
        }

        // Main combat loop called every frame
        public static void Update(Soldier local)
        {
            // Restore factory recoil values if the toggle was turned off
            if (!NoRecoil && noRecoilWasEnabled)
                RestoreRecoil();
            noRecoilWasEnabled = NoRecoil;

            if (local == null || local.life_total.Value <= 0) return;

            LocalFaction = local.faction;

            // Retrieve currently held primary (slot 0) or secondary (slot 1) weapon
            GenericGun g = local.GetHeldGun(0);
            if (g == null || !g.isActiveAndEnabled) g = local.GetHeldGun(1);
            if (g == null) return;

            int gunID = g.GetInstanceID();
            var gTrav = Traverse.Create(g);

            // Prevent weapons from firing or playing looped audio while the game is paused
            if (Time.timeScale == 0)
            {
                if (RapidFire)
                {
                    g.StopUse(local);
                    gTrav.Field("playingFireLoopedSound").SetValue(false);
                }
                return;
            }

            try
            {
                // --- INFINITE AMMO ---
                // Automatically refills current magazine when ammo drops below 2 rounds
                if (InfiniteAmmo || RapidFire)
                {
                    if (g.GetCurrentAmmoCount() < 2)
                    {
                        int cap = gTrav.Field("ammoSpace").GetValue<int>();
                        if (cap <= 0) cap = 30; // Fallback capacity if ammoSpace field is missing/0
                        g.Refill(cap);
                        g.SetAmmoCount(cap);
                    }
                }

                // --- RAPID FIRE ---
                // Converts any weapon (including bolt-actions) into a full-auto 1500 RPM machine gun
                if (RapidFire)
                {
                    // Backup original weapon parameters before modifying
                    if (!originalWeaponStats.ContainsKey(gunID))
                    {
                        originalWeaponStats[gunID] = new WeaponStats
                        {
                            auto = gTrav.Field("automaticFire").GetValue<bool>(),
                            rpm = g.roundPerMinute,
                            bolt = gTrav.Field("bolt_required").GetValue<bool>(),
                            manual = g.isManualBoltOperated,
                            boltActionBefore = g.boltActionBeforeFiring
                        };
                    }

                    // Force full-auto settings and bypass bolt operation delays
                    g.roundPerMinute = 1500;
                    g.isManualBoltOperated = false;
                    g.boltActionBeforeFiring = false;
                    ResetWeaponTimers(g);

                    // Continuous fire while holding down the left mouse button
                    if (Input.GetMouseButton(0))
                    {
                        if (!g.IsReloadingOrUsingBolt()) g.Use(local);
                    }
                    else if (Input.GetMouseButtonUp(0))
                    {
                        g.StopUse(local);
                        gTrav.Field("playingFireLoopedSound").SetValue(false);
                    }
                }
                else if (originalWeaponStats.ContainsKey(gunID))
                {
                    // Restore original weapon statistics when Rapid Fire is toggled off
                    var s = originalWeaponStats[gunID];

                    gTrav.Field("automaticFire").SetValue(s.auto);
                    g.roundPerMinute = s.rpm;
                    gTrav.Field("bolt_required").SetValue(s.bolt);
                    g.isManualBoltOperated = s.manual;
                    g.boltActionBeforeFiring = s.boltActionBefore;

                    g.StopUse(local);
                    gTrav.Field("playingFireLoopedSound").SetValue(false);

                    originalWeaponStats.Remove(gunID);
                }

                // --- DAMAGE MULTIPLIER ---
                // Casts a ray towards the crosshair while firing to apply extra direct damage
                if (DamageMultiplier > 1.0f && g.IsFiring)
                {
                    Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
                    if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
                    {
                        var target = hit.collider.GetComponentInParent<Soldier>();
                        if (target != null && target != local)
                        {
                            target.Damage(100f * (DamageMultiplier - 1.0f));
                        }
                    }
                }

                // --- REALISTIC AMMO ---
                // Periodically refills reserve magazines in the inventory without giving infinite clip ammo
                if (RealisticAmmo && Time.time > nextRefillTime)
                {
                    nextRefillTime = Time.time + 1.2f;
                    RefillInventoryStrict(local);
                }

                // --- NO RECOIL ---
                // Removes physical recoil kick and weapon spread
                if (NoRecoil)
                {
                    if (!originalRecoilStats.ContainsKey(gunID))
                    {
                        originalRecoilStats[gunID] = new RecoilStats
                        {
                            gun = g,
                            recoilIntensity = g.recoilIntensity,
                            bulletDispersion = g.bulletDispersion
                        };
                    }

                    g.recoilIntensity = 0f;
                    g.bulletDispersion = 0f;
                }

                // --- EXPLOSIVE BULLETS ---
                // Triggers an explosive radius at the raycast hit point when firing
                if (ExplosiveBullets && (g.IsFiring || Input.GetMouseButton(0)))
                {
                    ProcessExplosiveBullets(local);
                }
            }
            catch { }
        }

        // Restores recoil intensity and bullet spread values back to original state
        private static void RestoreRecoil()
        {
            foreach (var stats in originalRecoilStats.Values)
            {
                try
                {
                    if (stats.gun == null) continue;
                    stats.gun.recoilIntensity = stats.recoilIntensity;
                    stats.gun.bulletDispersion = stats.bulletDispersion;
                }
                catch { }
            }
            originalRecoilStats.Clear();
        }

        // Resets internal weapon cooldown timers to allow instant next-shot execution
        private static void ResetWeaponTimers(GenericGun g)
        {
            var fields = g.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var f in fields)
            {
                if (f.FieldType == typeof(float) && (f.Name.Contains("next") || f.Name.Contains("time") || f.Name.Contains("Shot")))
                    f.SetValue(g, 0f);
                if (f.FieldType == typeof(bool) && f.Name.Contains("automatic"))
                    f.SetValue(g, true);
            }
        }

        // Iterates through inventory items and restores maximum ammo counts in magazines and ammo boxes
        private static void RefillInventoryStrict(Soldier local)
        {
            if (local.inventory == null || local.inventory.inventory == null) return;
            var items = local.inventory.inventory.items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null) continue;

                // Maximize rounds inside magazines
                var vMag = item.TryCast<VirtualMagazineItem>();
                if (vMag != null)
                {
                    int maxCap = vMag.GetMaxAmmoStackCount();
                    if (maxCap > 0) vMag.SetAmmoCount(maxCap);
                }

                // Maximize loose ammo boxes
                var vAmmo = item.TryCast<VirtualAmmo>();
                if (vAmmo != null) vAmmo.SetAmmoCount(500);
            }
        }

        // Handles explosive impact logic: casts a ray and damages all soldiers/vehicles within a 5m radius
        private static void ProcessExplosiveBullets(Soldier local)
        {
            if (Time.time < nextExplosionTime) return; // Rate limiter for performance and balance
            nextExplosionTime = Time.time + 0.15f;

            Camera cam = Camera.main;
            if (cam == null) return;

            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 2000f))
            {
                // Detect all colliders within 5 meters of impact point
                Collider[] objectsInRange = Physics.OverlapSphere(hit.point, 5f);
                foreach (var col in objectsInRange)
                {
                    if (col == null) continue;

                    // Kill enemy soldiers caught in the explosion
                    Soldier s = col.GetComponentInParent<Soldier>();
                    if (s != null && !s.IsPlayer() && s.life_total.Value > 0) s.Damage(9999f);

                    // Deal heavy damage to vehicles caught in the explosion
                    Vehicle v = col.GetComponentInParent<Vehicle>();
                    if (v != null) v.DamageHull((short)500);
                }
            }
        }

        // Teleports player soldier to whichever surface they are aiming at with a small height offset
        public static void TeleportToCrosshair(Soldier local)
        {
            if (local == null || Camera.main == null) return;
            Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, 2000f))
                local.transform.position = hit.point + Vector3.up * 1.5f;
        }
    }
}