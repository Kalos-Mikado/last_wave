using UnityEngine;

namespace LastWave.Player
{
    /// <summary>
    /// A simple soft oval shadow drawn under a character's feet, so players and enemies look grounded on the map.
    /// The oval texture is generated once in code (no art file). The shadow is a plain child sprite: it is visual only,
    /// has no collider and is not networked; every machine adds its own the same way.
    /// </summary>
    public static class DropShadow
    {
        private const int TextureSize = 64;     // pixels; the sprite is 1 x 1 world unit before scaling
        private const float EdgeSoftness = 0.4f; // fraction of the radius that fades out at the rim

        private static Sprite _sprite;

        /// <summary>
        /// Adds a shadow child under <paramref name="parent"/> and returns its renderer.
        /// </summary>
        /// <param name="parent">The character's root transform (its scale, such as a boss's Size, scales the shadow too).</param>
        /// <param name="material">Material to draw with: pass the character's own sprite material so it renders the same way.</param>
        /// <param name="width">Shadow width in the parent's local units.</param>
        /// <param name="heightRatio">Shadow height as a fraction of its width (an oval seen from a high angle).</param>
        /// <param name="feetY">Local Y of the character's feet; the shadow is centred there.</param>
        /// <param name="alpha">Darkness at the centre, 0 to 1.</param>
        /// <param name="sortingOrder">Sorting order: below the characters and above the map.</param>
        public static SpriteRenderer Attach(Transform parent, Material material, float width, float heightRatio, float feetY,
            float alpha, int sortingOrder)
        {
            var shadow = new GameObject("Shadow");
            shadow.transform.SetParent(parent, false);
            shadow.transform.localPosition = new Vector3(0f, feetY, 0f);
            shadow.transform.localScale = new Vector3(width, width * heightRatio, 1f);

            var renderer = shadow.AddComponent<SpriteRenderer>();
            renderer.sprite = GetSprite();
            if (material != null) renderer.sharedMaterial = material;
            renderer.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        // A white oval whose alpha fades from the centre to the edge; tinted black by the renderer colour.
        private static Sprite GetSprite()
        {
            if (_sprite != null) return _sprite;

            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = "DropShadow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[TextureSize * TextureSize];
            float half = TextureSize * 0.5f;
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);       // 0 at the centre, 1 at the edge
                    // Solid inside, soft only near the rim (the outer EdgeSoftness of the radius), so it reads as a shadow.
                    float a = Mathf.Clamp01((1f - distance) / EdgeSoftness);
                    a = a * a * (3f - 2f * a);
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            _sprite = Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize), new Vector2(0.5f, 0.5f), TextureSize);
            _sprite.name = "DropShadow";
            return _sprite;
        }
    }
}
