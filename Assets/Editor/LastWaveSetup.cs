using System.IO;
using System.Reflection;
using LastWave.Config;
using LastWave.Enemies;
using LastWave.Network;
using LastWave.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
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
        private const float BulletVisualSize = 0.25f; // world units; asset construction, not gameplay tuning
        private const string EnemyPrefabPath = "Assets/Prefabs/Enemy.prefab";
        private const float EnemyVisualSize = 0.8f; // world units; asset construction, not gameplay tuning
        private const float BaseVisualSize = 1.5f;
        private const float AimMarkerOffsetX = 0.5f;
        private static readonly Vector2 AimMarkerSize = new Vector2(0.4f, 0.2f);
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
            var bullet = new GameObject("Bullet");
            bullet.transform.localScale = new Vector3(BulletVisualSize, BulletVisualSize, 1f);
            bullet.AddComponent<SpriteRenderer>().sharedMaterial = material; // sprite is assigned at spawn
            var body = bullet.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic; // moved by velocity on the server, placed by NetworkTransform elsewhere
            body.gravityScale = 0f;
            // Kinematic bodies only get trigger callbacks against kinematic/static colliders with full kinematic contacts on.
            body.useFullKinematicContacts = true;
            var collider = bullet.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.5f; // half the unit sprite; the transform scale shrinks it with the visual
            bullet.AddComponent<NetworkObject>();
            var networkTransform = bullet.AddComponent<NetworkTransform>(); // default AuthorityMode = Server
            networkTransform.SyncPositionZ = false;
            networkTransform.SyncRotAngleX = false;
            networkTransform.SyncRotAngleY = false;
            networkTransform.SyncRotAngleZ = false; // a bullet flies straight; rotation comes with the spawn
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;
            bullet.AddComponent<Bullet>();
            return SaveNetworkPrefab(bullet, BulletPrefabPath);
        }

        private static GameObject BuildEnemyPrefab(Material material)
        {
            var enemy = new GameObject("Enemy");
            // Collider 0.8 units wide: well above the ~0.3 a bullet (0.25 wide, 14 units/s) can tunnel through.
            enemy.transform.localScale = new Vector3(EnemyVisualSize, EnemyVisualSize, 1f);
            enemy.AddComponent<SpriteRenderer>().sharedMaterial = material; // sprite and colour are set at spawn
            var body = enemy.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic; // moved by velocity on the server, placed by NetworkTransform elsewhere
            body.gravityScale = 0f;
            enemy.AddComponent<BoxCollider2D>().size = Vector2.one; // solid (not a trigger) so bullets register the hit
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
            return SaveNetworkPrefab(enemy, EnemyPrefabPath);
        }

        private static GameObject BuildPlayerPrefab(Material material, GameObject bulletPrefab)
        {
            var player = new GameObject("Player");
            player.AddComponent<SpriteRenderer>().sharedMaterial = material; // sprite is assigned at spawn
            var body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f; // top-down
            body.freezeRotation = true; // aim rotation is set by code, not by collisions
            player.AddComponent<BoxCollider2D>().size = Vector2.one;
            player.AddComponent<NetworkObject>();
            var networkTransform = player.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            // 2D top-down: sync X/Y position and the Z rotation (aim) only.
            networkTransform.SyncPositionZ = false;
            networkTransform.SyncRotAngleX = false;
            networkTransform.SyncRotAngleY = false;
            networkTransform.SyncRotAngleZ = true;

            var marker = new GameObject("AimMarker");
            marker.transform.SetParent(player.transform, false);
            marker.transform.localPosition = new Vector3(AimMarkerOffsetX, 0f, 0f);
            marker.transform.localScale = new Vector3(AimMarkerSize.x, AimMarkerSize.y, 1f);
            var markerRenderer = marker.AddComponent<SpriteRenderer>();
            markerRenderer.sharedMaterial = material;
            markerRenderer.sortingOrder = 1;

            var movement = player.AddComponent<PlayerMovement>();
            var movementSerialized = new SerializedObject(movement);
            movementSerialized.FindProperty("_aimMarker").objectReferenceValue = markerRenderer;
            movementSerialized.ApplyModifiedPropertiesWithoutUndo();

            player.AddComponent<PlayerHealth>();

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
                // Story 004 turns this marker into the base with health and repair.
                var baseMarker = new GameObject("Base");
                baseMarker.transform.position = Vector3.zero; // map centre
                baseMarker.transform.localScale = new Vector3(BaseVisualSize, BaseVisualSize, 1f);
                var baseRenderer = baseMarker.AddComponent<SpriteRenderer>(); // sprite and colour are set by WaveManager at start
                baseRenderer.sharedMaterial = material;
                baseRenderer.sortingOrder = -1; // under players and enemies

                var waves = new GameObject("WaveManager").AddComponent<WaveManager>();
                var wavesSerialized = new SerializedObject(waves);
                wavesSerialized.FindProperty("_enemyPrefab").objectReferenceValue = enemyPrefab;
                wavesSerialized.FindProperty("_basePoint").objectReferenceValue = baseMarker.transform;
                wavesSerialized.FindProperty("_baseMarker").objectReferenceValue = baseRenderer;
                wavesSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var path = $"{ScenesFolder}/{sceneName}.unity";
            if (!EditorSceneManager.SaveScene(scene, path)) throw new System.InvalidOperationException($"Could not save {path}.");
            return path;
        }
    }
}
