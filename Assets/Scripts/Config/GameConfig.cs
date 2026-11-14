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

    /// <summary>What an <see cref="Enemies.Enemy"/> is. Boss kinds get a health bar everyone sees (story 006).</summary>
    public enum EnemyKind : byte
    {
        /// <summary>An ordinary wave enemy: no bar.</summary>
        Normal = 0,
        /// <summary>A mini-boss. It does not end anything when it dies.</summary>
        MiniBoss = 1,
        /// <summary>The boss. Its death is the team's win (the rest of its wave is despawned).</summary>
        Boss = 2,
    }

    /// <summary>
    /// One boss entry in <see cref="GameConfig.Bosses"/>: an extra enemy that spawns alongside the normal
    /// enemies of one wave. The wave row still defines the normal enemies, which act as its escorts.
    /// </summary>
    [System.Serializable]
    public sealed class BossDefinition
    {
        /// <summary>1-based wave this boss joins.</summary>
        [Min(1)] public int Wave = 5;

        /// <summary><see cref="EnemyKind.MiniBoss"/> or <see cref="EnemyKind.Boss"/> (only the Boss ends the game).</summary>
        public EnemyKind Kind = EnemyKind.MiniBoss;

        /// <summary>Size as a multiple of a normal enemy (1 = same size).</summary>
        [Min(0.1f)] public float Size = 2f;

        /// <summary>Health in points.</summary>
        [Min(1)] public int Health = 300;

        /// <summary>Walking speed in world units per second.</summary>
        [Min(0f)] public float Speed = 1.2f;

        /// <summary>Damage per hit to a standing player it touches (whole points).</summary>
        [Min(0)] public int PlayerDamage = 20;

        /// <summary>Damage per hit to the base while it stands there (whole points).</summary>
        [Min(0)] public int BaseDamage = 25;

        /// <summary>Edge it spawns on, in the middle of that edge. If several are set the first (Top, Bottom, Left, Right) is used; None counts as Top.</summary>
        public SpawnEdges Edge = SpawnEdges.Top;
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

        /// <summary>A normal enemy starts chasing the nearest standing player within this distance, in world units (story 010, guessed: 4).</summary>
        [Min(0f)] public float EnemySightRange = 4f;

        /// <summary>A chasing enemy gives up and returns to the base when its player is farther than this, in world units. Keep it above <see cref="EnemySightRange"/> (guessed: 7).</summary>
        [Min(0f)] public float EnemyGiveUpRange = 7f;

        /// <summary>When true, a mini-boss and the boss also chase players; when false they go for the base only (guessed: false).</summary>
        public bool MiniBossAndBossChasePlayers = false;

        /// <summary>Enemy colour. Red is reserved for enemies (brief, Look).</summary>
        public Color EnemyColor = new Color(0.9f, 0.15f, 0.15f);

        /// <summary>Colour of the placeholder base marker. Story 004 turns the marker into the real base. Blue is reserved for the base.</summary>
        public Color BaseColor = new Color(0.2f, 0.45f, 1f);

        /// <summary>Font size of the "Wave n / total" label, in pixels.</summary>
        [Min(8)] public int WaveLabelFontSize = 28;

        /// <summary>
        /// One row per wave, in order; the row count is the number of waves (brief: 10).
        /// Every wave spawns on the right edge (lane layout, story 008); later waves are faster and bigger.
        /// Bullet damage is 10, so Health 20 is two hits.
        /// </summary>
        public WaveDefinition[] Waves =
        {
            new WaveDefinition { Count = 4,  Speed = 1.5f, Health = 20, Edges = SpawnEdges.Right,                                                 SpawnInterval = 1.0f },
            new WaveDefinition { Count = 6,  Speed = 1.6f, Health = 20, Edges = SpawnEdges.Right,                                                SpawnInterval = 1.0f },
            new WaveDefinition { Count = 10, Speed = 1.8f, Health = 20, Edges = SpawnEdges.Right,            SpawnInterval = 0.8f },
            new WaveDefinition { Count = 12, Speed = 1.9f, Health = 30, Edges = SpawnEdges.Right, SpawnInterval = 0.8f },
            new WaveDefinition { Count = 15, Speed = 2.1f, Health = 30, Edges = SpawnEdges.Right, SpawnInterval = 0.7f },
            new WaveDefinition { Count = 18, Speed = 2.3f, Health = 30, Edges = SpawnEdges.Right, SpawnInterval = 0.6f },
            new WaveDefinition { Count = 22, Speed = 2.5f, Health = 40, Edges = SpawnEdges.Right, SpawnInterval = 0.5f },
            new WaveDefinition { Count = 26, Speed = 2.7f, Health = 40, Edges = SpawnEdges.Right, SpawnInterval = 0.5f },
            new WaveDefinition { Count = 30, Speed = 2.9f, Health = 50, Edges = SpawnEdges.Right, SpawnInterval = 0.4f },
            new WaveDefinition { Count = 36, Speed = 3.1f, Health = 50, Edges = SpawnEdges.Right, SpawnInterval = 0.4f },
        };

        // ---- Story 004: base health and repair ----
        // Like the earlier blocks, these defaults apply when an older GameConfig.asset lacks the fields.
        // All numbers are first guesses for the designer to tune (brief gives none).

        /// <summary>Base health at the start (whole points).</summary>
        [Min(1)] public int BaseMaxHealth = 500;

        /// <summary>Damage one enemy deals to the base per hit (whole points).</summary>
        [Min(0)] public int EnemyBaseDamage = 5;

        /// <summary>Seconds between two hits from the same enemy while it stands at the base. The first hit comes this long after it arrives.</summary>
        [Min(0.05f)] public float EnemyBaseHitInterval = 1.5f;

        /// <summary>Base health restored per second to each player who is repairing (server applies it).</summary>
        [Min(0f)] public float RepairPerSecond = 15f;

        /// <summary>A player must be within this distance of the base centre to repair it, in world units.</summary>
        [Min(0f)] public float RepairRange = 3f;

        /// <summary>Key the player holds to repair the base.</summary>
        public UnityEngine.InputSystem.Key RepairKey = UnityEngine.InputSystem.Key.E;

        /// <summary>Base colour at 0 health. The base blends from <see cref="BaseColor"/> (full health) to this as health drops: darker, red-tinted blue.</summary>
        public Color BaseDamagedColor = new Color(0.30f, 0.08f, 0.22f);

        /// <summary>Base health bar size in screen pixels (width, height).</summary>
        public Vector2 BaseBarSize = new Vector2(160f, 12f);

        /// <summary>How far above the base centre the bar sits, in world units.</summary>
        public float BaseBarOffsetY = 1.2f;

        /// <summary>Base health bar fill colour.</summary>
        public Color BaseBarFillColor = new Color(0.35f, 0.65f, 1f);

        // ---- Story 005: downed, revive, death, respawn, enemy contact damage ----
        // Like the earlier blocks, these defaults apply when an older GameConfig.asset lacks the fields.
        // Every number is a first guess for the designer to tune, except the 10-20% revive range (brief).

        /// <summary>Seconds a friend must hold the revive key (same key as repair) next to a downed player (guessed: 3).</summary>
        [Min(0.1f)] public float ReviveHoldSeconds = 3f;

        /// <summary>A reviver must be within this distance of the downed player, centre to centre, in world units (guessed: 2).</summary>
        [Min(0f)] public float ReviveRange = 2f;

        /// <summary>
        /// Fraction of max health a revived player stands up with. The brief says 10-20%: the game clamps this
        /// to 0.10-0.20 whatever is written here (default 0.15).
        /// </summary>
        [Range(0.10f, 0.20f)] public float ReviveHealthFraction = 0.15f;

        /// <summary>Seconds a downed player waits for a revive before dying (guessed: 15).</summary>
        [Min(0.1f)] public float DownedTimeoutSeconds = 15f;

        /// <summary>Fraction of max health a dead player respawns with at the end of a wave (guessed: 1 = full).</summary>
        [Range(0f, 1f)] public float RespawnHealthFraction = 1f;

        // ---- Story 009: base on the left, players spawn near it ----

        /// <summary>
        /// Centre of the base in world units (brief feature 9): inside the left map edge, vertically centred.
        /// The scene's Base object is placed here by LastWaveSetup; read it from here, never hardcode it.
        /// </summary>
        public Vector2 BasePosition = new Vector2(-8.5f, 0f);

        /// <summary>
        /// Where players start, and where a dead player respawns and everyone returns after Restart, relative to
        /// <see cref="BasePosition"/>, in world units (guessed: just to the right of the base). Players are spread
        /// along Y by <see cref="SpawnSpacing"/> by slot so they do not stack (the lane is horizontal).
        /// </summary>
        public Vector2 PlayerSpawnOffset = new Vector2(2f, 0f);

        /// <summary>
        /// The one spawn formula (story 009): base position + <see cref="PlayerSpawnOffset"/> + vertical spread by
        /// slot (client id modulo <see cref="MaxPlayers"/>), centred on the spawn point. Used for start, respawn and Restart.
        /// </summary>
        /// <param name="ownerClientId">Network client id of the player's owner.</param>
        public Vector2 PlayerSpawnPosition(ulong ownerClientId)
        {
            int slot = (int)(ownerClientId % (ulong)MaxPlayers);
            float centre = (MaxPlayers - 1) * 0.5f;
            return BasePosition + PlayerSpawnOffset + Vector2.up * ((slot - centre) * SpawnSpacing);
        }

        /// <summary>Multiplied into a downed player's body colour (dims it towards grey).</summary>
        public Color DownedTint = new Color(0.4f, 0.4f, 0.4f, 1f);

        /// <summary>Fill colour of a downed player's bar, which shows the time left before death instead of health.</summary>
        public Color DownedBarColor = new Color(0.95f, 0.95f, 0.95f);

        /// <summary>Colour of the thin revive-progress bar under a downed player's bar.</summary>
        public Color ReviveBarColor = new Color(0.2f, 0.9f, 0.85f);

        /// <summary>Damage one enemy deals to a standing player per hit (guessed: 8).</summary>
        [Min(0)] public int EnemyPlayerDamage = 8;

        /// <summary>Seconds between two hits from the same enemy on the same player while touching. The first hit comes this long after contact starts (guessed: 1).</summary>
        [Min(0.05f)] public float EnemyPlayerHitInterval = 1f;

        /// <summary>
        /// An enemy counts as touching a player when the gap between their colliders is at most this, in world units.
        /// Solid colliders push each other apart and rest a hair away from each other, so 0 would miss real contact.
        /// </summary>
        [Min(0f)] public float EnemyContactRange = 0.05f;

        // ---- Story 006: mini-boss and boss ----
        // Like the earlier blocks, these defaults apply when an older GameConfig.asset lacks the fields.
        // Every number is a first guess for the designer to tune (the brief only says bigger, tougher, with escorts).

        /// <summary>
        /// Extra enemies that join a wave, one per wave (the first entry for a wave is used). The wave row
        /// still gives the normal enemies, which are the escorts. When a <see cref="EnemyKind.Boss"/> dies the team
        /// wins at once: the rest of that wave is despawned and any later waves are skipped.
        /// Defaults: a mini-boss in wave 5 and the boss in wave 10 (bullet damage is 10, so 300 health is 30 hits).
        /// </summary>
        public BossDefinition[] Bosses =
        {
            new BossDefinition { Wave = 5,  Kind = EnemyKind.MiniBoss, Size = 2f, Health = 300,  Speed = 1.2f, PlayerDamage = 20, BaseDamage = 25, Edge = SpawnEdges.Right },
            new BossDefinition { Wave = 10, Kind = EnemyKind.Boss,     Size = 3f, Health = 1000, Speed = 1.5f, PlayerDamage = 35, BaseDamage = 50, Edge = SpawnEdges.Right },
        };

        /// <summary>Name shown on the health bar of a <see cref="EnemyKind.MiniBoss"/>.</summary>
        public string MiniBossLabel = "Mini-boss";

        /// <summary>Name shown on the health bar of the <see cref="EnemyKind.Boss"/>.</summary>
        public string BossLabel = "Boss";

        /// <summary>Boss health bar size in screen pixels (width, height).</summary>
        public Vector2 BossBarSize = new Vector2(520f, 22f);

        /// <summary>Boss health bar fill colour.</summary>
        public Color BossBarFillColor = new Color(0.9f, 0.2f, 0.2f);

        /// <summary>Boss health bar background colour.</summary>
        public Color BossBarBackColor = new Color(0f, 0f, 0f, 0.7f);

        /// <summary>Font size of the boss name and health text above the bar, in pixels.</summary>
        [Min(8)] public int BossBarFontSize = 20;

        // ---- Story 007: win/lose screens, restart, room strip ----
        // Like the earlier blocks, these defaults apply when an older GameConfig.asset lacks the fields.
        // Texts are plain English like the rest of the UI. Every size, colour and time is a first guess.

        /// <summary>Title of the win screen.</summary>
        public string WinTitle = "You win!";

        /// <summary>Title of the lose screen.</summary>
        public string LoseTitle = "Game over";

        /// <summary>Cause line on the lose screen when the base reached 0 health.</summary>
        public string LoseBaseText = "The base was destroyed";

        /// <summary>Cause line on the lose screen when every player is dead.</summary>
        public string LoseEveryoneText = "Everyone is down";

        /// <summary>Host-only button that starts a new game in the same room.</summary>
        public string RestartButtonText = "Restart";

        /// <summary>Button that leaves the room and returns to the menu (everyone).</summary>
        public string BackToMenuButtonText = "Back to menu";

        /// <summary>Shown instead of the Restart button to everyone but the host.</summary>
        public string WaitingForHostText = "Waiting for the host to restart...";

        /// <summary>Colour of the full-screen overlay behind the end screen (alpha = how much the game is dimmed).</summary>
        public Color EndDimColor = new Color(0f, 0f, 0f, 0.75f);

        /// <summary>Colour of the win title.</summary>
        public Color EndWinColor = new Color(0.45f, 1f, 0.55f);

        /// <summary>Colour of the lose title.</summary>
        public Color EndLoseColor = new Color(1f, 0.35f, 0.3f);

        /// <summary>Colour of the cause line, the waiting text and the busy text.</summary>
        public Color EndTextColor = Color.white;

        /// <summary>Font size of the end screen title, in pixels.</summary>
        [Min(8)] public int EndTitleFontSize = 72;

        /// <summary>Font size of the cause line, in pixels.</summary>
        [Min(8)] public int EndCauseFontSize = 32;

        /// <summary>Font size of the waiting and busy text, in pixels.</summary>
        [Min(8)] public int EndInfoFontSize = 22;

        /// <summary>Font size of the end screen buttons, in pixels.</summary>
        [Min(8)] public int EndButtonFontSize = 26;

        /// <summary>Size of each end screen button in screen pixels (width, height).</summary>
        public Vector2 EndButtonSize = new Vector2(300f, 56f);

        /// <summary>Vertical gap between the end screen's title, cause line and buttons, in pixels.</summary>
        [Min(0f)] public float EndSpacing = 18f;

        /// <summary>Top-left corner of the room strip in the Game scene, in screen pixels.</summary>
        public Vector2 RoomStripPosition = new Vector2(10f, 10f);

        /// <summary>Size of the room strip in the Game scene, in screen pixels (width, height).</summary>
        public Vector2 RoomStripSize = new Vector2(300f, 30f);

        /// <summary>Font size of the room strip, in pixels.</summary>
        [Min(8)] public int RoomStripFontSize = 18;

        /// <summary>Word before the join code on the room strip.</summary>
        public string RoomStripCodeLabel = "Room";

        /// <summary>Word before the player count on the room strip.</summary>
        public string RoomStripPlayersLabel = "Players";

        /// <summary>
        /// DEBUG: seconds after the Game scene starts before <c>--force-outcome</c> takes effect, so every
        /// scene object has started. Only used when that command-line argument is given.
        /// </summary>
        [Min(0f)] public float DebugForceOutcomeDelaySeconds = 1f;

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
