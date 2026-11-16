using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LastWave.EditorTools
{
    /// <summary>
    /// Import step for generated art under <c>Assets/Art/</c>. Image generators often fake transparency, so the art
    /// prompts (design/art-prompts.md) ask for a flat key colour instead: magenta (#FF00FF), or cyan (#00FFFF) for art
    /// that is itself purple. Generated backgrounds are never exactly flat (a magenta corner can read 238,31,241), so
    /// the key is matched by kind, not by an exact value:
    /// <list type="number">
    /// <item>The kind (magenta or cyan) is read from the four corners; images whose corners are neither, or already
    /// transparent (map and menu backgrounds, PNGs with real alpha), are left untouched.</item>
    /// <item>Strongly key-coloured pixels anywhere become transparent (this also clears holes enclosed by the art).</item>
    /// <item>Weaker key-coloured pixels touching a transparent region become transparent too (soft, shaded edges).</item>
    /// <item>A thin rim of remaining pixels next to transparent ones loses its key tint (despill), so no pink or cyan
    /// outline is left around the art.</item>
    /// </list>
    /// Runs on every import; bump <see cref="Version"/> to make Unity re-import every image after changing the rules.
    /// </summary>
    public sealed class ArtKeyColorImporter : AssetPostprocessor
    {
        private const string ArtFolder = "Assets/Art/";
        private const uint Version = 2;

        // "Key strength" = how far the two key channels sit above the third one (magenta: min(R,B) - G).
        // "Key brightness" = the weaker of the two key channels (magenta: min(R,B)).
        private const int StrongStrength = 150, StrongBrightness = 190; // background anywhere in the image
        private const int WeakStrength = 90, WeakBrightness = 140;      // background only next to a cleared region
        private const int DespillRim = 2;                               // pixels of rim that lose the key tint

        private enum Key { None, Magenta, Cyan }

        /// <inheritdoc />
        public override uint GetVersion() => Version;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtFolder)) return;
            var importer = (TextureImporter)assetImporter;

            // First import only, so later manual changes (sprite slicing, filter mode) are kept.
            if (importer.importSettingsMissing)
            {
                importer.textureType = TextureImporterType.Sprite;
                // One sprite per file; otherwise Unity may split art with real alpha into islands
                // (a destroyed base became 27 pieces). Sprite sheets are sliced into frames later, on purpose.
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;

                // Generated PNGs often have no alpha channel; force one so the keyed pixels stay transparent.
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = "Standalone",
                    overridden = true,
                    maxTextureSize = 2048,
                    format = TextureImporterFormat.RGBA32,
                });
            }
        }

        private void OnPostprocessTexture(Texture2D texture)
        {
            if (!assetPath.StartsWith(ArtFolder)) return;

            int w = texture.width, h = texture.height;
            Color32[] px = texture.GetPixels32();
            Key key = DetectKey(px, w, h);
            if (key == Key.None) return;

            var clear = new bool[px.Length];
            var queue = new Queue<int>();

            // Strong background anywhere.
            for (int i = 0; i < px.Length; i++)
            {
                Measure(px[i], key, out int strength, out int brightness);
                if (strength >= StrongStrength && brightness >= StrongBrightness)
                {
                    clear[i] = true;
                    queue.Enqueue(i);
                }
            }

            // Weak background connected to a cleared region.
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, y = i / w;
                TryGrow(x - 1, y); TryGrow(x + 1, y); TryGrow(x, y - 1); TryGrow(x, y + 1);
            }

            void TryGrow(int x, int y)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) return;
                int j = y * w + x;
                if (clear[j]) return;
                Measure(px[j], key, out int strength, out int brightness);
                if (strength < WeakStrength || brightness < WeakBrightness) return;
                clear[j] = true;
                queue.Enqueue(j);
            }

            // Despill a thin rim next to cleared pixels.
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (clear[i] || !NearCleared(clear, w, h, x, y)) continue;
                    px[i] = Despill(px[i], key);
                }
            }

            for (int i = 0; i < px.Length; i++)
            {
                if (clear[i]) px[i] = new Color32(0, 0, 0, 0);
            }

            texture.SetPixels32(px);
            texture.Apply(texture.mipmapCount > 1);
        }

        // Majority of the four corners decides; a transparent corner means the image already has real alpha.
        private static Key DetectKey(Color32[] px, int w, int h)
        {
            int magenta = 0, cyan = 0;
            foreach (int i in new[] { 0, w - 1, (h - 1) * w, h * w - 1 })
            {
                Color32 c = px[i];
                if (c.a < 128) return Key.None;
                Measure(c, Key.Magenta, out int ms, out int mb);
                Measure(c, Key.Cyan, out int cs, out int cb);
                if (ms >= StrongStrength - 40 && mb >= StrongBrightness - 30) magenta++;
                else if (cs >= StrongStrength - 40 && cb >= StrongBrightness - 30) cyan++;
            }
            if (magenta >= 3) return Key.Magenta;
            if (cyan >= 3) return Key.Cyan;
            return Key.None;
        }

        private static void Measure(Color32 c, Key key, out int strength, out int brightness)
        {
            if (key == Key.Magenta)
            {
                brightness = Mathf.Min(c.r, c.b);
                strength = brightness - c.g;
            }
            else
            {
                brightness = Mathf.Min(c.g, c.b);
                strength = brightness - c.r;
            }
        }

        private static Color32 Despill(Color32 c, Key key)
        {
            Measure(c, key, out int strength, out _);
            if (strength <= 0) return c;
            if (key == Key.Magenta)
            {
                c.r = (byte)(c.r - strength);
                c.b = (byte)(c.b - strength);
            }
            else
            {
                c.g = (byte)(c.g - strength);
                c.b = (byte)(c.b - strength);
            }
            return c;
        }

        private static bool NearCleared(bool[] clear, int w, int h, int x, int y)
        {
            for (int dy = -DespillRim; dy <= DespillRim; dy++)
            {
                int yy = y + dy;
                if (yy < 0 || yy >= h) continue;
                for (int dx = -DespillRim; dx <= DespillRim; dx++)
                {
                    int xx = x + dx;
                    if (xx >= 0 && xx < w && clear[yy * w + xx]) return true;
                }
            }
            return false;
        }
    }
}
