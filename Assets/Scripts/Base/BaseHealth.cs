using System;
using LastWave.Config;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace LastWave.Base
{
    /// <summary>
    /// Plain base arithmetic with no Unity scene or network dependency, so it can be unit tested later.
    /// </summary>
    public static class BaseRules
    {
        /// <summary>Health after taking damage, clamped at 0. Damage of 0 or less changes nothing.</summary>
        public static float ApplyDamage(float current, int damage)
        {
            return damage <= 0 ? current : Mathf.Max(0f, current - damage);
        }

        /// <summary>Health after repairing for <paramref name="deltaSeconds"/>, clamped to <paramref name="max"/>. A dead base (0) stays dead.</summary>
        public static float ApplyRepair(float current, float max, float perSecond, float deltaSeconds)
        {
            if (current <= 0f || perSecond <= 0f || deltaSeconds <= 0f) return current;
            return Mathf.Min(max, current + perSecond * deltaSeconds);
        }

        /// <summary>True when the two points are at most <paramref name="range"/> apart.</summary>
        public static bool IsInRange(Vector2 a, Vector2 b, float range)
        {
            return (a - b).sqrMagnitude <= range * range;
        }

        /// <summary>The whole-point health shown and synchronised: rounded up, so only exactly 0 reads as 0.</summary>
        public static int DisplayHealth(float health)
        {
            return Mathf.CeilToInt(health);
        }

        /// <summary>Colour for a health fraction (0 to 1): blends from the damaged colour at 0 to the healthy colour at 1.</summary>
        public static Color ColorFor(float fraction, Color healthy, Color damaged)
        {
            return Color.Lerp(damaged, healthy, Mathf.Clamp01(fraction));
        }
    }

    /// <summary>
    /// The base in the Game scene: its health, damage from enemies, repair, health bar and look.
    /// Scene object, not a NetworkObject (same pattern as WaveManager): the server (host) holds the real
    /// health and is the only one that changes it; every machine learns the whole-point value through a
    /// Netcode named message (sent when the value changes, and on request when a client's scene finishes
    /// loading, so a late joiner is caught up). The host receives its own message through loopback.
    /// Brief MVP feature 4; values come from <see cref="GameConfig"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var baseHealth = FindFirstObjectByType&lt;BaseHealth&gt;();
    /// baseHealth.OnBaseDestroyed += () => Debug.Log("lose");   // story 007 listens here
    /// </code>
    /// </example>
    public sealed class BaseHealth : MonoBehaviour
    {
        private const string StateMessage = "LastWave.BaseState";
        private const string StateRequestMessage = "LastWave.BaseStateRequest";

        [SerializeField] private SpriteRenderer _renderer;

        private GameConfig _config;
        private NetworkManager _network;
        private float _health;      // server only: exact value
        private int _shown;         // every machine: whole points, same everywhere
        private bool _destroyed;    // every machine

        /// <summary>Whole-point health, 0 up to <see cref="MaxHealth"/>. Same value on every machine (clients: via message).</summary>
        public int Health => _shown;

        /// <summary>Starting and highest health, from <see cref="GameConfig.BaseMaxHealth"/>.</summary>
        public int MaxHealth => _config != null ? _config.BaseMaxHealth : GameConfig.Instance.BaseMaxHealth;

        /// <summary>True once health reached 0. Never turns back in this story.</summary>
        public bool IsDestroyed => _destroyed;

        /// <summary>True while the base is alive and below full health, so repairing does something.</summary>
        public bool NeedsRepair => !_destroyed && _shown < MaxHealth;

        /// <summary>World position of the base centre.</summary>
        public Vector2 Position => transform.position;

        /// <summary>Raised on every machine when <see cref="Health"/> changes (previous, current).</summary>
        public event Action<int, int> OnHealthChanged;

        /// <summary>
        /// Raised once when the base is destroyed: on the server at the moment health reaches 0, on a client
        /// when the message arrives. Nothing happens in response yet: the lose screen is story 007.
        /// </summary>
        public event Action OnBaseDestroyed;

        private void Start()
        {
            _config = GameConfig.Instance;
            _health = _config.BaseMaxHealth;
            _shown = _config.BaseMaxHealth;
            ApplyLook();

            _network = NetworkManager.Singleton;
            if (_network == null || !_network.IsListening)
            {
                Debug.LogWarning("BaseHealth: no running NetworkManager (game scene opened without a room); base health is local only.");
                return;
            }

            // The host receives its own broadcasts through loopback, so it registers the state handler too.
            _network.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, HandleState);
            if (_network.IsServer)
            {
                _network.CustomMessagingManager.RegisterNamedMessageHandler(StateRequestMessage, HandleStateRequest);
            }
            else
            {
                // Ask for the current state: this client may have joined after the last broadcast.
                using var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
                writer.WriteValueSafe(0);
                _network.CustomMessagingManager.SendNamedMessage(StateRequestMessage, NetworkManager.ServerClientId, writer);
            }
        }

        private void OnDestroy()
        {
            // During shutdown the messaging manager may already be gone.
            var messaging = _network != null ? _network.CustomMessagingManager : null;
            if (messaging == null) return;
            messaging.UnregisterNamedMessageHandler(StateMessage);
            if (_network.IsServer) messaging.UnregisterNamedMessageHandler(StateRequestMessage);
        }

        /// <summary>
        /// Server only: an enemy hit. Removes health, clamped at 0; at 0 the base is destroyed.
        /// Ignored (with a warning) on a client, for amounts of 0 or less, and once the base is destroyed.
        /// </summary>
        /// <param name="amount">Points of damage, positive.</param>
        public void TakeDamage(int amount)
        {
            if (!IsServerRunning("BaseHealth.TakeDamage")) return;
            if (amount <= 0 || _health <= 0f) return;
            SetServerHealth(BaseRules.ApplyDamage(_health, amount));
        }

        /// <summary>
        /// Server only: restores health, clamped to <see cref="MaxHealth"/>. Ignored (with a warning) on a client,
        /// for amounts of 0 or less, and once the base is destroyed. The caller (PlayerRepair) checks the range.
        /// </summary>
        /// <param name="perSecond">Points restored per second.</param>
        /// <param name="deltaSeconds">Time this repair covers, in seconds (frame time).</param>
        public void Repair(float perSecond, float deltaSeconds)
        {
            if (!IsServerRunning("BaseHealth.Repair")) return;
            if (_health <= 0f) return;
            SetServerHealth(BaseRules.ApplyRepair(_health, _config.BaseMaxHealth, perSecond, deltaSeconds));
        }

        private bool IsServerRunning(string caller)
        {
            if (_network == null || !_network.IsListening || !_network.IsServer)
            {
                Debug.LogWarning($"{caller} is server-only; call ignored.");
                return false;
            }
            return true;
        }

        // Server: store the exact value, and broadcast when the whole-point value or the destroyed flag changed.
        private void SetServerHealth(float value)
        {
            _health = value;
            int shown = BaseRules.DisplayHealth(_health);
            bool destroyed = _health <= 0f;
            if (shown == _shown && destroyed == _destroyed) return;

            using var writer = CreateStateWriter(shown, destroyed);
            _network.CustomMessagingManager.SendNamedMessageToAll(StateMessage, writer); // host: handled through loopback
        }

        private void HandleStateRequest(ulong senderClientId, FastBufferReader payload)
        {
            using var writer = CreateStateWriter(_shown, _destroyed);
            _network.CustomMessagingManager.SendNamedMessage(StateMessage, senderClientId, writer);
        }

        private static FastBufferWriter CreateStateWriter(int shown, bool destroyed)
        {
            var writer = new FastBufferWriter(sizeof(int) * 2, Allocator.Temp);
            writer.WriteValueSafe(shown);
            writer.WriteValueSafe(destroyed ? 1 : 0);
            return writer;
        }

        // Every machine, the server included (loopback): the one place the shown state changes.
        private void HandleState(ulong senderClientId, FastBufferReader payload)
        {
            payload.ReadValueSafe(out int shown);
            payload.ReadValueSafe(out int destroyedFlag);
            bool destroyed = destroyedFlag != 0;

            int previous = _shown;
            bool wasDestroyed = _destroyed;
            _shown = shown;
            _destroyed = destroyed;
            ApplyLook();

            if (previous != shown) OnHealthChanged?.Invoke(previous, shown);
            if (destroyed && !wasDestroyed) OnBaseDestroyed?.Invoke();
        }

        // The base's colour follows health: blue at full, darker red-tinted blue near 0 (guessed look).
        private void ApplyLook()
        {
            if (_renderer == null) return;
            _renderer.sprite = Player.PlayerMovement.SquareSprite;
            _renderer.color = BaseRules.ColorFor((float)_shown / _config.BaseMaxHealth, _config.BaseColor, _config.BaseDamagedColor);
        }

        private void OnGUI()
        {
            if (_config == null || Event.current.type != EventType.Repaint) return;
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * _config.BaseBarOffsetY);
            if (screen.z < 0f) return;

            Vector2 size = _config.BaseBarSize;
            var back = new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y * 0.5f, size.x, size.y);
            float fraction = Mathf.Clamp01((float)_shown / _config.BaseMaxHealth);

            var previousColor = GUI.color;
            GUI.color = _config.HealthBarBackColor;
            GUI.DrawTexture(back, Texture2D.whiteTexture);
            GUI.color = _config.BaseBarFillColor;
            GUI.DrawTexture(new Rect(back.x, back.y, back.width * fraction, back.height), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }
    }
}
