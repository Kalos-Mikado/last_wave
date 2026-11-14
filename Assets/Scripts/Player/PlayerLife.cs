using System;
using System.Collections.Generic;
using LastWave.Base;
using LastWave.Config;
using LastWave.Enemies;
using LastWave.Flow;
using Unity.Netcode;
using UnityEngine;

namespace LastWave.Player
{
    /// <summary>A player's life state. Written by the server, readable by everyone.</summary>
    public enum PlayerState : byte
    {
        /// <summary>On their feet: can move, shoot, take damage and help others.</summary>
        Standing = 0,
        /// <summary>Health reached 0: cannot move or shoot, waits for a friend to revive them or dies on a timer.</summary>
        Downed = 1,
        /// <summary>Not in play (hidden, no collider) until the current wave ends, then respawns.</summary>
        Dead = 2,
    }

    /// <summary>
    /// Plain downed/revive/respawn arithmetic with no Unity scene or network dependency, so it can be unit
    /// tested later. Design record: <c>design/game-brief.md</c> (Feature 5, Goal and fail state).
    /// </summary>
    public static class PlayerLifeRules
    {
        /// <summary>Lowest share of max health a revived player stands up with (brief: 10%).</summary>
        public const float MinReviveFraction = 0.10f;

        /// <summary>Highest share of max health a revived player stands up with (brief: 20%).</summary>
        public const float MaxReviveFraction = 0.20f;

        /// <summary>The revive health fraction forced into the brief's 10-20% range.</summary>
        public static float ClampReviveFraction(float fraction)
        {
            return Mathf.Clamp(fraction, MinReviveFraction, MaxReviveFraction);
        }

        /// <summary>Whole-point health after a revive: the clamped fraction of max, rounded, at least 1.</summary>
        public static int ReviveHealth(int maxHealth, float fraction)
        {
            return FractionOfMax(maxHealth, ClampReviveFraction(fraction));
        }

        /// <summary>Whole-point health after a respawn: the fraction (0 to 1) of max, rounded, at least 1.</summary>
        public static int RespawnHealth(int maxHealth, float fraction)
        {
            return FractionOfMax(maxHealth, Mathf.Clamp01(fraction));
        }

        private static int FractionOfMax(int maxHealth, float fraction)
        {
            int max = Mathf.Max(1, maxHealth);
            return Mathf.Clamp(Mathf.RoundToInt(max * fraction), 1, max);
        }

        /// <summary>Revive progress, 0 to 1, for the seconds held so far.</summary>
        public static float ReviveFraction(float heldSeconds, float holdSeconds)
        {
            return Mathf.Clamp01(heldSeconds / Mathf.Max(holdSeconds, 0.01f));
        }

        /// <summary>True when the key has been held long enough to revive.</summary>
        public static bool IsReviveComplete(float heldSeconds, float holdSeconds)
        {
            return heldSeconds >= Mathf.Max(holdSeconds, 0.01f);
        }

        /// <summary>Seconds left before a downed player dies after <paramref name="deltaSeconds"/>, never below 0.</summary>
        public static float AdvanceDowned(float remainingSeconds, float deltaSeconds)
        {
            return Mathf.Max(0f, remainingSeconds - Mathf.Max(0f, deltaSeconds));
        }

        /// <summary>
        /// The state table: Standing to Downed (health 0), Downed to Standing (revived), Downed to Dead
        /// (timed out), Dead to Standing (respawn). Every other change is refused.
        /// </summary>
        public static bool CanTransition(PlayerState from, PlayerState to)
        {
            switch (from)
            {
                case PlayerState.Standing: return to == PlayerState.Downed;
                case PlayerState.Downed: return to == PlayerState.Standing || to == PlayerState.Dead;
                case PlayerState.Dead: return to == PlayerState.Standing;
                default: return false;
            }
        }

        /// <summary>The team loses when there is at least one player and every one of them is dead.</summary>
        public static bool AllDead(int playerCount, int deadCount)
        {
            return playerCount > 0 && deadCount == playerCount;
        }
    }

    /// <summary>
    /// A player's life state machine (brief MVP feature 5): standing, downed, dead. The server decides every
    /// change; the state, the downed countdown and the revive progress are server-written NetworkVariables
    /// that every machine reads, and every machine applies the look from them (dimmed when downed, hidden
    /// and without a collider when dead).
    /// <para>
    /// Flow: health reaches 0 (<see cref="PlayerHealth"/> calls <see cref="ServerDown"/>) then Downed. A standing
    /// friend holding E in range (<see cref="PlayerRepair"/> calls <see cref="ServerHoldRevive"/>) for the hold
    /// time revives; the countdown running out kills instead. A Dead player respawns at the base when the wave
    /// ends (<see cref="WaveManager.OnWaveCleared"/>), except after the last wave. Revive is applied before
    /// the timeout check in the same frame, so a revive that finishes on the last frame wins.
    /// </para>
    /// <para>
    /// Only one reviver counts per frame: several friends holding E do not revive faster (guessed).
    /// If the wave ends while a player is Downed, they stay Downed until revived or dead.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// var life = player.GetComponent&lt;PlayerLife&gt;();
    /// life.OnStateChanged += (from, to) => Debug.Log($"{from} to {to}");   // every machine
    /// </code>
    /// </example>
    public sealed class PlayerLife : NetworkBehaviour
    {
        private static readonly List<PlayerLife> AllPlayers = new List<PlayerLife>();

        // State travels as a byte: plain, no enum serializer question.
        private readonly NetworkVariable<byte> _state = new NetworkVariable<byte>(
            (byte)PlayerState.Standing, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _downedRemaining = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _reviveProgress = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private GameConfig _config;
        private PlayerHealth _health;
        private Collider2D _collider;
        private SpriteRenderer _bodyRenderer;
        private SpriteRenderer[] _renderers;
        private Rigidbody2D _body;
        private Unity.Netcode.Components.NetworkTransform _networkTransform;
        private WaveManager _waves;    // server: the one whose OnWaveCleared we listen to
        private float _reviveSeconds;  // server: seconds held so far
        private bool _reviveTouched;   // server: a reviver held E this frame

        /// <summary>Every spawned player on this machine (all machines spawn every player object).</summary>
        public static IReadOnlyList<PlayerLife> All => AllPlayers;

        /// <summary>Current state. Same value on every machine.</summary>
        public PlayerState State => (PlayerState)_state.Value;

        /// <summary>True while standing: the only state that can move, shoot, revive, repair or take damage.</summary>
        public bool IsStanding => State == PlayerState.Standing;

        /// <summary>True while downed (waiting for a revive).</summary>
        public bool IsDowned => State == PlayerState.Downed;

        /// <summary>True while dead (waiting for the wave to end).</summary>
        public bool IsDead => State == PlayerState.Dead;

        /// <summary>Seconds left before a downed player dies; 0 when not downed. Same on every machine (tick-rate fresh).</summary>
        public float DownedRemaining => _downedRemaining.Value;

        /// <summary>Revive progress 0 to 1 while a friend holds E; 0 otherwise. Same on every machine.</summary>
        public float ReviveProgress => _reviveProgress.Value;

        /// <summary>This player's health component.</summary>
        public PlayerHealth Health => _health;

        /// <summary>This player's body collider (disabled while dead).</summary>
        public Collider2D BodyCollider => _collider;

        /// <summary>Raised on every machine when the state changes (previous, current).</summary>
        public event Action<PlayerState, PlayerState> OnStateChanged;

        /// <summary>Caches components, registers the player and applies the current look.</summary>
        public override void OnNetworkSpawn()
        {
            _config = GameConfig.Instance;
            _health = GetComponent<PlayerHealth>();
            _collider = GetComponent<Collider2D>();
            _bodyRenderer = GetComponent<SpriteRenderer>();
            _renderers = GetComponentsInChildren<SpriteRenderer>(true);
            _body = GetComponent<Rigidbody2D>();
            _networkTransform = GetComponent<Unity.Netcode.Components.NetworkTransform>();

            _reviveSeconds = 0f;
            _reviveTouched = false;
            if (!AllPlayers.Contains(this)) AllPlayers.Add(this);
            _state.OnValueChanged += HandleStateChanged;
            ApplyLook(); // late joiners: the initial value does not raise OnValueChanged
        }

        /// <summary>Unregisters the player.</summary>
        public override void OnNetworkDespawn()
        {
            AllPlayers.Remove(this);
            _state.OnValueChanged -= HandleStateChanged;
            if (_waves != null) _waves.OnWaveCleared -= HandleWaveCleared;
            _waves = null;
        }

        /// <summary>
        /// Server only: health reached 0, so a standing player goes down and the death countdown starts.
        /// Called by <see cref="PlayerHealth"/>. Ignored on a client and when not standing.
        /// </summary>
        public void ServerDown()
        {
            if (!IsServer || !IsSpawned) return;
            if (!SetState(PlayerState.Downed)) return;
            _downedRemaining.Value = _config.DownedTimeoutSeconds;
            ResetRevive();
        }

        /// <summary>
        /// Server only: a standing friend in range is holding E on this downed player for
        /// <paramref name="deltaSeconds"/>. Counts once per frame; at the hold time the player stands up with
        /// the configured share of max health. Progress resets by itself in any frame nobody holds.
        /// Called by <see cref="PlayerRepair"/>, which checks the reviver is standing and in range.
        /// </summary>
        /// <param name="deltaSeconds">Frame time in seconds.</param>
        public void ServerHoldRevive(float deltaSeconds)
        {
            if (!IsServer || !IsSpawned || State != PlayerState.Downed || _reviveTouched) return;
            if (GameOutcome.IsOver) return; // story 007: no reviving after the game ended
            _reviveTouched = true;
            _reviveSeconds += Mathf.Max(0f, deltaSeconds);
            if (PlayerLifeRules.IsReviveComplete(_reviveSeconds, _config.ReviveHoldSeconds)) Revive();
        }

        /// <summary>
        /// Teleports the owner's player to the respawn point. Server calls it, the owner runs it, because
        /// movement is owner-authoritative (an owner-authority NetworkTransform ignores a server-side move).
        /// </summary>
        /// <param name="position">World position to appear at.</param>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        public void RespawnRpc(Vector2 position)
        {
            var target = new Vector3(position.x, position.y, transform.position.z);
            transform.position = target;
            if (_body != null)
            {
                _body.position = position;
                _body.linearVelocity = Vector2.zero;
            }
            if (_networkTransform != null) _networkTransform.Teleport(target, transform.rotation, transform.localScale);
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer) return;

            if (_waves == null)
            {
                // The wave manager only exists in the Game scene; players carry over from the Menu scene.
                _waves = FindFirstObjectByType<WaveManager>();
                if (_waves != null) _waves.OnWaveCleared += HandleWaveCleared;
            }

            if (State != PlayerState.Downed) return;
            if (GameOutcome.IsOver) return; // story 007: the downed countdown is frozen while the end screen is up

            // PlayerRepair (execution order -50) already ran this frame, so _reviveTouched is current.
            if (!_reviveTouched) _reviveSeconds = 0f;
            _reviveTouched = false;
            WriteIfChanged(_reviveProgress, PlayerLifeRules.ReviveFraction(_reviveSeconds, _config.ReviveHoldSeconds));

            _downedRemaining.Value = PlayerLifeRules.AdvanceDowned(_downedRemaining.Value, Time.deltaTime);
            if (_downedRemaining.Value <= 0f) Die();
        }

        private void Revive()
        {
            // Health first, so nobody ever sees a standing player at 0 health.
            _health.ServerSetHealth(PlayerLifeRules.ReviveHealth(_config.MaxPlayerHealth, _config.ReviveHealthFraction));
            if (!SetState(PlayerState.Standing)) return;
            _downedRemaining.Value = 0f;
            ResetRevive();
        }

        private void Die()
        {
            if (!SetState(PlayerState.Dead)) return;
            _downedRemaining.Value = 0f;
            ResetRevive();
            // The team loses when everyone is dead: TeamDefeat watches for that.
        }

        // Server: a wave was cleared. Dead players come back, unless that was the last wave.
        private void HandleWaveCleared(int wave)
        {
            if (!IsServer || !IsSpawned || State != PlayerState.Dead) return;
            if (GameOutcome.IsOver) return; // story 007: nobody respawns after the game ended
            if (wave >= _config.Waves.Length) return; // all waves cleared: nothing respawns

            _health.ServerSetHealth(PlayerLifeRules.RespawnHealth(_config.MaxPlayerHealth, _config.RespawnHealthFraction));
            if (!SetState(PlayerState.Standing)) return;
            RespawnRpc(RespawnPosition());
        }

        /// <summary>
        /// Server only (story 007): a new game. Full health, Standing (from Downed or Dead), downed countdown and
        /// revive progress cleared, and the owner's player teleported back to its start position. Ignored on a client.
        /// Call it after the game-over state has been cleared.
        /// </summary>
        public void ServerResetForNewGame()
        {
            if (!IsServer || !IsSpawned) return;

            // Health first, so nobody ever sees a standing player at 0 health (same order as Revive).
            _health.ServerSetHealth(_config.MaxPlayerHealth);
            if (State != PlayerState.Standing) SetState(PlayerState.Standing); // Downed and Dead may stand up
            _downedRemaining.Value = 0f;
            ResetRevive();
            RespawnRpc(StartPosition());
        }

        // Same slot formula as PlayerMovement.OnNetworkSpawn (that file is outside story 007's list, so it is repeated here).
        private Vector2 StartPosition()
        {
            int slot = (int)(OwnerClientId % (ulong)_config.MaxPlayers);
            float centre = (_config.MaxPlayers - 1) * 0.5f;
            return new Vector2((slot - centre) * _config.SpawnSpacing, 0f);
        }

        private Vector2 RespawnPosition()
        {
            var baseHealth = FindFirstObjectByType<BaseHealth>();
            Vector2 origin = baseHealth != null ? baseHealth.Position : Vector2.zero;
            int slot = (int)(OwnerClientId % (ulong)_config.MaxPlayers);
            float centre = (_config.MaxPlayers - 1) * 0.5f;
            return origin + _config.RespawnOffset + Vector2.right * ((slot - centre) * _config.SpawnSpacing);
        }

        private bool SetState(PlayerState next)
        {
            var current = (PlayerState)_state.Value;
            if (!PlayerLifeRules.CanTransition(current, next))
            {
                Debug.LogWarning($"PlayerLife: refused {current} to {next}.");
                return false;
            }
            _state.Value = (byte)next;
            return true;
        }

        private void ResetRevive()
        {
            _reviveSeconds = 0f;
            _reviveTouched = false;
            WriteIfChanged(_reviveProgress, 0f);
        }

        private static void WriteIfChanged(NetworkVariable<float> variable, float value)
        {
            if (!Mathf.Approximately(variable.Value, value)) variable.Value = value;
        }

        // Every machine, the server included.
        private void HandleStateChanged(byte previous, byte current)
        {
            ApplyLook();
            OnStateChanged?.Invoke((PlayerState)previous, (PlayerState)current);
        }

        // The one place the player's colour and visibility are set (PlayerMovement no longer colours the body).
        private void ApplyLook()
        {
            if (_config == null) return;
            var state = State;
            bool inPlay = state != PlayerState.Dead;

            foreach (var spriteRenderer in _renderers) spriteRenderer.enabled = inPlay;
            if (_collider != null) _collider.enabled = inPlay;

            Color body = _config.PlayerColors[(int)(OwnerClientId % (ulong)_config.PlayerColors.Length)];
            if (state == PlayerState.Downed) body *= _config.DownedTint;
            _bodyRenderer.color = body;
        }
    }
}
