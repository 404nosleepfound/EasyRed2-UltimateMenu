using System;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.Random;

namespace EasyRed2Mod
{
    /// Main controller for the mod: manages UI rendering (IMGUI), 
    /// player caching, entity scanning, and feature update loops.
    public class DebugMenuController : MonoBehaviour
    {
        // Required constructor for IL2CPP custom MonoBehaviours
        public DebugMenuController(IntPtr ptr) : base(ptr) { }

        public bool showMenu = false;
        private int currentTab = 0;
        private Soldier _localPlayer;
        private int _spawnerCategory = 0;
        private int _subCat = 0;

        // Cached entities for ESP and targeted cheats
        public static List<Soldier> Soldiers = new List<Soldier>();
        public static List<Vehicle> Vehicles = new List<Vehicle>();

        // Scan timers to avoid checking the scene every frame (performance optimization)
        private float _nextScan = 0f;
        private float _nextPlayerScan = 0f;

        // UI Textures & Styles
        private Texture2D _whiteTex;
        private GUIStyle headerStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _tabStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _smallButtonStyle;
        private GUIStyle _panelStyle;
        private GUIStyle _statusStyle;

        private bool _spawnerInitialized = false;
        private int _currentPage = 0;
        private const int ItemsPerPage = 14;

        void Start()
        {
            CreateTex();
            Spawner.Reset();
            _spawnerInitialized = false;
        }

        // Creates a 1x1 white texture used for drawing solid colored boxes in IMGUI
        void CreateTex()
        {
            _whiteTex = new Texture2D(1, 1);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply();
        }

        /// Stores reference to the active player soldier called from Harmony patches
        public void CachePlayer(Component comp)
        {
            if (comp == null) return;
            var s = comp.TryCast<Soldier>();
            if (s != null && s.IsPlayer()) _localPlayer = s;
        }

        private bool IsPlayerValid() => _localPlayer != null && _localPlayer.gameObject != null;

        void Update()
        {
            PerformanceTab.UpdateFPS();

            // Toggle menu visibility and manage mouse cursor locking
            bool fallbackMenuKey = SettingsTab.MenuKey != KeyCode.F10 && Input.GetKeyDown(KeyCode.F10);
            if (Input.GetKeyDown(SettingsTab.MenuKey) || fallbackMenuKey)
            {
                showMenu = !showMenu;
                if (!showMenu)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
            if (showMenu)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            // Periodic search for local player if lost (e.g. after respawn or map reload)
            if (!IsPlayerValid() && Time.time > _nextPlayerScan)
            {
                _nextPlayerScan = Time.time + 0.5f;
                PerformanceTab.Begin("Player Search");
                RefreshLocalPlayer();
                PerformanceTab.End("Player Search");

                // Reset spawner database if returned to main menu or loading screen
                if (!IsPlayerValid() && Spawner.IsInitialized())
                {
                    Debug.Log("[ER2-MOD] Missing player detected (Menu/Loading). Resetting Spawner...");
                    Spawner.Reset();
                    _spawnerInitialized = false;
                }

                if (!IsPlayerValid()) return;
            }

            // Check if player died to quickly refresh references on respawn
            bool isDead = false;
            try { if (_localPlayer.life_total.Value <= 0) isDead = true; } catch { isDead = true; }

            if (isDead && Time.time > _nextPlayerScan)
            {
                _nextPlayerScan = Time.time + 0.2f;
                PerformanceTab.Begin("Death Handling");
                RefreshLocalPlayer();
                PerformanceTab.End("Death Handling");
            }

            // Always update keybinds (even when paused or dead)
            SettingsTab.Update(_localPlayer);

            if (Time.timeScale == 0 && !showMenu) return;

            // Execute cheat loops only while player is alive
            if (IsPlayerValid() && !isDead)
            {
                PlayerTab.UpdateAutoHeal(_localPlayer);

                PerformanceTab.Begin("Combat Tab");
                try { CombatTab.Update(_localPlayer); } catch { PerformanceTab.LogError("Combat Tab"); }
                PerformanceTab.End("Combat Tab");

                PerformanceTab.Begin("Squad God Mode");
                PlayerTab.UpdateSquadGodMode(_localPlayer);
                PerformanceTab.End("Squad God Mode");

                PerformanceTab.Begin("Vehicle Tab");
                try { VehicleTab.Update(_localPlayer); } catch { PerformanceTab.LogError("Vehicle Tab"); }
                PerformanceTab.End("Vehicle Tab");

                PerformanceTab.Begin("Player Tab");
                try { PlayerTab.Update(_localPlayer); } catch { PerformanceTab.LogError("Player Tab"); }
                PerformanceTab.End("Player Tab");

                if (VisualsTab.EspEnabled || VisualsTab.EspBoxes || VisualsTab.EspNames)
                {
                    PerformanceTab.Begin("Visuals Logic");
                    try { VisualsTab.UpdateLogic(Soldiers, _localPlayer); } catch { PerformanceTab.LogError("Visuals Logic"); }
                    PerformanceTab.End("Visuals Logic");
                }
            }

            // Throttled Entity Scanner (Scans scene every 2.0 seconds only when features require it)
            PerformanceTab.Begin("Entity Scanner");
            bool needsScan = VisualsTab.EspEnabled || VisualsTab.VehicleEsp || CombatTab.ExplosiveBullets ||
                             VehicleTab.UnlockAll || VehicleTab.InfAmmoTanks || VehicleTab.InfAmmoPlanes;

            if (needsScan && Time.time > _nextScan)
            {
                _nextScan = Time.time + 2.0f;
                Soldiers.Clear();
                var foundSoldiers = UnityEngine.Object.FindObjectsOfType<Soldier>();
                foreach (var s in foundSoldiers) if (s != null && s.gameObject.activeInHierarchy) Soldiers.Add(s);

                Vehicles.Clear();
                var foundVehicles = UnityEngine.Object.FindObjectsOfType<Vehicle>();
                foreach (var v in foundVehicles) if (v != null && v.gameObject.activeInHierarchy) Vehicles.Add(v);
            }
            else if (!needsScan && (Soldiers.Count > 0 || Vehicles.Count > 0))
            {
                // Clear lists when visual/targeting cheats are disabled
                Soldiers.Clear();
                Vehicles.Clear();
            }
            PerformanceTab.End("Entity Scanner");
        }

        // Finds the local player's Soldier component in the scene hierarchy
        void RefreshLocalPlayer()
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Soldier>();
                foreach (var s in all)
                {
                    if (s != null && s.gameObject != null && s.IsPlayer() && s.life_total.Value > 0)
                    {
                        _localPlayer = s;
                        VisualsTab.ForceResetAll();
                        break;
                    }
                }
            }
            catch { }
        }

        void OnGUI()
        {
            // Draw world overlay features (ESP, Tracers, Boxes)
            if ((VisualsTab.EspEnabled || VisualsTab.VehicleEsp) && Camera.main != null && IsPlayerValid())
            {
                if (_whiteTex == null) CreateTex();
                VisualsTab.Draw(Soldiers, Vehicles, _whiteTex, _localPlayer);
            }

            if (!showMenu) return;

            // Lazy initialize spawner data only when the Spawner tab is opened
            if (currentTab == 4 && !_spawnerInitialized)
            {
                Spawner.Initialize();
                _spawnerInitialized = true;
            }

            DrawMenu();
        }

        void DrawMenu()
        {
            InitializeMenuStyles();

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            Color previousBackground = GUI.backgroundColor;

            // Screen dimming background overlay
            DrawSolidRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0.025f, 0.028f, 0.02f, 0.7f));

            // Dynamic UI scaling based on window resolution
            const float menuWidth = 1040f;
            const float menuHeight = 720f;
            float scale = Mathf.Min(Screen.width / (menuWidth + 40f), Screen.height / (menuHeight + 40f));
            scale = Mathf.Clamp(scale, 0.5f, 0.92f);
            float offsetX = (Screen.width - menuWidth * scale) * 0.5f;
            float offsetY = (Screen.height - menuHeight * scale) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));

            // Main Window Panel & Borders
            Rect mR = new Rect(0, 0, menuWidth, menuHeight);
            GUI.backgroundColor = new Color(0.095f, 0.105f, 0.075f, 0.99f);
            GUI.Box(mR, GUIContent.none, _panelStyle);
            DrawSolidRect(new Rect(0, 0, 7, menuHeight), new Color(0.48f, 0.16f, 0.12f, 1f));
            DrawSolidRect(new Rect(195, 0, 2, menuHeight), new Color(0.38f, 0.39f, 0.29f, 0.75f));
            DrawSolidRect(new Rect(196, 92, menuWidth - 196, 2), new Color(0.38f, 0.39f, 0.29f, 0.7f));
            DrawSolidRect(new Rect(210, 14, 815, 3), new Color(0.33f, 0.37f, 0.22f, 0.9f));

            // Header titles
            GUI.Label(new Rect(220, 24, 600, 34), "EASY RED 2  <color=#C7A45C>ULTIMATE MENU</color>", _titleStyle);
            GUI.Label(new Rect(222, 58, 500, 22), "FIELD COMMAND / TACTICAL CONTROL", _subtitleStyle);
            GUI.Label(new Rect(900, 27, 105, 28), "v1.4.6", _statusStyle);

            GUI.Label(new Rect(24, 24, 150, 26), "ER2 FIELD HQ", _titleStyle);
            GUI.Label(new Rect(25, 56, 150, 20), "OPERATIONS BOARD", _subtitleStyle);

            // Left Navigation Tabs
            string[] tbs = { "PLAYER", "COMBAT", "VEHICLES", "VISUALS", "SPAWNER", "SETTINGS", "PERF" };
            for (int i = 0; i < tbs.Length; i++)
            {
                Rect tabRect = new Rect(18, 112 + (i * 58), 160, 46);
                bool selected = currentTab == i;
                GUI.backgroundColor = selected ? new Color(0.35f, 0.4f, 0.21f) : new Color(0.15f, 0.16f, 0.115f);
                if (GUI.Button(tabRect, tbs[i], _tabStyle)) currentTab = i;
                if (selected) DrawSolidRect(new Rect(tabRect.x, tabRect.y + 8, 4, tabRect.height - 16), new Color(0.78f, 0.64f, 0.34f));
            }

            GUI.Label(new Rect(24, 640, 150, 20), $"ACCESS  {SettingsTab.GetKeyName(SettingsTab.MenuKey)} / F10", _subtitleStyle);
            GUI.Label(new Rect(24, 666, 150, 20), IsPlayerValid() ? "UNIT READY" : "AWAITING UNIT",
                IsPlayerValid() ? _statusStyle : _subtitleStyle);

            // Tab Content Background Panel
            float bX = 220; float bY = 118;
            DrawSolidRect(new Rect(210, 104, 815, 596), new Color(0.12f, 0.13f, 0.09f, 0.82f));
            DrawSolidRect(new Rect(214, 108, 5, 5), new Color(0.55f, 0.53f, 0.39f));
            DrawSolidRect(new Rect(1016, 108, 5, 5), new Color(0.55f, 0.53f, 0.39f));
            DrawSolidRect(new Rect(214, 691, 5, 5), new Color(0.55f, 0.53f, 0.39f));
            DrawSolidRect(new Rect(1016, 691, 5, 5), new Color(0.55f, 0.53f, 0.39f));

            // Render selected tab contents
            switch (currentTab)
            {
                case 0: // PLAYER TAB
                    float startY = bY;

                    // Row 1
                    if (Btn(bX, startY, "GOD MODE", PlayerTab.GodMode)) PlayerTab.GodMode = !PlayerTab.GodMode;
                    if (Btn(bX + 400, startY, $"NOCLIP (FLY) [{SettingsTab.GetKeyName(SettingsTab.NoclipKey)}]", PlayerTab.Noclip)) PlayerTab.Noclip = !PlayerTab.Noclip;

                    // Row 2
                    if (Btn(bX, startY + 60, "SQUAD GOD MODE", PlayerTab.GodModeSquad)) PlayerTab.GodModeSquad = !PlayerTab.GodModeSquad;
                    if (Btn(bX + 400, startY + 60, "UNLIMITED WEIGHT", PlayerTab.UnlimitedWeight)) PlayerTab.UnlimitedWeight = !PlayerTab.UnlimitedWeight;

                    // Row 3 
                    if (Btn(bX, startY + 120, "INDESTRUCTIBLE HELMETS", PlayerTab.IndestructibleHelmets)) PlayerTab.IndestructibleHelmets = !PlayerTab.IndestructibleHelmets;

                    // Speed & Jump Multipliers Section
                    float multiplierSectionY = startY + 210;

                    GUI.color = new Color(0.78f, 0.67f, 0.42f);
                    GUI.Label(new Rect(bX, multiplierSectionY, 790, 30), "--- SPEED & JUMP MULTIPLIERS ---", headerStyle);
                    GUI.color = Color.white;

                    GUI.backgroundColor = new Color(0.18f, 0.19f, 0.13f, 0.95f);
                    GUI.Box(new Rect(bX, multiplierSectionY + 35, 790, 110), "");
                    GUI.backgroundColor = new Color(0.12f, 0.13f, 0.09f, 0.98f);

                    // Move Speed controls
                    GUI.Label(new Rect(bX + 20, multiplierSectionY + 55, 250, 30), $"Move Speed Multiplier: <color=yellow>{PlayerTab.MoveSpeedMultiplier}x</color>");
                    if (SmallButton(new Rect(bX + 350, multiplierSectionY + 50, 100, 35), "-0.5x")) PlayerTab.MoveSpeedMultiplier = Mathf.Max(0.5f, PlayerTab.MoveSpeedMultiplier - 0.5f);
                    if (SmallButton(new Rect(bX + 460, multiplierSectionY + 50, 100, 35), "+0.5x")) PlayerTab.MoveSpeedMultiplier += 0.5f;

                    // Jump Force controls
                    GUI.Label(new Rect(bX + 20, multiplierSectionY + 100, 250, 30), $"Jump Force Multiplier: <color=yellow>{PlayerTab.JumpMultiplier}x</color>");
                    if (SmallButton(new Rect(bX + 350, multiplierSectionY + 95, 100, 35), "-0.5x")) PlayerTab.JumpMultiplier = Mathf.Max(1.0f, PlayerTab.JumpMultiplier - 0.5f);
                    if (SmallButton(new Rect(bX + 460, multiplierSectionY + 95, 100, 35), "+0.5x")) PlayerTab.JumpMultiplier += 0.5f;
                    break;

                case 1: // COMBAT TAB
                    if (Btn(bX, bY, "NO RECOIL", CombatTab.NoRecoil)) CombatTab.NoRecoil = !CombatTab.NoRecoil;
                    if (Btn(bX + 400, bY, "EXPLOSIVE BULLETS", CombatTab.ExplosiveBullets)) CombatTab.ExplosiveBullets = !CombatTab.ExplosiveBullets;

                    if (Btn(bX, bY + 60, "INFINITE AMMO", CombatTab.InfiniteAmmo)) CombatTab.InfiniteAmmo = !CombatTab.InfiniteAmmo;
                    if (Btn(bX + 400, bY + 60, "RAPID FIRE + AUTO", CombatTab.RapidFire)) CombatTab.RapidFire = !CombatTab.RapidFire;

                    if (Btn(bX, bY + 120, "INFINITE RESERVE MAGS", CombatTab.RealisticAmmo)) CombatTab.RealisticAmmo = !CombatTab.RealisticAmmo;
                    if (Btn(bX + 400, bY + 120, $"TELEPORT [{SettingsTab.GetKeyName(SettingsTab.TeleportKey)}]", false)) CombatTab.TeleportToCrosshair(_localPlayer);

                    // Row 4: Damage Multiplier
                    float dmgY = bY + 180;

                    // Highlight box in reddish tint if damage is boosted
                    GUI.backgroundColor = (CombatTab.DamageMultiplier > 1.0f) ? new Color(0.48f, 0.16f, 0.12f) : new Color(0.18f, 0.19f, 0.13f, 0.95f);
                    GUI.Box(new Rect(bX, dmgY, 790, 50), "");

                    // Reset background for normal button styling
                    GUI.backgroundColor = new Color(0.12f, 0.13f, 0.09f, 0.98f);

                    GUI.Label(new Rect(bX + 20, dmgY + 12, 300, 30), $"DAMAGE MULTIPLIER: <color=yellow>{CombatTab.DamageMultiplier:F1}x</color>", headerStyle);

                    if (SmallButton(new Rect(bX + 400, dmgY + 5, 180, 40), "+0.5x"))
                        CombatTab.DamageMultiplier += 0.5f;

                    if (SmallButton(new Rect(bX + 600, dmgY + 5, 180, 40), "-0.5x"))
                        CombatTab.DamageMultiplier = Mathf.Max(1.0f, CombatTab.DamageMultiplier - 0.5f);
                    break;

                case 2: // VEHICLES TAB
                    if (Btn(bX, bY, "INF TANK AMMO", VehicleTab.InfAmmoTanks)) VehicleTab.InfAmmoTanks = !VehicleTab.InfAmmoTanks;
                    if (Btn(bX + 400, bY, "AUTO REPAIR", VehicleTab.AutoRepair)) VehicleTab.AutoRepair = !VehicleTab.AutoRepair;

                    if (Btn(bX, bY + 60, "INF PLANE AMMO", VehicleTab.InfAmmoPlanes)) VehicleTab.InfAmmoPlanes = !VehicleTab.InfAmmoPlanes;
                    if (Btn(bX + 400, bY + 60, "DESTROY ENEMY VEHS", false)) VehicleTab.DestroyEnemyVehicles(_localPlayer);

                    if (Btn(bX, bY + 120, "UNLOCK ALL VEHS", VehicleTab.UnlockAll)) VehicleTab.UnlockAll = !VehicleTab.UnlockAll;
                    if (Btn(bX + 400, bY + 120, $"VEHICLE FLY [{SettingsTab.VehFlyKey}]", VehicleTab.FlyMode)) VehicleTab.FlyMode = !VehicleTab.FlyMode;
                    break;

                case 3: // VISUALS TAB (ESP)
                    if (Btn(bX, bY, "MAIN ESP", VisualsTab.EspEnabled)) VisualsTab.EspEnabled = !VisualsTab.EspEnabled;
                    if (Btn(bX + 400, bY, "BOX ESP", VisualsTab.EspBoxes)) VisualsTab.EspBoxes = !VisualsTab.EspBoxes;
                    if (Btn(bX, bY + 60, "CHAMS (X-RAY)", VisualsTab.ChamsEnabled))
                    {
                        VisualsTab.ChamsEnabled = !VisualsTab.ChamsEnabled;
                        if (!VisualsTab.ChamsEnabled) VisualsTab.ForceResetAll();
                    }
                    if (Btn(bX + 400, bY + 60, "SNAPLINES", VisualsTab.EspLines)) VisualsTab.EspLines = !VisualsTab.EspLines;
                    if (Btn(bX, bY + 120, "VEHICLE ESP", VisualsTab.VehicleEsp)) VisualsTab.VehicleEsp = !VisualsTab.VehicleEsp;
                    if (Btn(bX + 400, bY + 120, "NAMES & DIST", VisualsTab.EspNames)) VisualsTab.EspNames = !VisualsTab.EspNames;
                    if (Btn(bX, bY + 180, "HEALTH BARS", VisualsTab.EspHealth)) VisualsTab.EspHealth = !VisualsTab.EspHealth;
                    if (Btn(bX + 400, bY + 180, "ONLY ENEMIES", VisualsTab.OnlyEnemies)) VisualsTab.OnlyEnemies = !VisualsTab.OnlyEnemies;
                    GUI.Label(new Rect(bX, bY + 245, 300, 30), $"MAX DIST: {VisualsTab.MaxDist}m", headerStyle);
                    if (SmallButton(new Rect(bX + 400, bY + 240, 190, 45), "DIST +50m")) VisualsTab.MaxDist += 50;
                    if (SmallButton(new Rect(bX + 600, bY + 240, 190, 45), "DIST -50m")) VisualsTab.MaxDist -= 50;
                    break;

                case 4: // ITEM & WEAPON SPAWNER TAB
                    Color actCol = new Color(0.37f, 0.42f, 0.22f);
                    Color inactCol = new Color(0.16f, 0.17f, 0.12f);
                    Color primaryCol = new Color(0.28f, 0.38f, 0.42f);
                    Color secondaryCol = new Color(0.34f, 0.43f, 0.2f);
                    Color bothCol = new Color(0.54f, 0.43f, 0.22f);

                    string primAmmo = "", primMag = "", primWeaponID = "";
                    string secAmmo = "", secMag = "", secWeaponID = "";
                    HashSet<string> supportedAtts = new HashSet<string>();

                    // Inspect equipped weapons to highlight compatible magazines/attachments
                    if (_localPlayer != null)
                    {
                        var gun0 = _localPlayer.GetHeldGun(0);
                        if (gun0 != null)
                        {
                            primAmmo = gun0.compatibleAmmo; primMag = GetMagID(gun0);
                            var io = gun0.GetComponent<ItemObject>();
                            if (io != null) primWeaponID = io.item_id;
                            if (gun0.supportedAttachments != null) foreach (var sa in gun0.supportedAttachments) supportedAtts.Add(sa.attachment_id);
                        }
                        var gun1 = _localPlayer.GetHeldGun(1);
                        if (gun1 != null)
                        {
                            secAmmo = gun1.compatibleAmmo; secMag = GetMagID(gun1);
                            var io = gun1.GetComponent<ItemObject>();
                            if (io != null) secWeaponID = io.item_id;
                            if (gun1.supportedAttachments != null) foreach (var sa in gun1.supportedAttachments) supportedAtts.Add(sa.attachment_id);
                        }
                    }

                    // Main Category selection buttons
                    GUI.backgroundColor = (_spawnerCategory == 0) ? actCol : inactCol;
                    if (SmallButton(new Rect(bX, bY, 260, 40), "WEAPONS")) { _spawnerCategory = 0; _currentPage = 0; _subCat = 0; }

                    GUI.backgroundColor = (_spawnerCategory == 1) ? actCol : inactCol;
                    if (SmallButton(new Rect(bX + 265, bY, 260, 40), "VEHICLES")) { _spawnerCategory = 1; _currentPage = 0; _subCat = 0; }

                    GUI.backgroundColor = (_spawnerCategory == 2) ? actCol : inactCol;
                    if (SmallButton(new Rect(bX + 530, bY, 260, 40), "ITEMS")) { _spawnerCategory = 2; _currentPage = 0; _subCat = 0; }

                    List<string> list = new List<string>();
                    int yOffset = 80;

                    // Subcategory logic & filtering
                    if (_spawnerCategory == 0) // WEAPONS (Guns, Ammo, Attachments)
                    {
                        GUI.backgroundColor = (_subCat == 0) ? actCol : inactCol;
                        if (SmallButton(new Rect(bX, bY + 45, 260, 30), "GUNS")) { _subCat = 0; _currentPage = 0; }
                        GUI.backgroundColor = (_subCat == 1) ? actCol : inactCol;
                        if (SmallButton(new Rect(bX + 265, bY + 45, 260, 30), "AMMO")) { _subCat = 1; _currentPage = 0; }
                        GUI.backgroundColor = (_subCat == 2) ? actCol : inactCol;
                        if (SmallButton(new Rect(bX + 530, bY + 45, 260, 30), "ATTACHMENTS")) { _subCat = 2; _currentPage = 0; }

                        if (_subCat == 0) list = Spawner.GetWeaponNames();
                        else if (_subCat == 1)
                        {
                            // Sort compatible magazines to the top
                            List<string> matches = new List<string>(); List<string> others = new List<string>();
                            foreach (var id in Spawner.AmmoNames)
                            {
                                if (IsCompatible(id, primAmmo, primMag, primWeaponID) || IsCompatible(id, secAmmo, secMag, secWeaponID)) matches.Add(id);
                                else others.Add(id);
                            }
                            list.AddRange(matches); list.AddRange(others);
                        }
                        else if (_subCat == 2)
                        {
                            // Sort compatible attachments to the top
                            List<string> matches = new List<string>(); List<string> others = new List<string>();
                            foreach (var id in Spawner.AttachmentNames)
                            {
                                if (supportedAtts.Contains(id)) matches.Add(id);
                                else others.Add(id);
                            }
                            list.AddRange(matches); list.AddRange(others);
                        }
                    }
                    else if (_spawnerCategory == 1) // VEHICLES
                    {
                        yOffset = 50;
                        list = Spawner.GetVehicleNames();
                    }
                    else if (_spawnerCategory == 2) // ITEMS (Grenades, Uniforms, Other)
                    {
                        GUI.backgroundColor = (_subCat == 0) ? actCol : inactCol;
                        if (SmallButton(new Rect(bX, bY + 45, 260, 30), "GRENADES")) { _subCat = 0; _currentPage = 0; }

                        GUI.backgroundColor = (_subCat == 1) ? actCol : inactCol;
                        if (SmallButton(new Rect(bX + 265, bY + 45, 260, 30), "UNIFORMS")) { _subCat = 1; _currentPage = 0; }

                        GUI.backgroundColor = (_subCat == 2) ? actCol : inactCol;
                        if (SmallButton(new Rect(bX + 530, bY + 45, 260, 30), "OTHER")) { _subCat = 2; _currentPage = 0; }

                        if (_subCat == 0) list = Spawner.GrenadeNames;
                        else if (_subCat == 1) list = Spawner.UniformNames;
                        else list = Spawner.OtherNames;
                    }

                    // Render paginated item grid
                    int displayLimit = 12;
                    if (list != null && list.Count > 0)
                    {
                        for (int i = 0; i < displayLimit; i++)
                        {
                            int idx = (_currentPage * displayLimit) + i;
                            if (idx < list.Count)
                            {
                                string currentItem = list[idx];

                                // Check for upgradeable weapons
                                bool isWeaponTab = (_spawnerCategory == 0 && _subCat == 0);
                                bool canBeUpgraded = isWeaponTab && Spawner.MaximizeAttachments && Spawner.UpgradableWeapons.Contains(currentItem);

                                // Check compatibility highlights
                                bool isAmmoTab = (_spawnerCategory == 0 && _subCat == 1);
                                bool isAttTab = (_spawnerCategory == 0 && _subCat == 2);
                                bool matchesPrim = (isAmmoTab && IsCompatible(currentItem, primAmmo, primMag, primWeaponID)) || (isAttTab && supportedAtts.Contains(currentItem));
                                bool matchesSec = (isAmmoTab && IsCompatible(currentItem, secAmmo, secMag, secWeaponID)) || (isAttTab && supportedAtts.Contains(currentItem));

                                if (matchesPrim && matchesSec && isAmmoTab) { GUI.backgroundColor = bothCol; currentItem = "✦ " + currentItem + " [BOTH]"; }
                                else if (matchesPrim) { GUI.backgroundColor = primaryCol; currentItem = "★ " + currentItem + " [MATCH]"; }
                                else if (matchesSec) { GUI.backgroundColor = secondaryCol; currentItem = "✚ " + currentItem + " [MATCH]"; }
                                else if (canBeUpgraded) { GUI.backgroundColor = new Color(0.52f, 0.43f, 0.22f); currentItem = "⚙ " + currentItem + " [UPGRADABLE]"; }
                                else GUI.backgroundColor = new Color(0.16f, 0.17f, 0.12f);

                                if (SmallButton(new Rect(bX, bY + yOffset + (i * 32), 790, 30), currentItem))
                                {
                                    if (_spawnerCategory == 0)
                                    {
                                        if (_subCat == 0) Spawner.SpawnWeapon(list[idx], _localPlayer);
                                        else if (_subCat == 1) Spawner.SpawnItem(list[idx], _localPlayer);
                                        else Spawner.AttachToHeldWeapon(list[idx], _localPlayer);
                                    }
                                    else if (_spawnerCategory == 1) Spawner.SpawnVehicle(list[idx], _localPlayer, this);
                                    else Spawner.SpawnItem(list[idx], _localPlayer);
                                }
                            }
                        }

                        // Bottom configuration toggles (Auto-Equip Attachments & AI Crew)
                        float bottomBtnY = bY + yOffset + (displayLimit * 32) + 5;
                        if (_spawnerCategory == 0 && _subCat == 0)
                        {
                            GUI.backgroundColor = Spawner.MaximizeAttachments ? new Color(0.34f, 0.43f, 0.2f) : inactCol;
                            if (SmallButton(new Rect(bX, bottomBtnY, 790, 30), $"AUTO-EQUIP ALL ATTACHMENTS: {(Spawner.MaximizeAttachments ? "ON" : "OFF")}")) Spawner.MaximizeAttachments = !Spawner.MaximizeAttachments;
                        }
                        else if (_spawnerCategory == 1)
                        {
                            GUI.backgroundColor = Spawner.SpawnWithCrew ? new Color(0.34f, 0.43f, 0.2f) : inactCol;
                            if (SmallButton(new Rect(bX, bottomBtnY, 790, 30), $"SPAWN WITH AI CREW: {(Spawner.SpawnWithCrew ? "ON" : "OFF")}")) Spawner.SpawnWithCrew = !Spawner.SpawnWithCrew;
                        }

                        // Pagination Navigation Buttons
                        GUI.backgroundColor = new Color(0.08f, 0.085f, 0.06f);
                        float navY = bottomBtnY + 40;
                        int tP = Mathf.CeilToInt((float)list.Count / displayLimit);
                        if (tP > 0)
                        {
                            GUI.Label(new Rect(bX + 350, navY, 120, 30), $"Page {_currentPage + 1} / {tP}", headerStyle);
                            if (_currentPage > 0) if (SmallButton(new Rect(bX, navY, 150, 40), "< PREV")) _currentPage--;
                            if ((_currentPage + 1) * displayLimit < list.Count) if (SmallButton(new Rect(bX + 640, navY, 150, 40), "NEXT >")) _currentPage++;
                        }
                    }
                    break;

                case 5: // SETTINGS & KEYBINDINGS TAB
                    if (Btn(bX, bY, $"BIND NOCLIP [{SettingsTab.GetKeyName(SettingsTab.NoclipKey)}]", SettingsTab.IsBindingNoclip)) SettingsTab.IsBindingNoclip = true;
                    if (Btn(bX + 400, bY, $"BIND TELEPORT [{SettingsTab.GetKeyName(SettingsTab.TeleportKey)}]", SettingsTab.IsBindingTeleport)) SettingsTab.IsBindingTeleport = true;
                    if (Btn(bX, bY + 60, $"BIND VEHICLE FLY [{SettingsTab.GetKeyName(SettingsTab.VehFlyKey)}]", SettingsTab.IsBindingVehFly)) SettingsTab.IsBindingVehFly = true;
                    if (Btn(bX + 400, bY + 60, $"BIND MENU KEY [{SettingsTab.GetKeyName(SettingsTab.MenuKey)}]", SettingsTab.IsBindingMenu)) SettingsTab.IsBindingMenu = true;
                    if (Btn(bX, bY + 120, $"BIND QUICK HEAL [{SettingsTab.GetKeyName(SettingsTab.AutoHealKey)}]", SettingsTab.IsBindingAutoHeal)) SettingsTab.IsBindingAutoHeal = true;
                    if (Btn(bX + 400, bY + 120, "CYCLE ESP COLOR", false)) SettingsTab.CycleColor();
                    if (Btn(bX, bY + 180, "TIME: " + (SettingsTab.TimeScale < 1.0f ? "SLOW" : "NORMAL"), SettingsTab.TimeScale < 1.0f)) SettingsTab.TimeScale = (SettingsTab.TimeScale == 1.0f) ? 0.3f : 1.0f;

                    // Toggle Vanilla vs Modded content in the spawner
                    string spawnerMode = Spawner.IncludeModdedWeapons ? "ALL MODS" : "VANILLA ONLY";
                    if (Btn(bX + 400, bY + 180, "SPAWNER CONTENT: " + spawnerMode, Spawner.IncludeModdedWeapons))
                    {
                        Spawner.IncludeModdedWeapons = !Spawner.IncludeModdedWeapons;
                        if (DebugMenuPlugin.ConfigIncludeModdedWeapons != null)
                            DebugMenuPlugin.ConfigIncludeModdedWeapons.Value = Spawner.IncludeModdedWeapons;
                        DebugMenuPlugin.PluginInstance?.Config.Save();
                        Spawner.Reset();
                        _spawnerInitialized = false;
                        _currentPage = 0;
                    }
                    break;

                case 6: // PERFORMANCE / PROFILER TAB
                    PerformanceTab.Draw(new Rect(bX, bY, 800, 500));
                    break;
            }

            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
            GUI.backgroundColor = previousBackground;
        }

        // Helper method to draw toggle buttons with ON/OFF status badges
        bool Btn(float x, float y, string t, bool s)
        {
            GUI.backgroundColor = s ? new Color(0.34f, 0.43f, 0.2f) : new Color(0.16f, 0.17f, 0.12f);
            bool clicked = GUI.Button(new Rect(x, y, 390, 50), t, _buttonStyle);
            if (!IsActionButton(t))
            {
                Rect indicator = new Rect(x + 326, y + 14, 48, 22);
                DrawSolidRect(indicator, s ? new Color(0.52f, 0.58f, 0.28f) : new Color(0.29f, 0.29f, 0.22f));
                GUI.Label(indicator, s ? "ON" : "OFF", _statusStyle);
            }
            return clicked;
        }

        // Identifies buttons that trigger single actions rather than toggle states
        private static bool IsActionButton(string text)
        {
            return text.StartsWith("BIND ")
                || text.StartsWith("TELEPORT")
                || text.StartsWith("DESTROY ")
                || text.StartsWith("CYCLE ")
                || text.StartsWith("TIME:")
                || text.StartsWith("SPAWNER CONTENT:");
        }

        private bool SmallButton(Rect rect, string text)
        {
            return GUI.Button(rect, text, _smallButtonStyle);
        }

        // Lazy initialization of custom IMGUI styles (tactical military aesthetic)
        private void InitializeMenuStyles()
        {
            if (_panelStyle != null) return;

            _panelStyle = new GUIStyle();
            _panelStyle.normal.background = _whiteTex;
            _panelStyle.normal.textColor = Color.white;

            _titleStyle = new GUIStyle();
            _titleStyle.fontSize = 24;
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.richText = true;
            _titleStyle.normal.textColor = new Color(0.86f, 0.82f, 0.67f);

            _subtitleStyle = new GUIStyle();
            _subtitleStyle.fontSize = 12;
            _subtitleStyle.fontStyle = FontStyle.Bold;
            _subtitleStyle.normal.textColor = new Color(0.62f, 0.6f, 0.45f);

            headerStyle = new GUIStyle();
            headerStyle.fontSize = 15;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.alignment = TextAnchor.MiddleCenter;
            headerStyle.richText = true;
            headerStyle.normal.textColor = new Color(0.82f, 0.76f, 0.57f);

            _tabStyle = CreateButtonStyle();
            _tabStyle.fontSize = 13;
            _tabStyle.fontStyle = FontStyle.Bold;
            _tabStyle.alignment = TextAnchor.MiddleLeft;
            _tabStyle.padding.left = 20;
            _tabStyle.padding.right = 8;
            _tabStyle.normal.textColor = new Color(0.78f, 0.75f, 0.6f);
            _tabStyle.hover.textColor = new Color(0.96f, 0.9f, 0.68f);
            _tabStyle.active.textColor = new Color(0.96f, 0.9f, 0.68f);
            _tabStyle.focused.textColor = new Color(0.96f, 0.9f, 0.68f);
            _tabStyle.onNormal.textColor = new Color(0.96f, 0.9f, 0.68f);
            _tabStyle.onHover.textColor = Color.white;
            _tabStyle.onActive.textColor = Color.white;
            _tabStyle.onFocused.textColor = Color.white;

            _buttonStyle = CreateButtonStyle();
            _buttonStyle.fontSize = 13;
            _buttonStyle.fontStyle = FontStyle.Bold;
            _buttonStyle.alignment = TextAnchor.MiddleLeft;
            _buttonStyle.padding.left = 18;
            _buttonStyle.padding.right = 70;
            _buttonStyle.normal.textColor = new Color(0.84f, 0.81f, 0.67f);
            _buttonStyle.hover.textColor = new Color(0.98f, 0.93f, 0.74f);
            _buttonStyle.active.textColor = new Color(0.98f, 0.93f, 0.74f);
            _buttonStyle.focused.textColor = new Color(0.98f, 0.93f, 0.74f);
            _buttonStyle.onNormal.textColor = new Color(0.98f, 0.93f, 0.74f);
            _buttonStyle.onHover.textColor = Color.white;
            _buttonStyle.onActive.textColor = Color.white;
            _buttonStyle.onFocused.textColor = Color.white;

            _smallButtonStyle = CreateButtonStyle();
            _smallButtonStyle.fontSize = 12;
            _smallButtonStyle.fontStyle = FontStyle.Bold;
            _smallButtonStyle.normal.textColor = new Color(0.84f, 0.81f, 0.67f);
            _smallButtonStyle.hover.textColor = new Color(1f, 0.94f, 0.7f);
            _smallButtonStyle.active.textColor = Color.white;
            _smallButtonStyle.focused.textColor = new Color(1f, 0.94f, 0.7f);
            _smallButtonStyle.onNormal.textColor = new Color(0.9f, 0.86f, 0.68f);
            _smallButtonStyle.onHover.textColor = Color.white;
            _smallButtonStyle.onActive.textColor = Color.white;
            _smallButtonStyle.onFocused.textColor = Color.white;

            _statusStyle = new GUIStyle();
            _statusStyle.fontSize = 11;
            _statusStyle.fontStyle = FontStyle.Bold;
            _statusStyle.alignment = TextAnchor.MiddleCenter;
            _statusStyle.normal.textColor = new Color(0.95f, 0.88f, 0.66f);
        }

        private GUIStyle CreateButtonStyle()
        {
            GUIStyle style = new GUIStyle();
            style.normal.background = GUI.skin.button.normal.background;
            style.hover.background = GUI.skin.button.hover.background;
            style.active.background = GUI.skin.button.active.background;
            style.focused.background = GUI.skin.button.focused.background;
            style.alignment = TextAnchor.MiddleCenter;
            return style;
        }

        private void DrawSolidRect(Rect rect, Color color)
        {
            if (_whiteTex == null) CreateTex();
            Color oldColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _whiteTex);
            GUI.color = oldColor;
        }

        /// Smart matching algorithm: Determines if an ammo box or magazine fits the currently held weapon
        /// by stripping prefixes (faction identifiers, common noise) and comparing root weapon names.
        private bool IsCompatible(string itemID, string ammoID, string magID, string weaponID)
        {
            if (string.IsNullOrEmpty(itemID)) return false;

            // 1. Direct match with weapon's internal fields
            if (!string.IsNullOrEmpty(ammoID) && itemID == ammoID) return true;
            if (!string.IsNullOrEmpty(magID) && itemID == magID) return true;

            // 2. Fuzzy substring matching based on item and weapon identifiers
            if (!string.IsNullOrEmpty(weaponID))
            {
                string lowItem = itemID.ToLower();
                string lowWeapon = weaponID.ToLower();

                // Faction and naming prefixes to strip
                string[] noise = { "us_", "ger_", "jap_", "rus_", "uk_", "eng_", "weapon", "infantry", "1918", "m1a1", "mk2", "mk3", "model" };

                string cleanWeapon = lowWeapon;
                foreach (var s in noise) cleanWeapon = cleanWeapon.Replace(s, "");
                cleanWeapon = cleanWeapon.Trim('_');

                string cleanItem = lowItem.Replace("_mag", "").Replace("_clip", "").Replace("_belt", "").Replace("_box", "");
                foreach (var s in noise) cleanItem = cleanItem.Replace(s, "");
                cleanItem = cleanItem.Trim('_');

                if (cleanWeapon.Length > 1 && cleanItem.Length > 1)
                {
                    if (cleanWeapon.Contains(cleanItem) || cleanItem.Contains(cleanWeapon))
                    {
                        if (lowItem.Contains("mag") || lowItem.Contains("clip") || lowItem.Contains("belt") || lowItem.Contains("ammo"))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        // Safely retrieves the magazine item ID from a gun with fallback name stripping
        private string GetMagID(GenericGun gun)
        {
            try
            {
                if (gun == null || gun.currentMagazine == null) return "";
                var io = gun.currentMagazine.GetComponent<ItemObject>();
                if (io != null && !string.IsNullOrEmpty(io.item_id)) return io.item_id;
                return gun.currentMagazine.name.Replace("(Clone)", "").Trim();
            }
            catch { return ""; }
        }
    }
}