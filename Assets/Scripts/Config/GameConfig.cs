using UnityEngine;

namespace LastWave.Config
{
    /// <summary>Map edges an enemy can spawn on. Combine values to spawn from several edges at once.</summary>
    [System.Flags]
    public enum SpawnEdges
    {
        /// <summary>No edge set; treated as all four edges.</summary>
        None = 0,
        /// <summary>The top edge of the map.</summary>
        Top = 1,
        /// <summary>The bottom edge of the map.</summary>
        Bottom = 2,
        /// <summary>The left edge of the map.</summary>
        Left = 4,
        /// <summary>The right edge of the map.</summary>
        Right = 8,
    }

    /// <summary>One row of the wave table in <see cref="GameConfig.Waves"/>.</summary>
    [System.Serializable]
    public sealed class WaveDefinition
    {
        /// <summary>Enemies in the wave.</summary>
        [Min(1)] public int Count = 1;

        /// <summary>Enemy walking speed in world units per second.</summary>
        [Min(0f)] public float Speed = 1.5f;

        /// <summary>Enemy health in points.</summary>
        [Min(1)] public int Health = 20;

        /// <summary>Edges the enemies spawn on; they are shared out evenly.</summary>
        public SpawnEdges Edges = SpawnEdges.Top;

        /// <summary>Seconds between two enemies of this wave appearing.</summary>
        [Min(0f)] public float SpawnInterval = 1f;
    }

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

        // ---- Story 002: shooting, health ----
        // Defaults below are the values used when an older GameConfig.asset lacks these fields
        // (Unity keeps C# field initialisers for fields missing from the serialized asset).

        /// <summary>Health every player starts with (server writes it; whole points).</summary>
        [Min(1)] public int MaxPlayerHealth = 100;

        /// <summary>Seconds between two shots from one player (server enforces it).</summary>
        [Min(0.01f)] public float FireInterval = 0.25f;

        /// <summary>
        /// Slack the server grants on <see cref="FireInterval"/>, as a fraction of it, so network jitter
        /// that bunches two requests together does not drop a legitimate shot. Average rate stays capped.
        /// </summary>
        [Range(0f, 1f)] public float FireIntervalTolerance = 0.5f;

        /// <summary>Bullet speed in world units per second.</summary>
        [Min(0f)] public float BulletSpeed = 14f;

        /// <summary>Damage one bullet deals (story 003 applies it to enemies).</summary>
        [Min(0)] public int BulletDamage = 10;

        /// <summary>Seconds before a bullet despawns on its own.</summary>
        [Min(0.01f)] public float BulletLifetime = 1.5f;

        /// <summary>Distance from the player centre, along the aim direction, where a bullet appears.</summary>
        [Min(0f)] public float BulletSpawnOffset = 0.7f;

        /// <summary>Colour of every bullet. Not a player colour, red (enemies) or blue (base).</summary>
        public Color BulletColor = new Color(1f, 1f, 1f);

        /// <summary>Colour of the small marker on a player that shows the aim direction.</summary>
        public Color AimMarkerColor = new Color(0.1f, 0.1f, 0.1f);

        /// <summary>Health bar size in screen pixels (width, height).</summary>
        public Vector2 HealthBarSize = new Vector2(60f, 8f);

        /// <summary>How far above the player centre the health bar sits, in world units.</summary>
        public float HealthBarOffsetY = 0.9f;

        /// <summary>Health bar fill colour.</summary>
        public Color HealthBarFillColor = new Color(0.25f, 0.9f, 0.3f);

        /// <summary>Health bar background colour.</summary>
        public Color HealthBarBackColor = new Color(0f, 0f, 0f, 0.7f);

        /// <summary>
        /// DEBUG: on the host, pressing this key deals <see cref="BulletDamage"/> to the host's own player,
        /// so the server-side damage path can be seen on every machine before enemies exist (story 003).
        /// </summary>
        public UnityEngine.InputSystem.Key DebugDamageKey = UnityEngine.InputSystem.Key.K;

        // ---- Story 003: enemy waves ----
        // Like the story-002 block, these defaults apply when an older GameConfig.asset lacks the fields.

        /// <summary>Seconds from the game scene starting to wave 1 (brief: about 20).</summary>
        [Min(0f)] public float FirstWaveDelaySeconds = 20f;

        /// <summary>Seconds of rest after a wave is cleared, before the next one (guessed: 10).</summary>
        [Min(0f)] public float WaveRestSeconds = 10f;

        /// <summary>Half width and half height of the play area, in world units; enemies spawn on its edges.</summary>
        public Vector2 MapHalfExtents = new Vector2(10f, 5.5f);

        /// <summary>Enemies walk towards the base point and stop this far from it, in world units.</summary>
        [Min(0f)] public float EnemyStopDistance = 1.2f;

        /// <summary>Enemy colour. Red is reserved for enemies (brief, Look).</summary>
        public Color EnemyColor = new Color(0.9f, 0.15f, 0.15f);

        /// <summary>Colour of the placeholder base marker. Story 004 turns the marker into the real base. Blue is reserved for the base.</summary>
        public Color BaseColor = new Color(0.2f, 0.45f, 1f);

        /// <summary>Font size of the "Wave n / total" label, in pixels.</summary>
        [Min(8)] public int WaveLabelFontSize = 28;

        /// <summary>
        /// One row per wave, in order; the row count is the number of waves (brief: 10).
        /// Wave 1 is weak and from one edge, waves 3-4 come from several edges, later waves are faster and bigger.
        /// Bullet damage is 10, so Health 20 is two hits.
        /// </summary>
        public WaveDefinition[] Waves =
        {
            new WaveDefinition { Count = 4,  Speed = 1.5f, Health = 20, Edges = SpawnEdges.Top,                                                 SpawnInterval = 1.0f },
            new WaveDefinition { Count = 6,  Speed = 1.6f, Health = 20, Edges = SpawnEdges.Left,                                                SpawnInterval = 1.0f },
            new WaveDefinition { Count = 10, Speed = 1.8f, Health = 20, Edges = SpawnEdges.Top | SpawnEdges.Left | SpawnEdges.Right,            SpawnInterval = 0.8f },
            new WaveDefinition { Count = 12, Speed = 1.9f, Health = 30, Edges = SpawnEdges.Top | SpawnEdges.Bottom | SpawnEdges.Left | SpawnEdges.Right, SpawnInterval = 0.8f },
            new WaveDefinition { Count = 15, Speed = 2.1f, Health = 30, Edges = SpawnEdges.Top | SpawnEdges.Bottom | SpawnEdges.Left | SpawnEdges.Right, SpawnInterval = 0.7f },
            new WaveDefinition { Count = 18, Speed = 2.3f, Health = 30, Edges = SpawnEdges.Top | SpawnEdges.Bottom | SpawnEdges.Left | SpawnEdges.Right, SpawnInterval = 0.6f },
            new WaveDefinition { Count = 22, Speed = 2.5f, Health = 40, Edges = SpawnEdges.Top | SpawnEdges.Bottom | SpawnEdges.Left | SpawnEdges.Right, SpawnInterval = 0.5f },
            new WaveDefinition { Count = 26, Speed = 2.7f, Health = 40, Edges = SpawnEdges.Top | SpawnEdges.Bottom | SpawnEdges.Left | SpawnEdges.Right, SpawnInterval = 0.5f },
            new WaveDefinition { Count = 30, Speed = 2.9f, Health = 50, Edges = SpawnEdges.Top | SpawnEdges.Bottom | SpawnEdges.Left | SpawnEdges.Right, SpawnInterval = 0.4f },
            new WaveDefinition { Count = 36, Speed = 3.1f, Health = 50, Edges = SpawnEdges.Top | SpawnEdges.Bottom | SpawnEdges.Left | SpawnEdges.Right, SpawnInterval = 0.4f },
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
