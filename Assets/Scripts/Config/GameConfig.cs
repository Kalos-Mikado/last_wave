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

    /// <summary>How the map picture is fitted to the camera view (story 015, <see cref="GameConfig.MapFit"/>).</summary>
    public enum MapFitMode
    {
        /// <summary>Width and height scaled separately so the whole picture fills the view exactly (no bars, no crop; the picture is stretched).</summary>
        Stretch = 0,
        /// <summary>One scale (the larger ratio) so the picture covers the view without distortion; the overflow is cropped.</summary>
        Cover = 1,
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
    /// How one enemy kind is drawn and what its hitbox is (story 014), at size 1; the boss <c>Size</c> multiplies
    /// everything on top (the whole object is scaled). Measured from the sheet in <c>Art/Enemies/Map1</c>.
    /// </summary>
    [System.Serializable]
    public sealed class EnemyLookDefinition
    {
        /// <summary>
        /// Pixels per world unit of the sheet at size 1. The body is about <c>bodyPixels / PixelsPerUnit</c> units wide,
        /// so every kind is about 1 unit wide at size 1 and a mini-boss (size 2) and boss (size 3) come out 2x and 3x.
        /// </summary>
        [Min(1f)] public float PixelsPerUnit = 170f;

        /// <summary>
        /// Empty pixels between the creature's feet and the bottom of its 256 px cell (the sprite pivot is the cell
        /// bottom). The sprite is lowered by this much so the feet sit on the bottom edge of the hitbox.
        /// </summary>
        [Min(0f)] public float FeetGapPixels = 43f;

        /// <summary>
        /// Body size in world units at size 1 (the old box hitbox). Since story 015 the hitbox is a polygon traced from the
        /// sprite, so this no longer sizes a collider: Y still places the sprite (the feet sit Y/2 below the enemy centre),
        /// and X is used only by the fallback base stop distance when the base has no usable collider.
        /// </summary>
        public Vector2 ColliderSize = new Vector2(0.9f, 0.65f);

        /// <summary>
        /// Animation speed multiplier for this kind (1 = the shared enemy fps). Below 1 plays every clip slower, so the
        /// big mini-boss and boss move heavily instead of skittering like the small crawlers.
        /// </summary>
        [Min(0.05f)] public float AnimationSpeed = 1f;
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

    /// <summary>What a wave-rest upgrade card improves (story 012). Values are packed one byte each into a network variable: keep fewer than 8 kinds.</summary>
    public enum UpgradeKind : byte
    {
        /// <summary>Bullet damage multiplier.</summary>
        Damage = 0,
        /// <summary>Fire rate: the interval between shots is divided by the multiplier.</summary>
        FireRate = 1,
        /// <summary>Walking speed multiplier.</summary>
        MoveSpeed = 2,
        /// <summary>Flat extra max health.</summary>
        MaxHealth = 3,
        /// <summary>Base repair rate multiplier.</summary>
        RepairSpeed = 4,
        /// <summary>Revive speed multiplier (the player doing the reviving).</summary>
        ReviveSpeed = 5,
    }

    /// <summary>One upgrade card in <see cref="GameConfig.Upgrades"/>. Each level adds <see cref="Value"/> (levels stack linearly).</summary>
    [System.Serializable]
    public sealed class UpgradeDefinition
    {
        /// <summary>What it improves.</summary>
        public UpgradeKind Kind = UpgradeKind.Damage;

        /// <summary>Name on the card, e.g. "Damage". Also used (lower case) in the value line, e.g. "+20% damage".</summary>
        public string DisplayName = "Damage";

        /// <summary>Gain per level: a fraction (0.2 = +20%) when <see cref="IsPercent"/>, otherwise a flat amount (25 = +25).</summary>
        public float Value = 0.2f;

        /// <summary>True when <see cref="Value"/> is a fraction shown as a percent; false for a flat amount.</summary>
        public bool IsPercent = true;
    }

    /// <summary>
    /// Story 020 (<c>design/game-brief.md</c> Feature 20): one "character", a full set of player pictures in one colour.
    /// Every set must line up with the white set (same sheet sizes, cells, shoulder and muzzle pixels), so only the
    /// pictures change. A new set is data only: add an entry to <see cref="GameConfig.Characters"/> and either drag the
    /// three textures in, or put them at <c>Assets/Art/Player/&lt;ArtFolder&gt;/player-body-&lt;ArtFolder&gt;.png</c>
    /// (and <c>player-arms-rifle-</c>, <c>player-b-</c>) and run <c>Last Wave &gt; Build Scenes and Prefabs</c>, which fills
    /// empty references from those paths. Prepared for a later character-select screen (out of scope).
    /// </summary>
    [System.Serializable]
    public sealed class CharacterDefinition
    {
        /// <summary>Name shown to players, e.g. "Olive".</summary>
        public string DisplayName = "Olive";

        /// <summary>Folder and file suffix under <c>Assets/Art/Player/</c> that LastWaveSetup fills empty references from, e.g. "olive".</summary>
        public string ArtFolder = "olive";

        /// <summary>Body sheet: idle, walk, hurt (no arms), repair/revive. Same layout as <c>player-body.png</c>.</summary>
        public Texture2D BodySheet;

        /// <summary>Arm + rifle piece that turns at the shoulder. Same layout as <c>player-arms-rifle.png</c>.</summary>
        public Texture2D ArmTexture;

        /// <summary>Sheet B: down, death, get up. Same layout as <c>player-b.png</c>.</summary>
        public Texture2D SheetB;

        /// <summary>Creates a set with no textures yet (LastWaveSetup fills them from <paramref name="artFolder"/>).</summary>
        public CharacterDefinition(string displayName, string artFolder)
        {
            DisplayName = displayName;
            ArtFolder = artFolder;
        }

        /// <summary>For Unity serialization.</summary>
        public CharacterDefinition() { }
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

        /// <summary>
        /// One colour per player slot. Red is kept for enemies and blue for the base (brief, Look).
        /// Story 020: no longer tints the player sprite (each slot has its own picture set, <see cref="Characters"/>);
        /// nothing reads it now. Kept for small per-player markers.
        /// </summary>
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

        /// <summary>Seconds a standing player must go without taking damage before health starts to regenerate (story 011, guessed: 4).</summary>
        [Min(0f)] public float HealthRegenDelaySeconds = 4f;

        /// <summary>Health regenerated per second once the wait is over, up to max health (story 011, guessed: 5; fractions are kept, so it need not be a whole number).</summary>
        [Min(0f)] public float HealthRegenPerSecond = 5f;

        /// <summary>When true, every standing player is healed to full the moment a wave rest starts (story 011, guessed: true).</summary>
        public bool FullHealOnWaveCleared = true;

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

        /// <summary>
        /// Distance from the player centre, along the aim direction, where a bullet appeared. Unused since story 019:
        /// bullets start at the rifle's muzzle (<see cref="PlayerArmMuzzlePixel"/>).
        /// </summary>
        [Min(0f)] public float BulletSpawnOffset = 0.7f;

        /// <summary>Colour of the small marker on a player that shows the aim direction.</summary>
        public Color AimMarkerColor = new Color(0.1f, 0.1f, 0.1f);

        /// <summary>Health bar size (width, height). Story 017: reference pixels (see <see cref="UiReferenceHeight"/>), framed by <see cref="HudPlayerBarTexture"/>.</summary>
        public Vector2 HealthBarSize = new Vector2(110f, 22f);

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

        /// <summary>Seconds of rest after a wave is cleared, before the next one (story 012 raised it from 10 to 15 so the upgrade cards can be read; own call).</summary>
        [Min(0f)] public float WaveRestSeconds = 15f;

        /// <summary>
        /// Half width and half height of the play area, in world units; enemies spawn on its edges (right edge: y from
        /// -Y to +Y). Story 015: Y is 4.2 so enemies spawn and walk in the grass lane between the map walls (top wall at
        /// y 4.87, bottom wall at -4.68 for the 12-unit view): 4.68 minus a normal enemy's half body and a small margin.
        /// </summary>
        public Vector2 MapHalfExtents = new Vector2(10f, 4.2f);

        /// <summary>
        /// Gap, in world units, between an enemy's hitbox and the base's hitbox where the enemy stops and hits the base.
        /// Story 015: measured with Collider2D.Distance between the two polygon hitboxes. Only when the base has no usable
        /// collider does the old centre-distance fallback apply: half <see cref="BaseColliderSize"/>.x plus half the enemy's
        /// ColliderSize.x x Size plus this gap.
        /// </summary>
        [Min(0f)] public float EnemyStopDistance = 0.05f;

        /// <summary>
        /// Gap, in world units, between an enemy's hitbox and the base's hitbox within which the enemy hits the base.
        /// Longer than <see cref="EnemyStopDistance"/>, so an enemy stopped at the base edge, or held a little back by
        /// the enemies around it, is always in reach.
        /// </summary>
        [Min(0f)] public float EnemyBaseHitReach = 0.25f;

        /// <summary>A normal enemy starts chasing the nearest standing player within this distance, in world units (story 010, guessed: 4).</summary>
        [Min(0f)] public float EnemySightRange = 4f;

        /// <summary>A chasing enemy gives up and returns to the base when its player is farther than this, in world units. Keep it above <see cref="EnemySightRange"/> (guessed: 7).</summary>
        [Min(0f)] public float EnemyGiveUpRange = 7f;

        /// <summary>When true, a mini-boss and the boss also chase players; when false they go for the base only (guessed: false).</summary>
        public bool MiniBossAndBossChasePlayers = false;

        /// <summary>Colour of the placeholder base marker. Since story 015 the base shows images and this is no longer used by the scene (kept only because WaveManager still reads it for an optional marker).</summary>
        public Color BaseColor = new Color(0.2f, 0.45f, 1f);

        /// <summary>Font size of the "Wave n / total" label. Story 017: reference pixels, drawn on <see cref="HudWavePlateTexture"/>.</summary>
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

        /// <summary>Base health bar size (width, height). Story 017: reference pixels, framed by <see cref="HudBaseBarTexture"/>.</summary>
        public Vector2 BaseBarSize = new Vector2(260f, 34f);

        /// <summary>How far above the base centre (<see cref="BasePosition"/>) the bar sits, in world units. Story 015: raised to clear the taller base image (top is about 1.7 above the centre).</summary>
        public float BaseBarOffsetY = 2.0f;

        // ---- Story 015: base images and map background ----
        // Like the earlier blocks, these defaults apply when an older GameConfig.asset lacks the fields.

        /// <summary>
        /// The base image is intact while health is ABOVE this fraction of <see cref="BaseMaxHealth"/>; from this
        /// fraction down to just above 0 it shows the damaged image; at 0 the destroyed image (default 0.5).
        /// </summary>
        [Range(0f, 1f)] public float BaseDamagedThreshold = 0.5f;

        /// <summary>How wide the base is drawn in the game, in world units (outline width of the intact image; default 3).</summary>
        [Min(0.1f)] public float BaseWorldWidth = 3.2f;

        /// <summary>Width in image pixels of the intact base outline (about 1228 of the 1254 px image). With <see cref="BaseWorldWidth"/> it sets the pixels per unit shared by all three images.</summary>
        [Min(1f)] public float BaseOutlinePixelWidth = 1215f;

        /// <summary>
        /// Row of the lowest outline pixel, counted from the TOP of each 1254 px image, per state
        /// (x = intact, y = damaged, z = destroyed). It becomes the sprite pivot, so the three bases stand on the same
        /// ground line and do not jump when the state changes.
        /// </summary>
        public Vector3 BaseOutlineBottomPixels = new Vector3(1074f, 1074f, 1074f);

        /// <summary>
        /// Column of the horizontal middle of the outline, counted from the LEFT of each image, per state
        /// (x = intact, y = damaged, z = destroyed). It becomes the sprite pivot, so the base stays centred on <see cref="BasePosition"/>.
        /// </summary>
        public Vector3 BaseOutlineCenterPixels = new Vector3(626f, 626f, 627f);

        /// <summary>
        /// Per state (x intact, y damaged, z destroyed): the image row, counted from the image top, where the base's
        /// hitbox stops. Everything below it (the flat dirt-and-grass foundation patch drawn around the base's foot) is
        /// ground the players can walk on, so it is left out when the outline is traced.
        /// </summary>
        public Vector3 BaseHitboxBottomPixels = new Vector3(985f, 985f, 1010f);

        /// <summary>Where the base's ground line (outline bottom) sits, in world units relative to <see cref="BasePosition"/> (default -0.75: the bottom of the old 1.5 unit square).</summary>
        public float BaseGroundOffsetY = -0.75f;

        /// <summary>
        /// Approximate base body size in world units (the old box hitbox). Since story 015 the base hitbox is a polygon
        /// traced from each base image, so this sizes no collider; only X is used, by the enemy's fallback stop distance
        /// when the base has no usable collider.
        /// </summary>
        public Vector2 BaseColliderSize = new Vector2(2.9f, 2.3f);

        /// <summary>Sorting order of the map background sprite. Keep it below every other sprite (the base is -1, characters 0).</summary>
        public int MapSortingOrder = -100;

        /// <summary>
        /// How the map picture fits the camera view. Stretch (default): the whole picture fills the view exactly, stretched
        /// horizontally at 16:9 (about 18%). Cover: no distortion, the top and bottom are cropped.
        /// </summary>
        public MapFitMode MapFit = MapFitMode.Stretch;

        /// <summary>
        /// Height of the rock band along the TOP of map1.png, as a fraction of the picture's height (0.094). The top wall's
        /// inner edge sits this far below the top of the displayed picture.
        /// </summary>
        [Range(0f, 0.5f)] public float MapTopBandFraction = 0.094f;

        /// <summary>
        /// Height of the rock band along the BOTTOM of map1.png, as a fraction of the picture's height (0.11). The bottom
        /// wall's inner edge sits this far above the bottom of the displayed picture.
        /// </summary>
        [Range(0f, 0.5f)] public float MapBottomBandFraction = 0.11f;

        /// <summary>Thickness of each solid map wall (top, bottom, left, right), in world units. Thick enough that a player never tunnels through.</summary>
        [Min(0.1f)] public float MapWallThickness = 2f;

        /// <summary>
        /// Outline simplification for the base hitbox polygons (Ramer-Douglas-Peucker tolerance, world units): an outline
        /// point closer than this to the simplified line is dropped. Larger = fewer points, rougher fit.
        /// </summary>
        [Min(0.001f)] public float BaseHitboxTolerance = 0.06f;

        /// <summary>
        /// Outline simplification for the player hitbox polygon (world units). Raise it if the player snags on the walls or
        /// the base (fewer points = smoother sliding).
        /// </summary>
        [Min(0.001f)] public float PlayerHitboxTolerance = 0.06f;

        /// <summary>Outline simplification for the enemy hitbox polygons (world units at size 1; a boss's polygon is scaled with it).</summary>
        [Min(0.001f)] public float EnemyHitboxTolerance = 0.05f;

        /// <summary>Outline islands smaller than this area (square world units, at size 1) are dropped from a hitbox: loose specks of art are not solid.</summary>
        [Min(0f)] public float HitboxMinPathArea = 0.02f;

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
            int slot = PlayerSlot(ownerClientId);
            float centre = (MaxPlayers - 1) * 0.5f;
            return BasePosition + PlayerSpawnOffset + Vector2.up * ((slot - centre) * SpawnSpacing);
        }

        /// <summary>The player slot, 0 to <see cref="MaxPlayers"/> - 1: client id modulo MaxPlayers (story 009). Same on every machine.</summary>
        /// <param name="ownerClientId">Network client id of the player's owner.</param>
        public int PlayerSlot(ulong ownerClientId)
        {
            return (int)(ownerClientId % (ulong)Mathf.Max(1, MaxPlayers));
        }

        /// <summary>
        /// Story 020: the picture set a player's slot uses (slot modulo the number of sets), or null when
        /// <see cref="Characters"/> is empty (the white set is used then).
        /// </summary>
        /// <param name="ownerClientId">Network client id of the player's owner.</param>
        public CharacterDefinition CharacterForClient(ulong ownerClientId)
        {
            if (Characters == null || Characters.Length == 0) return null;
            return Characters[PlayerSlot(ownerClientId) % Characters.Length];
        }

        /// <summary>
        /// Story 020: renderer colour multiplied onto a downed player's (coloured) sprite, a mild darken so the downed
        /// state reads without recolouring the character. White = no change. Guessed for the designer to tune.
        /// </summary>
        public Color DownedTint = new Color(0.7f, 0.7f, 0.7f, 1f);

        /// <summary>
        /// Story 020: one picture set per player slot, in slot order (slot 1 Olive, 2 Desert, 3 Slate, 4 Graphite).
        /// A slot past the end wraps around. A set with any texture missing falls back to the white set with a warning.
        /// </summary>
        public CharacterDefinition[] Characters =
        {
            new CharacterDefinition("Olive", "olive"),
            new CharacterDefinition("Desert", "desert"),
            new CharacterDefinition("Slate", "slate"),
            new CharacterDefinition("Graphite", "graphite"),
        };

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
            new BossDefinition { Wave = 5,  Kind = EnemyKind.MiniBoss, Size = 2f, Health = 300,  Speed = 0.8f, PlayerDamage = 20, BaseDamage = 25, Edge = SpawnEdges.Right },
            new BossDefinition { Wave = 10, Kind = EnemyKind.Boss,     Size = 3f, Health = 1000, Speed = 0.7f, PlayerDamage = 35, BaseDamage = 50, Edge = SpawnEdges.Right },
        };

        /// <summary>Name shown on the health bar of a <see cref="EnemyKind.MiniBoss"/>.</summary>
        public string MiniBossLabel = "Mini-boss";

        /// <summary>Name shown on the health bar of the <see cref="EnemyKind.Boss"/>.</summary>
        public string BossLabel = "Boss";

        /// <summary>Boss health bar size (width, height). Story 017: reference pixels, framed by <see cref="HudBossBarTexture"/>.</summary>
        public Vector2 BossBarSize = new Vector2(600f, 44f);

        /// <summary>Boss health bar fill colour.</summary>
        public Color BossBarFillColor = new Color(0.9f, 0.2f, 0.2f);

        /// <summary>Boss health bar background colour.</summary>
        public Color BossBarBackColor = new Color(0f, 0f, 0f, 0.7f);

        /// <summary>Font size of the boss name and health text above the bar. Story 017: reference pixels.</summary>
        [Min(8)] public int BossBarFontSize = 20;

        // ---- Story 007: win/lose screens, restart, room strip ----
        // Like the earlier blocks, these defaults apply when an older GameConfig.asset lacks the fields.
        // Texts are plain English like the rest of the UI. Every size, colour and time is a first guess.

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

        /// <summary>Colour of the cause line, the waiting text and the busy text.</summary>
        public Color EndTextColor = Color.white;

        /// <summary>Font size of the cause line under the banner. Story 018: reference pixels (see <see cref="UiReferenceHeight"/>).</summary>
        [Min(8)] public int EndCauseFontSize = 34;

        /// <summary>Font size of the waiting and busy text. Story 018: reference pixels.</summary>
        [Min(8)] public int EndInfoFontSize = 26;

        /// <summary>Font size of the end screen buttons (UiSkin art buttons). Story 018: reference pixels.</summary>
        [Min(8)] public int EndButtonFontSize = 28;

        /// <summary>Size of each end screen button (width, height). Story 018: reference pixels, same height as the menu buttons.</summary>
        public Vector2 EndButtonSize = new Vector2(360f, 72f);

        /// <summary>Vertical gap between the end screen's banner, cause line and buttons. Story 018: reference pixels.</summary>
        [Min(0f)] public float EndSpacing = 18f;

        /// <summary>Story 018: banner shown on a win (Art/UI/banner-VICTORY.png), trimmed to its opaque pixels at runtime. Must be Read/Write (LastWaveSetup sets it).</summary>
        public Texture2D EndWinBannerTexture;

        /// <summary>Story 018: banner shown on a loss (Art/UI/banner-DEFEAT.png), trimmed like the win banner. Must be Read/Write.</summary>
        public Texture2D EndLoseBannerTexture;

        /// <summary>Story 018: height of the end banner in reference pixels; its width follows the trimmed picture's aspect. The banner, cause line and buttons are stacked and centred on the screen.</summary>
        [Min(1f)] public float EndBannerHeight = 420f;

        /// <summary>
        /// Gap between the room strip and the screen corner given by <see cref="RoomStripAnchor"/>, in reference pixels (story 017)
        /// (x from the left or right edge, y from the top or bottom edge).
        /// </summary>
        public Vector2 RoomStripPosition = new Vector2(16f, 16f);

        /// <summary>Size of the room strip in the Game scene (width, height). Story 017: reference pixels, on the <see cref="HudRoomPlateTexture"/> plate.</summary>
        public Vector2 RoomStripSize = new Vector2(400f, 46f);

        /// <summary>
        /// Screen corner the room strip sits in, as a fraction of the screen (0,0 = top-left, 1,0 = top-right). Story 016
        /// moved it to the top-right (1,0) so it no longer covers the base at the far left; the wave label and boss bars
        /// hold the top centre.
        /// </summary>
        public Vector2 RoomStripAnchor = new Vector2(1f, 0f);

        /// <summary>Font size of the room strip. Story 017: reference pixels.</summary>
        [Min(8)] public int RoomStripFontSize = 20;

        /// <summary>Word before the join code on the room strip.</summary>
        public string RoomStripCodeLabel = "Room";

        /// <summary>Word before the player count on the room strip.</summary>
        public string RoomStripPlayersLabel = "Players";

        // ---- Story 012: upgrade cards between waves ----
        // Like the earlier blocks, these defaults apply when an older GameConfig.asset lacks the fields.
        // Every number is a first guess for the designer to tune.

        /// <summary>The upgrade set. Each rest, every player is dealt <see cref="UpgradeChoicesPerRest"/> distinct kinds from it (a kind listed twice counts once).</summary>
        public UpgradeDefinition[] Upgrades =
        {
            new UpgradeDefinition { Kind = UpgradeKind.Damage,      DisplayName = "Damage",       Value = 0.20f, IsPercent = true },
            new UpgradeDefinition { Kind = UpgradeKind.FireRate,    DisplayName = "Fire rate",    Value = 0.15f, IsPercent = true },
            new UpgradeDefinition { Kind = UpgradeKind.MoveSpeed,   DisplayName = "Move speed",   Value = 0.10f, IsPercent = true },
            new UpgradeDefinition { Kind = UpgradeKind.MaxHealth,   DisplayName = "Max health",   Value = 25f,   IsPercent = false },
            new UpgradeDefinition { Kind = UpgradeKind.RepairSpeed, DisplayName = "Repair speed", Value = 0.30f, IsPercent = true },
            new UpgradeDefinition { Kind = UpgradeKind.ReviveSpeed, DisplayName = "Revive speed", Value = 0.30f, IsPercent = true },
        };

        /// <summary>Cards each player is dealt per rest (brief: 3; at most 8).</summary>
        [Range(1, 8)] public int UpgradeChoicesPerRest = 3;

        /// <summary>Hint above the cards.</summary>
        public string UpgradeHintText = "Pick an upgrade (1/2/3)";

        /// <summary>Size of one card (width, height). Story 018: reference pixels; keep the trimmed frame's aspect (about 970 x 1488).</summary>
        public Vector2 UpgradeCardSize = new Vector2(240f, 368f);

        /// <summary>Gap between two cards. Story 018: reference pixels.</summary>
        [Min(0f)] public float UpgradeCardGap = 24f;

        /// <summary>Distance from the bottom of the screen to the bottom of the cards. Story 018: reference pixels.</summary>
        [Min(0f)] public float UpgradeCardBottomMargin = 60f;

        /// <summary>Font size of the card name, in the frame's text panel. Story 018: reference pixels.</summary>
        [Min(8)] public int UpgradeNameFontSize = 24;

        /// <summary>Font size of the card value line, under the name. Story 018: reference pixels.</summary>
        [Min(8)] public int UpgradeValueFontSize = 20;

        /// <summary>Font size of the key number in the icon window's top-left corner. Story 018: reference pixels.</summary>
        [Min(8)] public int UpgradeKeyFontSize = 22;

        /// <summary>Font size of the hint. Story 018: reference pixels.</summary>
        [Min(8)] public int UpgradeHintFontSize = 26;

        /// <summary>Colour of the card name and the hint.</summary>
        public Color UpgradeNameColor = Color.white;

        /// <summary>Colour of the card value line and key number.</summary>
        public Color UpgradeValueColor = new Color(0.55f, 1f, 0.6f, 1f);

        // Story 018: card art. The frames are trimmed together to their opaque pixels at runtime; the rectangles below
        // are fractions of the trimmed frame (x, y from its top-left; width, height), measured from the 1024 x 1536
        // source (opaque area about x 27-996, y 24-1511; magenta icon hole about x 198-825, y 148-772; text panel
        // about x 100-920, y 852-1434).

        /// <summary>Story 018: card frame (Art/UI/Upgrades/frame-blue.png). Its icon window is a transparent hole. Must be Read/Write (LastWaveSetup sets it).</summary>
        public Texture2D UpgradeFrameTexture;

        /// <summary>Story 018: card frame drawn instead of <see cref="UpgradeFrameTexture"/> while the mouse is over the card (Art/UI/Upgrades/frame-gold.png). Must be Read/Write.</summary>
        public Texture2D UpgradeFrameHoverTexture;

        /// <summary>Story 018: the upgrade icon sheet (Art/UI/Upgrades/icon-upgrade.png), cut into <see cref="UpgradeIconGrid"/> cells in <see cref="UpgradeKind"/> order. Must be Read/Write.</summary>
        public Texture2D UpgradeIconsTexture;

        /// <summary>Story 018: columns and rows of the upgrade icon sheet (3 x 2 cells of 512 px: Damage, FireRate, MoveSpeed / MaxHealth, RepairSpeed, ReviveSpeed).</summary>
        public Vector2Int UpgradeIconGrid = new Vector2Int(3, 2);

        /// <summary>Story 018: the frame's icon window as fractions of the card. Filled with <see cref="UpgradeIconBackColor"/> before the frame is drawn over it, so it is a few pixels larger than the hole and its edges sit under the frame's line.</summary>
        public Rect UpgradeIconWindow = new Rect(0.172f, 0.081f, 0.655f, 0.425f);

        /// <summary>Story 018: the space left around the icon inside <see cref="UpgradeIconWindow"/>, as a fraction of the window's width and height on each side.</summary>
        [Range(0f, 0.4f)] public float UpgradeIconPadding = 0.12f;

        /// <summary>Story 018: fill behind the icon, so the window never shows the game behind the card.</summary>
        public Color UpgradeIconBackColor = new Color(0.05f, 0.07f, 0.10f, 1f);

        /// <summary>Story 018: where the name and value text go, as fractions of the card: the frame's lower panel, inset from its edges. The name takes the top <see cref="UpgradeNameShare"/>, the value the rest.</summary>
        public Rect UpgradeTextPanel = new Rect(0.09f, 0.567f, 0.82f, 0.37f);

        /// <summary>Story 018: share of <see cref="UpgradeTextPanel"/>'s height given to the name (the value line wraps in the rest).</summary>
        [Range(0.1f, 0.9f)] public float UpgradeNameShare = 0.4f;

        // ---- Story 013: player art and animation ----
        // Like the earlier blocks, these defaults apply when an older GameConfig.asset lacks the fields.
        // Every number is a first guess for the designer to tune.

        /// <summary>Width and height of one cell in the player sprite sheets, in pixels (the sheets are 6 x 4 cells).</summary>
        [Min(1)] public int PlayerSheetCellPixels = 256;

        /// <summary>
        /// Pixels per world unit for the player sprites: sets how big the soldier is drawn. The soldier is about
        /// 190 pixels tall in its cell, so 190 makes it about 1 unit tall, the size of the old square body.
        /// The collider does not change with this.
        /// </summary>
        [Min(1f)] public float PlayerSpritePixelsPerUnit = 190f;

        /// <summary>
        /// Vertical offset of the sprite's cell bottom from the player centre, in world units. The sprite pivot is the
        /// bottom of the cell and the soldier's feet sit 16 px above it, so -0.5 - 16/190 = -0.584 puts the feet 0.5 below
        /// the player centre (where the old 1-unit box ended). The polygon hitbox (story 015) follows the sprite, so it moves with this.
        /// </summary>
        public float PlayerSpriteOffsetY = -0.584f;

        // ---- Drop shadows (soft ovals under players and enemies) ----

        /// <summary>Darkness of the drop shadow at its centre, 0 (none) to 1 (black). It fades to nothing at the edge.</summary>
        [Range(0f, 1f)] public float ShadowAlpha = 0.45f;

        /// <summary>Shadow height as a fraction of its width: a flat oval, as seen from the map's high side angle.</summary>
        [Range(0.05f, 1f)] public float ShadowHeightRatio = 0.3f;

        /// <summary>Sorting order of the shadows: below the base (-1) and the characters (0), above the map (-100).</summary>
        public int ShadowSortingOrder = -2;

        /// <summary>Player shadow width in world units (the soldier's body is about 0.75 wide).</summary>
        [Min(0f)] public float PlayerShadowWidth = 1.1f;

        /// <summary>Local Y of the player's feet, where the shadow is centred (the feet sit half a unit below the player's centre).</summary>
        public float PlayerShadowFeetY = -0.56f;

        /// <summary>Enemy shadow width as a multiple of its kind's ColliderSize.x (it scales with Size for the bosses).</summary>
        [Min(0f)] public float EnemyShadowWidthScale = 1.1f;

        /// <summary>Frames per second of the idle loop.</summary>
        [Min(0f)] public float PlayerIdleFps = 6f;

        /// <summary>Frames per second of the walk loop (while the player's position is changing).</summary>
        [Min(0f)] public float PlayerWalkFps = 12f;

        /// <summary>Frames per second of the repair/revive loop (while holding E with something to repair or revive).</summary>
        [Min(0f)] public float PlayerRepairFps = 8f;

        /// <summary>Frames per second of the falling, lying, death and getting-up animations.</summary>
        [Min(0f)] public float PlayerDownFps = 8f;

        /// <summary>Seconds the shoot animation lasts after each shot; the whole row is played in this time. About the fire interval keeps it continuous while firing.</summary>
        [Min(0.01f)] public float PlayerShootSeconds = 0.3f;

        /// <summary>Seconds the hurt flinch lasts after losing health while still standing; the whole row is played in this time.</summary>
        [Min(0.01f)] public float PlayerHurtSeconds = 0.3f;

        /// <summary>First frame (0-based) of the lying pose in the downed row. After the fall plays once, frames from here to the last loop.</summary>
        [Min(0)] public int PlayerDownedLoopStartFrame = 4;

        /// <summary>
        /// First frame (0-based) of the death row that is played. A player only ever dies from Downed (lying on the
        /// ground), and the original death row started from a standing pose (then 4 skipped it). The redrawn row starts
        /// from the downed pose, so 0 plays it all.
        /// </summary>
        [Min(0)] public int PlayerDeathStartFrame = 0;

        /// <summary>
        /// Pixel nudge (in sheet pixels, facing right) applied while the death row plays, so it lines up with the
        /// downed pose: in player-b.png the death row sits 12 px further right and 18 px higher than row 2 frame 6.
        /// </summary>
        public Vector2 PlayerDeathRowOffsetPixels = new Vector2(-12f, -18f);

        /// <summary>Seconds the last death frame stays visible before the body is hidden (a dead player is out of play until the wave ends).</summary>
        [Min(0f)] public float PlayerDeadHoldSeconds = 1.5f;

        /// <summary>A player counts as walking when its position changes faster than this, in world units per second.</summary>
        [Min(0f)] public float PlayerWalkSpeedThreshold = 0.3f;

        /// <summary>Seconds the walk animation continues after movement stops, so network smoothing does not make it flicker.</summary>
        [Min(0f)] public float PlayerWalkStopDelaySeconds = 0.12f;

        /// <summary>The aim must be at least this far sideways (the X of the unit aim direction, 0 to 1) before the soldier turns around.</summary>
        [Range(0f, 1f)] public float PlayerFacingDeadZone = 0.1f;

        // ---- Story 019: separate arm + rifle rotating at the shoulder (design/game-brief.md Feature 19, "แบบ A") ----
        // Pixel positions are measured from the art, top-left origin like the art is described. The rest are first
        // guesses for the designer to tune. Older GameConfig.asset files take these defaults.

        /// <summary>
        /// The body's shoulder joint in a player-body.png cell, in pixels from the cell's top-left, facing right.
        /// The arm's shoulder pivot is placed here (mirrored when facing left), so the shoulder caps overlap.
        /// </summary>
        public Vector2 PlayerBodyShoulderPixel = new Vector2(103f, 122f);

        /// <summary>
        /// Extra shoulder nudge per body row (0 idle, 1 walk, 2 hit), in body pixels, facing right, added to
        /// <see cref="PlayerBodyShoulderPixel"/>. Missing rows count as zero.
        /// </summary>
        public Vector2[] PlayerBodyShoulderRowOffsetPixels = { Vector2.zero, Vector2.zero, Vector2.zero };

        /// <summary>The shoulder pivot of the arm piece in player-arms-rifle.png, in pixels from the image's top-left. The arm rotates around it.</summary>
        public Vector2 PlayerArmShoulderPixel = new Vector2(612f, 632f);

        /// <summary>The muzzle tip in player-arms-rifle.png, in pixels from the image's top-left. Bullets and the muzzle flash start here.</summary>
        public Vector2 PlayerArmMuzzlePixel = new Vector2(787f, 637f);

        /// <summary>Pixels per world unit of the arm piece: the arm art is drawn larger than the body sheet, so this is above <see cref="PlayerSpritePixelsPerUnit"/>.</summary>
        [Min(1f)] public float PlayerArmPixelsPerUnit = 300f;

        /// <summary>The arm turns at most this many degrees up or down from horizontal. Aiming further still fires toward the mouse; the arm holds at the limit.</summary>
        [Range(0f, 90f)] public float PlayerArmClampDegrees = 60f;

        /// <summary>The arm draws this many sorting orders above the body (the muzzle flash one more): in front of the body, under the HUD.</summary>
        public int PlayerArmSortingOffset = 1;

        /// <summary>Frames of muzzle-flash.png played per shot, in reading order (the 2 x 2 grid's 4th cell holds only sparks).</summary>
        [Range(1, 4)] public int PlayerMuzzleFlashFrames = 3;

        /// <summary>Frames per second of the muzzle flash.</summary>
        [Min(0.01f)] public float PlayerMuzzleFlashFps = 30f;

        /// <summary>Pixels per world unit of muzzle-flash.png (627 px cells): sets how big the flash is drawn.</summary>
        [Min(1f)] public float PlayerMuzzleFlashPixelsPerUnit = 1600f;

        /// <summary>Extra scale on the muzzle flash, on top of its pixels per unit.</summary>
        [Min(0f)] public float PlayerMuzzleFlashScale = 1f;

        /// <summary>The owner sends its aim angle rounded to this many degrees (a 16-bit value); remote players smooth toward it.</summary>
        [Min(0.01f)] public float PlayerAimSyncStepDegrees = 0.5f;

        /// <summary>How fast a remote player's arm catches up with the synced aim angle (exponential rate per second; higher = snappier).</summary>
        [Min(0f)] public float PlayerAimRemoteSmoothing = 20f;

        // ---- Story 014: enemy art, bullet art and hit spark ----
        // Like the earlier blocks, these defaults apply when an older GameConfig.asset lacks the fields.
        // Sheet numbers are measured from the art; the rest are first guesses for the designer to tune.

        /// <summary>Width and height of one cell in the enemy sheets, in pixels (the sheets are 6 x 4 cells: idle, walk, attack, death rows).</summary>
        [Min(1)] public int EnemySheetCellPixels = 256;

        /// <summary>Look and hitbox of a normal enemy (basic.png). Body about 172 x 127 px, so 170 px per unit is 1.0 x 0.75 units.</summary>
        public EnemyLookDefinition EnemyNormalLook = new EnemyLookDefinition
        {
            PixelsPerUnit = 170f, FeetGapPixels = 43f, ColliderSize = new Vector2(0.9f, 0.65f),
        };

        /// <summary>Look and hitbox of a mini-boss (miniboss.png). Body about 184 x 192 px, so 184 px per unit is 1.0 x 1.04 units at size 1.</summary>
        public EnemyLookDefinition EnemyMiniBossLook = new EnemyLookDefinition
        {
            PixelsPerUnit = 184f, FeetGapPixels = 24f, ColliderSize = new Vector2(0.9f, 0.95f), AnimationSpeed = 0.6f,
        };

        /// <summary>Look and hitbox of the boss (boss.png). Body about 208 x 208 px, so 208 px per unit is 1.0 x 1.0 units at size 1.</summary>
        public EnemyLookDefinition EnemyBossLook = new EnemyLookDefinition
        {
            PixelsPerUnit = 208f, FeetGapPixels = 25f, ColliderSize = new Vector2(0.9f, 0.92f), AnimationSpeed = 0.45f,
        };

        /// <summary>Frames per second of the enemy idle loop.</summary>
        [Min(0f)] public float EnemyIdleFps = 6f;

        /// <summary>Frames per second of the enemy walk loop.</summary>
        [Min(0f)] public float EnemyWalkFps = 10f;

        /// <summary>
        /// Seconds the attack animation lasts from the moment an enemy's hit lands on a player or the base; the whole
        /// row is played in this time. Keep it below the hit intervals so the pose returns to walk or idle between hits.
        /// </summary>
        [Min(0.01f)] public float EnemyAttackSeconds = 0.5f;

        /// <summary>Frames per second of the death animation (the row is played once).</summary>
        [Min(0f)] public float EnemyDeathFps = 10f;

        /// <summary>Seconds the last death frame stays on screen before the body disappears.</summary>
        [Min(0f)] public float EnemyDeathHoldSeconds = 0.5f;

        /// <summary>An enemy counts as walking when its position changes faster than this, in world units per second.</summary>
        [Min(0f)] public float EnemyWalkSpeedThreshold = 0.3f;

        /// <summary>Seconds the walk animation continues after movement stops, so physics steps and network smoothing do not make it flicker.</summary>
        [Min(0f)] public float EnemyWalkStopDelaySeconds = 0.12f;

        /// <summary>An enemy turns to face left or right only when it moves sideways faster than this, in world units per second; otherwise it keeps its facing.</summary>
        [Min(0f)] public float EnemyFacingMinSpeed = 0.1f;

        /// <summary>The way a new enemy faces before it has moved: true = left (towards the base, which is on the left).</summary>
        public bool EnemyStartsFacingLeft = true;

        /// <summary>Pixels per world unit of bullet.png (1254 px square, the bullet is 476 x 80 px in its middle): 1000 makes the bullet about 0.48 units long.</summary>
        [Min(1f)] public float BulletSpritePixelsPerUnit = 1000f;

        /// <summary>Cell columns in bullet-impact.png (the 4 spark frames are in the top row; the image is 1254 px, so cells are 313.5 px).</summary>
        [Min(1)] public int ImpactSheetColumns = 4;

        /// <summary>Cell rows in bullet-impact.png (only the top row has frames).</summary>
        [Min(1)] public int ImpactSheetRows = 4;

        /// <summary>Spark frames played, taken from the left of the top row.</summary>
        [Min(1)] public int ImpactFrameCount = 4;

        /// <summary>Pixels per world unit of the spark cells: 300 makes a cell about 1 unit, the bright burst about 0.5.</summary>
        [Min(1f)] public float ImpactPixelsPerUnit = 300f;

        /// <summary>Frames per second of the spark; 16 plays the 4 frames in a quarter of a second.</summary>
        [Min(0.1f)] public float ImpactFps = 16f;

        // ---- Story 016: menu art, shared button and panel skin (UiSkin) ----
        // Like the earlier blocks, these defaults apply when an older GameConfig.asset lacks the fields.
        // Sizes and positions are reference pixels: pixels on a screen UiReferenceHeight tall, scaled with the screen
        // height, so the layout keeps its proportions at any resolution. Every number is a first guess to tune.

        /// <summary>Full-screen picture behind the room menu while not in a room (Art/UI/menu-background.png). Set by LastWaveSetup.</summary>
        public Texture2D MenuBackgroundTexture;

        /// <summary>Game name logo above the menu buttons (Art/UI/logo-name.png; its empty margins are trimmed at load). Set by LastWaveSetup.</summary>
        public Texture2D MenuLogoTexture;

        /// <summary>Button picture, normal state (Art/UI/button.png). Must be Read/Write (LastWaveSetup sets it) so its margins can be trimmed.</summary>
        public Texture2D UiButtonTexture;

        /// <summary>Button picture while the mouse is over it (Art/UI/button-hover-state.png). Same size as <see cref="UiButtonTexture"/>.</summary>
        public Texture2D UiButtonHoverTexture;

        /// <summary>Button picture while it is held down (Art/UI/button-pressed-state.png). Same size as <see cref="UiButtonTexture"/>.</summary>
        public Texture2D UiButtonPressedTexture;

        /// <summary>Panel picture behind the menu rows (Art/UI/dialog-panel.png). Must be Read/Write (LastWaveSetup sets it).</summary>
        public Texture2D UiPanelTexture;

        /// <summary>Screen height, in pixels, at which the reference-pixel sizes below are drawn 1:1 (1080). A 720 px screen draws them at 2/3.</summary>
        [Min(1f)] public float UiReferenceHeight = 1080f;

        /// <summary>Where the logo is drawn, in reference pixels from the top-left (x, y, width, height). The logo keeps its aspect inside this box.</summary>
        public Rect MenuLogoRect = new Rect(60f, 110f, 500f, 150f);

        /// <summary>Top-left corner of the menu panel, in reference pixels. Left of the base in the menu picture, under the logo.</summary>
        public Vector2 MenuPanelPosition = new Vector2(80f, 300f);

        /// <summary>Menu panel width in reference pixels. Its height follows the rows it holds.</summary>
        [Min(1f)] public float MenuPanelWidth = 460f;

        /// <summary>Space between the panel edge and its rows, in reference pixels (x left and right, y top and bottom); keeps the rows inside the panel frame.</summary>
        public Vector2 MenuPanelPadding = new Vector2(48f, 46f);

        /// <summary>Height of a menu button, in reference pixels.</summary>
        [Min(1f)] public float MenuButtonHeight = 72f;

        /// <summary>Height of the room code field, in reference pixels.</summary>
        [Min(1f)] public float MenuFieldHeight = 52f;

        /// <summary>Smallest height of a text row, in reference pixels (longer text wraps and the row grows).</summary>
        [Min(1f)] public float MenuLabelHeight = 32f;

        /// <summary>Vertical gap between two menu rows, in reference pixels.</summary>
        [Min(0f)] public float MenuRowSpacing = 14f;

        /// <summary>Font size of menu button text, in reference pixels.</summary>
        [Min(8)] public int MenuButtonFontSize = 28;

        /// <summary>Font size of menu text rows (room code hint, players, messages), in reference pixels.</summary>
        [Min(8)] public int MenuLabelFontSize = 22;

        /// <summary>Font size inside the room code field, in reference pixels.</summary>
        [Min(8)] public int MenuFieldFontSize = 26;

        /// <summary>Colour of the error/status message under the menu rows.</summary>
        public Color MenuMessageColor = new Color(1f, 0.6f, 0.6f, 1f);

        /// <summary>
        /// Reference pixels per pixel of the trimmed button picture. Sets how big the 9-slice corners are drawn: the
        /// trimmed button is about 1230 x 300 px, so 0.3 draws it about 370 x 90 at its natural size.
        /// </summary>
        [Min(0.01f)] public float UiButtonArtScale = 0.3f;

        /// <summary>Reference pixels per pixel of the trimmed panel picture (about 1190 px square), so the frame and rivets keep a sensible size.</summary>
        [Min(0.01f)] public float UiPanelArtScale = 0.35f;

        /// <summary>9-slice border of the trimmed button picture, in its pixels (x left, y right, z top, w bottom): the rounded ends are never stretched.</summary>
        public Vector4 UiButtonBorder = new Vector4(80f, 80f, 70f, 70f);

        /// <summary>9-slice border of the trimmed panel picture, in its pixels (x left, y right, z top, w bottom): the frame and corner rivets are never stretched.</summary>
        public Vector4 UiPanelBorder = new Vector4(110f, 110f, 110f, 110f);

        /// <summary>Colour of button text and menu text: light, to read on the dark button and panel art.</summary>
        public Color UiTextColor = Color.white;

        /// <summary>Colour of the shadow drawn under button and menu text.</summary>
        public Color UiTextShadowColor = new Color(0f, 0f, 0f, 0.85f);

        /// <summary>Offset of the text shadow, in reference pixels (right, down).</summary>
        public Vector2 UiTextShadowOffset = new Vector2(2f, 2f);

        // ---- Story 017: HUD frames, plates and icons (UiSkin.Frame / Bar / Icon) ----
        // Frame pictures are trimmed to their opaque pixels at runtime, scaled to the bar height and 9-sliced across the
        // width. Borders and fill insets are in trimmed-picture pixels (x left, y right, z top, w bottom), measured by eye
        // from the art (player and base frames about 1180 x 125 with a 12 px line; boss about 1225 x 158; wave plate about
        // 1180 x 243; room plate about 1310 x 155). Fill insets sit a little inside the frame line so the line covers the
        // fill's edge. Other sizes are reference pixels. Every number is a first guess to tune.

        /// <summary>Frame of the bar above each player (Art/UI/player-health-bar.png). Must be Read/Write (LastWaveSetup sets it).</summary>
        public Texture2D HudPlayerBarTexture;

        /// <summary>Frame of the base bar, with the shield at its left end (Art/UI/base-health-bar.png). Must be Read/Write.</summary>
        public Texture2D HudBaseBarTexture;

        /// <summary>Frame of a mini-boss or boss bar, spiked with a skull at its left end (Art/UI/boss-health-bar.png). Must be Read/Write.</summary>
        public Texture2D HudBossBarTexture;

        /// <summary>Dark plate behind the "Wave n / total" label (Art/UI/wave.png; the story calls it wave-counter.png). Must be Read/Write.</summary>
        public Texture2D HudWavePlateTexture;

        /// <summary>Plate behind the room code and player count in the Game scene (Art/UI/room-info.png). Must be Read/Write.</summary>
        public Texture2D HudRoomPlateTexture;

        /// <summary>The HUD icon sheet (Art/UI/hud-icons.png), cut into <see cref="HudIconGrid"/> cells in <c>UiSkin.HudIcon</c> order. Must be Read/Write.</summary>
        public Texture2D HudIconsTexture;

        /// <summary>Columns and rows of equal cells the icon sheet is divided into; each icon is trimmed to its opaque pixels within its cell (the sheet is 4 x 4 cells of about 313 px, top two rows used).</summary>
        public Vector2Int HudIconGrid = new Vector2Int(4, 4);

        /// <summary>9-slice border of the player bar frame.</summary>
        public Vector4 HudPlayerBarBorder = new Vector4(16f, 16f, 16f, 16f);

        /// <summary>Inner (fill) area of the player bar frame, as insets from its edges.</summary>
        public Vector4 HudPlayerBarFillInset = new Vector4(9f, 9f, 9f, 9f);

        /// <summary>9-slice border of the base bar frame; the left one holds the shield.</summary>
        public Vector4 HudBaseBarBorder = new Vector4(92f, 16f, 16f, 16f);

        /// <summary>Inner (fill) area of the base bar frame; the fill starts right of the shield.</summary>
        public Vector4 HudBaseBarFillInset = new Vector4(84f, 9f, 9f, 9f);

        /// <summary>9-slice border of the boss bar frame; the left one holds the skull, the others the spikes.</summary>
        public Vector4 HudBossBarBorder = new Vector4(80f, 40f, 38f, 38f);

        /// <summary>Inner (fill) area of the boss bar frame; the fill starts right of the skull.</summary>
        public Vector4 HudBossBarFillInset = new Vector4(74f, 28f, 28f, 30f);

        /// <summary>9-slice border of the wave plate: the chamfered corners and the red line at the bottom.</summary>
        public Vector4 HudWavePlateBorder = new Vector4(40f, 40f, 32f, 48f);

        /// <summary>9-slice border of the room plate: its rounded corners.</summary>
        public Vector4 HudRoomPlateBorder = new Vector4(34f, 34f, 34f, 34f);

        /// <summary>Size of the wave plate at the top centre, in reference pixels (width, height).</summary>
        public Vector2 HudWavePlateSize = new Vector2(320f, 64f);

        /// <summary>Gap between the top of the screen and the wave plate, in reference pixels.</summary>
        [Min(0f)] public float HudWavePlateTop = 12f;

        /// <summary>How far the wave text is lifted from the plate's centre, in reference pixels, so it sits above the red line.</summary>
        public float HudWaveTextLift = 4f;

        /// <summary>Gap between the wave plate and the first boss bar's text, and between two boss bars, in reference pixels.</summary>
        [Min(0f)] public float HudBossBarSpacing = 6f;

        /// <summary>Space between the room plate's left edge and its text, in reference pixels.</summary>
        [Min(0f)] public float HudRoomTextPadding = 24f;

        /// <summary>Side of a HUD icon over a player, in reference pixels.</summary>
        [Min(1f)] public float HudIconSize = 36f;

        /// <summary>Gap between a player's bar and the icon above it (E, wrench, hand, skull), in reference pixels.</summary>
        [Min(0f)] public float HudIconAboveGap = 4f;

        /// <summary>Gap between a player's bar and the icon left of it (hourglass while down, regen while healing), in reference pixels.</summary>
        [Min(0f)] public float HudIconSideGap = 4f;

        /// <summary>
        /// Seconds the regen icon stays up after the last small health rise. Regen runs on the server only, so every
        /// machine infers it from the synced health: a rise of at most (regen per second x this) + 1 points while standing,
        /// from above 0 (revive and respawn start at 0), and below max health.
        /// </summary>
        [Min(0.05f)] public float HudRegenIconHoldSeconds = 0.6f;

        /// <summary>Side of the crosshair drawn at the mouse during play (the OS cursor is hidden), in reference pixels.</summary>
        [Min(1f)] public float CrosshairSize = 40f;

        /// <summary>IMGUI depth of the crosshair: lower draws on top. Below the end screen (-1000) and the upgrade cards (-100).</summary>
        public int CrosshairGuiDepth = -2000;

        /// <summary>The look and hitbox for an enemy kind.</summary>
        public EnemyLookDefinition GetEnemyLook(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.MiniBoss: return EnemyMiniBossLook;
                case EnemyKind.Boss: return EnemyBossLook;
                default: return EnemyNormalLook;
            }
        }

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
