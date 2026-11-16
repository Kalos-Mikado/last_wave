using LastWave.Config;
using LastWave.Flow;
using LastWave.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace LastWave.Player
{
    /// <summary>
    /// Left mouse button fires. The owner sends a shoot request to the server; the server checks the
    /// fire rate, spawns the bullet and owns everything that happens to it. The client never spawns
    /// anything itself, so every machine sees the same bullets.
    /// </summary>
    /// <example>
    /// Hold the left mouse button in the Game scene; one request goes out per fire interval.
    /// </example>
    public sealed class PlayerShooting : NetworkBehaviour
    {
        [SerializeField] private Bullet _bulletPrefab;

        private PlayerMovement _movement;
        private PlayerRepair _repair;
        private PlayerLife _life;
        private PlayerUpgrades _upgrades;
        private float _nextLocalShotTime;
        private float _nextServerShotTime;

        /// <summary>
        /// Raised on every machine (host included) each time the server accepts a shot from this player.
        /// Drives the shoot animation (story 013).
        /// </summary>
        public event System.Action OnShotFired;

        /// <summary>Caches the aim source.</summary>
        public override void OnNetworkSpawn()
        {
            _movement = GetComponent<PlayerMovement>();
            _repair = GetComponent<PlayerRepair>();
            _life = GetComponent<PlayerLife>();
            _upgrades = GetComponent<PlayerUpgrades>();
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.isPressed) return;

            var config = GameConfig.Instance;
            // Players also walk around in the Menu scene (story 001); do not shoot there (menu clicks).
            if (SceneManager.GetActiveScene().name != config.GameSceneName) return;
            if (_life != null && !_life.IsStanding) return; // downed or dead: no shooting (story 005)
            if (_repair != null && _repair.IsInteractingLocal) return; // repairing or reviving: no shooting (stories 004, 005)
            if (GameOutcome.IsOver) return; // story 007: the end screen is up
            if (UiPointerBlock.IsPointerOver(mouse.position.ReadValue())) return; // story 007: a click on an IMGUI control is not a shot
            if (Time.time < _nextLocalShotTime) return;

            _nextLocalShotTime = Time.time + CurrentFireInterval(config);
            ShootRpc(_movement.AimDirection);
        }

        /// <summary>
        /// Shoot request, sent by the owner and run on the server. The server validates the direction
        /// and the fire rate, then spawns the bullet at its own copy of the player's position.
        /// </summary>
        /// <param name="direction">Aim direction as the client sees it; normalised by the server.</param>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void ShootRpc(Vector2 direction)
        {
            float sqrLength = direction.sqrMagnitude;
            if (!(sqrLength > Mathf.Epsilon) || float.IsInfinity(sqrLength)) return; // zero, NaN or infinite

            if (GameOutcome.IsOver) return; // story 007: the server refuses every shot once the game ended
            if (_life != null && !_life.IsStanding) return; // the server refuses shots from downed or dead players (story 005)
            if (_repair != null && _repair.IsInteractingOnServer) return; // ...and while repairing or reviving (stories 004, 005)

            var config = GameConfig.Instance;
            float now = Time.time;
            float interval = CurrentFireInterval(config); // story 012: same upgraded interval as the client's local one
            if (now < _nextServerShotTime - interval * config.FireIntervalTolerance) return;
            _nextServerShotTime = Mathf.Max(_nextServerShotTime, now) + interval;

            Vector2 aim = direction.normalized;
            Vector2 origin = (Vector2)transform.position + aim * config.BulletSpawnOffset;
            var bulletObject = NetworkObject.InstantiateAndSpawn(
                _bulletPrefab.gameObject, NetworkManager, destroyWithScene: true,
                position: origin,
                rotation: Quaternion.Euler(0f, 0f, Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg));
            int damage = _upgrades != null ? _upgrades.BulletDamage : config.BulletDamage; // story 012: the shooter's upgraded damage, decided on the server
            bulletObject.GetComponent<Bullet>().Launch(aim, OwnerClientId, damage);
            ShotFiredRpc(); // story 013: tell every machine, so the shoot animation shows everywhere
        }

        /// <summary>Server to everyone: a shot of this player was accepted. Raises <see cref="OnShotFired"/>.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void ShotFiredRpc()
        {
            OnShotFired?.Invoke();
        }

        // Seconds between shots: the config interval, divided by this player's fire rate upgrades (story 012).
        private float CurrentFireInterval(GameConfig config)
        {
            return _upgrades != null ? _upgrades.FireInterval : config.FireInterval;
        }
    }
}
