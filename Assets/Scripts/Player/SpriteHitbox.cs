using System.Collections.Generic;
using UnityEngine;

namespace LastWave.Player
{
    /// <summary>
    /// Shaped hitboxes from sprite art (story 015): a <see cref="PolygonCollider2D"/> path traced from a sprite's alpha
    /// outline, so a collision happens where the art actually is. Shared by the base, the player and the enemies.
    /// <para>
    /// How: a throwaway Tight sprite is made from the same texture rect, pivot and pixels per unit as the drawn frame
    /// with <c>generateFallbackPhysicsShape</c> on; its physics shapes (sprite-local units, relative to the pivot) are
    /// read, simplified with a Ramer-Douglas-Peucker pass (<paramref name="tolerance"/> in collider-local units), tiny
    /// islands dropped, and moved from the child Visual's local space into the root's local space (child position and
    /// scale; mirrored around the pivot for a flipped sprite, the way <see cref="SpriteRenderer.flipX"/> draws it).
    /// The result is cached per (texture, rect, pivot, ppu, offset, scale, tolerance, min area), both facings at once,
    /// so a spawn or a facing change only copies points into the collider. The texture must be Read/Write enabled
    /// (LastWaveSetup sets it); otherwise a warning is logged and the sprite's rectangle is used.
    /// </para>
    /// Every machine builds the same polygon from the same art and config, so hitboxes match everywhere.
    /// </summary>
    /// <example>
    /// <code>
    /// var shape = SpriteHitbox.Get(idleFrame, visual.localPosition, visual.localScale, 0.06f, 0.02f);
    /// shape.ApplyTo(polygonCollider, facingRight: true);
    /// </code>
    /// </example>
    public sealed class SpriteHitbox
    {
        private static readonly Dictionary<(int texture, Rect rect, Vector2 pivot, float ppu, Vector2 offset, Vector2 scale, float tolerance, float minArea), SpriteHitbox> Cache =
            new Dictionary<(int, Rect, Vector2, float, Vector2, Vector2, float, float), SpriteHitbox>();

        private static readonly List<Vector2> Scratch = new List<Vector2>(1024);

        private readonly Vector2[][] _right; // root-local paths, sprite drawn unflipped
        private readonly Vector2[][] _left;  // the same, mirrored (flipX)

        /// <summary>Number of paths (islands) in the polygon.</summary>
        public int PathCount => _right.Length;

        /// <summary>Total points over all paths.</summary>
        public int PointCount
        {
            get
            {
                int count = 0;
                foreach (var path in _right) count += path.Length;
                return count;
            }
        }

        private SpriteHitbox(Vector2[][] right, Vector2[][] left)
        {
            _right = right;
            _left = left;
        }

        /// <summary>
        /// The cached hitbox for <paramref name="frame"/> drawn by a child at <paramref name="childOffset"/> /
        /// <paramref name="childScale"/> under the collider's object. Built on first use (one outline trace).
        /// </summary>
        /// <param name="frame">A drawn sprite (its texture, rect, pivot and pixels per unit are copied).</param>
        /// <param name="childOffset">The Visual child's local position (x, y) under the collider's object.</param>
        /// <param name="childScale">The Visual child's local scale (x, y); usually one.</param>
        /// <param name="tolerance">Simplification tolerance in the collider object's local units.</param>
        /// <param name="minArea">Paths with a smaller area (collider-local square units) are dropped.</param>
        public static SpriteHitbox Get(Sprite frame, Vector2 childOffset, Vector2 childScale, float tolerance, float minArea)
        {
            var texture = frame.texture;
            var key = (texture.GetInstanceID(), frame.rect, frame.pivot, frame.pixelsPerUnit, childOffset, childScale, tolerance, minArea);
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var right = new List<Vector2[]>();
            foreach (var path in TraceSpritePaths(frame))
            {
                var simplified = SimplifyClosed(path, tolerance);
                if (simplified.Count < 3) continue;
                var points = new Vector2[simplified.Count];
                for (int i = 0; i < points.Length; i++)
                {
                    points[i] = childOffset + Vector2.Scale(childScale, simplified[i]);
                }
                if (Mathf.Abs(SignedArea(points)) < minArea) continue;
                right.Add(points);
            }

            if (right.Count == 0)
            {
                Debug.LogWarning($"SpriteHitbox: no usable outline for {frame.name}; using its rectangle.");
                Bounds b = frame.bounds;
                right.Add(new[]
                {
                    childOffset + Vector2.Scale(childScale, new Vector2(b.min.x, b.min.y)),
                    childOffset + Vector2.Scale(childScale, new Vector2(b.max.x, b.min.y)),
                    childOffset + Vector2.Scale(childScale, new Vector2(b.max.x, b.max.y)),
                    childOffset + Vector2.Scale(childScale, new Vector2(b.min.x, b.max.y)),
                });
            }

            // Mirror around the child's pivot (x = childOffset.x), as flipX draws it; reverse to keep the winding.
            var left = new Vector2[right.Count][];
            for (int p = 0; p < right.Count; p++)
            {
                var src = right[p];
                var dst = new Vector2[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    Vector2 v = src[src.Length - 1 - i];
                    dst[i] = new Vector2(2f * childOffset.x - v.x, v.y);
                }
                left[p] = dst;
            }

            var hitbox = new SpriteHitbox(right.ToArray(), left);
            Cache[key] = hitbox;
            // Once per shape (cached afterwards): the real point counts, for tuning the tolerance.
            Debug.Log($"SpriteHitbox: {frame.name} traced to {hitbox.PathCount} path(s), {hitbox.PointCount} points (tolerance {tolerance}).");
            return hitbox;
        }

        /// <summary>Writes this polygon into <paramref name="collider"/> (copies the points; no per-call allocation by this class).</summary>
        /// <param name="collider">The root object's polygon collider.</param>
        /// <param name="facingRight">True for the sprite as drawn (art faces right), false for the flipX mirror.</param>
        public void ApplyTo(PolygonCollider2D collider, bool facingRight)
        {
            if (collider == null) return;
            var paths = facingRight ? _right : _left;
            collider.pathCount = paths.Length;
            for (int i = 0; i < paths.Length; i++) collider.SetPath(i, paths[i]);
        }

        // The physics shapes of a throwaway Tight copy of the frame. The copy is never drawn and is destroyed at once.
        private static List<List<Vector2>> TraceSpritePaths(Sprite frame)
        {
            var result = new List<List<Vector2>>();
            var texture = frame.texture;
            if (!texture.isReadable)
            {
                Debug.LogWarning($"SpriteHitbox: texture {texture.name} is not Read/Write enabled, so no outline can be traced (re-run Last Wave > Build Scenes and Prefabs).");
                return result;
            }

            Rect rect = frame.rect;
            var pivot = new Vector2(frame.pivot.x / rect.width, frame.pivot.y / rect.height);
            var shapeSprite = Sprite.Create(texture, rect, pivot, frame.pixelsPerUnit, 0, SpriteMeshType.Tight, Vector4.zero, true);
            try
            {
                int count = shapeSprite.GetPhysicsShapeCount();
                for (int i = 0; i < count; i++)
                {
                    Scratch.Clear();
                    shapeSprite.GetPhysicsShape(i, Scratch);
                    if (Scratch.Count >= 3) result.Add(new List<Vector2>(Scratch));
                }
            }
            finally
            {
                Object.Destroy(shapeSprite);
            }
            return result;
        }

        // Ramer-Douglas-Peucker on a closed ring: split at the point farthest from the first, simplify both halves.
        private static List<Vector2> SimplifyClosed(List<Vector2> ring, float tolerance)
        {
            int n = ring.Count;
            if (n < 4) return new List<Vector2>(ring);
            int far = 0;
            float best = -1f;
            for (int i = 1; i < n; i++)
            {
                float d = (ring[i] - ring[0]).sqrMagnitude;
                if (d > best) { best = d; far = i; }
            }

            var keep = new bool[n];
            keep[0] = keep[far] = true;
            Mark(ring, 0, far, tolerance, keep);
            Mark(ring, far, n, tolerance, keep); // index n wraps to 0

            var result = new List<Vector2>();
            for (int i = 0; i < n; i++) if (keep[i]) result.Add(ring[i]);
            return result;
        }

        // Keeps the farthest point between first and last (indices modulo the ring length) when it is beyond tolerance, recursively.
        private static void Mark(List<Vector2> ring, int first, int last, float tolerance, bool[] keep)
        {
            int n = ring.Count;
            if (last - first < 2) return;
            Vector2 a = ring[first % n], b = ring[last % n];
            Vector2 ab = b - a;
            float length = ab.magnitude;
            int index = -1;
            float best = tolerance;
            for (int i = first + 1; i < last; i++)
            {
                Vector2 p = ring[i % n];
                float d = length > 1e-6f ? Mathf.Abs(ab.x * (p.y - a.y) - ab.y * (p.x - a.x)) / length : (p - a).magnitude;
                if (d > best) { best = d; index = i; }
            }
            if (index < 0) return;
            keep[index % n] = true;
            Mark(ring, first, index, tolerance, keep);
            Mark(ring, index, last, tolerance, keep);
        }

        private static float SignedArea(Vector2[] points)
        {
            float area = 0f;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                area += (points[j].x * points[i].y) - (points[i].x * points[j].y);
            }
            return area * 0.5f;
        }
    }
}
