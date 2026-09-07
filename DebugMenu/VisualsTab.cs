using UnityEngine;
using System.Collections.Generic;

namespace EasyRed2Mod
{
    // Stores individual renderer references and their factory materials for Chams restoration
    public class ChamsEntry
    {
        public Renderer renderer;
        public Material[] originalMaterials;
        public float nextCheck;
        public bool isVisible;
    }

    // Handles visual overlays: 2D Screen ESP (Boxes, Snaplines, Health, Names) and 3D Wallhack (Chams)
    public static class VisualsTab
    {
        // Feature toggles and render distance
        public static bool EspEnabled, EspNames = true, EspLines, EspBoxes, EspHealth, OnlyEnemies = true, VehicleEsp = true, ChamsEnabled;
        public static float MaxDist = 400f;

        // Custom ESP and Chams color palette
        public static Color VisibleColor = new Color(0.8f, 0.1f, 0.1f, 0.8f);  // Red for enemies in line of sight
        public static Color FriendlyColor = new Color(0.1f, 0.8f, 0.1f, 0.8f); // Green for allies
        public static Color OccludedColor = new Color(0.4f, 0.0f, 0.6f, 0.4f); // Purple for entities behind walls/cover

        // Cached materials per soldier to avoid repeated allocations
        private static Dictionary<int, List<ChamsEntry>> chamsCache = new Dictionary<int, List<ChamsEntry>>();

        // Unlit shaders that bypass Unity's depth buffer (ZTest Always)
        private static Material _matEnemyVisible;
        private static Material _matFriendVisible;
        private static Material _matOccluded;

        private static float nextCleanup = 0f;

        // Main logic loop for Chams materials, line-of-sight checks, and cache cleanup
        public static void UpdateLogic(List<Soldier> currentSoldiers, Soldier local)
        {
            if (_matEnemyVisible == null) CreateMaterials();
            if (_matEnemyVisible != null) _matEnemyVisible.color = VisibleColor;

            if (local == null) return;

            // Garbage collection check every 5s to remove despawned soldiers from cache
            if (Time.time > nextCleanup)
            {
                nextCleanup = Time.time + 5f;
                CleanupOrphanedCache(currentSoldiers);
            }

            // If feature is disabled, restore all original soldier textures and exit
            if (!EspEnabled || !ChamsEnabled)
            {
                if (chamsCache.Count > 0) ForceResetAll();
                return;
            }

            foreach (var s in currentSoldiers)
            {
                if (s == null || s.gameObject == null || s.IsPlayer()) continue;

                // Restore textures on dead soldiers
                if (s.life_total.Value <= 0)
                {
                    ResetSoldier(s);
                    continue;
                }

                bool isEnemy = !s.faction.Contains(local.faction.Substring(0, 3));
                bool shouldHaveChams = Vector3.Distance(local.transform.position, s.transform.position) < MaxDist &&
                                       (!OnlyEnemies || isEnemy);

                if (shouldHaveChams) ApplyChamsInternal(s, isEnemy);
                else ResetSoldier(s);
            }
        }

        // Creates flat-color materials that draw on top of all world geometry (Wallhack effect)
        private static void CreateMaterials()
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");

            // Enemy visible material
            _matEnemyVisible = new Material(shader);
            _matEnemyVisible.SetInt("_ZWrite", 0);
            _matEnemyVisible.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            _matEnemyVisible.color = VisibleColor;

            // Ally material
            _matFriendVisible = new Material(shader);
            _matFriendVisible.SetInt("_ZWrite", 0);
            _matFriendVisible.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            _matFriendVisible.color = FriendlyColor;

            // Occluded / behind wall material
            _matOccluded = new Material(shader);
            _matOccluded.SetInt("_ZWrite", 0);
            _matOccluded.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            _matOccluded.color = OccludedColor;
        }

        // Overrides soldier mesh renderers with flat chams materials
        private static void ApplyChamsInternal(Soldier s, bool isEnemy)
        {
            int id = s.GetInstanceID();

            // Cache renderers and original materials if soldier is seen for the first time
            if (!chamsCache.ContainsKey(id))
            {
                var rends = s.GetComponentsInChildren<Renderer>(true);
                var entries = new List<ChamsEntry>();
                foreach (var r in rends)
                {
                    if (r == null || r.gameObject.layer == 5) continue; // Skip UI layer

                    // Ignore particles, muzzle flashes, and held gun meshes
                    string n = r.gameObject.name.ToLower();
                    if (n.Contains("flash") || n.Contains("vfx") || n.Contains("crosshair") ||
                        n.Contains("muzzle") || n.Contains("blood") || n.Contains("particle") ||
                        r.GetComponentInParent<GenericGun>() || r.GetComponent<GenericGun>()) continue;

                    entries.Add(new ChamsEntry { renderer = r, originalMaterials = r.sharedMaterials });
                }
                chamsCache[id] = entries;
            }

            var soldierEntries = chamsCache[id];

            // Line-of-sight raycast to detect if soldier is hidden behind terrain or buildings (layer mask 1 << 0)
            bool visible = !Physics.Linecast(Camera.main.transform.position, s.transform.position + Vector3.up * 1.6f, 1 << 0);

            Material targetMat;
            if (visible)
            {
                targetMat = isEnemy ? _matEnemyVisible : _matFriendVisible;
            }
            else
            {
                targetMat = _matOccluded;
            }

            // Apply selected material to all cached body renderers
            foreach (var entry in soldierEntries)
            {
                if (entry.renderer == null) continue;

                if (entry.renderer.sharedMaterial != targetMat)
                {
                    entry.renderer.sharedMaterial = targetMat;
                }
            }
        }

        // Main 2D ESP overlay rendering loop using Unity IMGUI
        public static void Draw(List<Soldier> soldiers, List<Vehicle> vehicles, Texture2D whiteTex, Soldier localPlayer)
        {
            if (!EspEnabled ||
                whiteTex == null ||
                Camera.main == null ||
                localPlayer == null ||
                localPlayer.gameObject == null ||
                localPlayer.life_total.Value <= 0)
            {
                return;
            }

            bool oldWrap = GUI.skin.label.wordWrap;
            GUI.skin.label.wordWrap = false;

            Vector3 camPos = Camera.main.transform.position;
            string myFaction = localPlayer.faction;
            if (myFaction == null) myFaction = "";

            // --- SOLDIER ESP ---
            foreach (var s in soldiers)
            {
                if (s == null || s.gameObject == null) continue;
                if (s.life_total.Value <= 0 || s.IsPlayer()) continue;

                try
                {
                    bool isEnemy = !s.faction.Contains(myFaction.Length >= 3 ? myFaction.Substring(0, 3) : myFaction);

                    if (OnlyEnemies && !isEnemy) continue;

                    float dist = Vector3.Distance(camPos, s.transform.position);
                    if (dist > MaxDist) continue;

                    // Convert world coordinates to 2D screen coordinates
                    Vector3 screenPivot = Camera.main.WorldToScreenPoint(s.transform.position);
                    if (screenPivot.z > 0) // Target is in front of the camera
                    {
                        // Adjust bounding box height based on stance (prone/crouch vs standing)
                        float heightOffset = 1.7f;
                        var cc = s.GetComponent<CharacterController>();
                        if (cc != null && cc.height < 1.0f) heightOffset = 0.45f;

                        Vector3 headPos = s.transform.position + Vector3.up * heightOffset;
                        Vector3 screenHead = Camera.main.WorldToScreenPoint(headPos + Vector3.up * 0.2f);

                        float h = Mathf.Abs(screenHead.y - screenPivot.y);
                        if (h < 15f) h = 20f; // Minimum box height fallback
                        float x = screenPivot.x - (h / 3);
                        float y = Screen.height - screenHead.y;

                        // Check line of sight for 2D box colors
                        bool isVisible = true;
                        if (EspBoxes || EspLines)
                            isVisible = !Physics.Linecast(camPos, headPos, 1 << 0);

                        Color enemyCol = VisibleColor;
                        Color friendCol = FriendlyColor;
                        Color drawCol = isVisible ? (isEnemy ? enemyCol : friendCol) : OccludedColor;

                        // 2D Bounding Box
                        if (EspBoxes) DrawBox(x, y, h / 1.5f, h, drawCol, whiteTex);

                        // Snapline from screen bottom center to target feet
                        if (EspLines) DrawLine(new Vector2(Screen.width / 2, Screen.height), new Vector2(screenPivot.x, Screen.height - screenPivot.y), drawCol, whiteTex);

                        // Health Bar (left of the bounding box)
                        if (EspHealth)
                        {
                            float hp = (float)s.life_total.Value / 100f;
                            DrawBox(x - 8, y, 4, h, Color.black, whiteTex);
                            GUI.color = Color.Lerp(Color.red, Color.green, hp);
                            GUI.DrawTexture(new Rect(x - 7, y + h, 2, -h * hp), whiteTex);
                            GUI.color = Color.white;
                        }

                        // Name, Faction, and Distance tag above head
                        if (EspNames)
                        {
                            GUI.color = Color.white;
                            GUI.Label(new Rect(x, y - 22, 400, 25), $"{(s.IsAI() ? "[BOT]" : "[P]")} {s.faction} ({Mathf.Round(dist)}m)");
                        }
                    }
                }
                catch { continue; }
            }

            // --- VEHICLE ESP ---
            if (VehicleEsp)
            {
                foreach (var v in vehicles)
                {
                    if (v == null || v.gameObject == null) continue;
                    // Only render vehicles with active crew/passengers inside
                    if (v.GetDriver() == null && v.CalculatePeopleInside() <= 0) continue;

                    try
                    {
                        string vFaction = v.GetVehicleFaction();
                        if (vFaction == null) continue;

                        bool isEnemyV = !vFaction.Contains(myFaction.Length >= 3 ? myFaction.Substring(0, 3) : myFaction);
                        if (OnlyEnemies && !isEnemyV) continue;

                        Vector3 vScreen = Camera.main.WorldToScreenPoint(v.transform.position);
                        if (vScreen.z > 0)
                        {
                            GUI.color = isEnemyV ? VisibleColor : FriendlyColor;
                            GUI.Label(new Rect(vScreen.x - 100, Screen.height - vScreen.y, 400, 25), $"[VEHICLE] {v.name}");
                        }
                    }
                    catch { continue; }
                }
            }
            GUI.skin.label.wordWrap = oldWrap;
            GUI.color = Color.white;
        }

        // Restores the original materials to a single soldier
        public static void ResetSoldier(Soldier s)
        {
            if (s == null) return;
            int id = s.GetInstanceID();
            if (chamsCache.TryGetValue(id, out var entries))
            {
                foreach (var e in entries)
                {
                    if (e.renderer != null)
                        e.renderer.sharedMaterials = e.originalMaterials;
                }
                chamsCache.Remove(id);
            }
        }

        // Removes destroyed or despawned soldier IDs from cache to prevent memory leaks
        private static void CleanupOrphanedCache(List<Soldier> activeSoldiers)
        {
            HashSet<int> activeIDs = new HashSet<int>();
            foreach (var s in activeSoldiers) { if (s != null) activeIDs.Add(s.GetInstanceID()); }

            List<int> toRemove = new List<int>();
            foreach (var key in chamsCache.Keys)
            {
                if (!activeIDs.Contains(key)) toRemove.Add(key);
            }

            foreach (int id in toRemove) chamsCache.Remove(id);
        }

        // Global restore: sets all tracked entities back to their original textures
        public static void ForceResetAll()
        {
            foreach (var id in chamsCache.Keys)
            {
                foreach (var e in chamsCache[id])
                {
                    if (e.renderer != null)
                        e.renderer.sharedMaterials = e.originalMaterials;
                }
            }
            chamsCache.Clear();
        }

        // Helper: draws a 2D hollow rectangle using 4 textured lines
        private static void DrawBox(float x, float y, float w, float h, Color c, Texture2D t)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y, w, 1.2f), t);
            GUI.DrawTexture(new Rect(x, y + h, w, 1.2f), t);
            GUI.DrawTexture(new Rect(x, y, 1.2f, h), t);
            GUI.DrawTexture(new Rect(x + w, y, 1.2f, h), t);
            GUI.color = Color.white;
        }

        // Helper: draws a 2D line between two screen points using rotation matrix
        private static void DrawLine(Vector2 s, Vector2 e, Color c, Texture2D t)
        {
            GUI.color = c;
            var d = e - s; float a = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(a, s);
            GUI.DrawTexture(new Rect(s.x, s.y, d.magnitude, 1.2f), t);
            GUIUtility.RotateAroundPivot(-a, s);
            GUI.color = Color.white;
        }
    }
}