using LastWave.Config;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastWave.Player
{
    /// <summary>
    /// Top-down WASD movement and mouse aiming for the local player. The owner drives a
    /// <see cref="Rigidbody2D"/>; a <see cref="Unity.Netcode.Components.NetworkTransform"/>
    /// set to Owner authority on the same prefab syncs the position to everyone
    /// (movement is owner-authoritative on purpose, see story 002). On every other machine the body is
    /// kinematic, so only the NetworkTransform moves it. The body no longer rotates to the mouse (story 013):
    /// the soldier sprite is flipped left or right by <see cref="FacingRight"/>, which the owner writes and
    /// everyone reads. The colour by client id is applied by <see cref="PlayerLife"/>.
    /// </summary>
    /// <example>
    /// Player prefab = Rigidbody2D + PolygonCollider2D (traced by PlayerAnimator, story 015) + NetworkObject + NetworkTransform
    /// (AuthorityMode = Owner) + PlayerMovement + PlayerHealth + PlayerShooting + a child Visual
    /// (SpriteRenderer) driven by PlayerAnimator, assigned as
    /// NetworkConfig.PlayerPrefab (built by <c>LastWaveSetup.Run</c>).
    /// </example>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerMovement : NetworkBehaviour
    {
        private static Sprite _squareSprite;

        // Which way the soldier faces (story 013). Written by the owner, read by every machine, so everyone
        // draws the same facing. Aim itself stays owner-only; only this one bit needs to travel.
        private readonly NetworkVariable<bool> _facingRight = new NetworkVariable<bool>(
            true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private GameConfig _config;
        private Rigidbody2D _body;
        private PlayerRepair _repair;
        private PlayerLife _life;
        private PlayerUpgrades _upgrades;

        /// <summary>1 x 1 world unit white square sprite shared by players, aim markers and bullets.</summary>
        internal static Sprite SquareSprite
        {
            get
            {
                if (_squareSprite == null)
                {
                    var texture = Texture2D.whiteTexture;
                    _squareSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), texture.width);
                }
                return _squareSprite;
            }
        }

        /// <summary>
        /// Unit vector from this player towards the mouse, updated every frame on the owner.
        /// Meaningful on the owner only; defaults to facing right.
        /// </summary>
        public Vector2 AimDirection { get; private set; } = Vector2.right;

        /// <summary>True when the soldier faces right (aim to the right of the player), false when left. Same on every machine.</summary>
        public bool FacingRight => _facingRight.Value;

        /// <summary>Caches components and, on the owner, sets the spawn position.</summary>
        public override void OnNetworkSpawn()
        {
            _config = GameConfig.Instance;
            _body = GetComponent<Rigidbody2D>();
            _repair = GetComponent<PlayerRepair>();
            _life = GetComponent<PlayerLife>();

            // The body sprite is drawn by PlayerAnimator; its colour (and the downed dimming) is set by PlayerLife.

            if (IsOwner)
            {
                transform.position = _config.PlayerSpawnPosition(OwnerClientId); // near the base (story 009)
            }
            else
            {
                // Remote copies are placed by NetworkTransform only; physics must not fight it.
                _body.bodyType = RigidbodyType2D.Kinematic;
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner) return;

            // Downed or dead (story 005): stay put, and stop turning. Movement is owner-authoritative
            // (story 002), so this is a client-side rule; only shooting is also enforced on the server.
            if (_life != null && !_life.IsStanding)
            {
                // Frozen, not only stopped: enemies are kinematic and would otherwise shove a downed player away.
                _body.constraints = RigidbodyConstraints2D.FreezeAll;
                _body.linearVelocity = Vector2.zero;
                return;
            }
            _body.constraints = RigidbodyConstraints2D.FreezeRotation;

            UpdateAim();

            var keyboard = Keyboard.current;
            var direction = Vector2.zero;
            if (keyboard != null)
            {
                direction = new Vector2(
                    (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                    (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
            }
            // Repairing the base (story 004) or reviving a friend (story 005) means standing still.
            if (_repair != null && _repair.IsInteractingLocal) direction = Vector2.zero;
            if (_upgrades == null) _upgrades = GetComponent<PlayerUpgrades>();
            float speed = _upgrades != null ? _upgrades.MoveSpeed : _config.MoveSpeed; // story 012
            _body.linearVelocity = direction.normalized * speed;
        }

        private void UpdateAim()
        {
            var mouse = Mouse.current;
            var cam = Camera.main;
            if (mouse == null || cam == null) return;

            Vector2 mouseWorld = cam.ScreenToWorldPoint(mouse.position.ReadValue());
            Vector2 toMouse = mouseWorld - (Vector2)transform.position;
            if (toMouse.sqrMagnitude <= Mathf.Epsilon) return; // keep the last aim when the mouse is on the player

            AimDirection = toMouse.normalized;

            // Flip only once the mouse is clearly on the other side (dead zone), so a mouse straight above
            // or below the player does not make the sprite jitter. Written only when it changes.
            if (Mathf.Abs(AimDirection.x) >= _config.PlayerFacingDeadZone)
            {
                bool right = AimDirection.x > 0f;
                if (right != _facingRight.Value) _facingRight.Value = right;
            }
        }
    }
}
