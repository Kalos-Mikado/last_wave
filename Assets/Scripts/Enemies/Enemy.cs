using System;
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
    /// Health is a server-only value: clients never need it, they just see the enemy vanish on death.
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

        /// <summary>Health left on the server; 0 on a client (not synchronised on purpose).</summary>
        public int Health => _health;

        /// <summary>Raised on the server just before the enemy despawns because its health reached 0.</summary>
        public event Action<Enemy> OnDied;

        /// <summary>Sets the look on every machine.</summary>
        public override void OnNetworkSpawn()
        {
            _body = GetComponent<Rigidbody2D>();
            var spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.sprite = PlayerMovement.SquareSprite;
            spriteRenderer.color = GameConfig.Instance.EnemyColor;
        }

        /// <summary>
        /// Server only, right after spawning: gives the enemy its goal, speed and health. Ignored on a client.
        /// </summary>
        /// <param name="target">Point to walk towards; the enemy stops near it.</param>
        /// <param name="speed">Walking speed in world units per second.</param>
        /// <param name="health">Starting health in points.</param>
        public void Launch(Transform target, float speed, int health)
        {
            if (!IsServer) return;
            _target = target;
            _base = target != null ? target.GetComponent<BaseHealth>() : null; // null: walks there but hits nothing
            _speed = speed;
            _health = health;
            _stopDistance = GameConfig.Instance.EnemyStopDistance;
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
            if (_health > 0) return;

            OnDied?.Invoke(this);
            NetworkObject.Despawn();
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || _target == null) return;

            Vector2 toTarget = (Vector2)_target.position - _body.position;
            bool atTarget = toTarget.magnitude <= _stopDistance;
            // Stay put once close enough; otherwise walk straight at the target. Never overshoots: the stop
            // distance is far larger than one frame of walking.
            _body.linearVelocity = atTarget ? Vector2.zero : toTarget.normalized * _speed;

            HitBase(atTarget);
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

            _base.TakeDamage(config.EnemyBaseDamage);
            _nextHitTime = Time.time + config.EnemyBaseHitInterval;
        }
    }
}
