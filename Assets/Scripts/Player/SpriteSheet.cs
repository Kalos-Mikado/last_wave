using System.Collections.Generic;
using UnityEngine;

namespace LastWave.Player
{
    /// <summary>
    /// A sprite sheet cut into a grid of equal cells at runtime (no importer slicing, no hand-edited .meta files).
    /// Cells are numbered from the TOP-LEFT of the image (row 0 = top row, column 0 = left), the way the art is
    /// described; each sprite's pivot is the bottom centre of its cell. Built once per (texture, cell size,
    /// pixels per unit) and cached, so animating costs no allocation. Reusable for the enemy sheets (story 014).
    /// Design record: <c>design/game-brief.md</c> (Feature 13), story 013.
    /// </summary>
    /// <example>
    /// <code>
    /// var sheet = SpriteSheet.Get(texture, 256, 190f);
    /// Sprite frame = sheet.Frame(row: 1, column: 3);
    /// </code>
    /// </example>
    public sealed class SpriteSheet
    {
        private static readonly Dictionary<(int texture, int cell, float ppu), SpriteSheet> Cache =
            new Dictionary<(int, int, float), SpriteSheet>();

        private static readonly Dictionary<(int texture, int columns, int rows, float ppu, Vector2 pivot), SpriteSheet> GridCache =
            new Dictionary<(int, int, int, float, Vector2), SpriteSheet>();

        private readonly Sprite[] _frames;

        /// <summary>Cells per row.</summary>
        public int Columns { get; }

        /// <summary>Rows in the sheet.</summary>
        public int Rows { get; }

        private SpriteSheet(Sprite[] frames, int columns, int rows)
        {
            _frames = frames;
            Columns = columns;
            Rows = rows;
        }

        /// <summary>The sprite at <paramref name="row"/> (0 = top) and <paramref name="column"/> (0 = left), both clamped into the grid.</summary>
        public Sprite Frame(int row, int column)
        {
            row = Mathf.Clamp(row, 0, Rows - 1);
            column = Mathf.Clamp(column, 0, Columns - 1);
            return _frames[row * Columns + column];
        }

        /// <summary>
        /// The cached sheet for a texture, cut into <paramref name="cellPixels"/> square cells and scaled to
        /// <paramref name="pixelsPerUnit"/>. Rebuilt if the cached sprites were destroyed (editor play mode restarts).
        /// </summary>
        /// <param name="texture">The sheet image (needs no Read/Write).</param>
        /// <param name="cellPixels">Width and height of one cell, in pixels.</param>
        /// <param name="pixelsPerUnit">Pixels per world unit: sets how big the frames are drawn.</param>
        public static SpriteSheet Get(Texture2D texture, int cellPixels, float pixelsPerUnit)
        {
            cellPixels = Mathf.Max(1, cellPixels);
            pixelsPerUnit = Mathf.Max(1f, pixelsPerUnit);
            var key = (texture.GetInstanceID(), cellPixels, pixelsPerUnit);
            if (Cache.TryGetValue(key, out var cached) && cached._frames[0] != null) return cached;

            int columns = Mathf.Max(1, texture.width / cellPixels);
            int rows = Mathf.Max(1, texture.height / cellPixels);
            var frames = new Sprite[columns * rows];
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    // Sprite rects are measured from the bottom-left, the art is described from the top-left.
                    var rect = new Rect(column * cellPixels, texture.height - (row + 1) * cellPixels, cellPixels, cellPixels);
                    var sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
                    sprite.name = $"{texture.name}_{row}_{column}";
                    frames[row * columns + column] = sprite;
                }
            }
            var sheet = new SpriteSheet(frames, columns, rows);
            Cache[key] = sheet;
            return sheet;
        }

        /// <summary>
        /// The cached sheet for a texture cut into <paramref name="columns"/> x <paramref name="rows"/> equal cells whose
        /// size need not be a whole number of pixels (story 014: a 1254 px image in 4 columns has 313.5 px cells). Cell edges
        /// are rounded to whole pixels. Cells are numbered from the top-left like <see cref="Get"/>; the pivot is a
        /// normalised point of each cell (0.5, 0.5 = centre).
        /// </summary>
        /// <param name="texture">The sheet image (needs no Read/Write).</param>
        /// <param name="columns">Cells per row.</param>
        /// <param name="rows">Number of rows.</param>
        /// <param name="pixelsPerUnit">Pixels per world unit: sets how big the frames are drawn.</param>
        /// <param name="pivot">Pivot inside each cell, 0 to 1 from the bottom-left.</param>
        public static SpriteSheet GetGrid(Texture2D texture, int columns, int rows, float pixelsPerUnit, Vector2 pivot)
        {
            columns = Mathf.Max(1, columns);
            rows = Mathf.Max(1, rows);
            pixelsPerUnit = Mathf.Max(1f, pixelsPerUnit);
            var key = (texture.GetInstanceID(), columns, rows, pixelsPerUnit, pivot);
            if (GridCache.TryGetValue(key, out var cached) && cached._frames[0] != null) return cached;

            float cellWidth = texture.width / (float)columns;
            float cellHeight = texture.height / (float)rows;
            var frames = new Sprite[columns * rows];
            for (int row = 0; row < rows; row++)
            {
                int top = Mathf.RoundToInt(row * cellHeight);
                int bottom = Mathf.RoundToInt((row + 1) * cellHeight);
                for (int column = 0; column < columns; column++)
                {
                    int left = Mathf.RoundToInt(column * cellWidth);
                    int right = Mathf.RoundToInt((column + 1) * cellWidth);
                    var rect = new Rect(left, texture.height - bottom, right - left, bottom - top);
                    var sprite = Sprite.Create(texture, rect, pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
                    sprite.name = $"{texture.name}_{row}_{column}";
                    frames[row * columns + column] = sprite;
                }
            }
            var sheet = new SpriteSheet(frames, columns, rows);
            GridCache[key] = sheet;
            return sheet;
        }
    }
}
