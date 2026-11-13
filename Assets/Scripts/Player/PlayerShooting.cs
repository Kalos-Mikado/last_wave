using LastWave.Config;
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
        private float _nextLocalShotTime;
        private float _nextServerShotTime;

        /// <summary>Caches the aim source.</summary>
        public override void OnNetworkSpawn()
        {
            _movement = GetComponent<PlayerMovement>();
            _repair = GetComponent<PlayerRepair>();
            _life = GetComponent<PlayerLife>();
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
            if (Time.time < _nextLocalShotTime) return;

            _nextLocalShotTime = Time.time + config.FireInterval;
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

            if (_life != null && !_life.IsStanding) return; // the server refuses shots from downed or dead players (story 005)
            if (_repair != null && _repair.IsInteractingOnServer) return; // ...and while repairing or reviving (stories 004, 005)

            var config = GameConfig.Instance;
            float now = Time.time;
            if (now < _nextServerShotTime - config.FireInterval * config.FireIntervalTolerance) return;
            _nextServerShotTime = Mathf.Max(_nextServerShotTime, now) + config.FireInterval;

            Vector2 aim = direction.normalized;
            Vector2 origin = (Vector2)transform.position + aim * config.BulletSpawnOffset;
            var bulletObject = NetworkObject.InstantiateAndSpawn(
                _bulletPrefab.gameObject, NetworkManager, destroyWithScene: true,
                position: origin,
                rotation: Quaternion.Euler(0f, 0f, Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg));
            bulletObject.GetComponent<Bullet>().Launch(aim, OwnerClientId);
        }
    }
}
