using LastWave.Config;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastWave.Player
{
    /// <summary>
    /// A player's health. Only the server writes it (NetworkVariable with server write permission);
    /// every machine reads it and draws a small bar above the player (IMGUI, kept minimal on purpose).
    /// Later stories deal damage by calling <see cref="TakeDamage"/> on the server.
    /// </summary>
    /// <example>
    /// <code>
    /// // On the server, e.g. from an enemy hit:
    /// player.GetComponent&lt;PlayerHealth&gt;().TakeDamage(10);
    /// </code>
    /// </example>
    public sealed class PlayerHealth : NetworkBehaviour
    {
        private readonly NetworkVariable<int> _health = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private GameConfig _config;

        /// <summary>Current health, 0 up to <see cref="MaxHealth"/>. Same value on every machine.</summary>
        public int Health => _health.Value;

        /// <summary>Health a player starts with (from <see cref="GameConfig.MaxPlayerHealth"/>).</summary>
        public int MaxHealth => GameConfig.Instance.MaxPlayerHealth;

        /// <summary>Raised on every machine when <see cref="Health"/> changes (previous, current).</summary>
        public event System.Action<int, int> OnHealthChanged;

        /// <summary>Server sets full health; every machine subscribes to changes.</summary>
        public override void OnNetworkSpawn()
        {
            _config = GameConfig.Instance;
            _health.OnValueChanged += HandleHealthChanged;
            if (IsServer) _health.Value = _config.MaxPlayerHealth;
        }

        /// <summary>Unsubscribes from the health variable.</summary>
        public override void OnNetworkDespawn()
        {
            _health.OnValueChanged -= HandleHealthChanged;
        }

        /// <summary>
        /// Server only: removes health, clamped at 0. Ignored (with a warning) when called on a client,
        /// and ignored for amounts of 0 or less.
        /// Reaching 0 does nothing special yet: downed, revive and respawn belong to story 005.
        /// </summary>
        /// <param name="amount">Points of damage, positive.</param>
        public void TakeDamage(int amount)
        {
            if (!IsServer)
            {
                Debug.LogWarning("PlayerHealth.TakeDamage is server-only; call ignored.");
                return;
            }
            if (amount <= 0) return;
            _health.Value = Mathf.Max(0, _health.Value - amount);
        }

        private void HandleHealthChanged(int previous, int current)
        {
            OnHealthChanged?.Invoke(previous, current);
        }

        // DEBUG PATH (story 002): the host presses the configured key to damage its own player, which
        // shows the server-written health reaching every client. Remove once enemies deal damage (story 003).
        private void Update()
        {
            if (!IsSpawned || !IsServer || !IsOwner) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[_config.DebugDamageKey].wasPressedThisFrame)
            {
                TakeDamage(_config.BulletDamage);
            }
        }

        private void OnGUI()
        {
            if (!IsSpawned || Event.current.type != EventType.Repaint) return;
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * _config.HealthBarOffsetY);
            if (screen.z < 0f) return;

            Vector2 size = _config.HealthBarSize;
            var back = new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y * 0.5f, size.x, size.y);
            float fraction = Mathf.Clamp01((float)_health.Value / _config.MaxPlayerHealth);

            var previousColor = GUI.color;
            GUI.color = _config.HealthBarBackColor;
            GUI.DrawTexture(back, Texture2D.whiteTexture);
            GUI.color = _config.HealthBarFillColor;
            GUI.DrawTexture(new Rect(back.x, back.y, back.width * fraction, back.height), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }
    }
}
