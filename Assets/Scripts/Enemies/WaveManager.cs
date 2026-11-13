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
        private int _wave;   // 0 until wave 1 starts; on every machine
        private int _total;
        private bool _allCleared;

        /// <summary>Server only: a wave began (1-based wave number).</summary>
        public event Action<int> OnWaveStarted;

        /// <summary>Server only: every enemy of a wave is dead (1-based wave number).</summary>
        public event Action<int> OnWaveCleared;

        /// <summary>
        /// Server only: the last wave's enemies are all dead. This is the "win" event. Nothing listens yet:
        /// story 006 adds the boss condition and story 007 the win screen.
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
                for (int i = 0; i < definition.Count; i++)
                {
                    SpawnEnemy(definition, i);
                    if (i < definition.Count - 1) yield return new WaitForSeconds(definition.SpawnInterval);
                }

                // Dead or despawned (destroyed objects compare equal to null).
                _alive.RemoveAll(IsGone);
                while (_alive.Count > 0)
                {
                    yield return null;
                    _alive.RemoveAll(IsGone);
                }

                OnWaveCleared?.Invoke(wave);
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
            Vector2 position = (Vector2)_basePoint.position
                + WavePlanner.EdgePoint(edge, _config.MapHalfExtents, UnityEngine.Random.Range(-1f, 1f));
            var enemy = NetworkObject.InstantiateAndSpawn(_enemyPrefab.gameObject, _network,
                destroyWithScene: true, position: position, rotation: Quaternion.identity).GetComponent<Enemy>();
            enemy.Launch(_basePoint, definition.Speed, definition.Health);
            _alive.Add(enemy);
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
        }
    }
}
