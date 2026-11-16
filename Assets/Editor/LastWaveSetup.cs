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
        private const string PlayerSheetAPath = "Assets/Art/Player/player-a.png"; // story 013: idle, walk, shoot, repair
        private const string PlayerSheetBPath = "Assets/Art/Player/player-b.png"; // story 013: hurt, down, death, get up
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
            foreach (var path in new[] { PlayerSheetAPath, EnemyNormalSheetPath, EnemyMiniBossSheetPath, EnemyBossSheetPath })
            {
                EnsureReadable(path);
            }
            foreach (var path in BaseTexturePaths) EnsureReadable(path);
            AssignUiArt(config);
            SetGameIcon();
            var bulletPrefab = BuildBulletPrefab(material);
            var prefabList = RegisterNetworkPrefab(bulletPrefab);
            var enemyPrefab = BuildEnemyPrefab(material);
            RegisterNetworkPrefab(enemyPrefab);
            var playerPrefab = BuildPlayerPrefab(material, bulletPrefab);
            var networkManagerPrefab = BuildNetworkManagerPrefab(playerPrefab, prefabList);

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
            EditorUtility.SetDirty(config);
        }

        // Story 016: Player Settings > Icon > Default Icon (NamedBuildTarget.Unknown is the default for every platform),
        // which Windows uses for LastWave.exe. Saved into ProjectSettings.asset.
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
            var sheetA = AssetDatabase.LoadAssetAtPath<Texture2D>(PlayerSheetAPath);
            var sheetB = AssetDatabase.LoadAssetAtPath<Texture2D>(PlayerSheetBPath);
            if (sheetA == null || sheetB == null) throw new System.InvalidOperationException($"Player sheets not found at {PlayerSheetAPath} / {PlayerSheetBPath}.");

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

            var animator = player.AddComponent<PlayerAnimator>();
            var animatorSerialized = new SerializedObject(animator);
            animatorSerialized.FindProperty("_renderer").objectReferenceValue = visualRenderer;
            animatorSerialized.FindProperty("_sheetA").objectReferenceValue = sheetA;
            animatorSerialized.FindProperty("_sheetB").objectReferenceValue = sheetB;
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

        private static GameObject BuildNetworkManagerPrefab(GameObject playerPrefab, NetworkPrefabsList prefabList)
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
            root.AddComponent<RoomService>();
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
