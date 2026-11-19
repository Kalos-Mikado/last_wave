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
    /// <para>
    /// Story 019 (<c>design/game-brief.md</c> Feature 19): bullets start at the rifle's muzzle. The arm pose math lives
    /// here as pure static functions (<see cref="ArmLocalAngle"/>, <see cref="ShoulderLocal"/>, <see cref="MuzzleLocal"/>)
    /// so the server's bullet spawn and <see cref="PlayerAnimator"/>'s drawing use the very same numbers. The bullet flies
    /// along the aim direction even when the arm is held at <see cref="GameConfig.PlayerArmClampDegrees"/>.
    /// </para>
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
        /// Drives the muzzle flash (story 019; it drove the old shoot animation in story 013).
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
            if (PauseMenu.GameplayBlocked) return; // story 024: pause menu or Setting screen open
            if (UiPointerBlock.IsPointerOver(mouse.position.ReadValue())) return; // story 007: a click on an IMGUI control is not a shot
            if (Time.time < _nextLocalShotTime) return;

            _nextLocalShotTime = Time.time + CurrentFireInterval(config);
            ShootRpc(_movement.AimDirection, _movement.FacingRight);
        }

        /// <summary>
        /// Shoot request, sent by the owner and run on the server. The server validates the direction
        /// and the fire rate, then spawns the bullet at its own copy of the player's position.
        /// </summary>
        /// <param name="direction">Aim direction as the client sees it; normalised by the server.</param>
        /// <param name="facingRight">
        /// The owner's facing when it fired (story 019). Sent with the shot so the muzzle matches the arm the owner saw,
        /// even if the facing NetworkVariable has not reached the server yet. It can only mirror the muzzle, not move it further.
        /// </param>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void ShootRpc(Vector2 direction, bool facingRight)
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
            float aimDegrees = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;
            Vector2 origin = (Vector2)transform.position + MuzzleLocal(config, aimDegrees, facingRight, 0); // story 019: the muzzle tip
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

        /// <summary>
        /// The arm's rotation from horizontal, in degrees, as drawn in its own (mirrored when facing left) frame:
        /// the aim mirrored to the facing side, clamped to +/- <paramref name="clampDegrees"/>. Positive = up.
        /// </summary>
        /// <param name="aimDegrees">Aim angle in world degrees (0 = right, 90 = up).</param>
        /// <param name="facingRight">Which way the body faces.</param>
        /// <param name="clampDegrees">Largest turn up or down.</param>
        public static float ArmLocalAngle(float aimDegrees, bool facingRight, float clampDegrees)
        {
            float local = Mathf.DeltaAngle(0f, facingRight ? aimDegrees : 180f - aimDegrees);
            return Mathf.Clamp(local, -clampDegrees, clampDegrees);
        }

        /// <summary>
        /// The arm transform's Z rotation in degrees. Facing left the arm also has local scale X = -1 (mirrored, never
        /// upside down), and the mirror reverses the turn, hence the sign flip.
        /// </summary>
        public static float ArmRotationZ(float armLocalAngle, bool facingRight)
        {
            return facingRight ? armLocalAngle : -armLocalAngle;
        }

        /// <summary>
        /// Where the shoulder joint sits relative to the player root, in world units: the body's shoulder pixel
        /// (plus the per-row nudge for <paramref name="bodyRow"/>) converted with the body's pixels per unit and offset,
        /// mirrored when facing left.
        /// </summary>
        /// <param name="config">Tuning values.</param>
        /// <param name="facingRight">Which way the body faces.</param>
        /// <param name="bodyRow">player-body.png row shown (0 idle, 1 walk, 2 hit); rows without a nudge use none.</param>
        public static Vector2 ShoulderLocal(GameConfig config, bool facingRight, int bodyRow)
        {
            Vector2 pixel = config.PlayerBodyShoulderPixel;
            var rowOffsets = config.PlayerBodyShoulderRowOffsetPixels;
            if (rowOffsets != null && bodyRow >= 0 && bodyRow < rowOffsets.Length) pixel += rowOffsets[bodyRow];
            float ppu = Mathf.Max(1f, config.PlayerSpritePixelsPerUnit);
            float x = (pixel.x - config.PlayerSheetCellPixels * 0.5f) / ppu; // the body sprite's pivot is its cell's bottom centre
            float y = config.PlayerSpriteOffsetY + (config.PlayerSheetCellPixels - pixel.y) / ppu;
            return new Vector2(facingRight ? x : -x, y);
        }

        /// <summary>The muzzle tip relative to the arm's shoulder pivot, in the arm's own unrotated, unmirrored frame (world units).</summary>
        public static Vector2 MuzzleFromShoulder(GameConfig config)
        {
            Vector2 shoulder = config.PlayerArmShoulderPixel, muzzle = config.PlayerArmMuzzlePixel;
            // Image Y grows downward, world Y upward.
            return new Vector2(muzzle.x - shoulder.x, shoulder.y - muzzle.y) / Mathf.Max(1f, config.PlayerArmPixelsPerUnit);
        }

        /// <summary>
        /// The muzzle tip relative to the player root, in world units, for an aim angle and facing: the shoulder plus the
        /// muzzle offset turned by the clamped arm angle. Used by the server for the bullet spawn; matches what
        /// <see cref="PlayerAnimator"/> draws.
        /// </summary>
        /// <param name="config">Tuning values.</param>
        /// <param name="aimDegrees">Aim angle in world degrees (0 = right, 90 = up).</param>
        /// <param name="facingRight">Which way the body faces.</param>
        /// <param name="bodyRow">player-body.png row shown, for the per-row shoulder nudge (the server uses 0, idle).</param>
        public static Vector2 MuzzleLocal(GameConfig config, float aimDegrees, bool facingRight, int bodyRow)
        {
            Vector2 m = MuzzleFromShoulder(config);
            if (!facingRight) m.x = -m.x; // the arm's scale X = -1
            float r = ArmRotationZ(ArmLocalAngle(aimDegrees, facingRight, config.PlayerArmClampDegrees), facingRight) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(r), sin = Mathf.Sin(r);
            return ShoulderLocal(config, facingRight, bodyRow) + new Vector2(m.x * cos - m.y * sin, m.x * sin + m.y * cos);
        }

        // Seconds between shots: the config interval, divided by this player's fire rate upgrades (story 012).
        private float CurrentFireInterval(GameConfig config)
        {
            return _upgrades != null ? _upgrades.FireInterval : config.FireInterval;
        }
    }
}
