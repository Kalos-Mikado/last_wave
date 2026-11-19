using System.IO;
using System.Reflection;
using LastWave.Base;
using LastWave.Config;
using LastWave.Enemies;
using LastWave.Flow;
using LastWave.Map;
using LastWave.Network;
using LastWave.Player;
using LastWave.UI;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LastWave.EditorTools
{
    /// <summary>
    /// Builds the story-001 assets so no scene or prefab YAML is hand-written:
    /// the GameConfig asset, an unlit sprite material, the Player and NetworkManager
    /// prefabs, the Menu and Game scenes, and the Build Settings scene list.
    /// Safe to re-run: prefabs and scenes are rebuilt, the config asset and material are kept.
    /// </summary>
    /// <example>
    /// Menu: <c>Last Wave &gt; Build Scenes and Prefabs</c>, or batchmode:
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath Multi -executeMethod LastWave.EditorTools.LastWaveSetup.Run -logFile -
    /// </code>
    /// </example>
    public static class LastWaveSetup
    {
        private const string ConfigPath = "Assets/Resources/GameConfig.asset";
        private const string MaterialPath = "Assets/Materials/PlayerUnlit.mat";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string BulletPrefabPath = "Assets/Prefabs/Bullet.prefab";
        private const string DefaultPrefabListPath = "Assets/DefaultNetworkPrefabs.asset"; // created by Netcode's importer
        private const float BulletColliderRadius = 0.125f; // world units, same hit size as the old 0.25 square bullet; asset construction, not gameplay tuning
        private const string BulletTexturePath = "Assets/Art/bullet.png";               // story 014
        private const string BulletImpactTexturePath = "Assets/Art/bullet-impact.png";  // story 014
        private const string EnemyPrefabPath = "Assets/Prefabs/Enemy.prefab";
        private const string CharacterSelectionPrefabPath = "Assets/Prefabs/CharacterSelection.prefab"; // story 023
        private const string EnemyNormalSheetPath = "Assets/Art/Enemies/Map1/basic.png";      // story 014
        private const string EnemyMiniBossSheetPath = "Assets/Art/Enemies/Map1/miniboss.png"; // story 014
        private const string EnemyBossSheetPath = "Assets/Art/Enemies/Map1/boss.png";         // story 014
        private static readonly string[] BaseTexturePaths =   // story 015: intact, damaged, destroyed
        {
            "Assets/Art/Base/Map1/base-intact.png",
            "Assets/Art/Base/Map1/base-damaged.png",
            "Assets/Art/Base/Map1/base-destroyed.png",
        };
        private const string Map1TexturePath = "Assets/Art/Maps/map1.png"; // story 015
        private const string PlayerBodySheetPath = "Assets/Art/Player/player-body.png"; // story 019: idle, walk, hurt (no arms), repair
        private const string PlayerSheetBPath = "Assets/Art/Player/player-b.png"; // story 013: (unused), down, death, get up
        private const string PlayerArmTexturePath = "Assets/Art/Player/player-arms-rifle.png"; // story 019: arm + rifle, turns at the shoulder
        private const string MuzzleFlashTexturePath = "Assets/Art/Player/muzzle-flash.png";    // story 019: 2 x 2 flash frames
        private const string GunPreviewFolder = "../production/qa/evidence/story-019-separate-gun"; // story 019: offline composite check
        private const string PlayerArtFolder = "Assets/Art/Player"; // story 020: character sets live in <ArtFolder> subfolders
        private const string ColourPreviewFolder = "../production/qa/evidence/story-020-player-colour-sprites"; // story 020
        private const string MenuBackgroundTexturePath = "Assets/Art/UI/menu-background.png"; // story 016
        private const string MenuLogoTexturePath = "Assets/Art/UI/logo-name.png";             // story 016
        private const string ButtonTexturePath = "Assets/Art/UI/button.png";                  // story 016
        private const string ButtonHoverTexturePath = "Assets/Art/UI/button-hover-state.png"; // story 016
        private const string ButtonPressedTexturePath = "Assets/Art/UI/button-pressed-state.png"; // story 016
        private const string PanelTexturePath = "Assets/Art/UI/dialog-panel.png";             // story 016
        private const string GameIconTexturePath = "Assets/Art/UI/game-icon.png";             // story 016: the exe icon
        private const string PlayerBarTexturePath = "Assets/Art/UI/player-health-bar.png";    // story 017
        private const string BaseBarTexturePath = "Assets/Art/UI/base-health-bar.png";        // story 017
        private const string BossBarTexturePath = "Assets/Art/UI/boss-health-bar.png";        // story 017
        private const string WavePlateTexturePath = "Assets/Art/UI/wave.png";                 // story 017 (the story's "wave-counter.png")
        private const string RoomPlateTexturePath = "Assets/Art/UI/room-info.png";            // story 017
        private const string HudIconsTexturePath = "Assets/Art/UI/hud-icons.png";             // story 017
        private const string WinBannerTexturePath = "Assets/Art/UI/banner-VICTORY.png";       // story 018
        private const string LoseBannerTexturePath = "Assets/Art/UI/banner-DEFEAT.png";       // story 018
        private const string CardFrameTexturePath = "Assets/Art/UI/Upgrades/frame-blue.png";  // story 018
        private const string CardFrameHoverTexturePath = "Assets/Art/UI/Upgrades/frame-gold.png"; // story 018
        private const string UpgradeIconsTexturePath = "Assets/Art/UI/Upgrades/icon-upgrade.png"; // story 018
        private const string SliderTrackTexturePath = "Assets/Art/UI/slider-track.png";     // story 021, optional
        private const string SliderKnobTexturePath = "Assets/Art/UI/slider-knob.png";       // story 021, optional
        private const string CheckboxOffTexturePath = "Assets/Art/UI/checkbox-off.png";     // story 021, optional
        private const string CheckboxOnTexturePath = "Assets/Art/UI/checkbox-on.png";       // story 021, optional
        private const string ArrowLeftTexturePath = "Assets/Art/UI/arrow-left.png";         // story 021, optional
        private const string ArrowRightTexturePath = "Assets/Art/UI/arrow-right.png";       // story 021, optional
        private const string NetworkManagerPrefabPath = "Assets/Prefabs/NetworkManager.prefab";
        private const string ScenesFolder = "Assets/Scenes";
        private const string UnlitSpriteShader = "Universal Render Pipeline/2D/Sprite-Unlit-Default";

        /// <summary>Creates or rebuilds every asset listed on the class. Throws on failure (non-zero batchmode exit).</summary>
        [MenuItem("Last Wave/Build Scenes and Prefabs")]
        public static void Run()
        {
            foreach (var folder in new[] { "Assets/Resources", "Assets/Materials", "Assets/Prefabs", ScenesFolder })
            {
                Directory.CreateDirectory(folder);
            }
            AssetDatabase.Refresh();

            var config = LoadOrCreateConfig();
            var material = LoadOrCreateMaterial();
            // Story 015: hitboxes are traced from these images at runtime, which needs their pixels on the CPU.
            // Story 019: the arm's tight sprite mesh is built from its alpha at runtime, which needs Read/Write too.
            foreach (var path in new[] { PlayerBodySheetPath, PlayerArmTexturePath, EnemyNormalSheetPath, EnemyMiniBossSheetPath, EnemyBossSheetPath })
            {
                EnsureReadable(path);
            }
            foreach (var path in BaseTexturePaths) EnsureReadable(path);
            AssignUiArt(config);
            AssignCharacterArt(config);
            SetGameIcon();
            var bulletPrefab = BuildBulletPrefab(material);
            var prefabList = RegisterNetworkPrefab(bulletPrefab);
            var enemyPrefab = BuildEnemyPrefab(material);
            RegisterNetworkPrefab(enemyPrefab);
            var playerPrefab = BuildPlayerPrefab(material, bulletPrefab);
            // Story 023: the server spawns this in the lobby (RoomService); it must be a registered network prefab.
            var selectionObject = new GameObject("CharacterSelection");
            selectionObject.AddComponent<NetworkObject>();
            selectionObject.AddComponent<CharacterSelection>();
            var selectionPrefab = SaveNetworkPrefab(selectionObject, CharacterSelectionPrefabPath);
            RegisterNetworkPrefab(selectionPrefab);
            var networkManagerPrefab = BuildNetworkManagerPrefab(playerPrefab, prefabList, selectionPrefab.GetComponent<CharacterSelection>());

            var menuPath = BuildScene(config.MenuSceneName, networkManagerPrefab, null, null);
            var gamePath = BuildScene(config.GameSceneName, networkManagerPrefab, enemyPrefab.GetComponent<Enemy>(), material);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(menuPath, true),
                new EditorBuildSettingsScene(gamePath, true),
            };

            AssetDatabase.SaveAssets();
            AssertPrefabHash(PlayerPrefabPath);
            AssertPrefabHash(BulletPrefabPath);
            AssertPrefabHash(EnemyPrefabPath);
            AssertPrefabHash(CharacterSelectionPrefabPath);
            Debug.Log($"LastWaveSetup: built {BulletPrefabPath}, {EnemyPrefabPath}, {PlayerPrefabPath}, {NetworkManagerPrefabPath}, {menuPath}, {gamePath}.");
        }

        private static GameConfig LoadOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null)
            {
                // An asset saved before new fields existed loads them from the C# defaults; write them back.
                EditorUtility.SetDirty(config);
                return config;
            }
            config =ScriptableObject.CreateInstance<GameConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        // Story 016 (and 017's HUD art, 018's end banners and cards): the menu, skin and HUD pictures UiSkin draws. The trimmed ones are cut to their opaque pixels at runtime,
        // which needs their pixels on the CPU (Read/Write).
        private static void AssignUiArt(GameConfig config)
        {
            foreach (var path in new[] { MenuLogoTexturePath, ButtonTexturePath, ButtonHoverTexturePath, ButtonPressedTexturePath, PanelTexturePath,
                PlayerBarTexturePath, BaseBarTexturePath, BossBarTexturePath, WavePlateTexturePath, RoomPlateTexturePath, HudIconsTexturePath,
                WinBannerTexturePath, LoseBannerTexturePath, CardFrameTexturePath, CardFrameHoverTexturePath, UpgradeIconsTexturePath })
            {
                EnsureReadable(path);
            }
            config.MenuBackgroundTexture = LoadTexture(MenuBackgroundTexturePath);
            config.MenuLogoTexture = LoadTexture(MenuLogoTexturePath);
            config.UiButtonTexture = LoadTexture(ButtonTexturePath);
            config.UiButtonHoverTexture = LoadTexture(ButtonHoverTexturePath);
            config.UiButtonPressedTexture = LoadTexture(ButtonPressedTexturePath);
            config.UiPanelTexture = LoadTexture(PanelTexturePath);
            // Story 017: HUD frames, plates and the icon sheet, all trimmed or cut at runtime (hence Read/Write above).
            config.HudPlayerBarTexture = LoadTexture(PlayerBarTexturePath);
            config.HudBaseBarTexture = LoadTexture(BaseBarTexturePath);
            config.HudBossBarTexture = LoadTexture(BossBarTexturePath);
            config.HudWavePlateTexture = LoadTexture(WavePlateTexturePath);
            config.HudRoomPlateTexture = LoadTexture(RoomPlateTexturePath);
            config.HudIconsTexture = LoadTexture(HudIconsTexturePath);
            // Story 018: end banners, card frames and the upgrade icon sheet, trimmed or cut at runtime (Read/Write above).
            config.EndWinBannerTexture = LoadTexture(WinBannerTexturePath);
            config.EndLoseBannerTexture = LoadTexture(LoseBannerTexturePath);
            config.UpgradeFrameTexture = LoadTexture(CardFrameTexturePath);
            config.UpgradeFrameHoverTexture = LoadTexture(CardFrameHoverTexturePath);
            config.UpgradeIconsTexture = LoadTexture(UpgradeIconsTexturePath);
            // Story 021: optional Setting control art. Each slot is filled only when its file exists; otherwise the slot
            // is left as it is (empty = UiSkin's fallback), with no warning.
            config.UiSliderTrackTexture = LoadOptionalTexture(SliderTrackTexturePath, config.UiSliderTrackTexture);
            config.UiSliderKnobTexture = LoadOptionalTexture(SliderKnobTexturePath, config.UiSliderKnobTexture);
            config.UiCheckboxOffTexture = LoadOptionalTexture(CheckboxOffTexturePath, config.UiCheckboxOffTexture);
            config.UiCheckboxOnTexture = LoadOptionalTexture(CheckboxOnTexturePath, config.UiCheckboxOnTexture);
            config.UiArrowLeftTexture = LoadOptionalTexture(ArrowLeftTexturePath, config.UiArrowLeftTexture);
            config.UiArrowRightTexture = LoadOptionalTexture(ArrowRightTexturePath, config.UiArrowRightTexture);
            EditorUtility.SetDirty(config);
        }

        // Story 021: the texture at path made Read/Write (UiSkin trims it), or current when no texture is there.
        private static Texture2D LoadOptionalTexture(string path, Texture2D current)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter)) return current;
            EnsureReadable(path);
            return LoadTexture(path);
        }

        // Story 016: Player Settings > Icon > Default Icon (NamedBuildTarget.Unknown is the default for every platform),
        // which Windows uses for LastWave.exe. Saved into ProjectSettings.asset.
        // Story 020: fills each character set's EMPTY texture references from Assets/Art/Player/<ArtFolder>/ (a reference
        // set by hand is kept; extra sets added in the asset are filled too). A missing file is a warning, not a failure: the
        // game falls back to the white set for that slot. The arm gets Read/Write like the white one (tight mesh, story 019);
        // the body does not need it because the hitbox is traced from the white body. Sizes are checked against the white set.
        private static void AssignCharacterArt(GameConfig config)
        {
            if (config.Characters == null) return;
            var whiteBody = LoadTexture(PlayerBodySheetPath);
            var whiteArm = LoadTexture(PlayerArmTexturePath);
            var whiteB = LoadTexture(PlayerSheetBPath);
            foreach (var set in config.Characters)
            {
                if (set == null || string.IsNullOrEmpty(set.ArtFolder)) continue;
                string folder = $"{PlayerArtFolder}/{set.ArtFolder}";
                string armPath = $"{folder}/player-arms-rifle-{set.ArtFolder}.png";
                string readablePath = set.ArmTexture != null ? AssetDatabase.GetAssetPath(set.ArmTexture) : armPath;
                if (AssetImporter.GetAtPath(readablePath) is TextureImporter) EnsureReadable(readablePath);
                set.BodySheet = FillCharacterTexture(set, set.BodySheet, $"{folder}/player-body-{set.ArtFolder}.png", whiteBody);
                set.ArmTexture = FillCharacterTexture(set, set.ArmTexture, armPath, whiteArm);
                set.SheetB = FillCharacterTexture(set, set.SheetB, $"{folder}/player-b-{set.ArtFolder}.png", whiteB);
            }
            EditorUtility.SetDirty(config);
        }

        private static Texture2D FillCharacterTexture(CharacterDefinition set, Texture2D current, string path, Texture2D white)
        {
            var texture = current != null ? current : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                Debug.LogWarning($"LastWaveSetup: character '{set.DisplayName}' has no {path}; that slot will use the white set.");
                return null;
            }
            string size = $"{texture.width}x{texture.height}";
            if (texture.width != white.width || texture.height != white.height)
            {
                Debug.LogWarning($"LastWaveSetup: character '{set.DisplayName}' {texture.name} is {size}, the white {white.name} is " +
                                 $"{white.width}x{white.height}; frames, shoulder and muzzle pixels will not line up.");
            }
            else
            {
                Debug.Log($"LastWaveSetup: character '{set.DisplayName}' {texture.name} {size} (matches {white.name}).");
            }
            return texture;
        }

        private static void SetGameIcon()
        {
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { LoadTexture(GameIconTexturePath) }, IconKind.Any);
        }

        private static Material LoadOrCreateMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;
            var shader = Shader.Find(UnlitSpriteShader);
            if (shader == null) throw new System.InvalidOperationException($"Shader '{UnlitSpriteShader}' not found (is URP installed?).");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        private static GameObject BuildBulletPrefab(Material material)
        {
            var bulletTexture = LoadTexture(BulletTexturePath);
            var impactTexture = LoadTexture(BulletImpactTexturePath);

            var bullet = new GameObject("Bullet");
            bullet.AddComponent<SpriteRenderer>().sharedMaterial = material; // sprite is assigned at spawn; size comes from config (pixels per unit)
            var body = bullet.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic; // moved by velocity on the server, placed by NetworkTransform elsewhere
            body.gravityScale = 0f;
            // Kinematic bodies only get trigger callbacks against kinematic/static colliders with full kinematic contacts on.
            body.useFullKinematicContacts = true;
            var collider = bullet.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = BulletColliderRadius;
            bullet.AddComponent<NetworkObject>();
            var networkTransform = bullet.AddComponent<NetworkTransform>(); // default AuthorityMode = Server
            networkTransform.SyncPositionZ = false;
            networkTransform.SyncRotAngleX = false;
            networkTransform.SyncRotAngleY = false;
            networkTransform.SyncRotAngleZ = false; // a bullet flies straight; rotation comes with the spawn
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;
            var bulletScript = bullet.AddComponent<Bullet>();
            var bulletSerialized = new SerializedObject(bulletScript);
            bulletSerialized.FindProperty("_bulletTexture").objectReferenceValue = bulletTexture;
            bulletSerialized.FindProperty("_impactTexture").objectReferenceValue = impactTexture;
            bulletSerialized.ApplyModifiedPropertiesWithoutUndo();
            return SaveNetworkPrefab(bullet, BulletPrefabPath);
        }

        /// <summary>
        /// Story 019 offline check: logs the arm piece's opaque bounds, shoulder-cap centre and muzzle tip (image pixels,
        /// top-left origin), then draws body idle frame 1 (and walk frame 1) with the arm placed by the same math as the game
        /// (<see cref="PlayerShooting.ShoulderLocal"/>, <see cref="PlayerShooting.MuzzleLocal"/>) at several aims, on the
        /// CPU, and writes one PNG to <c>production/qa/evidence/story-019-separate-gun/</c>. A red dot marks the muzzle.
        /// </summary>
        [MenuItem("Last Wave/Story 019 Gun Preview")]
        public static void PreviewGun()
        {
            foreach (var path in new[] { PlayerBodySheetPath, PlayerArmTexturePath, MuzzleFlashTexturePath }) EnsureReadable(path);
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            var body = LoadTexture(PlayerBodySheetPath);
            var armTex = LoadTexture(PlayerArmTexturePath);
            var flashTex = LoadTexture(MuzzleFlashTexturePath);
            Color32[] bodyPx = body.GetPixels32(), armPx = armTex.GetPixels32(), flashPx = flashTex.GetPixels32();
            int aw = armTex.width, ah = armTex.height;

            // Measure the arm: opaque bounds, the cap (leftmost 30 columns) centre, the rightmost column's middle.
            int minX = aw, maxX = -1, minY = ah, maxY = -1;
            for (int y = 0; y < ah; y++)
                for (int x = 0; x < aw; x++)
                    if (armPx[y * aw + x].a > 128) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
            double capX = 0, capY = 0; int capN = 0; double tipY = 0; int tipN = 0;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= Mathf.Min(maxX, minX + 30); x++)
                    if (armPx[y * aw + x].a > 128) { capX += x; capY += y; capN++; }
                for (int x = Mathf.Max(minX, maxX - 2); x <= maxX; x++)
                    if (armPx[y * aw + x].a > 128) { tipY += y; tipN++; }
            }
            // Texture rows count from the bottom; report from the top like the config.
            Debug.Log($"GunPreview: arm {aw}x{ah} opaque x {minX}-{maxX}, y(top) {ah - 1 - maxY}-{ah - 1 - minY}; " +
                      $"cap centre ({capX / capN:F1}, {ah - 1 - capY / capN:F1}); muzzle tip ({maxX}, {ah - 1 - tipY / tipN:F1}).");

            int fw = flashTex.width;
            for (int fc = 0; fc < 4; fc++)
            {
                int cx0 = (fc % 2) * fw / 2, cyTop = (fc / 2) * flashTex.height / 2, n = 0; int fx0 = fw, fx1 = -1;
                for (int y = flashTex.height - cyTop - flashTex.height / 2; y < flashTex.height - cyTop; y++)
                    for (int x = cx0; x < cx0 + fw / 2; x++)
                        if (flashPx[y * fw + x].a > 128) { n++; fx0 = Mathf.Min(fx0, x - cx0); fx1 = Mathf.Max(fx1, x - cx0); }
                Debug.Log($"GunPreview: flash cell {fc} opaque px {n}, x {fx0}-{fx1}");
            }

            // One tile per pose: (body row, aim degrees). 1 output pixel = 1 body pixel.
            var poses = new (int row, float aim, string label)[]
            {
                (0, 0f, "idle straight"), (0, 45f, "idle up45"), (0, 90f, "idle up (clamped)"), (0, -45f, "idle down45"),
                (0, 180f, "idle left"), (0, 150f, "idle up-left"), (1, 0f, "walk straight"), (2, 20f, "hurt"),
            };
            int cell = config.PlayerSheetCellPixels, tile = 320;
            float bodyPpu = config.PlayerSpritePixelsPerUnit, armScale = config.PlayerArmPixelsPerUnit / bodyPpu;
            var outTex = new Texture2D(tile * poses.Length, tile, TextureFormat.RGBA32, false);
            var outPx = new Color32[outTex.width * outTex.height];
            for (int i = 0; i < outPx.Length; i++) outPx[i] = new Color32(70, 90, 70, 255);
            Vector2 shoulderArm = config.PlayerArmShoulderPixel;
            for (int p = 0; p < poses.Length; p++)
            {
                var pose = poses[p];
                bool facingRight = Mathf.Cos(pose.aim * Mathf.Deg2Rad) >= 0f;
                // Root at tile centre; world -> tile pixels: x * bodyPpu, y * bodyPpu.
                float ox = p * tile + tile * 0.5f, oy = tile * 0.5f;
                for (int y = 0; y < tile; y++)
                {
                    for (int x = 0; x < tile; x++)
                    {
                        float wx = (p * tile + x + 0.5f - ox) / bodyPpu, wy = (y + 0.5f - oy) / bodyPpu;
                        // Body: cell pivot bottom centre at (0, PlayerSpriteOffsetY), mirrored when facing left.
                        float bx = (facingRight ? wx : -wx) * bodyPpu + cell * 0.5f;
                        float by = (wy - config.PlayerSpriteOffsetY) * bodyPpu; // from the cell bottom
                        Color32 c = outPx[y * outTex.width + p * tile + x];
                        if (bx >= 0 && bx < cell && by >= 0 && by < cell)
                        {
                            int sx = (int)bx, sy = body.height - (pose.row + 1) * cell + (int)by;
                            c = Over(c, bodyPx[sy * body.width + sx]);
                        }
                        // Arm: inverse of translate(shoulder) * rotate(rotZ) * scale(+-1, 1).
                        Vector2 sh = PlayerShooting.ShoulderLocal(config, facingRight, pose.row);
                        float rz = PlayerShooting.ArmRotationZ(PlayerShooting.ArmLocalAngle(pose.aim, facingRight, config.PlayerArmClampDegrees), facingRight) * Mathf.Deg2Rad;
                        float dx = wx - sh.x, dy = wy - sh.y;
                        float lx = dx * Mathf.Cos(-rz) - dy * Mathf.Sin(-rz), ly = dx * Mathf.Sin(-rz) + dy * Mathf.Cos(-rz);
                        if (!facingRight) lx = -lx;
                        int ax = (int)(shoulderArm.x + lx * config.PlayerArmPixelsPerUnit);
                        int ayTop = (int)(shoulderArm.y - ly * config.PlayerArmPixelsPerUnit);
                        if (ax >= 0 && ax < aw && ayTop >= 0 && ayTop < ah) c = Over(c, armPx[(ah - 1 - ayTop) * aw + ax]);
                        outPx[y * outTex.width + p * tile + x] = c;
                    }
                }
                bool fr = Mathf.Cos(pose.aim * Mathf.Deg2Rad) >= 0f;
                Vector2 muzzle = PlayerShooting.MuzzleLocal(config, pose.aim, fr, pose.row);
                int mx = (int)(ox + muzzle.x * bodyPpu), my = (int)(oy + muzzle.y * bodyPpu);
                for (int y = my - 2; y <= my + 2; y++)
                    for (int x = mx - 2; x <= mx + 2; x++)
                        if (x >= p * tile && x < (p + 1) * tile && y >= 0 && y < tile) outPx[y * outTex.width + x] = new Color32(255, 0, 0, 255);
                Debug.Log($"GunPreview: tile {p} = {pose.label} (aim {pose.aim}, facing {(fr ? "right" : "left")})");
            }
            outTex.SetPixels32(outPx);
            Directory.CreateDirectory(GunPreviewFolder);
            string file = Path.Combine(GunPreviewFolder, "offline-composite.png");
            File.WriteAllBytes(file, outTex.EncodeToPNG());
            Object.DestroyImmediate(outTex);
            Debug.Log($"GunPreview: wrote {Path.GetFullPath(file)} (armScale {armScale:F2} body px per arm px).");
        }

        /// <summary>
        /// Story 020 offline check: one column per picture set (white, then <see cref="GameConfig.Characters"/>); top row idle
        /// frame 1 with the arm at aim 0 (same math as <see cref="PreviewGun"/>), bottom row the lying downed frame with
        /// <see cref="GameConfig.DownedTint"/>. Logs each arm's opaque bounds to show the sets line up. Writes
        /// <c>production/qa/evidence/story-020-player-colour-sprites/offline-colours.png</c>. Run after <see cref="Run"/>.
        /// </summary>
        [MenuItem("Last Wave/Story 020 Colour Preview")]
        public static void PreviewColours()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            var sets = new System.Collections.Generic.List<(string name, Texture2D body, Texture2D arm, Texture2D b)>
            {
                ("White", LoadTexture(PlayerBodySheetPath), LoadTexture(PlayerArmTexturePath), LoadTexture(PlayerSheetBPath)),
            };
            foreach (var c in config.Characters)
            {
                if (c.BodySheet == null || c.ArmTexture == null || c.SheetB == null) throw new System.InvalidOperationException($"ColourPreview: set {c.DisplayName} incomplete.");
                sets.Add((c.DisplayName, c.BodySheet, c.ArmTexture, c.SheetB));
            }

            int cell = config.PlayerSheetCellPixels, tile = 320;
            float ppu = config.PlayerSpritePixelsPerUnit;
            var outTex = new Texture2D(tile * sets.Count, tile * 2, TextureFormat.RGBA32, false);
            var outPx = new Color32[outTex.width * outTex.height];
            for (int i = 0; i < outPx.Length; i++) outPx[i] = new Color32(70, 90, 70, 255);
            Vector2 sh = PlayerShooting.ShoulderLocal(config, true, 0);
            float rz = PlayerShooting.ArmRotationZ(PlayerShooting.ArmLocalAngle(0f, true, config.PlayerArmClampDegrees), true) * Mathf.Deg2Rad;
            int downCol = Mathf.Max(0, sets[0].b.width / cell - 1);

            for (int s = 0; s < sets.Count; s++)
            {
                var set = sets[s];
                Color32[] bodyPx = ReadPixelsForPreview(set.body), armPx = ReadPixelsForPreview(set.arm), bPx = ReadPixelsForPreview(set.b);
                int aw = set.arm.width, ah = set.arm.height;
                int minX = aw, maxX = -1, minY = ah, maxY = -1;
                for (int y = 0; y < ah; y++)
                    for (int x = 0; x < aw; x++)
                        if (armPx[y * aw + x].a > 128) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
                Debug.Log($"ColourPreview: {set.name} body {set.body.width}x{set.body.height}, b {set.b.width}x{set.b.height}, " +
                          $"arm {aw}x{ah} opaque x {minX}-{maxX}, y(top) {ah - 1 - maxY}-{ah - 1 - minY}");

                for (int row = 0; row < 2; row++)
                {
                    int ty0 = (1 - row) * tile; // top row = idle
                    float ox = s * tile + tile * 0.5f, oy = ty0 + tile * 0.5f;
                    for (int y = ty0; y < ty0 + tile; y++)
                    {
                        for (int x = s * tile; x < (s + 1) * tile; x++)
                        {
                            float wx = (x + 0.5f - ox) / ppu, wy = (y + 0.5f - oy) / ppu;
                            float bx = wx * ppu + cell * 0.5f, by = (wy - config.PlayerSpriteOffsetY) * ppu;
                            Color32 c = outPx[y * outTex.width + x];
                            if (bx >= 0 && bx < cell && by >= 0 && by < cell)
                            {
                                if (row == 0)
                                {
                                    int sx = (int)bx, sy = set.body.height - cell + (int)by; // body row 0, column 0
                                    c = Over(c, bodyPx[sy * set.body.width + sx]);
                                }
                                else
                                {
                                    int sx = downCol * cell + (int)bx, sy = set.b.height - 2 * cell + (int)by; // sheet B row 1
                                    Color32 p = bPx[sy * set.b.width + sx];
                                    Color t = config.DownedTint;
                                    p = new Color32((byte)(p.r * t.r), (byte)(p.g * t.g), (byte)(p.b * t.b), p.a);
                                    c = Over(c, p);
                                }
                            }
                            if (row == 0)
                            {
                                float dx = wx - sh.x, dy = wy - sh.y;
                                float lx = dx * Mathf.Cos(-rz) - dy * Mathf.Sin(-rz), ly = dx * Mathf.Sin(-rz) + dy * Mathf.Cos(-rz);
                                int ax = (int)(config.PlayerArmShoulderPixel.x + lx * config.PlayerArmPixelsPerUnit);
                                int ayTop = (int)(config.PlayerArmShoulderPixel.y - ly * config.PlayerArmPixelsPerUnit);
                                if (ax >= 0 && ax < aw && ayTop >= 0 && ayTop < ah) c = Over(c, armPx[(ah - 1 - ayTop) * aw + ax]);
                            }
                            outPx[y * outTex.width + x] = c;
                        }
                    }
                }
            }
            outTex.SetPixels32(outPx);
            Directory.CreateDirectory(ColourPreviewFolder);
            string file = Path.Combine(ColourPreviewFolder, "offline-colours.png");
            File.WriteAllBytes(file, outTex.EncodeToPNG());
            Object.DestroyImmediate(outTex);
            Debug.Log($"ColourPreview: wrote {Path.GetFullPath(file)} (columns: {string.Join(", ", sets.ConvertAll(x => x.name))}).");
        }

        // Pixels of any texture, readable or not: a non-readable one is copied through the GPU (editor preview only).
        private static Color32[] ReadPixelsForPreview(Texture2D texture)
        {
            if (texture.isReadable) return texture.GetPixels32();
            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, false);
            copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            var pixels = copy.GetPixels32();
            Object.DestroyImmediate(copy);
            return pixels;
        }

        private static Color32 Over(Color32 under, Color32 over)
        {
            float a = over.a / 255f;
            return new Color32((byte)(under.r + (over.r - under.r) * a), (byte)(under.g + (over.g - under.g) * a),
                (byte)(under.b + (over.b - under.b) * a), 255);
        }

        // Turns on Read/Write for an image so SpriteHitbox can trace its outline at runtime (story 015). Re-imports only when it changes.
        private static void EnsureReadable(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new System.InvalidOperationException($"Texture not found at {path}.");
            if (importer.isReadable) return;
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        private static Texture2D LoadTexture(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) throw new System.InvalidOperationException($"Texture not found at {path}.");
            return texture;
        }

        private static GameObject BuildEnemyPrefab(Material material)
        {
            var normalSheet = LoadTexture(EnemyNormalSheetPath);
            var miniBossSheet = LoadTexture(EnemyMiniBossSheetPath);
            var bossSheet = LoadTexture(EnemyBossSheetPath);

            // Root scale 1: the server's Size multiplier (2, 3 for the bosses) is the only scaling, applied by Enemy.ApplyLook.
            var enemy = new GameObject("Enemy");
            var body = enemy.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic; // moved by velocity on the server, placed by NetworkTransform elsewhere
            // Smooth between 50 Hz physics steps: without it the host sees big enemies stutter at high frame rates.
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.gravityScale = 0f;
            // Solid (not a trigger) so bullets register the hit. Story 015: a polygon traced from the kind's idle sprite by
            // EnemyAnimator at spawn (empty here). About 1 unit wide, wider than the ~0.3 a bullet moves per physics step.
            enemy.AddComponent<PolygonCollider2D>().pathCount = 0;
            enemy.AddComponent<NetworkObject>();
            var networkTransform = enemy.AddComponent<NetworkTransform>(); // default AuthorityMode = Server
            networkTransform.SyncPositionZ = false;
            networkTransform.SyncRotAngleX = false;
            networkTransform.SyncRotAngleY = false;
            networkTransform.SyncRotAngleZ = false; // enemies do not turn
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;
            enemy.AddComponent<Enemy>();

            // The alien is a child so its pivot (feet) can sit at the bottom of the collider; sprites are cut at runtime (SpriteSheet).
            var visual = new GameObject("Visual");
            visual.transform.SetParent(enemy.transform, false);
            var visualRenderer = visual.AddComponent<SpriteRenderer>();
            visualRenderer.sharedMaterial = material;

            var animator = enemy.AddComponent<EnemyAnimator>();
            var animatorSerialized = new SerializedObject(animator);
            animatorSerialized.FindProperty("_renderer").objectReferenceValue = visualRenderer;
            animatorSerialized.FindProperty("_normalSheet").objectReferenceValue = normalSheet;
            animatorSerialized.FindProperty("_miniBossSheet").objectReferenceValue = miniBossSheet;
            animatorSerialized.FindProperty("_bossSheet").objectReferenceValue = bossSheet;
            animatorSerialized.ApplyModifiedPropertiesWithoutUndo();
            return SaveNetworkPrefab(enemy, EnemyPrefabPath);
        }

        private static GameObject BuildPlayerPrefab(Material material, GameObject bulletPrefab)
        {
            var bodySheet = LoadTexture(PlayerBodySheetPath);
            var sheetB = LoadTexture(PlayerSheetBPath);
            var armTexture = LoadTexture(PlayerArmTexturePath);
            var flashTexture = LoadTexture(MuzzleFlashTexturePath);

            var player = new GameObject("Player");
            var body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f; // top-down
            body.freezeRotation = true; // the body never turns: the sprite is flipped instead (story 013)
            // Story 015: a polygon traced from the soldier's idle sprite by PlayerAnimator at spawn (empty here).
            player.AddComponent<PolygonCollider2D>().pathCount = 0;
            player.AddComponent<NetworkObject>();
            var networkTransform = player.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            // 2D top-down: sync X/Y position only (the body no longer rotates; facing is a NetworkVariable).
            networkTransform.SyncPositionZ = false;
            networkTransform.SyncRotAngleX = false;
            networkTransform.SyncRotAngleY = false;
            networkTransform.SyncRotAngleZ = false;

            // The soldier is a child so its pivot (feet) can sit at the bottom of the collider; sprites are cut at runtime (SpriteSheet).
            var visual = new GameObject("Visual");
            visual.transform.SetParent(player.transform, false);
            var visualRenderer = visual.AddComponent<SpriteRenderer>();
            visualRenderer.sharedMaterial = material;

            // Story 019: the arm + rifle turns at the shoulder (placed, rotated and shown by PlayerAnimator); the muzzle
            // flash is its child, so it turns with the rifle.
            var arm = new GameObject("Arm");
            arm.transform.SetParent(player.transform, false);
            var armRenderer = arm.AddComponent<SpriteRenderer>();
            armRenderer.sharedMaterial = material;
            armRenderer.enabled = false;
            var flash = new GameObject("MuzzleFlash");
            flash.transform.SetParent(arm.transform, false);
            var flashRenderer = flash.AddComponent<SpriteRenderer>();
            flashRenderer.sharedMaterial = material;
            flashRenderer.enabled = false;

            var animator = player.AddComponent<PlayerAnimator>();
            var animatorSerialized = new SerializedObject(animator);
            animatorSerialized.FindProperty("_renderer").objectReferenceValue = visualRenderer;
            animatorSerialized.FindProperty("_bodySheet").objectReferenceValue = bodySheet;
            animatorSerialized.FindProperty("_sheetB").objectReferenceValue = sheetB;
            animatorSerialized.FindProperty("_armRenderer").objectReferenceValue = armRenderer;
            animatorSerialized.FindProperty("_flashRenderer").objectReferenceValue = flashRenderer;
            animatorSerialized.FindProperty("_armTexture").objectReferenceValue = armTexture;
            animatorSerialized.FindProperty("_flashTexture").objectReferenceValue = flashTexture;
            animatorSerialized.ApplyModifiedPropertiesWithoutUndo();

            player.AddComponent<PlayerMovement>();

            player.AddComponent<PlayerHealth>();
            player.AddComponent<PlayerLife>(); // story 005: downed, revive, death, respawn
            player.AddComponent<PlayerRepair>();
            player.AddComponent<PlayerUpgrades>(); // story 012: upgrade levels and the card offer

            var shooting = player.AddComponent<PlayerShooting>();
            var shootingSerialized = new SerializedObject(shooting);
            shootingSerialized.FindProperty("_bulletPrefab").objectReferenceValue = bulletPrefab.GetComponent<Bullet>();
            shootingSerialized.ApplyModifiedPropertiesWithoutUndo();

            return SaveNetworkPrefab(player, PlayerPrefabPath);
        }

        private static GameObject SaveNetworkPrefab(GameObject instance, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);

            // Netcode computes GlobalObjectIdHash only in NetworkObject.OnValidate (internal), from the
            // object's GlobalObjectId. On the scene-only GameObject above that id is null, so the hash
            // stayed 0, and Unity did not re-run OnValidate on the saved asset in batchmode. Run the
            // package's own OnValidate on the prefab ASSET (now an imported-asset id); it marks the
            // asset dirty when the hash changes, and Run's SaveAssets writes it.
            var onValidate = typeof(NetworkObject).GetMethod("OnValidate",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (onValidate == null) throw new System.InvalidOperationException("NetworkObject.OnValidate not found in this Netcode version.");
            onValidate.Invoke(prefab.GetComponent<NetworkObject>(), null);
            // SaveAssets alone left the computed hash only in memory (YAML still 0): save the prefab asset explicitly.
            PrefabUtility.SavePrefabAsset(prefab);
            return prefab;
        }

        private static void AssertPrefabHash(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            uint hash = prefab.GetComponent<NetworkObject>().PrefabIdHash;
            if (hash == 0) throw new System.InvalidOperationException($"{path} has GlobalObjectIdHash 0; Netcode cannot spawn it.");
            Debug.Log($"LastWaveSetup: {path} GlobalObjectIdHash = {hash}.");
        }

        // The bullet is spawned by the server from PlayerShooting, so clients must know it: it has to be
        // in a NetworkPrefabsList that the NetworkManager uses. Netcode's own importer normally adds it to
        // the default list; this makes sure, and returns the list for the NetworkManager prefab.
        private static NetworkPrefabsList RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(DefaultPrefabListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, DefaultPrefabListPath);
            }
            if (!list.Contains(prefab))
            {
                list.Add(new NetworkPrefab { Prefab = prefab });
                EditorUtility.SetDirty(list);
            }
            return list;
        }

        private static GameObject BuildNetworkManagerPrefab(GameObject playerPrefab, NetworkPrefabsList prefabList, CharacterSelection selectionPrefab)
        {
            var root = new GameObject("NetworkManager");
            var transport = root.AddComponent<UnityTransport>();
            var networkManager = root.AddComponent<NetworkManager>();
            networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = playerPrefab,
                EnableSceneManagement = true,
            };
            networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabList);
            var roomService = root.AddComponent<RoomService>();
            var roomSerialized = new SerializedObject(roomService);
            roomSerialized.FindProperty("_characterSelectionPrefab").objectReferenceValue = selectionPrefab;
            roomSerialized.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, NetworkManagerPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // enemyPrefab and material are given for the Game scene only (they add the wave manager and base marker).
        private static string BuildScene(string sceneName, GameObject networkManagerPrefab, Enemy enemyPrefab, Material material)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 6f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.12f, 0.15f);
            cameraObject.AddComponent<AudioListener>();

            var menu = new GameObject("RoomMenu").AddComponent<RoomMenu>();
            var serialized = new SerializedObject(menu);
            serialized.FindProperty("_networkManagerPrefab").objectReferenceValue =
                networkManagerPrefab.GetComponent<RoomService>();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (enemyPrefab != null)
            {
                // The base: BaseHealth (scene object, no NetworkObject) owns its health, look and bar (story 004).
                var baseMarker = new GameObject("Base");
                var baseConfig = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
                baseMarker.transform.position = baseConfig.BasePosition; // far left, vertically centred (story 009)
                // Solid, static hitbox shaped like the base image: players cannot walk onto it (story 015). BaseHealth traces
                // one polygon per state at runtime and swaps it with the picture; empty here.
                baseMarker.AddComponent<PolygonCollider2D>().pathCount = 0;

                // Story 015: the image lives on a child so the Base object keeps scale 1 and position BasePosition;
                // BaseHealth sets the child's height (ground line) and sprite from config.
                var baseVisual = new GameObject("Visual");
                baseVisual.transform.SetParent(baseMarker.transform, false);
                var baseRenderer = baseVisual.AddComponent<SpriteRenderer>();
                baseRenderer.sharedMaterial = material;
                baseRenderer.sortingOrder = -1; // under players and enemies, above the map (-100)

                var baseHealth = baseMarker.AddComponent<BaseHealth>();
                var baseSerialized = new SerializedObject(baseHealth);
                baseSerialized.FindProperty("_renderer").objectReferenceValue = baseRenderer;
                var baseTextures = baseSerialized.FindProperty("_textures");
                baseTextures.arraySize = BaseTexturePaths.Length;
                for (int i = 0; i < BaseTexturePaths.Length; i++)
                {
                    baseTextures.GetArrayElementAtIndex(i).objectReferenceValue = LoadTexture(BaseTexturePaths[i]);
                }
                baseSerialized.ApplyModifiedPropertiesWithoutUndo();

                // Story 015: the map picture behind everything (Game scene only; the Menu scene has no map).
                var mapObject = new GameObject("Map");
                mapObject.transform.position = Vector3.zero;
                var mapRenderer = mapObject.AddComponent<SpriteRenderer>();
                mapRenderer.sharedMaterial = material;
                var mapBackground = mapObject.AddComponent<MapBackground>();
                var mapSerialized = new SerializedObject(mapBackground);
                mapSerialized.FindProperty("_texture").objectReferenceValue = LoadTexture(Map1TexturePath);
                mapSerialized.ApplyModifiedPropertiesWithoutUndo();

                // Story 015: four static walls (no Rigidbody2D) on the rock bands and the view edges; MapWalls sizes and
                // places them at runtime from the fitted map, so they follow a resized window.
                var wallsObject = new GameObject("Walls");
                wallsObject.transform.position = Vector3.zero;
                var walls = wallsObject.AddComponent<MapWalls>();
                var wallsSerialized = new SerializedObject(walls);
                wallsSerialized.FindProperty("_map").objectReferenceValue = mapBackground;
                foreach (var side in new[] { "Top", "Bottom", "Left", "Right" })
                {
                    var wall = new GameObject($"Wall{side}");
                    wall.transform.SetParent(wallsObject.transform, false);
                    wallsSerialized.FindProperty($"_{side.ToLowerInvariant()}").objectReferenceValue = wall.AddComponent<BoxCollider2D>();
                }
                wallsSerialized.ApplyModifiedPropertiesWithoutUndo();

                var waves = new GameObject("WaveManager").AddComponent<WaveManager>();
                var wavesSerialized = new SerializedObject(waves);
                wavesSerialized.FindProperty("_enemyPrefab").objectReferenceValue = enemyPrefab;
                wavesSerialized.FindProperty("_basePoint").objectReferenceValue = baseMarker.transform;
                // _baseMarker is left empty since story 015: BaseHealth owns the base look; WaveManager must not paint it.
                wavesSerialized.ApplyModifiedPropertiesWithoutUndo();

                // Story 005: the "everyone is dead" lose condition (scene object, no NetworkObject).
                new GameObject("TeamDefeat").AddComponent<TeamDefeat>();

                // Story 007: the one place that decides win or lose, and the screens it shows (scene objects, no NetworkObject).
                new GameObject("GameOutcome").AddComponent<GameOutcome>();
                new GameObject("EndScreen").AddComponent<EndScreen>();

                // Story 012: the upgrade cards shown during a wave rest (scene object, no NetworkObject).
                new GameObject("UpgradeCards").AddComponent<UpgradeCards>();
            }

            var path = $"{ScenesFolder}/{sceneName}.unity";
            if (!EditorSceneManager.SaveScene(scene, path)) throw new System.InvalidOperationException($"Could not save {path}.");
            return path;
        }
    }
}
