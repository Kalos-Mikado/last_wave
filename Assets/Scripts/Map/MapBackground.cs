using System;
using LastWave.Config;
using UnityEngine;

namespace LastWave.Map
{
    /// <summary>
    /// The map picture behind everything (story 015, Brief MVP feature 15). One SpriteRenderer on this object;
    /// the sprite is made in code from the texture, and the object is rescaled whenever the main camera's view changes
    /// (orthographic size or window shape). <see cref="GameConfig.MapFit"/> picks how:
    /// <list type="bullet">
    /// <item>Stretch (default): X and Y scaled separately (view width / image width, view height / image height), so the
    /// WHOLE picture fills the view exactly, with no bars and no cropping (a 3:2 picture on a 16:9 window is stretched
    /// about 18% horizontally).</item>
    /// <item>Cover: one scale, the larger of the two ratios; no distortion, the overflow is cropped.</item>
    /// </list>
    /// The object stays where it was placed (the world origin, where the camera looks). <see cref="MapWalls"/> reads
    /// <see cref="DisplayedRect"/> to put the walls on the rock bands. Sorting order: <see cref="GameConfig.MapSortingOrder"/>.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class MapBackground : MonoBehaviour
    {
        private const float PixelsPerUnit = 100f; // only sets the sprite's own size; the fit scale cancels it out

        [SerializeField] private Texture2D _texture;

        private SpriteRenderer _renderer;
        private Camera _camera;
        private float _lastSize = -1f;
        private float _lastAspect = -1f;
        private MapFitMode _lastMode;

        /// <summary>True once the map has been fitted to a camera view at least once.</summary>
        public bool HasFitted { get; private set; }

        /// <summary>World rectangle the picture covers (the camera view in Stretch mode; larger in Cover mode). Without a texture, the view.</summary>
        public Rect DisplayedRect { get; private set; }

        /// <summary>World rectangle the main camera sees.</summary>
        public Rect ViewRect { get; private set; }

        /// <summary>Raised after every refit (the view size or the fit mode changed).</summary>
        public event Action<MapBackground> Fitted;

        private void Start()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _renderer.sortingOrder = GameConfig.Instance.MapSortingOrder;
            if (_texture != null)
            {
                var sprite = Sprite.Create(_texture, new Rect(0f, 0f, _texture.width, _texture.height),
                    new Vector2(0.5f, 0.5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
                sprite.name = _texture.name;
                _renderer.sprite = sprite;
            }
            Fit();
        }

        private void Update()
        {
            Fit();
        }

        // Does nothing until the camera size, the window shape or the fit mode changes.
        private void Fit()
        {
            if (_renderer == null) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null || !_camera.orthographic) return;

            float size = _camera.orthographicSize;
            float aspect = _camera.aspect;
            MapFitMode mode = GameConfig.Instance.MapFit;
            if (HasFitted && Mathf.Approximately(size, _lastSize) && Mathf.Approximately(aspect, _lastAspect) && mode == _lastMode) return;
            _lastSize = size;
            _lastAspect = aspect;
            _lastMode = mode;

            Vector2 view = new Vector2(2f * size * aspect, 2f * size);
            Vector2 camera = _camera.transform.position;
            ViewRect = new Rect(camera - view * 0.5f, view);

            if (_renderer.sprite != null)
            {
                Vector2 image = _renderer.sprite.bounds.size; // world units at scale 1
                Vector2 ratio = new Vector2(view.x / image.x, view.y / image.y);
                Vector2 scale = mode == MapFitMode.Cover ? Vector2.one * Mathf.Max(ratio.x, ratio.y) : ratio;
                transform.localScale = new Vector3(scale.x, scale.y, 1f);
                Vector2 shown = Vector2.Scale(image, scale);
                DisplayedRect = new Rect((Vector2)transform.position - shown * 0.5f, shown);
            }
            else
            {
                DisplayedRect = ViewRect;
            }

            HasFitted = true;
            Fitted?.Invoke(this);
        }
    }
}
