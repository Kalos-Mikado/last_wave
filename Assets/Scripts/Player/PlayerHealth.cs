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
        private PlayerLife _life;

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
            _life = GetComponent<PlayerLife>();
            _health.OnValueChanged += HandleHealthChanged;
            if (IsServer) _health.Value = _config.MaxPlayerHealth;
        }

        /// <summary>Unsubscribes from the health variable.</summary>
        public override void OnNetworkDespawn()
        {
            _health.OnValueChanged -= HandleHealthChanged;
        }

        /// <summary>True while this player can be hurt: standing. Downed and dead players take no damage.</summary>
        public bool CanTakeDamage => _life == null || _life.IsStanding;

        /// <summary>
        /// Server only: removes health, clamped at 0. Ignored (with a warning) when called on a client,
        /// for amounts of 0 or less, and while the player is downed or dead.
        /// Reaching 0 downs the player (<see cref="PlayerLife.ServerDown"/>, story 005).
        /// </summary>
        /// <param name="amount">Points of damage, positive.</param>
        public void TakeDamage(int amount)
        {
            if (!IsServer)
            {
                Debug.LogWarning("PlayerHealth.TakeDamage is server-only; call ignored.");
                return;
            }
            if (amount <= 0 || !CanTakeDamage) return;
            _health.Value = Mathf.Max(0, _health.Value - amount);
            if (_health.Value == 0 && _life != null) _life.ServerDown();
        }

        /// <summary>
        /// Server only: sets health directly, clamped to 0 up to <see cref="MaxHealth"/> (revive and respawn use it).
        /// Ignored (with a warning) on a client.
        /// </summary>
        /// <param name="value">New health in whole points.</param>
        public void ServerSetHealth(int value)
        {
            if (!IsServer)
            {
                Debug.LogWarning("PlayerHealth.ServerSetHealth is server-only; call ignored.");
                return;
            }
            _health.Value = Mathf.Clamp(value, 0, _config.MaxPlayerHealth);
        }

        private void HandleHealthChanged(int previous, int current)
        {
            OnHealthChanged?.Invoke(previous, current);
        }

        // DEBUG PATH (story 002): the host presses the configured key to damage its own player, which
        // shows the server-written health reaching every client. It goes through TakeDamage, so it also
        // downs the player (story 005). Remove when no longer needed for testing.
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
            if (_life != null && _life.IsDead) return; // not in play: no bar
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * _config.HealthBarOffsetY);
            if (screen.z < 0f) return;

            Vector2 size = _config.HealthBarSize;
            var back = new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y * 0.5f, size.x, size.y);
            bool downed = _life != null && _life.IsDowned;
            // A downed player's bar shows the time left before death (distinct colour), not health.
            float fraction = downed
                ? Mathf.Clamp01(_life.DownedRemaining / _config.DownedTimeoutSeconds)
                : Mathf.Clamp01((float)_health.Value / _config.MaxPlayerHealth);

            var previousColor = GUI.color;
            GUI.color = _config.HealthBarBackColor;
            GUI.DrawTexture(back, Texture2D.whiteTexture);
            GUI.color = downed ? _config.DownedBarColor : _config.HealthBarFillColor;
            GUI.DrawTexture(new Rect(back.x, back.y, back.width * fraction, back.height), Texture2D.whiteTexture);
            if (downed && _life.ReviveProgress > 0f)
            {
                // Thin bar right under it: how far the revive has got.
                float thickness = back.height * 0.5f;
                GUI.color = _config.HealthBarBackColor;
                GUI.DrawTexture(new Rect(back.x, back.yMax + 1f, back.width, thickness), Texture2D.whiteTexture);
                GUI.color = _config.ReviveBarColor;
                GUI.DrawTexture(new Rect(back.x, back.yMax + 1f, back.width * _life.ReviveProgress, thickness), Texture2D.whiteTexture);
            }
            GUI.color = previousColor;
        }
    }
}
