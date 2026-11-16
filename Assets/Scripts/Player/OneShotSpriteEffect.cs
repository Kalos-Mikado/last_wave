using UnityEngine;

namespace LastWave.Player
{
    /// <summary>
    /// A plain local (non-networked) sprite effect: plays a row of a <see cref="SpriteSheet"/> once, holds the last
    /// frame for a while, then destroys its own GameObject. Used for the enemy death (the enemy NetworkObject is
    /// already despawned) and the bullet hit spark, which every machine spawns for itself (story 014).
    /// Frame picking is arithmetic only: no allocation per frame; the GameObject is the only allocation per effect.
    /// Design record: <c>design/game-brief.md</c> (Feature 14).
    /// </summary>
    /// <example>
    /// <code>
    /// OneShotSpriteEffect.Play(sheet, row: 3, firstColumn: 0, frameCount: 6, fps: 10f, holdSeconds: 0.5f,
    ///     position, scale: Vector3.one, flipX: true, rotationZ: 0f, material, sortingOrder: 0, "EnemyDeath");
    /// </code>
    /// </example>
    public sealed class OneShotSpriteEffect : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private SpriteSheet _sheet;
        private int _row;
        private int _firstColumn;
        private int _frameCount;
        private float _fps;
        private float _holdSeconds;
        private float _startTime;
        private int _shownIndex = -1;

        /// <summary>Creates the effect GameObject and starts playing. It destroys itself after the clip and the hold.</summary>
        /// <param name="sheet">Sheet the frames come from.</param>
        /// <param name="row">Row (0 = top) of the frames.</param>
        /// <param name="firstColumn">First column played.</param>
        /// <param name="frameCount">Frames played, to the right of <paramref name="firstColumn"/> (clamped to the sheet).</param>
        /// <param name="fps">Frames per second.</param>
        /// <param name="holdSeconds">Seconds the last frame stays after the clip ends.</param>
        /// <param name="position">World position of the sprite pivot.</param>
        /// <param name="scale">World scale of the effect.</param>
        /// <param name="flipX">Mirror the sprite horizontally.</param>
        /// <param name="rotationZ">Rotation about Z in degrees.</param>
        /// <param name="material">Sprite material to share (null keeps the default).</param>
        /// <param name="sortingOrder">Sorting order of the renderer.</param>
        /// <param name="effectName">Name of the GameObject, for the hierarchy.</param>
        public static OneShotSpriteEffect Play(SpriteSheet sheet, int row, int firstColumn, int frameCount, float fps,
            float holdSeconds, Vector3 position, Vector3 scale, bool flipX, float rotationZ, Material material,
            int sortingOrder, string effectName)
        {
            var go = new GameObject(effectName);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, rotationZ));
            go.transform.localScale = scale;

            var renderer = go.AddComponent<SpriteRenderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.flipX = flipX;
            renderer.sortingOrder = sortingOrder;

            var effect = go.AddComponent<OneShotSpriteEffect>();
            effect._renderer = renderer;
            effect._sheet = sheet;
            effect._row = row;
            effect._firstColumn = Mathf.Clamp(firstColumn, 0, sheet.Columns - 1);
            effect._frameCount = Mathf.Clamp(frameCount, 1, sheet.Columns - effect._firstColumn);
            effect._fps = Mathf.Max(0.01f, fps);
            effect._holdSeconds = Mathf.Max(0f, holdSeconds);
            effect._startTime = Time.time;
            effect.Show(0);
            return effect;
        }

        private void Update()
        {
            float elapsed = Time.time - _startTime;
            int index = (int)(elapsed * _fps);
            if (index >= _frameCount)
            {
                index = _frameCount - 1;
                if (elapsed >= _frameCount / _fps + _holdSeconds)
                {
                    Destroy(gameObject);
                    return;
                }
            }
            Show(index);
        }

        private void Show(int index)
        {
            if (index == _shownIndex) return;
            _shownIndex = index;
            _renderer.sprite = _sheet.Frame(_row, _firstColumn + index);
        }
    }
}
