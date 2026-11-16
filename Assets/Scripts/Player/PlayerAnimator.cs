using LastWave.Config;
using Unity.Netcode;
using UnityEngine;

namespace LastWave.Player
{
    /// <summary>
    /// Draws the soldier: picks a frame from the two sprite sheets by time, flips it left or right, and hides it
    /// after the death animation. No Animator controller; one sprite assignment per frame, no allocation.
    /// Design record: <c>design/game-brief.md</c> (Feature 13), story 013.
    /// <para>
    /// Every machine runs this for every player and reads only state that is already synced, so all machines
    /// show the same pose and facing: <see cref="PlayerLife"/> (standing, downed, dead, revived),
    /// <see cref="PlayerHealth"/> (a drop while standing = hurt), <see cref="PlayerShooting.OnShotFired"/> (server
    /// broadcast), <see cref="PlayerRepair.IsInteractingSynced"/> (server-written), <see cref="PlayerMovement.FacingRight"/>
    /// (owner-written) and the position delta of the networked transform (walking).
    /// </para>
    /// <para>
    /// Sheet A rows (0-based): 0 idle, 1 walk, 2 shoot, 3 repair/revive. Sheet B rows: 0 hurt, 1 falling then lying
    /// (loops from <see cref="GameConfig.PlayerDownedLoopStartFrame"/>), 2 death, 3 getting up after a revive.
    /// Priority, highest first: dead, downed, getting up, hurt, repair/revive, shoot, walk, idle.
    /// </para>
    /// <para>
    /// Story 015: also owns the player's solid hitbox, a <see cref="PolygonCollider2D"/> on the root traced ONCE from
    /// sheet A idle frame 1 (<see cref="SpriteHitbox"/>, cached for every player), never per animation frame, and mirrored
    /// only when <see cref="PlayerMovement.FacingRight"/> changes. Same polygon on every machine.
    /// </para>
    /// </summary>
    public sealed class PlayerAnimator : NetworkBehaviour
    {
        private const int RowIdle = 0, RowWalk = 1, RowShoot = 2, RowRepair = 3;      // sheet A
        private const int RowHurt = 0, RowDown = 1, RowDeath = 2, RowGetUp = 3;       // sheet B

        private enum Clip { None, Idle, Walk, Shoot, Repair, Hurt, Down, Dead, GetUp }

        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private Texture2D _sheetA;
        [SerializeField] private Texture2D _sheetB;

        private GameConfig _config;
        private PlayerLife _life;
        private PlayerHealth _health;
        private PlayerShooting _shooting;
        private PlayerRepair _repair;
        private PlayerMovement _movement;
        private SpriteSheet _a;
        private SpriteSheet _b;

        private Clip _clip;
        private float _clipStart;   // when the current clip started (looping clips)
        private float _stateStart;  // when the life state last changed (down, death)
        private float _shootStart, _shootUntil;
        private float _hurtStart, _hurtUntil;
        private float _getUpStart, _getUpUntil;
        private float _movingUntil;
        private Vector2 _lastPosition;
        private PolygonCollider2D _hitbox;  // story 015
        private SpriteHitbox _hitboxShape;
        private bool _hitboxFacingRight;

        /// <summary>The renderer that draws the soldier. <see cref="PlayerLife"/> tints it per player slot and when downed.</summary>
        public SpriteRenderer BodyRenderer => _renderer;

        /// <summary>Slices the sheets, positions the visual and subscribes to the events that trigger one-shot clips.</summary>
        public override void OnNetworkSpawn()
        {
            _config = GameConfig.Instance;
            _life = GetComponent<PlayerLife>();
            _health = GetComponent<PlayerHealth>();
            _shooting = GetComponent<PlayerShooting>();
            _repair = GetComponent<PlayerRepair>();
            _movement = GetComponent<PlayerMovement>();

            _a = SpriteSheet.Get(_sheetA, _config.PlayerSheetCellPixels, _config.PlayerSpritePixelsPerUnit);
            _b = SpriteSheet.Get(_sheetB, _config.PlayerSheetCellPixels, _config.PlayerSpritePixelsPerUnit);
            // The sprite pivot is the foot; lower the visual so the feet sit half a unit below the root (the old box's bottom).
            _renderer.transform.localPosition = new Vector3(0f, _config.PlayerSpriteOffsetY, 0f);

            // Story 015: the polygon hitbox, traced from idle frame 1 at the base visual offset (not the death-row nudge).
            _hitbox = GetComponent<PolygonCollider2D>();
            if (_hitbox != null)
            {
                _hitboxShape = SpriteHitbox.Get(_a.Frame(RowIdle, 0), new Vector2(0f, _config.PlayerSpriteOffsetY),
                    _renderer.transform.localScale, _config.PlayerHitboxTolerance, _config.HitboxMinPathArea);
                _hitboxFacingRight = _movement.FacingRight;
                _hitboxShape.ApplyTo(_hitbox, _hitboxFacingRight);
            }

            float now = Time.time;
            _clip = Clip.None;
            _stateStart = now - 1000f; // joined late: a downed or dead player is already lying down, skip the fall
            _shootUntil = _hurtUntil = _getUpUntil = _movingUntil = 0f;
            _lastPosition = transform.position;

            _life.OnStateChanged += HandleStateChanged;
            _health.OnHealthChanged += HandleHealthChanged;
            _shooting.OnShotFired += HandleShotFired;
        }

        /// <summary>Unsubscribes from the events.</summary>
        public override void OnNetworkDespawn()
        {
            if (_life != null) _life.OnStateChanged -= HandleStateChanged;
            if (_health != null) _health.OnHealthChanged -= HandleHealthChanged;
            if (_shooting != null) _shooting.OnShotFired -= HandleShotFired;
        }

        private void HandleStateChanged(PlayerState previous, PlayerState current)
        {
            float now = Time.time;
            _stateStart = now;
            _shootUntil = _hurtUntil = 0f;
            _getUpUntil = 0f;
            if (previous == PlayerState.Downed && current == PlayerState.Standing)
            {
                // A revive finished (a respawn from Dead just stands, no get-up).
                _getUpStart = now;
                _getUpUntil = now + _a.Columns / Mathf.Max(0.01f, _config.PlayerDownFps);
            }
        }

        private void HandleHealthChanged(int previous, int current)
        {
            // Hurt only for a hit that leaves the player standing; going down plays the fall instead.
            if (current >= previous || current <= 0 || !_life.IsStanding) return;
            _hurtStart = Time.time;
            _hurtUntil = _hurtStart + _config.PlayerHurtSeconds;
        }

        private void HandleShotFired()
        {
            _shootStart = Time.time;
            _shootUntil = _shootStart + _config.PlayerShootSeconds;
        }

        private void Update()
        {
            if (!IsSpawned || _a == null) return;
            float now = Time.time;

            TrackMovement(now);
            _renderer.flipX = !_movement.FacingRight;
            if (_hitboxShape != null && _movement.FacingRight != _hitboxFacingRight)
            {
                // Story 015: mirror the hitbox only on a facing change.
                _hitboxFacingRight = _movement.FacingRight;
                _hitboxShape.ApplyTo(_hitbox, _hitboxFacingRight);
            }

            Clip want = Choose(now);
            if (want != _clip)
            {
                _clip = want;
                _clipStart = now;
            }
            _renderer.sprite = Pick(_clip, now);

            // The death row is drawn a little higher and further right in its cells than the downed row, so it is
            // nudged to line up and the body does not jump when a downed player dies.
            Vector2 nudge = _clip == Clip.Dead ? _config.PlayerDeathRowOffsetPixels / _config.PlayerSpritePixelsPerUnit : Vector2.zero;
            if (!_movement.FacingRight) nudge.x = -nudge.x;
            _renderer.transform.localPosition = new Vector3(nudge.x, _config.PlayerSpriteOffsetY + nudge.y, 0f);

            if (_clip == Clip.Dead)
            {
                // PlayerLife leaves the body on while dead; it is hidden here once the death has played and held.
                int deathFrames = _b.Columns - Mathf.Clamp(_config.PlayerDeathStartFrame, 0, _b.Columns - 1);
                float visibleFor = deathFrames / Mathf.Max(0.01f, _config.PlayerDownFps) + _config.PlayerDeadHoldSeconds;
                _renderer.enabled = now - _stateStart <= visibleFor;
            }
        }

        // Walking is read from the networked transform on every machine, so remote players animate too.
        private void TrackMovement(float now)
        {
            Vector2 position = transform.position;
            float dt = Time.deltaTime;
            if (dt > 0f && (position - _lastPosition).magnitude / dt > _config.PlayerWalkSpeedThreshold)
            {
                _movingUntil = now + _config.PlayerWalkStopDelaySeconds; // short hold: interpolation jitter must not flicker
            }
            _lastPosition = position;
        }

        private Clip Choose(float now)
        {
            switch (_life.State)
            {
                case PlayerState.Dead: return Clip.Dead;
                case PlayerState.Downed: return Clip.Down;
            }
            if (now < _getUpUntil) return Clip.GetUp;
            if (now < _hurtUntil) return Clip.Hurt;
            if (_repair.IsInteractingSynced) return Clip.Repair;
            if (now < _shootUntil) return Clip.Shoot;
            if (now < _movingUntil) return Clip.Walk;
            return Clip.Idle;
        }

        private Sprite Pick(Clip clip, float now)
        {
            switch (clip)
            {
                case Clip.Walk: return _a.Frame(RowWalk, Loop(now - _clipStart, _config.PlayerWalkFps, _a.Columns));
                case Clip.Repair: return _a.Frame(RowRepair, Loop(now - _clipStart, _config.PlayerRepairFps, _a.Columns));
                case Clip.Shoot: return _a.Frame(RowShoot, Fit(now - _shootStart, _config.PlayerShootSeconds, _a.Columns));
                case Clip.Hurt: return _b.Frame(RowHurt, Fit(now - _hurtStart, _config.PlayerHurtSeconds, _b.Columns));
                case Clip.Down: return _b.Frame(RowDown, FallThenLoop(now - _stateStart, _config.PlayerDownFps, _b.Columns, _config.PlayerDownedLoopStartFrame));
                case Clip.Dead:
                {
                    // Death always follows Downed, so skip the row's standing frames and stay on the ground.
                    int start = Mathf.Clamp(_config.PlayerDeathStartFrame, 0, _b.Columns - 1);
                    return _b.Frame(RowDeath, start + Once(now - _stateStart, _config.PlayerDownFps, _b.Columns - start));
                }
                case Clip.GetUp: return _b.Frame(RowGetUp, Once(now - _getUpStart, _config.PlayerDownFps, _b.Columns));
                default: return _a.Frame(RowIdle, Loop(now - _clipStart, _config.PlayerIdleFps, _a.Columns));
            }
        }

        // Frame index helpers: pure arithmetic, no allocation.
        private static int Loop(float elapsed, float fps, int count)
        {
            return (int)(Mathf.Max(0f, elapsed) * Mathf.Max(0f, fps)) % count;
        }

        private static int Once(float elapsed, float fps, int count)
        {
            return Mathf.Min((int)(Mathf.Max(0f, elapsed) * Mathf.Max(0f, fps)), count - 1);
        }

        // The whole row spread over <duration> seconds: the clip always shows every frame however short it is.
        private static int Fit(float elapsed, float duration, int count)
        {
            return Mathf.Clamp((int)(Mathf.Max(0f, elapsed) / Mathf.Max(0.01f, duration) * count), 0, count - 1);
        }

        // Plays every frame once, then loops from loopStart to the last frame (the lying pose).
        private static int FallThenLoop(float elapsed, float fps, int count, int loopStart)
        {
            int index = (int)(Mathf.Max(0f, elapsed) * Mathf.Max(0f, fps));
            if (index < count) return index;
            loopStart = Mathf.Clamp(loopStart, 0, count - 1);
            return loopStart + (index - loopStart) % (count - loopStart);
        }
    }
}
