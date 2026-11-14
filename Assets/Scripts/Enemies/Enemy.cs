using System;
using System.Collections.Generic;
using LastWave.Base;
using LastWave.Config;
using LastWave.Player;
using Unity.Netcode;
using UnityEngine;

namespace LastWave.Enemies
{
    /// <summary>
    /// A wave enemy. Spawn, movement, health and death all happen on the server; a server-authority
    /// NetworkTransform shows the same walk on every machine. The enemy walks towards a target point
    /// (the base) and stays there, hitting the base on a timer (story 004).
    /// Health is a server-only value for normal enemies (clients just see them vanish on death). A mini-boss or
    /// boss (<see cref="EnemyKind"/>) is the same enemy with other numbers, a larger body, and a health pair the
    /// server writes to NetworkVariables so every machine can draw its bar (story 006).
    /// </summary>
    /// <example>
    /// <code>
    /// // Server only, done by WaveManager:
    /// var enemy = NetworkObject.InstantiateAndSpawn(prefab, NetworkManager, position: p).GetComponent&lt;Enemy&gt;();
    /// enemy.Launch(basePoint, speed: 1.5f, health: 20);
    /// // Server only, done by Bullet:
    /// enemy.TakeDamage(10);
    /// </code>
    /// </example>
    [RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer))]
    public sealed class Enemy : NetworkBehaviour
    {
        private Transform _target;
        private float _speed;
        private int _health; // server only
        private float _stopDistance;
        private Rigidbody2D _body;
        private BaseHealth _base;
        private bool _wasAtBase;
        private float _nextHitTime;
        private Collider2D _collider;
        private PlayerLife _chased;  // server: the player being chased, or null when walking to the base (story 010)
        private Collider2D _chasedCollider; // server: cached BodyCollider of _chased
        private bool _canChase;     // server: false for a boss kind unless the config allows it
        private readonly Dictionary<PlayerLife, float> _playerNextHit = new Dictionary<PlayerLife, float>(); // server

        // Story 006. Kind and size are written once by the server so every machine draws a boss the same way
        // (the prefab's NetworkTransform does not sync scale). The shown health pair is written ONLY for boss
        // kinds, so the many normal enemies cost no extra traffic; clients read it for the boss bar.
        private readonly NetworkVariable<byte> _kind = new NetworkVariable<byte>(
            (byte)EnemyKind.Normal, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _size = new NetworkVariable<float>(
            1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _shownHealth = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _shownMaxHealth = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private static readonly List<Enemy> BossList = new List<Enemy>();
        private Vector3 _baseScale;
        private int _playerDamage; // server
        private int _baseDamage;   // server

        /// <summary>Health left on the server; 0 on a client (use <see cref="ShownHealth"/> for a boss).</summary>
        public int Health => _health;

        /// <summary>Raised on the server just before the enemy despawns because its health reached 0.</summary>
        public event Action<Enemy> OnDied;

        /// <summary>What this enemy is; same on every machine. Normal until the server launches it.</summary>
        public EnemyKind Kind => (EnemyKind)_kind.Value;

        /// <summary>Boss health on every machine (0 for normal enemies, which never write it).</summary>
        public int ShownHealth => _shownHealth.Value;

        /// <summary>Boss maximum health on every machine (0 for normal enemies).</summary>
        public int ShownMaxHealth => _shownMaxHealth.Value;

        /// <summary>Spawned mini-bosses and bosses on this machine, for the health bar. Do not modify.</summary>
        public static IReadOnlyList<Enemy> Bosses => BossList;

        /// <summary>Sets the look on every machine and follows later changes of kind and size.</summary>
        public override void OnNetworkSpawn()
        {
            _body = GetComponent<Rigidbody2D>();
            _collider = GetComponent<Collider2D>();
            var spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.sprite = PlayerMovement.SquareSprite;
            spriteRenderer.color = GameConfig.Instance.EnemyColor;

            _baseScale = transform.localScale;
            _kind.OnValueChanged += HandleKindOrSizeChanged;
            _size.OnValueChanged += HandleSizeChanged;
            ApplyLook(); // a client may already hold the synced kind and size; the server launches after spawning
        }

        /// <summary>Leaves the boss list and unsubscribes.</summary>
        public override void OnNetworkDespawn()
        {
            _kind.OnValueChanged -= HandleKindOrSizeChanged;
            _size.OnValueChanged -= HandleSizeChanged;
            BossList.Remove(this);
        }

        private void HandleKindOrSizeChanged(byte previous, byte current) => ApplyLook();

        private void HandleSizeChanged(float previous, float current) => ApplyLook();

        // Every machine: scale by the synced size and list boss kinds for the health bar (idempotent).
        private void ApplyLook()
        {
            transform.localScale = new Vector3(_baseScale.x * _size.Value, _baseScale.y * _size.Value, _baseScale.z);
            if (Kind != EnemyKind.Normal && !BossList.Contains(this)) BossList.Add(this);
        }

        /// <summary>
        /// Server only, right after spawning: gives the enemy its goal, speed and health. Ignored on a client.
        /// A boss kind also gets a size and its own damage numbers and shows a health bar on every machine.
        /// </summary>
        /// <param name="target">Point to walk towards; the enemy stops near it.</param>
        /// <param name="speed">Walking speed in world units per second.</param>
        /// <param name="health">Starting health in points.</param>
        /// <param name="kind">Normal for a wave enemy; a boss kind turns on the shared health bar.</param>
        /// <param name="size">Size as a multiple of a normal enemy (1 = same).</param>
        /// <param name="playerDamage">Damage per hit to a touched player; -1 uses <see cref="GameConfig.EnemyPlayerDamage"/>.</param>
        /// <param name="baseDamage">Damage per hit to the base; -1 uses <see cref="GameConfig.EnemyBaseDamage"/>.</param>
        public void Launch(Transform target, float speed, int health, EnemyKind kind = EnemyKind.Normal,
            float size = 1f, int playerDamage = -1, int baseDamage = -1)
        {
            if (!IsServer) return;
            var config = GameConfig.Instance;
            _target = target;
            _base = target != null ? target.GetComponent<BaseHealth>() : null; // null: walks there but hits nothing
            _speed = speed;
            _health = health;
            _stopDistance = config.EnemyStopDistance * size; // a bigger body stops proportionally further out
            _canChase = kind == EnemyKind.Normal || config.MiniBossAndBossChasePlayers;
            SetChased(null);
            _playerDamage = playerDamage >= 0 ? playerDamage : config.EnemyPlayerDamage;
            _baseDamage = baseDamage >= 0 ? baseDamage : config.EnemyBaseDamage;

            if (kind == EnemyKind.Normal && size == 1f) return; // nothing to tell the clients

            _size.Value = size;
            _kind.Value = (byte)kind;
            if (kind != EnemyKind.Normal)
            {
                _shownMaxHealth.Value = health;
                _shownHealth.Value = health;
            }
            ApplyLook(); // the host may not get its own change callback in the same call; this is idempotent
        }

        /// <summary>
        /// Server only: removes health; at 0 or less the enemy raises <see cref="OnDied"/> and despawns.
        /// Ignored (with a warning) on a client, for amounts of 0 or less, and once the enemy is already dead.
        /// </summary>
        /// <param name="amount">Points of damage, positive.</param>
        public void TakeDamage(int amount)
        {
            if (!IsServer)
            {
                Debug.LogWarning("Enemy.TakeDamage is server-only; call ignored.");
                return;
            }
            if (amount <= 0 || _health <= 0 || !IsSpawned) return;

            _health -= amount;
            if (Kind != EnemyKind.Normal) _shownHealth.Value = Mathf.Max(_health, 0);
            if (_health > 0) return;

            OnDied?.Invoke(this);
            NetworkObject.Despawn();
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || _target == null) return;

            UpdateChaseTarget();

            Vector2 toTarget;
            bool atTarget;
            bool atBase = false;
            if (_chased != null)
            {
                // Chasing a player: walk until the colliders are in contact (the same test HitPlayers uses for
                // damage), then hold still. The enemy body is kinematic, so nothing else would stop it: without this
                // it would walk onto the player's centre and jitter, or shove a dynamic player around.
                // The base is not hit while chasing.
                bool touching = InContact(_collider.Distance(_chasedCollider), GameConfig.Instance.EnemyContactRange);
                Vector2 toPlayer = (Vector2)_chased.transform.position - _body.position;
                _body.linearVelocity = touching ? Vector2.zero : toPlayer.normalized * _speed;
            }
            else
            {
                // Stay put once close enough to the base; otherwise walk straight at it. Never overshoots: the
                // stop distance is far larger than one frame of walking.
                Vector2 toBase = (Vector2)_target.position - _body.position;
                atBase = toBase.magnitude <= _stopDistance;
                _body.linearVelocity = atBase ? Vector2.zero : toBase.normalized * _speed;
            }

            HitBase(atBase);
            HitPlayers();
        }

        // Story 010, server only. Hysteresis: a chase starts on the nearest standing player within SightRange and
        // lasts until that player is not standing or is beyond GiveUpRange. No allocation (plain loop, squared distances).
        private void UpdateChaseTarget()
        {
            if (!_canChase)
            {
                SetChased(null);
                return;
            }

            var config = GameConfig.Instance;
            Vector2 here = _body.position;

            if (_chased != null)
            {
                float giveUp = config.EnemyGiveUpRange;
                if (_chased.IsStanding && IsChaseable(_chased)
                    && ((Vector2)_chased.transform.position - here).sqrMagnitude <= giveUp * giveUp) return;
                SetChased(null); // lost it: fall through and look for another player in sight, else back to the base
            }

            float sight = config.EnemySightRange;
            float bestSqr = sight * sight;
            var players = PlayerLife.All;
            for (int i = 0; i < players.Count; i++)
            {
                var life = players[i];
                if (life == null || !life.IsStanding || !IsChaseable(life)) continue;
                float sqr = ((Vector2)life.transform.position - here).sqrMagnitude;
                if (sqr > bestSqr) continue;
                bestSqr = sqr;
                SetChased(life);
            }
        }

        // A player with no enabled body collider cannot be touched, so the contact stop could never trigger.
        private static bool IsChaseable(PlayerLife life)
        {
            var collider = life.BodyCollider;
            return collider != null && collider.enabled;
        }

        // Caches the chased player's collider when the target changes, so Update does no per-frame lookups.
        private void SetChased(PlayerLife life)
        {
            _chased = life;
            _chasedCollider = life != null ? life.BodyCollider : null;
        }

        // Story 005: an enemy touching a STANDING player hurts it once per hit interval (its own timer per
        // player). The first hit comes one interval after contact starts; the timer restarts when contact
        // breaks or the player stops standing. Contact is "the gap between the two colliders is at most
        // EnemyContactRange", measured with Collider2D.Distance each frame rather than collision callbacks:
        // callbacks need a dynamic body or full kinematic contacts, stop for sleeping bodies, and a remote
        // player's server copy is kinematic. The solid collision itself is untouched (enemies still shove).
        // An invalid result (a disabled collider or one with no shapes) carries a default distance of 0: not contact.
        private static bool InContact(ColliderDistance2D distance, float range)
        {
            return distance.isValid && distance.distance <= range;
        }

        private void HitPlayers()
        {
            var config = GameConfig.Instance;
            float now = Time.time;
            foreach (var life in PlayerLife.All)
            {
                var health = life.Health;
                var collider = life.BodyCollider;
                if (health == null || collider == null || !life.IsStanding || !collider.enabled
                    || !InContact(_collider.Distance(collider), config.EnemyContactRange))
                {
                    _playerNextHit.Remove(life); // no contact (or nothing to hurt): the timer restarts on the next contact
                    continue;
                }

                if (!_playerNextHit.TryGetValue(life, out float nextHit))
                {
                    _playerNextHit[life] = now + config.EnemyPlayerHitInterval;
                    continue;
                }
                if (now < nextHit) continue;

                health.TakeDamage(_playerDamage);
                _playerNextHit[life] = now + config.EnemyPlayerHitInterval;
            }
        }

        // Story 004: an enemy standing at the base hits it once per hit interval (its own timer).
        // The first hit comes one interval after arriving. Nothing happens once the base is destroyed.
        private void HitBase(bool atTarget)
        {
            if (_base == null || _base.IsDestroyed || !atTarget)
            {
                _wasAtBase = false; // pushed away: the timer restarts on the next arrival
                return;
            }

            var config = GameConfig.Instance;
            if (!_wasAtBase)
            {
                _wasAtBase = true;
                _nextHitTime = Time.time + config.EnemyBaseHitInterval;
            }
            if (Time.time < _nextHitTime) return;

            _base.TakeDamage(_baseDamage);
            _nextHitTime = Time.time + config.EnemyBaseHitInterval;
        }
    }
}
