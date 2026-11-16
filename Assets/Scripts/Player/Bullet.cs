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

        // Story 014: set by the editor setup (LastWaveSetup).
        [SerializeField] private Texture2D _bulletTexture; // bullet.png: one bullet flying right, centred in the image
        [SerializeField] private Texture2D _impactTexture; // bullet-impact.png: spark frames in the top row of a 4 x 4 grid

        private SpriteRenderer _renderer;

        /// <summary>
        /// Sets the look on every machine: bullet.png, no tint. The sprite points right and the spawn rotation
        /// (set by the shooter) turns it to the flight direction.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            var config = GameConfig.Instance;
            _renderer = GetComponent<SpriteRenderer>();
            _renderer.sprite = SpriteSheet.GetGrid(_bulletTexture, 1, 1, config.BulletSpritePixelsPerUnit, new Vector2(0.5f, 0.5f)).Frame(0, 0);
            _renderer.color = Color.white;
        }

        /// <summary>
        /// Server to everyone (host included): this bullet hit an enemy at <paramref name="position"/>. Every machine
        /// plays the spark as a local effect. Sent just before the bullet despawns (RPC and despawn travel in order).
        /// </summary>
        /// <param name="position">World position of the hit.</param>
        [Rpc(SendTo.ClientsAndHost)]
        public void HitRpc(Vector2 position)
        {
            var config = GameConfig.Instance;
            var sheet = SpriteSheet.GetGrid(_impactTexture, config.ImpactSheetColumns, config.ImpactSheetRows,
                config.ImpactPixelsPerUnit, new Vector2(0.5f, 0.5f));
            OneShotSpriteEffect.Play(sheet, 0, 0, config.ImpactFrameCount, config.ImpactFps, 0f, position, Vector3.one,
                false, 0f, _renderer.sharedMaterial, _renderer.sortingOrder + 1, "BulletImpact");
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
            if (enemy != null)
            {
                HitRpc(transform.position); // story 014: the spark, before the damage and despawns below
                enemy.TakeDamage(Damage);
            }

            // An enemy (damaged above) or anything else solid (a wall): the bullet ends.
            NetworkObject.Despawn();
        }
    }
}
