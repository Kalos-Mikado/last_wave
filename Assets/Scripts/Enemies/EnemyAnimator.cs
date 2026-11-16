using LastWave.Config;
using LastWave.Player;
using Unity.Netcode;
using UnityEngine;

namespace LastWave.Enemies
{
    /// <summary>
    /// Draws an enemy: picks a frame from its kind's sprite sheet by time, flips it to face the way it walks, and
    /// leaves a local death effect behind when the enemy is killed. No Animator controller, no allocation per frame.
    /// Design record: <c>design/game-brief.md</c> (Feature 14), story 014.
    /// <para>
    /// Every machine runs this for every enemy and reads only synced state, so all machines show the same thing:
    /// <see cref="Enemy.Kind"/> (which sheet), <see cref="Enemy.IsAttacking"/> (server-written for a moment after
    /// each hit on a player or the base), the position delta of the networked transform (walk and facing) and
    /// <see cref="Enemy.Died"/> (set by an RPC just before the server despawns a kill).
    /// </para>
    /// <para>
    /// Sheet rows (0-based, art faces right): 0 idle, 1 walk, 2 attack, 3 death. Priority: attack, walk, idle.
    /// The death row is not played on the enemy itself (its NetworkObject is gone at once) but by
    /// <see cref="OneShotSpriteEffect"/>, spawned in <see cref="OnNetworkDespawn"/> on every machine.
    /// </para>
    /// <para>
    /// Story 015: also owns the enemy's solid hitbox, a <see cref="PolygonCollider2D"/> on the root traced ONCE per kind
    /// from the idle row's first frame (<see cref="SpriteHitbox"/>, cached across enemies), never per animation frame.
    /// The root scale (boss Size) scales it. It is mirrored only when the facing (flipX) changes. Every machine traces the
    /// same polygon; the server's copy decides bullet hits, contact damage and the base stop.
    /// </para>
    /// </summary>
    public sealed class EnemyAnimator : NetworkBehaviour
    {
        private const int RowIdle = 0, RowWalk = 1, RowAttack = 2, RowDeath = 3;

        private enum Clip { None, Idle, Walk, Attack }

        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private Texture2D _normalSheet;
        [SerializeField] private Texture2D _miniBossSheet;
        [SerializeField] private Texture2D _bossSheet;

        private GameConfig _config;
        private Enemy _enemy;
        private SpriteSheet _sheet;
        private EnemyKind _shownKind;
        private float _walkTime;   // seconds spent walking; the walk loop keeps its phase across short stops
        private float _animSpeed = 1f; // per-kind multiplier from the look (bosses animate slower)
        private Clip _clip;
        private float _clipStart;
        private float _attackStart;
        private float _movingUntil;
        private bool _wasAttacking;
        private Vector2 _lastPosition;
        private PolygonCollider2D _hitbox;     // story 015: the root's solid hitbox
        private SpriteHitbox _hitboxShape;     // the current kind's traced polygon (both facings)
        private bool _hitboxFacingRight;       // which facing is written into _hitbox

        /// <summary>Picks the sheet for the current kind, sets the starting facing and starts reading movement.</summary>
        public override void OnNetworkSpawn()
        {
            _config = GameConfig.Instance;
            _enemy = GetComponent<Enemy>();
            _hitbox = GetComponent<PolygonCollider2D>();
            _hitboxShape = null;
            _renderer.flipX = _config.EnemyStartsFacingLeft;
            _clip = Clip.None;
            _wasAttacking = false;
            _movingUntil = 0f;
            _lastPosition = transform.position;
            ApplyKind(_enemy.Kind);
        }

        /// <summary>On every machine, when the enemy was killed (not on a scene or game cleanup): leaves the death animation behind as a local effect.</summary>
        public override void OnNetworkDespawn()
        {
            if (_enemy == null || !_enemy.Died || _sheet == null || _renderer == null) return;

            var visual = _renderer.transform;
            OneShotSpriteEffect.Play(_sheet, RowDeath, 0, _sheet.Columns, _config.EnemyDeathFps * _animSpeed, _config.EnemyDeathHoldSeconds,
                visual.position, visual.lossyScale, _renderer.flipX, 0f, _renderer.sharedMaterial,
                _renderer.sortingOrder, "EnemyDeath");
        }

        // The kind arrives from the server shortly after the spawn, so this runs again when it changes.
        private void ApplyKind(EnemyKind kind)
        {
            _shownKind = kind;
            var look = _config.GetEnemyLook(kind);
            _animSpeed = look.AnimationSpeed;
            Texture2D texture = kind == EnemyKind.Boss ? _bossSheet : kind == EnemyKind.MiniBoss ? _miniBossSheet : _normalSheet;
            _sheet = SpriteSheet.Get(texture, _config.EnemySheetCellPixels, look.PixelsPerUnit);

            // The sprite pivot is the bottom of its cell and the feet sit FeetGapPixels above it; the feet go
            // ColliderSize.y / 2 below the enemy centre (where the old box ended). The root scale (Size) scales this too.
            float y = -look.ColliderSize.y * 0.5f - look.FeetGapPixels / look.PixelsPerUnit;
            _renderer.transform.localPosition = new Vector3(0f, y, 0f);

            // Story 015: the kind's polygon, from idle frame 1, in root space (the Visual's offset included).
            if (_hitbox != null)
            {
                var visual = _renderer.transform;
                _hitboxShape = SpriteHitbox.Get(_sheet.Frame(RowIdle, 0), visual.localPosition, visual.localScale,
                    _config.EnemyHitboxTolerance, _config.HitboxMinPathArea);
                _hitboxFacingRight = !_renderer.flipX;
                _hitboxShape.ApplyTo(_hitbox, _hitboxFacingRight);
            }
        }

        // Story 015: rewrites the polygon only when the facing actually changed (a few dozen points, no per-frame cost).
        private void SyncHitboxFacing()
        {
            bool right = !_renderer.flipX;
            if (_hitboxShape == null || right == _hitboxFacingRight) return;
            _hitboxFacingRight = right;
            _hitboxShape.ApplyTo(_hitbox, right);
        }

        private void Update()
        {
            if (!IsSpawned || _sheet == null) return;
            float now = Time.time;

            if (_enemy.Kind != _shownKind) ApplyKind(_enemy.Kind);
            TrackMovement(now);
            SyncHitboxFacing();

            bool attacking = _enemy.IsAttacking;
            if (attacking && !_wasAttacking) _attackStart = now;
            _wasAttacking = attacking;

            Clip want = attacking ? Clip.Attack : now < _movingUntil ? Clip.Walk : Clip.Idle;
            if (want == Clip.Walk) _walkTime += Time.deltaTime; // continues where it stopped instead of restarting at frame 1
            if (want != _clip)
            {
                _clip = want;
                _clipStart = now;
            }
            _renderer.sprite = Pick(now);
        }

        // Walking and facing are read from the networked transform on every machine, so remote enemies animate too.
        private void TrackMovement(float now)
        {
            Vector2 position = transform.position;
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                Vector2 delta = position - _lastPosition;
                if (delta.magnitude / dt > _config.EnemyWalkSpeedThreshold)
                {
                    _movingUntil = now + _config.EnemyWalkStopDelaySeconds; // short hold: physics steps and interpolation jitter must not flicker
                }
                if (Mathf.Abs(delta.x) / dt > _config.EnemyFacingMinSpeed)
                {
                    _renderer.flipX = delta.x < 0f; // the art faces right; standing still keeps the last facing
                }
            }
            _lastPosition = position;
        }

        private Sprite Pick(float now)
        {
            switch (_clip)
            {
                case Clip.Walk: return _sheet.Frame(RowWalk, Loop(_walkTime, _config.EnemyWalkFps * _animSpeed, _sheet.Columns));
                case Clip.Attack: return _sheet.Frame(RowAttack, Fit(now - _attackStart, _config.EnemyAttackSeconds / _animSpeed, _sheet.Columns));
                default: return _sheet.Frame(RowIdle, Loop(now - _clipStart, _config.EnemyIdleFps * _animSpeed, _sheet.Columns));
            }
        }

        // Frame index helpers: pure arithmetic, no allocation.
        private static int Loop(float elapsed, float fps, int count)
        {
            return (int)(Mathf.Max(0f, elapsed) * Mathf.Max(0f, fps)) % count;
        }

        // The whole row spread over <duration> seconds.
        private static int Fit(float elapsed, float duration, int count)
        {
            return Mathf.Clamp((int)(Mathf.Max(0f, elapsed) / Mathf.Max(0.01f, duration) * count), 0, count - 1);
        }
    }
}
