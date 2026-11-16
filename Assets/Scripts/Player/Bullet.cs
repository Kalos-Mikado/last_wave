using LastWave.Config;
using LastWave.Enemies;
using Unity.Netcode;
using UnityEngine;

namespace LastWave.Player
{
    /// <summary>
    /// A networked bullet. The server spawns it, moves it (kinematic <see cref="Rigidbody2D"/> velocity)
    /// and decides when it despawns; a server-authority NetworkTransform shows the same flight on every
    /// machine. Hits are decided on the server only, so a hit counts once.
    /// Players are never hurt by bullets (friendly fire off). A bullet that touches an enemy deals
    /// <see cref="Damage"/> to it once and despawns.
    /// </summary>
    /// <example>
    /// <code>
    /// // Server only, done by PlayerShooting:
    /// var bullet = NetworkObject.InstantiateAndSpawn(prefab, NetworkManager, position: p, rotation: r)
    ///     .GetComponent&lt;Bullet&gt;();
    /// bullet.Launch(direction, shooterClientId);
    /// </code>
    /// </example>
    [RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer))]
    public sealed class Bullet : NetworkBehaviour
    {
        private float _despawnTime = float.MaxValue;
        private bool _hasHit;

        /// <summary>Client id of the player who fired this bullet (set by <see cref="Launch"/>, server only).</summary>
        public ulong ShooterClientId { get; private set; }

        private int _damage = -1; // set by Launch; negative = use the config value

        /// <summary>Damage this bullet deals: the shooter's upgraded damage given to <see cref="Launch"/>, else <see cref="GameConfig.BulletDamage"/>.</summary>
        public int Damage => _damage >= 0 ? _damage : GameConfig.Instance.BulletDamage;

        /// <summary>Sets the look on every machine.</summary>
        public override void OnNetworkSpawn()
        {
            var spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.sprite = PlayerMovement.SquareSprite;
            spriteRenderer.color = GameConfig.Instance.BulletColor;
        }

        /// <summary>
        /// Server only, right after spawning: sets velocity, shooter and lifetime. Does nothing on a client.
        /// </summary>
        /// <param name="direction">Unit direction of flight.</param>
        /// <param name="shooterClientId">Owner client id of the player who fired.</param>
        /// <param name="damage">Damage this bullet deals; negative uses <see cref="GameConfig.BulletDamage"/>.</param>
        public void Launch(Vector2 direction, ulong shooterClientId, int damage = -1)
        {
            if (!IsServer) return;
            var config = GameConfig.Instance;
            ShooterClientId = shooterClientId;
            _damage = damage;
            GetComponent<Rigidbody2D>().linearVelocity = direction * config.BulletSpeed;
            _despawnTime = Time.time + config.BulletLifetime;
        }

        private void Update()
        {
            if (IsServer && IsSpawned && Time.time >= _despawnTime)
            {
                NetworkObject.Despawn();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!IsServer || !IsSpawned) return;
            if (other.isTrigger) return; // other bullets, sensors
            if (other.GetComponentInParent<PlayerHealth>() != null) return; // friendly fire is off: fly through players
            if (_hasHit) return; // two overlaps in one physics step must not hit twice

            _hasHit = true;
            var enemy = other.GetComponentInParent<Enemy>();
            if (enemy != null) enemy.TakeDamage(Damage);

            // An enemy (damaged above) or anything else solid (a wall): the bullet ends.
            NetworkObject.Despawn();
        }
    }
}
