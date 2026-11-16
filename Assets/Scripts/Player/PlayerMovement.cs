using LastWave.Config;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastWave.Player
{
    /// <summary>
    /// Top-down WASD movement and mouse aiming for the local player. The owner drives a
    /// <see cref="Rigidbody2D"/> and its rotation; a <see cref="Unity.Netcode.Components.NetworkTransform"/>
    /// set to Owner authority on the same prefab syncs position and Z rotation to everyone
    /// (movement is owner-authoritative on purpose, see story 002). On every other machine the body is
    /// kinematic, so only the NetworkTransform moves it. Each player gets a colour by client id.
    /// </summary>
    /// <example>
    /// Player prefab = SpriteRenderer + Rigidbody2D + BoxCollider2D + NetworkObject + NetworkTransform
    /// (AuthorityMode = Owner) + PlayerMovement + PlayerHealth + PlayerShooting, assigned as
    /// NetworkConfig.PlayerPrefab (built by <c>LastWaveSetup.Run</c>).
    /// </example>
    [RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D))]
    public sealed class PlayerMovement : NetworkBehaviour
    {
        private static Sprite _squareSprite;

        [SerializeField] private SpriteRenderer _aimMarker;

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

        /// <summary>Applies the player's colour and, on the owner, its spawn position.</summary>
        public override void OnNetworkSpawn()
        {
            _config = GameConfig.Instance;
            _body = GetComponent<Rigidbody2D>();
            _repair = GetComponent<PlayerRepair>();
            _life = GetComponent<PlayerLife>();

            var spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.sprite = SquareSprite;
            // The body colour (and the downed dimming) is set by PlayerLife, the one owner of the player's look.
            _aimMarker.sprite = SquareSprite;
            _aimMarker.color = _config.AimMarkerColor;

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
                _body.linearVelocity = Vector2.zero;
                return;
            }

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
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(AimDirection.y, AimDirection.x) * Mathf.Rad2Deg);
        }
    }
}
