using LastWave.Config;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastWave.Player
{
    /// <summary>
    /// Placeholder top-down walking for story 001 (story 002 replaces or extends it with
    /// aiming and shooting). The owner moves its own transform with WASD; a
    /// <see cref="Unity.Netcode.Components.NetworkTransform"/> set to Owner authority on the
    /// same prefab syncs the position to everyone. Each player gets a colour by client id.
    /// </summary>
    /// <example>
    /// Player prefab = SpriteRenderer + NetworkObject + NetworkTransform (AuthorityMode = Owner)
    /// + PlayerMovement, assigned as NetworkConfig.PlayerPrefab (built by <c>LastWaveSetup.Run</c>).
    /// </example>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class PlayerMovement : NetworkBehaviour
    {
        private static Sprite _squareSprite;

        private GameConfig _config;

        /// <summary>Applies the player's colour and, on the owner, its spawn position.</summary>
        public override void OnNetworkSpawn()
        {
            _config = GameConfig.Instance;

            if (_squareSprite == null)
            {
                var texture = Texture2D.whiteTexture;
                _squareSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), texture.width); // 1 x 1 world unit
            }

            var spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.sprite = _squareSprite;
            spriteRenderer.color = _config.PlayerColors[(int)(OwnerClientId % (ulong)_config.PlayerColors.Length)];

            if (IsOwner)
            {
                int slot = (int)(OwnerClientId % (ulong)_config.MaxPlayers);
                float centre = (_config.MaxPlayers - 1) * 0.5f;
                transform.position = new Vector3((slot - centre) * _config.SpawnSpacing, 0f, 0f);
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            var direction = new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
            if (direction == Vector2.zero) return;

            transform.position += (Vector3)(direction.normalized * (_config.MoveSpeed * Time.deltaTime));
        }
    }
}
