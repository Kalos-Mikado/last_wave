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
    /// broadcast, muzzle flash), <see cref="PlayerMovement.AimAngleDegrees"/> (owner-written, arm angle), <see cref="PlayerRepair.IsInteractingSynced"/> (server-written), <see cref="PlayerMovement.FacingRight"/>
    /// (owner-written) and the position delta of the networked transform (walking).
    /// </para>
    /// <para>
    /// Body sheet (player-body.png, story 019) rows (0-based): 0 idle, 1 walk, 2 hurt (these three have no arms or rifle),
    /// 3 repair/revive (kneeling, arms drawn, rifle slung). Sheet B (player-b.png) rows: 1 falling then lying
    /// (loops from <see cref="GameConfig.PlayerDownedLoopStartFrame"/>), 2 death, 3 getting up after a revive; row 0 is unused.
    /// Priority, highest first: dead, downed, getting up, hurt, repair/revive, walk, idle.
    /// </para>
    /// <para>
    /// Story 019 (<c>design/game-brief.md</c> Feature 19, "แบบ A"): a separate arm + rifle sprite turns at the shoulder toward
    /// the aim (<see cref="PlayerMovement.AimAngleDegrees"/>, synced; remote players smooth toward it), clamped to
    /// <see cref="GameConfig.PlayerArmClampDegrees"/>, mirrored with local scale X = -1 when facing left so the rifle is never
    /// upside down. It shows only over the idle, walk and hurt rows. A muzzle flash plays at the muzzle, as a child of the arm,
    /// on <see cref="PlayerShooting.OnShotFired"/>. The pose math is shared with the server's bullet spawn
    /// (<see cref="PlayerShooting.MuzzleLocal"/>).
    /// </para>
    /// <para>
    /// Story 015: also owns the player's solid hitbox, a <see cref="PolygonCollider2D"/> on the root traced ONCE from
    /// the body sheet's idle frame 1 (<see cref="SpriteHitbox"/>, cached for every player), never per animation frame, and mirrored
    /// only when <see cref="PlayerMovement.FacingRight"/> changes. Same polygon on every machine.
    /// </para>
    /// </summary>
    public sealed class PlayerAnimator : NetworkBehaviour
    {
        private const int RowIdle = 0, RowWalk = 1, RowHurt = 2, RowRepair = 3;       // body sheet (story 019)
        private const int RowDown = 1, RowDeath = 2, RowGetUp = 3;                    // sheet B
        private const int FlashColumns = 2, FlashRows = 2;                            // muzzle-flash.png grid

        private enum Clip { None, Idle, Walk, Repair, Hurt, Down, Dead, GetUp }

        // Story 019: the arm sprite per (texture, pixels per unit, pivot pixel), built once and shared by every player.
        private static readonly System.Collections.Generic.Dictionary<(int, float, Vector2), Sprite> ArmSprites =
            new System.Collections.Generic.Dictionary<(int, float, Vector2), Sprite>();

        [SerializeField] private SpriteRenderer _renderer;
        // Story 020: these three are the white set, the fallback when a slot's colour set (GameConfig.Characters) is incomplete.
        [SerializeField] private Texture2D _bodySheet;   // player-body.png (story 019); also the hitbox source
        [SerializeField] private Texture2D _sheetB;      // player-b.png
        [SerializeField] private SpriteRenderer _armRenderer;   // story 019: child "Arm" of the root
        [SerializeField] private SpriteRenderer _flashRenderer; // story 019: child "MuzzleFlash" of the arm
        [SerializeField] private Texture2D _armTexture;         // player-arms-rifle.png
        [SerializeField] private Texture2D _flashTexture;       // muzzle-flash.png

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
        private SpriteSheet _flash;
        private float _flashStart = float.NegativeInfinity;
        private float _shownAim;      // aim angle drawn, in world degrees (smoothed on remote players)
        private bool _aimInitialised;
        private float _hurtStart, _hurtUntil;
        private float _getUpStart, _getUpUntil;
        private float _movingUntil;
        private Vector2 _lastPosition;
        private PolygonCollider2D _hitbox;  // story 015
        private SpriteHitbox _hitboxShape;
        private bool _hitboxFacingRight;
        private SpriteRenderer _shadow; // soft oval under the feet; hidden whenever the body is hidden

        // Story 020: sets already warned about as incomplete, so each logs once per session, not once per player.
        private static readonly System.Collections.Generic.HashSet<string> WarnedSets = new System.Collections.Generic.HashSet<string>();

        /// <summary>The renderer that draws the soldier. <see cref="PlayerLife"/> darkens it while downed (story 020: no tint otherwise).</summary>
        public SpriteRenderer BodyRenderer => _renderer;

        /// <summary>Story 020: display name of the picture set this player is drawn with ("White" when it fell back).</summary>
        public string CharacterName { get; private set; } = "White";

        // Story 020 (design/game-brief.md Feature 20): picks the slot's set from GameConfig.Characters. If any of its three
        // textures is missing, the whole player uses the white set on the prefab, with one warning naming the set and file.
        private void ResolveCharacter(out Texture2D bodySheet, out Texture2D sheetB, out Texture2D armTexture)
        {
            bodySheet = _bodySheet;
            sheetB = _sheetB;
            armTexture = _armTexture;
            CharacterName = "White";
            // Story 023: the character this owner picked in the lobby (synced; kept across Restart). Without a selection
            // object (no session started through the room) or without a pick, the story 020 slot formula is used.
            CharacterDefinition set;
            var selection = LastWave.Network.CharacterSelection.Instance;
            if (selection != null && _config.Characters != null && _config.Characters.Length > 0)
                set = _config.Characters[selection.ResolvedIndexFor(OwnerClientId, _config)];
            else
                set = _config.CharacterForClient(OwnerClientId);
            if (set == null) return;

            string missing = null;
            if (set.BodySheet == null) missing = $"player-body-{set.ArtFolder}.png";
            else if (set.ArmTexture == null) missing = $"player-arms-rifle-{set.ArtFolder}.png";
            else if (set.SheetB == null) missing = $"player-b-{set.ArtFolder}.png";
            if (missing != null)
            {
                if (WarnedSets.Add(set.DisplayName))
                {
                    Debug.LogWarning($"PlayerAnimator: character set '{set.DisplayName}' is missing {missing} " +
                                     $"(expected in Assets/Art/Player/{set.ArtFolder}/); players in that slot use the white set.");
                }
                return;
            }
            bodySheet = set.BodySheet;
            sheetB = set.SheetB;
            armTexture = set.ArmTexture;
            CharacterName = set.DisplayName;
        }

        /// <summary>Slices the sheets, positions the visual and subscribes to the events that trigger one-shot clips.</summary>
        public override void OnNetworkSpawn()
        {
            _config = GameConfig.Instance;
            _life = GetComponent<PlayerLife>();
            _health = GetComponent<PlayerHealth>();
            _shooting = GetComponent<PlayerShooting>();
            _repair = GetComponent<PlayerRepair>();
            _movement = GetComponent<PlayerMovement>();

            // Story 023: the picture set is the owner's pick (CharacterSelection); rebuilt whenever the picks change.
            ApplyCharacter();
            LastWave.Network.CharacterSelection.PicksChanged -= ApplyCharacter;
            LastWave.Network.CharacterSelection.PicksChanged += ApplyCharacter;
            // The sprite pivot is the foot; lower the visual so the feet sit half a unit below the root (the old box's bottom).
            _renderer.transform.localPosition = new Vector3(0f, _config.PlayerSpriteOffsetY, 0f);
            if (_shadow == null)
            {
                _shadow = DropShadow.Attach(transform, _renderer.sharedMaterial, _config.PlayerShadowWidth,
                    _config.ShadowHeightRatio, _config.PlayerShadowFeetY, _config.ShadowAlpha, _config.ShadowSortingOrder);
            }

            // Story 019: the flash's pivot is the middle of its cell's left edge.
            if (_flashRenderer != null && _flashTexture != null)
            {
                _flash = SpriteSheet.GetGrid(_flashTexture, FlashColumns, FlashRows, _config.PlayerMuzzleFlashPixelsPerUnit, new Vector2(0f, 0.5f));
                Vector2 muzzle = PlayerShooting.MuzzleFromShoulder(_config);
                _flashRenderer.transform.localPosition = new Vector3(muzzle.x, muzzle.y, 0f);
                _flashRenderer.transform.localScale = Vector3.one * _config.PlayerMuzzleFlashScale;
                _flashRenderer.enabled = false;
            }
            _aimInitialised = false;
            _flashStart = float.NegativeInfinity;

            // Story 015: the polygon hitbox, traced from idle frame 1 at the base visual offset (not the death-row nudge).
            // Since story 019 that is the body without the arm and rifle. Story 020: always from the white body sheet
            // (the colour sets share its poses), so every player has the same polygon and only the white sheet needs Read/Write;
            // the drawn set is used only if the white sheet is missing.
            _hitbox = GetComponent<PolygonCollider2D>();
            if (_hitbox != null)
            {
                var hitboxSheet = _bodySheet != null
                    ? SpriteSheet.Get(_bodySheet, _config.PlayerSheetCellPixels, _config.PlayerSpritePixelsPerUnit)
                    : _a;
                _hitboxShape = SpriteHitbox.Get(hitboxSheet.Frame(RowIdle, 0), new Vector2(0f, _config.PlayerSpriteOffsetY),
                    _renderer.transform.localScale, _config.PlayerHitboxTolerance, _config.HitboxMinPathArea);
                _hitboxFacingRight = _movement.FacingRight;
                _hitboxShape.ApplyTo(_hitbox, _hitboxFacingRight);
            }

            float now = Time.time;
            _clip = Clip.None;
            _stateStart = now - 1000f; // joined late: a downed or dead player is already lying down, skip the fall
            _hurtUntil = _getUpUntil = _movingUntil = 0f;
            _lastPosition = transform.position;

            _life.OnStateChanged += HandleStateChanged;
            _health.OnHealthChanged += HandleHealthChanged;
            _shooting.OnShotFired += HandleShotFired;
        }

        // Story 023: resolves the owner's character and (re)builds the body and B sheets and the arm sprite from it.
        // Called at spawn and on CharacterSelection.PicksChanged (the player object usually spawns in the lobby before
        // anyone picked). The hitbox is not touched: it is always traced from the white body (story 020).
        private void ApplyCharacter()
        {
            if (_config == null) return;
            ResolveCharacter(out var bodySheet, out var sheetB, out var armTexture);
            _a = SpriteSheet.Get(bodySheet, _config.PlayerSheetCellPixels, _config.PlayerSpritePixelsPerUnit);
            _b = SpriteSheet.Get(sheetB, _config.PlayerSheetCellPixels, _config.PlayerSpritePixelsPerUnit);
            // Story 019: the arm pivots at its shoulder pixel. Update shows or hides it each frame.
            if (_armRenderer != null && armTexture != null)
            {
                bool shown = _armRenderer.enabled;
                _armRenderer.sprite = ArmSprite(armTexture, _config.PlayerArmPixelsPerUnit, _config.PlayerArmShoulderPixel);
                _armRenderer.enabled = shown && IsSpawned;
            }
            if (IsSpawned && _clip != Clip.None) _renderer.sprite = Pick(_clip, Time.time);
        }

        /// <summary>Unsubscribes from the events.</summary>
        public override void OnNetworkDespawn()
        {
            LastWave.Network.CharacterSelection.PicksChanged -= ApplyCharacter;
            if (_life != null) _life.OnStateChanged -= HandleStateChanged;
            if (_health != null) _health.OnHealthChanged -= HandleHealthChanged;
            if (_shooting != null) _shooting.OnShotFired -= HandleShotFired;
        }

        private void HandleStateChanged(PlayerState previous, PlayerState current)
        {
            float now = Time.time;
            _stateStart = now;
            _hurtUntil = 0f;
            _getUpUntil = 0f;
            _flashStart = float.NegativeInfinity;
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

        // Story 019: the shot no longer changes the body pose; it plays the muzzle flash.
        private void HandleShotFired()
        {
            _flashStart = Time.time;
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
            if (_shadow != null) _shadow.enabled = _renderer.enabled;
            UpdateArm(now);
        }

        // Story 019: turn the arm toward the aim, hide it outside the idle, walk and hurt poses, and play the muzzle flash.
        private void UpdateArm(float now)
        {
            if (_armRenderer == null) return;
            bool facingRight = _movement.FacingRight;

            float target = _movement.AimAngleDegrees;
            if (IsOwner || !_aimInitialised)
            {
                _shownAim = target;
                _aimInitialised = true;
            }
            else
            {
                float blend = 1f - Mathf.Exp(-_config.PlayerAimRemoteSmoothing * Time.deltaTime);
                _shownAim = Mathf.LerpAngle(_shownAim, target, blend);
            }

            int bodyRow;
            switch (_clip)
            {
                case Clip.Idle: bodyRow = RowIdle; break;
                case Clip.Walk: bodyRow = RowWalk; break;
                case Clip.Hurt: bodyRow = RowHurt; break;
                default: bodyRow = -1; break; // repair/revive kneels with its own arms; downed, dead, getting up: no arm
            }
            bool armVisible = bodyRow >= 0 && _renderer.enabled;
            _armRenderer.enabled = armVisible;
            if (armVisible)
            {
                Vector2 shoulder = PlayerShooting.ShoulderLocal(_config, facingRight, bodyRow);
                float angle = PlayerShooting.ArmLocalAngle(_shownAim, facingRight, _config.PlayerArmClampDegrees);
                var arm = _armRenderer.transform;
                arm.localPosition = new Vector3(shoulder.x, shoulder.y, 0f);
                arm.localScale = new Vector3(facingRight ? 1f : -1f, 1f, 1f);
                arm.localRotation = Quaternion.Euler(0f, 0f, PlayerShooting.ArmRotationZ(angle, facingRight));
                _armRenderer.color = _renderer.color; // same renderer colour as the body (white; story 020 has no tint)
                _armRenderer.sortingOrder = _renderer.sortingOrder + _config.PlayerArmSortingOffset;
            }

            if (_flashRenderer == null || _flash == null) return;
            int frames = Mathf.Clamp(_config.PlayerMuzzleFlashFrames, 1, FlashColumns * FlashRows);
            int index = (int)((now - _flashStart) * Mathf.Max(0.01f, _config.PlayerMuzzleFlashFps));
            bool flashVisible = armVisible && index >= 0 && index < frames;
            _flashRenderer.enabled = flashVisible;
            if (flashVisible)
            {
                _flashRenderer.sprite = _flash.Frame(index / FlashColumns, index % FlashColumns);
                _flashRenderer.sortingOrder = _armRenderer.sortingOrder + 1;
            }
        }

        // The whole arm image as one sprite pivoted at the shoulder pixel (top-left origin). Tight mesh: the piece is
        // small in a large transparent image (needs Read/Write, set by LastWaveSetup).
        private static Sprite ArmSprite(Texture2D texture, float pixelsPerUnit, Vector2 shoulderPixel)
        {
            pixelsPerUnit = Mathf.Max(1f, pixelsPerUnit);
            var key = (texture.GetInstanceID(), pixelsPerUnit, shoulderPixel);
            if (ArmSprites.TryGetValue(key, out var cached) && cached != null) return cached;
            var pivot = new Vector2(shoulderPixel.x / texture.width, 1f - shoulderPixel.y / texture.height);
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot, pixelsPerUnit, 0,
                texture.isReadable ? SpriteMeshType.Tight : SpriteMeshType.FullRect);
            sprite.name = texture.name + "_arm";
            ArmSprites[key] = sprite;
            return sprite;
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
            if (now < _movingUntil) return Clip.Walk;
            return Clip.Idle;
        }

        private Sprite Pick(Clip clip, float now)
        {
            switch (clip)
            {
                case Clip.Walk: return _a.Frame(RowWalk, Loop(now - _clipStart, _config.PlayerWalkFps, _a.Columns));
                case Clip.Repair: return _a.Frame(RowRepair, Loop(now - _clipStart, _config.PlayerRepairFps, _a.Columns));
                case Clip.Hurt: return _a.Frame(RowHurt, Fit(now - _hurtStart, _config.PlayerHurtSeconds, _a.Columns));
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

        /// <summary>Story 023: drops the selection listener if the object is destroyed without a despawn.</summary>
        public override void OnDestroy()
        {
            LastWave.Network.CharacterSelection.PicksChanged -= ApplyCharacter;
            base.OnDestroy();
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
