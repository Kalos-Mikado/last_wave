using UnityEngine;

namespace LastWave.Config
{
    /// <summary>
    /// Every tunable value for the room and the placeholder player, in one asset.
    /// The asset lives at <c>Assets/Resources/GameConfig.asset</c> and is created by
    /// <c>LastWave.EditorTools.LastWaveSetup.Run</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// int max = GameConfig.Instance.MaxPlayers;
    /// SceneManager.LoadScene(GameConfig.Instance.MenuSceneName);
    /// </code>
    /// </example>
    [CreateAssetMenu(menuName = "Last Wave/Game Config", fileName = "GameConfig")]
    public sealed class GameConfig : ScriptableObject
    {
        /// <summary>Path of the asset under a <c>Resources</c> folder, without extension.</summary>
        public const string ResourcePath = "GameConfig";

        /// <summary>Most players a room accepts, host included (brief: 2-4).</summary>
        [Min(1)] public int MaxPlayers = 4;

        /// <summary>Placeholder walking speed in world units per second.</summary>
        [Min(0f)] public float MoveSpeed = 5f;

        /// <summary>Distance between players' spawn points on the X axis, in world units.</summary>
        [Min(0f)] public float SpawnSpacing = 1.5f;

        /// <summary>One colour per player slot. Red is kept for enemies and blue for the base (brief, Look).</summary>
        public Color[] PlayerColors =
        {
            new Color(0.25f, 0.85f, 0.35f), // green
            new Color(1.00f, 0.85f, 0.20f), // yellow
            new Color(0.70f, 0.40f, 1.00f), // purple
            new Color(1.00f, 0.55f, 0.15f), // orange
        };

        /// <summary>Scene with the room menu. Build index 0.</summary>
        public string MenuSceneName = "Menu";

        /// <summary>Scene the host loads for everyone when pressing Start.</summary>
        public string GameSceneName = "Game";

        private static GameConfig _instance;

        /// <summary>
        /// The loaded config asset. Logs an error and returns null when the asset is missing
        /// (run <c>Last Wave &gt; Build Scenes and Prefabs</c> to create it).
        /// </summary>
        public static GameConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<GameConfig>(ResourcePath);
                    if (_instance == null)
                    {
                        Debug.LogError($"GameConfig not found at Resources/{ResourcePath}. Run 'Last Wave > Build Scenes and Prefabs'.");
                    }
                }
                return _instance;
            }
        }
    }
}
