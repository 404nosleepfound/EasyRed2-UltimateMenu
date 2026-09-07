using BepInEx.Unity.IL2CPP.Utils;
using HarmonyLib;
using Photon.Pun;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace EasyRed2Mod
{
    // Handles discovery, filtering, categorization, and runtime spawning of weapons, vehicles, items, and AI crews
    public static class Spawner
    {
        private static bool _isInitialized = false;

        // Cached item name lists for menu display and pagination
        public static List<string> _weaponNames = new List<string>();
        public static List<string> _vehicleNames = new List<string>();
        public static List<string> _itemNames = new List<string>();

        // Filtered subcategory lists
        public static List<string> AmmoNames = new List<string>();
        public static List<string> UniformNames = new List<string>();
        public static List<string> OtherNames = new List<string>();
        public static List<string> GrenadeNames = new List<string>();
        public static List<string> AttachmentNames = new List<string>();

        // Weapons that have attachment slots (scopes, bipods, bayonets)
        public static HashSet<string> UpgradableWeapons = new HashSet<string>();

        // Prefab lookup dictionaries mapped by Item ID or GameObject name
        public static Dictionary<string, GameObject> _weaponPrefabs = new Dictionary<string, GameObject>();
        public static Dictionary<string, GameObject> _vehiclePrefabs = new Dictionary<string, GameObject>();
        public static Dictionary<string, GameObject> _itemPrefabs = new Dictionary<string, GameObject>();

        public static bool WeaponsLoaded = false;
        public static bool VehiclesLoaded = false;
        public static bool ItemsLoaded = false;

        // Spawner settings toggles
        public static bool MaximizeAttachments = false;
        public static bool SpawnWithCrew = false;
        public static bool IncludeModdedWeapons = false;

        // Reflection caches for querying modded item IDs
        private static readonly Dictionary<Type, ModIdAccessor> ModIdAccessors = new Dictionary<Type, ModIdAccessor>();
        private static readonly string[] ModDataMemberNames = { "prop_data", "pd", "data" };

        private sealed class ModIdAccessor
        {
            public PropertyInfo DirectProperty;
            public FieldInfo DirectField;
            public PropertyInfo DataProperty;
            public FieldInfo DataField;
        }

        // Clears all cached lists and prefab dictionaries
        public static void Reset()
        {
            _isInitialized = false;
            _weaponNames.Clear(); _vehicleNames.Clear(); _itemNames.Clear();
            AmmoNames.Clear(); UniformNames.Clear(); OtherNames.Clear(); GrenadeNames.Clear();
            AttachmentNames.Clear(); UpgradableWeapons.Clear();
            _weaponPrefabs.Clear(); _vehiclePrefabs.Clear(); _itemPrefabs.Clear();
        }

        public static bool IsInitialized() => _isInitialized;

        // Queries the game's internal database (ItemsDatabase), caches prefabs, and sorts them into subcategories
        public static void Initialize()
        {
            if (_isInitialized) return;
            int skippedModdedWeapons = 0;
            try
            {
                Reset();

                // --- 1. POPULATE WEAPONS ---
                try
                {
                    var allWeapons = ItemsDatabase.GetAllItemsOfType<GenericGun>(PropData.PropType.weapons);
                    if (allWeapons != null)
                    {
                        foreach (var w in allWeapons)
                        {
                            if (w == null) continue;
                            var io = w.GetComponent<ItemObject>();

                            // Filter out Steam Workshop / user-modded weapons if disabled in settings
                            if (!IncludeModdedWeapons && IsModdedWeapon(w, io))
                            {
                                skippedModdedWeapons++;
                                continue;
                            }

                            string wName = (io != null && !string.IsNullOrEmpty(io.item_id)) ? io.item_id : w.name;
                            if (string.IsNullOrEmpty(wName) || wName.Contains("(Clone)")) continue;

                            if (!_weaponPrefabs.ContainsKey(wName))
                            {
                                _weaponPrefabs.Add(wName, w.gameObject);
                                _weaponNames.Add(wName);

                                // Track weapons capable of accepting scopes/bipods/bayonets
                                if (w.supportedAttachments != null && w.supportedAttachments.Length > 0)
                                {
                                    UpgradableWeapons.Add(wName);
                                }
                            }
                        }
                    }
                }
                catch (Exception e) { Debug.LogError("Init Weapons Error: " + e.Message); }

                // --- 2. POPULATE VEHICLES ---
                var allVehicles = ItemsDatabase.GetAllItemsOfType<Vehicle>(PropData.PropType.vehicles);
                if (allVehicles != null)
                {
                    foreach (var v in allVehicles)
                    {
                        if (v == null || string.IsNullOrEmpty(v.name)) continue;
                        if (!_vehiclePrefabs.ContainsKey(v.name))
                        {
                            _vehiclePrefabs.Add(v.name, v.gameObject);
                            _vehicleNames.Add(v.name);
                        }
                    }
                }

                // --- 3. POPULATE GENERAL ITEMS & CATEGORIZE BY ID KEYWORDS ---
                var allItems = ItemsDatabase.GetAllItemsOfType<ItemObject>(PropData.PropType.items);
                if (allItems != null)
                {
                    foreach (var item in allItems)
                    {
                        if (item == null || string.IsNullOrEmpty(item.item_id)) continue;

                        string id = item.item_id;
                        string low = id.ToLower();

                        if (!_itemPrefabs.ContainsKey(id)) _itemPrefabs.Add(id, item.gameObject);
                        if (!_itemNames.Contains(id)) _itemNames.Add(id);

                        // Explosives and thrown weapons
                        if (low.Contains("grenade") || low.Contains("molotov") || low.Contains("dynamite") ||
                            low.Contains("tnt") || low.Contains("mine") || low.Contains("hhl") || low.Contains("satchel"))
                        {
                            if (!GrenadeNames.Contains(id)) GrenadeNames.Add(id);
                        }
                        // Ammunition, magazines, and rocket charges
                        else if (low.Contains("ammo") || low.Contains("rocket") || low.Contains("shell") ||
                                 low.Contains("mag") || low.Contains("magazine") || low.Contains("clip") ||
                                 low.Contains("bullet") || low.Contains("round") || low.Contains("7.62") ||
                                 low.Contains("9mm") || low.Contains("8mm") || low.Contains("45acp") ||
                                 low.Contains("30cal") || low.Contains("50cal"))
                        {
                            if (!AmmoNames.Contains(id)) AmmoNames.Add(id);
                        }
                        // Uniforms, helmets, vests, and wearable gear
                        else if (low.Contains("uniform") || low.Contains("helmet") || low.Contains("vest") ||
                                 low.Contains("cap") || low.Contains("beret") || low.Contains("coat") ||
                                 low.Contains("parka") || low.Contains("zeltbahn") || low.Contains("turban") ||
                                 low.Contains("beanie") || low.Contains("radio") || low.Contains("flamethrower") ||
                                 low.Contains("gear") || low.Contains("crusher") || low.Contains("ushanka") ||
                                 low.Contains("panzer") || low.Contains("smock") || low.Contains("hat") ||
                                 low.Contains("gaiters") || low.Contains("trousers") || low.Contains("jacket") ||
                                 low.Contains("bloused") || low.Contains("long") || low.Contains("camo") || low.Contains("hp") ||
                                 low.Contains("inf") || low.Contains("luffwaffe") || low.Contains("tank") ||
                                 low.Contains("green") || low.Contains("blue") || low.Contains("1") ||
                                 low.Contains("2") || low.Contains("3") || low.Contains("plumblossom") ||
                                 low.Contains("hun_bocskai_artillery") || low.Contains("hun_bocskai_artillery_sl") || low.Contains("hun_bocskai_artillery_visor") ||
                                 low.Contains("hun_bocskai_artillery_visor_sl") || low.Contains("hun_bocskai_para") || low.Contains("hun_bocskai_para_sl") ||
                                 low.Contains("hun_bocskai_para_visor") || low.Contains("hun_bocskai_para_visor_sl") || low.Contains("hun_jerkin") ||
                                 low.Contains("hun_jerkin_para") || low.Contains("rom_tunic"))
                        {
                            if (!UniformNames.Contains(id)) UniformNames.Add(id);
                        }
                        // Miscellaneous items (food, medical supplies, tools)
                        else
                        {
                            if (!OtherNames.Contains(id)) OtherNames.Add(id);
                        }

                        // Attachments found as standalone ItemObjects
                        var att = item.GetComponent<Attachment>();
                        if (att != null)
                        {
                            id = !string.IsNullOrEmpty(item.item_id) ? item.item_id : item.name;
                            if (!AttachmentNames.Contains(id))
                            {
                                AttachmentNames.Add(id);
                                if (!_itemPrefabs.ContainsKey(id)) _itemPrefabs.Add(id, item.gameObject);
                            }
                        }
                    }
                }

                // --- 4. EXTRACT ATTACHMENTS DIRECTLY FROM WEAPONS ---
                try
                {
                    foreach (var gunPrefab in _weaponPrefabs.Values)
                    {
                        var gun = gunPrefab.GetComponent<GenericGun>();
                        if (gun == null || gun.supportedAttachments == null) continue;

                        foreach (var supported in gun.supportedAttachments)
                        {
                            string aid = supported.attachment_id;
                            if (!string.IsNullOrEmpty(aid) && !AttachmentNames.Contains(aid))
                                AttachmentNames.Add(aid);
                        }
                    }
                }
                catch (Exception e) { Debug.LogError("Init Attachments Error: " + e.Message); }

                // --- 5. POPULATE EXTRA AMMUNITION DEFINITIONS ---
                var extraAmmo = ItemsDatabase.GetAllItemsOfType<ItemObject>(PropData.PropType.ammo);
                if (extraAmmo != null)
                {
                    foreach (var a in extraAmmo)
                    {
                        if (a == null || string.IsNullOrEmpty(a.item_id)) continue;
                        if (!AmmoNames.Contains(a.item_id)) AmmoNames.Add(a.item_id);
                        if (!_itemPrefabs.ContainsKey(a.item_id)) _itemPrefabs.Add(a.item_id, a.gameObject);
                    }
                }

                // Sort all lists alphabetically for clean UI rendering
                _weaponNames.Sort(); _vehicleNames.Sort(); _itemNames.Sort();
                UniformNames.Sort(); GrenadeNames.Sort(); AmmoNames.Sort(); OtherNames.Sort(); AttachmentNames.Sort();
                _isInitialized = true;

                string mode = IncludeModdedWeapons ? "ALL MODS" : "VANILLA ONLY";
                Debug.Log($"[ER2-SPAWNER] Load complete ({mode}). Weapons: {_weaponNames.Count}, skipped modded: {skippedModdedWeapons}, Vehs: {_vehicleNames.Count}, Items: {_itemNames.Count}");
            }
            catch (Exception e) { Debug.LogError("[ER2-SPAWNER] Init Error: " + e.Message); }
        }

        // Helper to register prefabs while preventing duplicates and cloned instances
        private static void AddPrefab(Dictionary<string, GameObject> dict, List<string> list, string name, GameObject obj)
        {
            if (obj == null || string.IsNullOrEmpty(name) || name.Contains("(Clone)")) return;
            if (!dict.ContainsKey(name)) { dict.Add(name, obj); list.Add(name); }
        }

        public static List<string> GetWeaponNames() => _weaponNames;
        public static List<string> GetVehicleNames() => _vehicleNames;
        public static List<string> GetItemNames() => _itemNames;

        // Checks whether a weapon originated from Steam Workshop mod data
        private static bool IsModdedWeapon(GenericGun weapon, ItemObject item)
        {
            if (TryGetModId(weapon, out ulong modId) || TryGetModId(item, out modId))
                return modId != 0;

            try
            {
                foreach (var component in weapon.gameObject.GetComponents<Component>())
                {
                    if (component == null || component == weapon || component == item) continue;
                    if (TryGetModId(component, out modId))
                        return modId != 0;
                }
            }
            catch { }

            return false;
        }

        // Uses reflection to find any mod_id fields or nested metadata structures
        private static bool TryGetModId(object source, out ulong modId)
        {
            modId = 0;
            if (source == null) return false;

            try
            {
                var type = source.GetType();
                ModIdAccessor accessor = GetModIdAccessor(type);

                if (accessor.DirectProperty != null)
                {
                    modId = Convert.ToUInt64(accessor.DirectProperty.GetValue(source));
                    return true;
                }

                if (accessor.DirectField != null)
                {
                    modId = Convert.ToUInt64(accessor.DirectField.GetValue(source));
                    return true;
                }

                object nested = accessor.DataProperty?.GetValue(source)
                                ?? accessor.DataField?.GetValue(source);
                if (nested != null && TryGetDirectModId(nested, out modId))
                    return true;
            }
            catch { }

            return false;
        }

        // Caches PropertyInfo and FieldInfo lookups for mod_id to minimize reflection overhead
        private static ModIdAccessor GetModIdAccessor(Type type)
        {
            if (ModIdAccessors.TryGetValue(type, out var accessor))
                return accessor;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            accessor = new ModIdAccessor
            {
                DirectProperty = type.GetProperty("mod_id", flags),
                DirectField = type.GetField("mod_id", flags)
            };

            foreach (string memberName in ModDataMemberNames)
            {
                accessor.DataProperty = type.GetProperty(memberName, flags);
                accessor.DataField = type.GetField(memberName, flags);
                if (accessor.DataProperty != null || accessor.DataField != null)
                    break;
            }

            ModIdAccessors[type] = accessor;
            return accessor;
        }

        // Secondary reflection lookup for nested data objects
        private static bool TryGetDirectModId(object source, out ulong modId)
        {
            modId = 0;
            if (source == null) return false;

            try
            {
                ModIdAccessor accessor = GetModIdAccessor(source.GetType());
                object value = accessor.DirectProperty?.GetValue(source)
                             ?? accessor.DirectField?.GetValue(source);
                if (value == null) return false;

                modId = Convert.ToUInt64(value);
                return true;
            }
            catch { return false; }
        }

        // Spawns a weapon directly into the player's primary hands and optionally attaches best compatible attachments
        public static void SpawnWeapon(string name, Soldier player)
        {
            if (!_weaponPrefabs.TryGetValue(name, out var prefab))
            {
                _isInitialized = false;
                Initialize();
                _weaponPrefabs.TryGetValue(name, out prefab);
            }
            if (prefab == null) return;

            var gunObj = UnityEngine.Object.Instantiate(prefab);
            var gunComp = gunObj.GetComponent<GenericGun>();

            // Auto-equip compatible attachments (Scope, Bipod, Bayonet) if enabled
            if (gunComp != null && MaximizeAttachments)
            {
                var supported = gunComp.supportedAttachments;
                if (supported != null && supported.Length > 0)
                {
                    bool scopeInstalled = false;
                    bool bipodInstalled = false;
                    bool bayonetInstalled = false;

                    // Iterate backwards through supported attachments to find best fits
                    for (int i = supported.Length - 1; i >= 0; i--)
                    {
                        var supAtt = supported[i];
                        if (supAtt == null || string.IsNullOrEmpty(supAtt.attachment_id)) continue;

                        var attItem = ItemsDatabase.GetItemObject(supAtt.attachment_id);
                        if (attItem != null && attItem.gameObject != null)
                        {
                            try
                            {
                                GameObject attObj = UnityEngine.Object.Instantiate(attItem.gameObject);

                                bool isScope = attObj.GetComponent<AttachmentScope>() != null;
                                bool isBipod = attObj.GetComponent<AttachmentBipod>() != null;
                                bool isBayonet = attObj.GetComponent<AttachmentBayonet>() != null;
                                bool shouldInstall = false;

                                // Allow only one of each category to avoid mesh clipping
                                if (isScope && !scopeInstalled) { shouldInstall = true; scopeInstalled = true; }
                                else if (isBipod && !bipodInstalled) { shouldInstall = true; bipodInstalled = true; }
                                else if (isBayonet && !bayonetInstalled) { shouldInstall = true; bayonetInstalled = true; }

                                if (shouldInstall)
                                {
                                    gunComp.InstallAttachment(attObj.GetComponent<Attachment>(), true);
                                }
                                else
                                {
                                    UnityEngine.Object.Destroy(attObj);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }

            // Put weapon in primary slot (index 0)
            player.SetItem(gunObj, 0, true);

            if (gunComp != null)
            {
                try
                {
                    gunComp.OnSetFPSAttachments(true, true);
                    gunComp.RefreshSightPosition();
                }
                catch { }
            }
        }

        // Mounts an attachment to whichever gun the player is currently holding
        public static void AttachToHeldWeapon(string attachmentID, Soldier player)
        {
            var gun = player.GetHeldGun(0);
            if (gun == null || !gun.isActiveAndEnabled) gun = player.GetHeldGun(1);

            if (gun != null)
            {
                var attItem = ItemsDatabase.GetItemObject(attachmentID);
                if (attItem != null && attItem.gameObject != null)
                {
                    try
                    {
                        GameObject attObj = UnityEngine.Object.Instantiate(attItem.gameObject);
                        Attachment attComp = attObj.GetComponent<Attachment>();
                        if (attComp != null)
                        {
                            gun.InstallAttachment(attComp, true);
                            gun.OnSetFPSAttachments(true, true);
                            gun.RefreshSightPosition();
                        }
                    }
                    catch { }
                }
            }
        }

        // Spawns an item on the ground in front of the player and activates its physics so it can be picked up
        public static void SpawnItem(string name, Soldier player)
        {
            var itemData = ItemsDatabase.GetItemObject(name);
            GameObject prefab = (itemData != null) ? itemData.gameObject : null;
            if (prefab == null) _itemPrefabs.TryGetValue(name, out prefab);

            if (prefab != null)
            {
                Vector3 pos = player.transform.position + player.transform.forward * 1.5f + Vector3.up * 0.5f;
                GameObject itemInstance = UnityEngine.Object.Instantiate(prefab, pos, player.transform.rotation);

                // Assign to the game's interactive item layer
                int layerIndex = LayerMask.NameToLayer("item");
                if (layerIndex != -1) itemInstance.layer = layerIndex;

                // Add fallback collider if missing so the object doesn't fall through ground
                if (itemInstance.GetComponentInChildren<Collider>(true) == null)
                {
                    var col = itemInstance.AddComponent<BoxCollider>();
                    col.size = new Vector3(0.2f, 0.2f, 0.2f);
                }

                var itemObj = itemInstance.GetComponent<ItemObject>();
                if (itemObj != null)
                {
                    itemObj.EnablePhysic();
                }

                itemInstance.SetActive(true);
            }
        }

        // Ensures dropped item physics wake up and settle correctly
        private static IEnumerator DelayedEnablePhysics(GameObject obj)
        {
            yield return new WaitForSeconds(0.2f);
            if (obj != null)
            {
                var rb = obj.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = false;
                    rb.WakeUp();
                }
            }
        }

        // Spawns a vehicle in front of the player, raycasting ground height for land vehicles or elevating aircraft
        public static void SpawnVehicle(string name, Soldier player, MonoBehaviour coroutineRunner)
        {
            if (!_vehiclePrefabs.TryGetValue(name, out var prefab)) return;

            bool isPlane = prefab.GetComponent<VehiclePlane>() != null;

            Vector3 spawnPos = player.transform.position + player.transform.forward * 12f;

            // Ground vehicles raycast down to hug terrain surface
            if (!isPlane)
            {
                if (Physics.Raycast(spawnPos + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f))
                {
                    spawnPos = hit.point + Vector3.up * 0.1f;
                }
            }
            // Aircraft spawn elevated in the air
            else
            {
                spawnPos += Vector3.up * 5f;
            }

            // Support multiplayer room instantiation if connected via Photon
            GameObject spawned = PhotonNetwork.InRoom
                ? PhotonNetwork.Instantiate(name, spawnPos, player.transform.rotation)
                : UnityEngine.Object.Instantiate(prefab, spawnPos, player.transform.rotation);

            if (spawned != null)
                coroutineRunner.StartCoroutine(SetupVehicleAfterSpawn(spawned, player));
        }

        // Post-spawn routine: sets vehicle faction, unlocks it, repairs damage, and populates seats with friendly AI
        private static IEnumerator SetupVehicleAfterSpawn(GameObject spawnedVehicle, Soldier player)
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForEndOfFrame();

            var veh = spawnedVehicle.GetComponent<Vehicle>();
            if (veh == null) yield break;

            // Align vehicle faction to the player's team and unlock
            string factionName = (player != null && !string.IsNullOrEmpty(player.faction)) ? player.faction : "USA";
            veh.SetFaction(factionName);
            veh.SetLocked(false);
            VehicleTab.FullRepair(veh);

            // Populate all empty vehicle seats with cloned friendly AI squadmates
            if (SpawnWithCrew)
            {
                Soldier soldierPrefab = null;
                foreach (var s in UnityEngine.Resources.FindObjectsOfTypeAll<Soldier>())
                {
                    if (!s.name.Contains("(Clone)")) { soldierPrefab = s; break; }
                }

                if (soldierPrefab != null)
                {
                    foreach (var seat in veh.seats)
                    {
                        if (seat != null && !seat.hasUnitSet)
                        {
                            GameObject aiObj = UnityEngine.Object.Instantiate(soldierPrefab.gameObject, veh.transform.position, veh.transform.rotation);
                            var aiSoldier = aiObj.GetComponent<Soldier>();

                            aiObj.transform.localScale = Vector3.one;
                            if (aiSoldier.m_controller != null) aiSoldier.m_controller.enabled = false;
                            var rb = aiObj.GetComponent<Rigidbody>();
                            if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }

                            // Configure AI allegiance and behavior
                            aiSoldier.SetAI();
                            aiSoldier.SetFaction(factionName);
                            if (player.joinedSquad != null) aiSoldier.joinedSquad = player.joinedSquad;
                            aiSoldier.dontLeaveVehicle = true;

                            // Clone appearance from player (face, uniform, and headgear)
                            if (player.face != null)
                            {
                                aiSoldier.face = player.face;
                            }

                            if (player.GetUniform != null) aiSoldier.SetWerable(player.GetUniform.ToVirtualItem(), true);

                            var pTrav = Traverse.Create(player);
                            var headgear = pTrav.Field("headgear_ref").GetValue<VirtualItem>();
                            if (headgear != null) aiSoldier.SetWerable(headgear, true);

                            // Force visual clothing and mesh refresh
                            aiSoldier.TriggerClothingObjRefresh(true, true, true);
                            aiSoldier.RefreshFace(true, 0);
                            aiSoldier.StartCoroutine(aiSoldier.RefreshSkinAndHairsCR(true, null));

                            // Mount AI into the vehicle seat
                            veh.GetOnVehicle(seat.GetSeatIndexInVehicle(), aiSoldier);
                            aiSoldier.OnBecomePlayerOrGetOnVehicle();

                            aiSoldier.RefreshAnimatorControllerAsync();
                            aiSoldier.LodUpdate();

                            // Activate combat AI targeting
                            var ai = aiSoldier.GetComponent<SoldierAI>();
                            if (ai != null)
                            {
                                ai.enabled = true;
                                ai.MakeSureInRightVehicle();
                                aiSoldier.SetNotAlerted();
                                aiSoldier.SetAlertedFor(999f);
                            }

                            yield return null;
                        }
                    }
                }
            }
        }
    }
}