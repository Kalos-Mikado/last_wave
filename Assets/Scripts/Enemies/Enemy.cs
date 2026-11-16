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
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class Enemy : NetworkBehaviour
    {
        private Transform _target;
        private float _speed;
        private int _health; // server only
        private float _stopDistance;
        private Rigidbody2D _body;
        private BaseHealth _base;
        private Collider2D _baseCollider; // server: the base's polygon hitbox, found once in Launch (story 015)
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

        // Story 014. True on every machine while a hit on a player or the base is "just happening" (written by the
        // server for EnemyAttackSeconds after each hit; drives the attack animation). Written only on change.
        private readonly NetworkVariable<bool> _attacking = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private float _attackUntil; // server
        private bool _died;         // every machine: set by DiedRpc (and by the server) just before the despawn of a kill

        /// <summary>Health left on the server; 0 on a client (use <see cref="ShownHealth"/> for a boss).</summary>
        public int Health => _health;

        /// <summary>Raised on the server just before the enemy despawns because its health reached 0.</summary>
        public event Action<Enemy> OnDied;

        /// <summary>True on every machine while this enemy's hit on a player or the base is playing (see <see cref="GameConfig.EnemyAttackSeconds"/>).</summary>
        public bool IsAttacking => _attacking.Value;

        /// <summary>
        /// True on every machine once this enemy has been killed (set just before the server despawns it, so
        /// <c>OnNetworkDespawn</c> can tell a kill from a scene or game cleanup, which leave it false).
        /// </summary>
        public bool Died => _died;

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
            _died = false;
            // The picture is drawn by EnemyAnimator on the child "Visual" (story 014); no tint, the art has its own colours.

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
            // Story 015: the polygon hitbox is traced from the kind's sprite by EnemyAnimator (the scale above grows it with Size).
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
            _baseCollider = _base != null ? _base.Collider : target != null ? target.GetComponent<Collider2D>() : null;
            _speed = speed;
            _health = health;
            // Fallback only (story 015), when the base has no usable collider: stop at about the base's edge from the
            // centre distance: half the old base box + half this enemy's body (scaled by Size) + the gap.
            _stopDistance = config.BaseColliderSize.x * 0.5f + config.GetEnemyLook(kind).ColliderSize.x * size * 0.5f
                            + config.EnemyStopDistance;
            _canChase = kind == EnemyKind.Normal || config.MiniBossAndBossChasePlayers;
            SetChased(null);
            _attackUntil = 0f;
            _playerDamage =playerDamage >= 0 ? playerDamage : config.EnemyPlayerDamage;
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
            // Story 014: tell every machine this is a kill BEFORE the despawn (RPC and despawn travel in order), so each
            // machine can play the death animation as a local effect. A despawn without this call (end of game,
            // scene cleanup) plays nothing.
            _died = true;
            DiedRpc();
            NetworkObject.Despawn();
        }

        /// <summary>Server to everyone (host included): this enemy was killed and is about to despawn. Sets <see cref="Died"/>.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void DiedRpc()
        {
            _died = true;
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned) return;
            if (_target == null)
            {
                SyncAttackFlag();
                return;
            }

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
                _body.linearVelocity = touching ? Vector2.zero : KeepOutOfBase(toPlayer.normalized * _speed);
            }
            else
            {
                // Stay put once at the base's edge; otherwise walk straight at its centre. Story 015: the edge is the real
                // gap between the two polygon hitboxes; a step that overlaps a little gives a negative gap, still a stop.
                Vector2 toBase = (Vector2)_target.position - _body.position;
                atBase = IsAtBase(toBase);
                _body.linearVelocity = atBase ? Vector2.zero : KeepOutOfBase(toBase.normalized * _speed);
            }

            HitBase(atBase);
            HitPlayers();
            SyncAttackFlag();
        }

        // Story 015: enemies are kinematic, so colliders never stop them. Any move (walking to the base or chasing a player
        // standing near it) that would carry this enemy's hitbox into the base's within the next physics step loses its
        // component into the base, so the enemy slides along the base edge instead of walking through it; an enemy
        // already inside is pushed back out.
        private Vector2 KeepOutOfBase(Vector2 velocity)
        {
            if (velocity == Vector2.zero || _baseCollider == null || !_baseCollider.enabled || _collider == null || !_collider.enabled)
                return velocity;
            ColliderDistance2D gap = _collider.Distance(_baseCollider);
            if (!gap.isValid) return velocity;

            float stopGap = GameConfig.Instance.EnemyStopDistance;
            if (gap.distance > velocity.magnitude * Time.fixedDeltaTime + stopGap) return velocity; // not close yet

            // The separation direction, from the base towards this enemy (sanity-checked against the centres).
            Vector2 outward = gap.normal;
            if (Vector2.Dot(outward, _body.position - (Vector2)_baseCollider.bounds.center) < 0f) outward = -outward;

            float into = Vector2.Dot(velocity, outward);
            if (into < 0f) velocity -= into * outward;               // drop the part heading into the base
            if (gap.distance < 0f) velocity += outward * _speed;     // overlapping already: step back out
            return velocity;
        }

        // Story 015: at the base when the gap between this enemy's hitbox and the base's is at most EnemyStopDistance.
        // The old centre-distance rule is kept only for a base without a usable collider (none, disabled, or no shape yet).
        private bool IsAtBase(Vector2 toBase)
        {
            if (_baseCollider != null && _baseCollider.enabled && _collider != null && _collider.enabled)
            {
                ColliderDistance2D gap = _collider.Distance(_baseCollider);
                if (gap.isValid) return gap.distance <= GameConfig.Instance.EnemyStopDistance;
            }
            return toBase.magnitude <= _stopDistance;
        }

        // Story 014: a hit just landed; the attack pose is shown for EnemyAttackSeconds, longer for kinds that animate
        // slower (the clip is stretched by the same AnimationSpeed in EnemyAnimator, so it plays to the end).
        private void MarkAttack()
        {
            var config = GameConfig.Instance;
            _attackUntil = Time.time + config.EnemyAttackSeconds / config.GetEnemyLook(Kind).AnimationSpeed;
        }

        // Server: writes the synced flag only when it changes, so it costs two messages per hit.
        private void SyncAttackFlag()
        {
            bool attacking = Time.time < _attackUntil;
            if (_attacking.Value != attacking) _attacking.Value = attacking;
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
                MarkAttack();
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
            MarkAttack();
            _nextHitTime = Time.time + config.EnemyBaseHitInterval;
        }
    }
}
