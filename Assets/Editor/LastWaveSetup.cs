using System.IO;
using System.Reflection;
using LastWave.Config;
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
            var playerPrefab = BuildPlayerPrefab(material);
            var networkManagerPrefab = BuildNetworkManagerPrefab(playerPrefab);

            var menuPath = BuildScene(config.MenuSceneName, networkManagerPrefab);
            var gamePath = BuildScene(config.GameSceneName, networkManagerPrefab);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(menuPath, true),
                new EditorBuildSettingsScene(gamePath, true),
            };

            AssetDatabase.SaveAssets();
            AssertPlayerPrefabHash();
            Debug.Log($"LastWaveSetup: built {PlayerPrefabPath}, {NetworkManagerPrefabPath}, {menuPath}, {gamePath}.");
        }

        private static GameConfig LoadOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null) return config;
            config = ScriptableObject.CreateInstance<GameConfig>();
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

        private static GameObject BuildPlayerPrefab(Material material)
        {
            var player = new GameObject("Player");
            player.AddComponent<SpriteRenderer>().sharedMaterial = material; // sprite is assigned at spawn
            player.AddComponent<NetworkObject>();
            var networkTransform = player.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            // 2D top-down and players never rotate: sync X/Y position only.
            networkTransform.SyncPositionZ = false;
            networkTransform.SyncRotAngleX = false;
            networkTransform.SyncRotAngleY = false;
            networkTransform.SyncRotAngleZ = false;
            player.AddComponent<PlayerMovement>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            Object.DestroyImmediate(player);

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

        private static void AssertPlayerPrefabHash()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            uint hash = prefab.GetComponent<NetworkObject>().PrefabIdHash;
            if (hash == 0) throw new System.InvalidOperationException($"{PlayerPrefabPath} has GlobalObjectIdHash 0; Netcode cannot spawn it.");
            Debug.Log($"LastWaveSetup: {PlayerPrefabPath} GlobalObjectIdHash = {hash}.");
        }

        private static GameObject BuildNetworkManagerPrefab(GameObject playerPrefab)
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
            root.AddComponent<RoomService>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, NetworkManagerPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static string BuildScene(string sceneName, GameObject networkManagerPrefab)
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

            var path = $"{ScenesFolder}/{sceneName}.unity";
            if (!EditorSceneManager.SaveScene(scene, path)) throw new System.InvalidOperationException($"Could not save {path}.");
            return path;
        }
    }
}
