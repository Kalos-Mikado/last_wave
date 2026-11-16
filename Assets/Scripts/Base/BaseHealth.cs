using System;
using LastWave.Config;
using LastWave.UI;
using LastWave.Player;
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

        /// <summary>
        /// Which base image to show (story 015): 0 intact when health is above <paramref name="threshold"/> (a fraction
        /// of max), 1 damaged from the threshold down to just above 0, 2 destroyed at 0 or less.
        /// </summary>
        public static int LookIndexFor(int shown, int max, float threshold)
        {
            if (shown <= 0) return 2;
            float fraction = max > 0 ? (float)shown / max : 0f;
            return fraction > threshold ? 0 : 1;
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

        /// <summary>The base images in state order: intact, damaged, destroyed (set by LastWaveSetup).</summary>
        [SerializeField] private Texture2D[] _textures = new Texture2D[3];

        private Sprite[] _sprites;
        private SpriteHitbox[] _hitboxes; // story 015: one traced polygon per state, same index as _sprites
        private PolygonCollider2D _collider;     // static (no Rigidbody2D): players stop at the art, enemies stop at its edge
        private int _shownLook = -1;
        private GameConfig _config;
        private NetworkManager _network;
        private float _health;      // server only: exact value
        private int _shown;         // every machine: whole points, same everywhere
        private bool _destroyed;    // every machine

        /// <summary>Whole-point health, 0 up to <see cref="MaxHealth"/>. Same value on every machine (clients: via message).</summary>
        public int Health => _shown;

        /// <summary>Starting and highest health, from <see cref="GameConfig.BaseMaxHealth"/>.</summary>
        public int MaxHealth => _config != null ? _config.BaseMaxHealth : GameConfig.Instance.BaseMaxHealth;

        /// <summary>True once health reached 0; turns back only through <see cref="ServerReset"/> (a restart).</summary>
        public bool IsDestroyed => _destroyed;

        /// <summary>True while the base is alive and below full health, so repairing does something.</summary>
        public bool NeedsRepair => !_destroyed && _shown < MaxHealth;

        /// <summary>World position of the base centre.</summary>
        public Vector2 Position => transform.position;

        /// <summary>The base's solid hitbox (a polygon traced from the current image, story 015), or null when the scene has none.</summary>
        public Collider2D Collider => _collider != null ? _collider : GetComponent<PolygonCollider2D>();

        /// <summary>Raised on every machine when <see cref="Health"/> changes (previous, current).</summary>
        public event Action<int, int> OnHealthChanged;

        /// <summary>
        /// Raised once when the base is destroyed: on the server at the moment health reaches 0, on a client
        /// when the message arrives. Raised again in a later game after <see cref="ServerReset"/>.
        /// <c>GameOutcome</c> (story 007, server) turns it into the lose screen.
        /// </summary>
        public event Action OnBaseDestroyed;

        private void Start()
        {
            _config = GameConfig.Instance;
            _health = _config.BaseMaxHealth;
            _shown = _config.BaseMaxHealth;
            if (_renderer != null)
            {
                // The image is placed so its ground line sits at BasePosition + BaseGroundOffsetY (pivot is the outline bottom centre).
                _renderer.transform.localPosition = new Vector3(0f, _config.BaseGroundOffsetY, 0f);
            }
            _collider = GetComponent<PolygonCollider2D>();
            // Story 015: trace all three outlines now, so a state change mid-game never stalls on an outline trace.
            for (int i = 0; i < 3; i++) GetHitbox(i);
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
            if (_health <= 0f || Flow.GameOutcome.IsOver) return; // story 007: no repairing after the game ended
            SetServerHealth(BaseRules.ApplyRepair(_health, _config.BaseMaxHealth, perSecond, deltaSeconds));
        }

        /// <summary>
        /// Server only (story 007): a new game. Health back to full and the destroyed flag cleared on every machine
        /// (through the usual state message). Ignored (with a warning) on a client.
        /// </summary>
        public void ServerReset()
        {
            if (!IsServerRunning("BaseHealth.ServerReset")) return;
            SetServerHealth(_config.BaseMaxHealth);
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

        // Story 015: the image follows the shown health (same on every machine): intact, damaged, destroyed.
        private void ApplyLook()
        {
            if (_renderer == null) return;
            int index = _destroyed ? 2 : BaseRules.LookIndexFor(_shown, _config.BaseMaxHealth, _config.BaseDamagedThreshold);
            var sprite = GetSprite(index);
            if (sprite != null) _renderer.sprite = sprite;

            // Story 015: the solid hitbox follows the picture, swapped only when the state changes (same on every machine).
            if (index == _shownLook) return;
            _shownLook = index;
            var hitbox = GetHitbox(index);
            if (hitbox != null) hitbox.ApplyTo(_collider, facingRight: true);
        }

        // Traced once per state from the state's sprite, in the Base object's local space (the Visual child sits at the ground offset).
        private SpriteHitbox GetHitbox(int index)
        {
            if (_collider == null || _renderer == null) return null;
            if (_hitboxes == null) _hitboxes = new SpriteHitbox[3];
            if (_hitboxes[index] != null) return _hitboxes[index];

            var sprite = GetSprite(index);
            if (sprite == null) return null;

            // Trace only the building: a copy of the sprite cut off at BaseHitboxBottomPixels, so the flat foundation
            // patch below it stays walkable. The pivot is kept at the same image point, so the traced shape lands
            // exactly where the drawn base is.
            Texture2D texture = sprite.texture;
            float cutRow = Mathf.Clamp(_config.BaseHitboxBottomPixels[index], 1f, texture.height); // from the image top
            var cutRect = new Rect(0f, texture.height - cutRow, texture.width, cutRow);
            Vector2 pivotPixels = sprite.pivot - new Vector2(0f, cutRect.y);                      // same image point
            var hitboxSprite = Sprite.Create(texture, cutRect,
                new Vector2(pivotPixels.x / cutRect.width, pivotPixels.y / cutRect.height), sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            hitboxSprite.name = sprite.name + "-hitbox";

            var visual = _renderer.transform;
            _hitboxes[index] = SpriteHitbox.Get(hitboxSprite, visual.localPosition, visual.localScale,
                _config.BaseHitboxTolerance, _config.HitboxMinPathArea);
            return _hitboxes[index];
        }

        // Sprites are made from the textures once, in code (no importer slicing). The pivot is the bottom centre of
        // the outline of that image, so all three stand on the same ground line at the same pixels per unit.
        private Sprite GetSprite(int index)
        {
            if (_sprites == null) _sprites = new Sprite[3];
            if (_sprites[index] != null) return _sprites[index];

            Texture2D texture = _textures[index];
            if (texture == null) return null;
            float ppu = _config.BaseOutlinePixelWidth / Mathf.Max(0.01f, _config.BaseWorldWidth);
            float bottomRow = _config.BaseOutlineBottomPixels[index];   // counted from the image top
            float centreColumn = _config.BaseOutlineCenterPixels[index];
            var pivot = new Vector2(centreColumn / texture.width, (texture.height - bottomRow) / texture.height);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), pivot, ppu, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            _sprites[index] = sprite;
            return sprite;
        }

        private void OnGUI()
        {
            if (_config == null || Event.current.type != EventType.Repaint) return;
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * _config.BaseBarOffsetY);
            if (screen.z < 0f) return;

            // Story 017: the shield frame, sized in reference pixels.
            Vector2 size = _config.BaseBarSize * UiSkin.Scale;
            var back = new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y * 0.5f, size.x, size.y);
            float fraction = Mathf.Clamp01((float)_shown / _config.BaseMaxHealth);
            UiSkin.Bar(back, _config.HudBaseBarTexture, _config.HudBaseBarBorder, _config.HudBaseBarFillInset, fraction,
                _config.BaseBarFillColor, _config.HealthBarBackColor);
        }
    }
}
