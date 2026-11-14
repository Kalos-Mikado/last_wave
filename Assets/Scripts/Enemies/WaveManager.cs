using System;
using System.Collections;
using System.Collections.Generic;
using LastWave.Config;
using LastWave.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace LastWave.Enemies
{
    /// <summary>
    /// Plain wave-selection logic with no Unity scene or network dependency, so it can be unit tested later:
    /// which wave row applies, which edge the n-th enemy spawns on, and where on that edge.
    /// </summary>
    public static class WavePlanner
    {
        private const SpawnEdges AllEdges = SpawnEdges.Top | SpawnEdges.Bottom | SpawnEdges.Left | SpawnEdges.Right;

        /// <summary>Returns the table row for a wave.</summary>
        /// <param name="waves">The wave table (row 0 is wave 1).</param>
        /// <param name="waveNumber">1-based wave number.</param>
        /// <exception cref="ArgumentOutOfRangeException">The wave number is outside the table.</exception>
        public static WaveDefinition GetWave(IReadOnlyList<WaveDefinition> waves, int waveNumber)
        {
            if (waveNumber < 1 || waveNumber > waves.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(waveNumber), $"Wave {waveNumber} is not in a table of {waves.Count} waves.");
            }
            return waves[waveNumber - 1];
        }

        /// <summary>
        /// The boss entry for a wave: the first entry whose <see cref="BossDefinition.Wave"/> matches, or null
        /// when the wave has none (or the list is null). One boss per wave.
        /// </summary>
        public static BossDefinition FindBoss(IReadOnlyList<BossDefinition> bosses, int waveNumber)
        {
            if (bosses == null) return null;
            for (int i = 0; i < bosses.Count; i++)
            {
                if (bosses[i] != null && bosses[i].Wave == waveNumber) return bosses[i];
            }
            return null;
        }

        /// <summary>
        /// Whether a wave is over. A wave with a game-ending boss ends exactly when that boss is dead
        /// (its escorts do not matter); any other wave ends when every enemy has spawned and none is alive.
        /// </summary>
        /// <param name="allSpawned">Every enemy of the wave has been spawned.</param>
        /// <param name="aliveCount">Enemies of the wave still alive (the boss included).</param>
        /// <param name="hasFinalBoss">The wave has a boss whose death ends the game.</param>
        /// <param name="finalBossAlive">That boss is alive.</param>
        public static bool IsWaveOver(bool allSpawned, int aliveCount, bool hasFinalBoss, bool finalBossAlive)
        {
            if (hasFinalBoss) return !finalBossAlive;
            return allSpawned && aliveCount == 0;
        }

        /// <summary>
        /// The edge for the enemy at <paramref name="enemyIndex"/> (0-based): the set edges in order
        /// Top, Bottom, Left, Right, taken round-robin so every edge gets an equal share.
        /// An empty set counts as all four edges.
        /// </summary>
        public static SpawnEdges EdgeForSpawn(SpawnEdges edges, int enemyIndex)
        {
            if (edges == SpawnEdges.None) edges = AllEdges;
            var set = new List<SpawnEdges>(4);
            foreach (var edge in new[] { SpawnEdges.Top, SpawnEdges.Bottom, SpawnEdges.Left, SpawnEdges.Right })
            {
                if ((edges & edge) != 0) set.Add(edge);
            }
            return set[enemyIndex % set.Count];
        }

        /// <summary>
        /// A point on one edge of the rectangle centred on the origin.
        /// </summary>
        /// <param name="edge">A single edge (not a combination).</param>
        /// <param name="halfExtents">Half width and half height of the map.</param>
        /// <param name="along">-1 to 1: position along the edge, 0 is its middle.</param>
        public static Vector2 EdgePoint(SpawnEdges edge, Vector2 halfExtents, float along)
        {
            switch (edge)
            {
                case SpawnEdges.Top: return new Vector2(along * halfExtents.x, halfExtents.y);
                case SpawnEdges.Bottom: return new Vector2(along * halfExtents.x, -halfExtents.y);
                case SpawnEdges.Left: return new Vector2(-halfExtents.x, along * halfExtents.y);
                case SpawnEdges.Right: return new Vector2(halfExtents.x, along * halfExtents.y);
                default: throw new ArgumentException($"{edge} is not a single edge.", nameof(edge));
            }
        }
    }

    /// <summary>
    /// Runs the 10 enemy waves in the Game scene. Scene object, not a NetworkObject: the server (host)
    /// spawns every enemy and decides when waves start and end; clients never spawn. The current wave
    /// number reaches every machine through a Netcode named message (sent on every change, and on request
    /// when a client's scene finishes loading, so a late joiner is caught up).
    /// Wave 1 starts after <see cref="GameConfig.FirstWaveDelaySeconds"/>; waves are separated by
    /// <see cref="GameConfig.WaveRestSeconds"/>. All numbers come from <see cref="GameConfig"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var waves = FindFirstObjectByType&lt;WaveManager&gt;();
    /// waves.OnAllWavesCleared += () => Debug.Log("win");   // server only
    /// </code>
    /// </example>
    public sealed class WaveManager : MonoBehaviour
    {
        private const string StateMessage = "LastWave.WaveState";
        private const string StateRequestMessage = "LastWave.WaveStateRequest";

        [SerializeField] private Enemy _enemyPrefab;

        // Story 004 turns this marker into the base with health and repair. For now it is only the point
        // enemies walk to; its position in the scene is the base position.
        [SerializeField] private Transform _basePoint;
        [SerializeField] private SpriteRenderer _baseMarker;

        private readonly List<Enemy> _alive = new List<Enemy>();
        private GameConfig _config;
        private NetworkManager _network;
        private GUIStyle _labelStyle;
        private GUIStyle _bossStyle;
        private int _wave;   // 0 until wave 1 starts; on every machine
        private int _total;
        private bool _allCleared;

        /// <summary>Server only: a wave began (1-based wave number).</summary>
        public event Action<int> OnWaveStarted;

        /// <summary>Server only: every enemy of a wave is dead (1-based wave number).</summary>
        public event Action<int> OnWaveCleared;

        /// <summary>
        /// Server only: the team has won. Raised when the game-ending boss (<see cref="EnemyKind.Boss"/>) is dead
        /// (its escorts are despawned first, and <see cref="OnWaveCleared"/> fires for that wave), or, when the
        /// config has no such boss, when the last wave's enemies are all dead. Every machine learns it through
        /// the state message ("All waves cleared"). Nothing listens yet: story 007 adds the win screen.
        /// </summary>
        public event Action OnAllWavesCleared;

        /// <summary>Current wave, 1-based; 0 before wave 1. Same value on every machine (clients: via message).</summary>
        public int CurrentWave => _wave;

        private void Start()
        {
            _config = GameConfig.Instance;
            _total = _config.Waves.Length;

            if (_baseMarker != null)
            {
                // The editor script cannot reach the runtime square sprite, so the marker gets its look here.
                _baseMarker.sprite = PlayerMovement.SquareSprite;
                _baseMarker.color = _config.BaseColor;
            }

            _network = NetworkManager.Singleton;
            if (_network == null || !_network.IsListening)
            {
                Debug.LogWarning("WaveManager: no running NetworkManager (game scene opened without a room); no waves.");
                return;
            }

            if (_network.IsServer)
            {
                // The host receives its own broadcasts through loopback, so it registers the state handler too.
                _network.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, HandleState);
                _network.CustomMessagingManager.RegisterNamedMessageHandler(StateRequestMessage, HandleStateRequest);
                StartCoroutine(RunWaves());
            }
            else
            {
                _network.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, HandleState);
                // Ask for the current state: this client may have joined after the last broadcast.
                using var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
                writer.WriteValueSafe(0);
                _network.CustomMessagingManager.SendNamedMessage(StateRequestMessage, NetworkManager.ServerClientId, writer);
            }
        }

        private void OnDestroy()
        {
            // During shutdown the messaging manager may already be gone.
            var messaging = _network != null ? _network.CustomMessagingManager : null;
            if (messaging == null) return;
            messaging.UnregisterNamedMessageHandler(StateMessage);
            if (_network.IsServer) messaging.UnregisterNamedMessageHandler(StateRequestMessage);
        }

        private IEnumerator RunWaves()
        {
            yield return new WaitForSeconds(_config.FirstWaveDelaySeconds);

            for (int wave = 1; wave <= _total; wave++)
            {
                _wave = wave;
                BroadcastState();
                OnWaveStarted?.Invoke(wave);

                var definition = WavePlanner.GetWave(_config.Waves, wave);

                // Story 006: the wave's boss (if any) comes first, then the table's enemies follow as its escorts.
                var boss = WavePlanner.FindBoss(_config.Bosses, wave);
                bool bossEndsGame = boss != null && boss.Kind == EnemyKind.Boss;
                Enemy bossEnemy = boss != null ? SpawnBoss(boss) : null;

                for (int i = 0; i < definition.Count; i++)
                {
                    if (bossEndsGame && IsGone(bossEnemy)) break; // boss already dead: no more escorts
                    SpawnEnemy(definition, i);
                    if (i < definition.Count - 1) yield return new WaitForSeconds(definition.SpawnInterval);
                }

                // Dead or despawned (destroyed objects compare equal to null).
                _alive.RemoveAll(IsGone);
                while (!WavePlanner.IsWaveOver(true, _alive.Count, bossEndsGame && bossEnemy != null, bossEnemy != null && !IsGone(bossEnemy)))
                {
                    yield return null;
                    _alive.RemoveAll(IsGone);
                }

                if (bossEndsGame) DespawnEscorts(); // the boss is dead: the team has won, nothing keeps attacking
                OnWaveCleared?.Invoke(wave);
                if (bossEndsGame) break; // win now, even when the config put the boss before the last table row
                if (wave < _total) yield return new WaitForSeconds(_config.WaveRestSeconds);
            }

            _allCleared = true;
            BroadcastState();
            OnAllWavesCleared?.Invoke();
        }

        private static bool IsGone(Enemy enemy) => enemy == null || !enemy.IsSpawned;

        private void SpawnEnemy(WaveDefinition definition, int index)
        {
            var edge = WavePlanner.EdgeForSpawn(definition.Edges, index);
            SpawnAt(edge, UnityEngine.Random.Range(-1f, 1f), definition.Speed, definition.Health);
        }

        // Same enemy prefab as every wave enemy; only the numbers differ.
        private Enemy SpawnBoss(BossDefinition boss)
        {
            var edge = WavePlanner.EdgeForSpawn(boss.Edge, 0);
            return SpawnAt(edge, 0f, boss.Speed, boss.Health, boss.Kind, boss.Size, boss.PlayerDamage, boss.BaseDamage);
        }

        private Enemy SpawnAt(SpawnEdges edge, float along, float speed, int health, EnemyKind kind = EnemyKind.Normal,
            float size = 1f, int playerDamage = -1, int baseDamage = -1)
        {
            Vector2 position = (Vector2)_basePoint.position + WavePlanner.EdgePoint(edge, _config.MapHalfExtents, along);
            var enemy = NetworkObject.InstantiateAndSpawn(_enemyPrefab.gameObject, _network,
                destroyWithScene: true, position: position, rotation: Quaternion.identity).GetComponent<Enemy>();
            enemy.Launch(_basePoint, speed, health, kind, size, playerDamage, baseDamage);
            _alive.Add(enemy);
            return enemy;
        }

        // Server: the boss died, so the escorts are removed (own call: they would otherwise keep hitting after the win).
        private void DespawnEscorts()
        {
            foreach (var enemy in _alive)
            {
                if (!IsGone(enemy)) enemy.NetworkObject.Despawn();
            }
            _alive.Clear();
        }

        // Server: tell every machine (the host included, through loopback) the current state.
        private void BroadcastState()
        {
            using var writer = CreateStateWriter();
            _network.CustomMessagingManager.SendNamedMessageToAll(StateMessage, writer);
        }

        private void HandleStateRequest(ulong senderClientId, FastBufferReader payload)
        {
            using var writer = CreateStateWriter();
            _network.CustomMessagingManager.SendNamedMessage(StateMessage, senderClientId, writer);
        }

        private FastBufferWriter CreateStateWriter()
        {
            var writer = new FastBufferWriter(sizeof(int) * 3, Allocator.Temp);
            writer.WriteValueSafe(_wave);
            writer.WriteValueSafe(_total);
            writer.WriteValueSafe(_allCleared ? 1 : 0);
            return writer;
        }

        private void HandleState(ulong senderClientId, FastBufferReader payload)
        {
            payload.ReadValueSafe(out _wave);
            payload.ReadValueSafe(out _total);
            payload.ReadValueSafe(out int cleared);
            _allCleared = cleared != 0;
        }

        private void OnGUI()
        {
            if (_config == null || Event.current.type != EventType.Repaint) return;
            if (_network == null || !_network.IsListening) return;

            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = _config.WaveLabelFontSize,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };

            // "Get ready" covers the first-wave delay; the wave number stays up through the rest periods.
            string text = _allCleared ? "All waves cleared"
                : _wave == 0 ? "Get ready..."
                : $"Wave {_wave} / {_total}";
            GUI.Label(new Rect(0f, 8f, Screen.width, _config.WaveLabelFontSize * 2f), text, _labelStyle);

            DrawBossBars();
        }

        // Story 006: one wide bar per living mini-boss or boss, centred under the wave label, on every machine.
        // Reads the NetworkVariable-backed numbers on Enemy, so the host and clients draw the same thing.
        private void DrawBossBars()
        {
            _bossStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.LowerCenter,
                fontSize = _config.BossBarFontSize,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };

            float textHeight = _config.BossBarFontSize * 1.5f;
            float y = 8f + _config.WaveLabelFontSize * 1.6f;
            var previousColor = GUI.color;
            foreach (var boss in Enemy.Bosses)
            {
                if (boss == null || !boss.IsSpawned || boss.ShownMaxHealth <= 0) continue; // not synced yet, or gone

                string title = boss.Kind == EnemyKind.Boss ? _config.BossLabel : _config.MiniBossLabel;
                float fraction = Mathf.Clamp01((float)boss.ShownHealth / boss.ShownMaxHealth);
                var size = _config.BossBarSize;
                float x = (Screen.width - size.x) * 0.5f;

                GUI.Label(new Rect(x, y, size.x, textHeight), $"{title}  {boss.ShownHealth} / {boss.ShownMaxHealth}", _bossStyle);
                var back = new Rect(x, y + textHeight, size.x, size.y);
                GUI.color = _config.BossBarBackColor;
                GUI.DrawTexture(back, Texture2D.whiteTexture);
                GUI.color = _config.BossBarFillColor;
                GUI.DrawTexture(new Rect(back.x, back.y, back.width * fraction, back.height), Texture2D.whiteTexture);
                GUI.color = previousColor;

                y = back.yMax + 6f; // the next bar, if two are alive, sits below
            }
            GUI.color = previousColor;
        }
    }
}
