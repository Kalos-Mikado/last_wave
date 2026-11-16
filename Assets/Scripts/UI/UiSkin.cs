using System.Collections.Generic;
using LastWave.Config;
using UnityEngine;

namespace LastWave.UI
{
    /// <summary>
    /// The HUD icons (story 017), in the order they sit in <c>Art/UI/hud-icons.png</c>: row by row from the top-left of
    /// the <see cref="GameConfig.HudIconGrid"/> cells. Drawn with <see cref="UiSkin.Icon"/>.
    /// </summary>
    public enum HudIcon
    {
        /// <summary>The E key: something to repair or revive is in range.</summary>
        Interact,
        /// <summary>Wrench: repairing the base.</summary>
        Repair,
        /// <summary>Hand with a plus: reviving a friend.</summary>
        Revive,
        /// <summary>Crosshair: the mouse cursor during play.</summary>
        Crosshair,
        /// <summary>Skull: the player is down.</summary>
        Downed,
        /// <summary>Hourglass: the downed player's bleed-out time (the bar beside it).</summary>
        BleedOut,
        /// <summary>Green plus with a heart: health is regenerating.</summary>
        Regen,
    }

    /// <summary>
    /// The shared IMGUI look (story 016): art buttons with hover and pressed pictures, the dialog panel, and light text
    /// with a shadow. Pictures and sizes come from <see cref="GameConfig"/>. Call only from <c>OnGUI</c>.
    /// <para>
    /// The generated pictures sit in the middle of large transparent margins, and IMGUI draws a style's whole texture,
    /// so each picture is trimmed to its opaque pixels once, at first use, into a new texture with mipmaps (the source
    /// must be Read/Write; LastWaveSetup sets it). The three button pictures share one trim rectangle so the button
    /// does not jump between states. The 9-slice backgrounds are drawn under a scaled <see cref="GUI.matrix"/>
    /// (<see cref="GameConfig.UiButtonArtScale"/>, <see cref="GameConfig.UiPanelArtScale"/>) so their corners, which
    /// IMGUI keeps at one texture pixel per GUI pixel, come out at a sensible size; text is drawn unscaled on top.
    /// </para>
    /// <para>
    /// Story 017 adds the HUD: <see cref="Frame"/> and <see cref="Bar"/> draw the frame and plate pictures scaled to the
    /// rectangle's height and 9-sliced across its width, and <see cref="Icon"/> cuts the icon sheet into one texture per icon.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// void OnGUI()
    /// {
    ///     var rect = UiSkin.ToScreen(new Rect(100f, 100f, 360f, 72f));
    ///     if (UiSkin.Button(rect, "Restart", GameConfig.Instance.MenuButtonFontSize)) Restart();
    /// }
    /// </code>
    /// </example>
    public static class UiSkin
    {
        private static readonly Dictionary<Texture2D, Texture2D> Trims = new Dictionary<Texture2D, Texture2D>();
        private static readonly Dictionary<Texture2D, GUIStyle> Frames = new Dictionary<Texture2D, GUIStyle>();
        private static readonly int IconCount = System.Enum.GetValues(typeof(HudIcon)).Length;
        private static Texture2D[] _icons;
        private static GUIStyle _button, _panel, _text, _field;

        /// <summary>Screen pixels per reference pixel: the screen height over <see cref="GameConfig.UiReferenceHeight"/>.</summary>
        public static float Scale => Screen.height / GameConfig.Instance.UiReferenceHeight;

        /// <summary>A rectangle in reference pixels (from the top-left), in screen pixels.</summary>
        /// <param name="reference">Position and size in reference pixels.</param>
        public static Rect ToScreen(Rect reference)
        {
            float s = Scale;
            return new Rect(reference.x * s, reference.y * s, reference.width * s, reference.height * s);
        }

        /// <summary>
        /// The picture cut down to its opaque pixels (cached; built on first call). The source must be Read/Write.
        /// </summary>
        /// <param name="source">An imported picture with transparent margins.</param>
        public static Texture2D Trimmed(Texture2D source)
        {
            if (!Trims.TryGetValue(source, out var trimmed) || trimmed == null) TrimTogether(source);
            return Trims[source];
        }

        /// <summary>Draws the dialog panel picture as a 9-slice box filling <paramref name="rect"/> (screen pixels). Draws on repaint only.</summary>
        /// <param name="rect">Panel area in screen pixels.</param>
        public static void Panel(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyles();
            float art = Scale * GameConfig.Instance.UiPanelArtScale;
            var saved = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(art, art), rect.position);
            _panel.Draw(new Rect(rect.position, rect.size / art), false, false, false, false);
            GUI.matrix = saved;
        }

        /// <summary>
        /// An art button: normal, hover and pressed pictures, centred light text with a shadow. Returns true on the
        /// frame it is clicked, like <see cref="GUI.Button(Rect, string)"/>.
        /// </summary>
        /// <param name="rect">Button area in screen pixels.</param>
        /// <param name="text">Text on the button.</param>
        /// <param name="referenceFontSize">Font size in reference pixels (scaled with the screen).</param>
        public static bool Button(Rect rect, string text, int referenceFontSize)
        {
            EnsureStyles();
            float art = Scale * GameConfig.Instance.UiButtonArtScale;
            var saved = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(art, art), rect.position);
            bool clicked = GUI.Button(new Rect(rect.position, rect.size / art), GUIContent.none, _button);
            GUI.matrix = saved;
            Label(rect, text, referenceFontSize, TextAnchor.MiddleCenter, GameConfig.Instance.UiTextColor, FontStyle.Bold);
            return clicked;
        }

        /// <summary>Light, word-wrapped text with a shadow. Draws on repaint only.</summary>
        /// <param name="rect">Text area in screen pixels.</param>
        /// <param name="text">The text.</param>
        /// <param name="referenceFontSize">Font size in reference pixels (scaled with the screen).</param>
        /// <param name="alignment">Where the text sits in <paramref name="rect"/>.</param>
        /// <param name="color">Text colour (the shadow colour comes from config).</param>
        /// <param name="fontStyle">Normal or bold.</param>
        public static void Label(Rect rect, string text, int referenceFontSize, TextAnchor alignment, Color color,
            FontStyle fontStyle = FontStyle.Normal)
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyles();
            var config = GameConfig.Instance;
            _text.fontSize = FontSize(referenceFontSize);
            _text.alignment = alignment;
            _text.fontStyle = fontStyle;
            var content = new GUIContent(text);
            _text.normal.textColor = config.UiTextShadowColor;
            _text.Draw(new Rect(rect.position + config.UiTextShadowOffset * Scale, rect.size), content, false, false, false, false);
            _text.normal.textColor = color;
            _text.Draw(rect, content, false, false, false, false);
        }

        /// <summary>Height, in screen pixels, that <see cref="Label"/> needs for <paramref name="text"/> wrapped to <paramref name="width"/>.</summary>
        /// <param name="text">The text.</param>
        /// <param name="width">Available width in screen pixels.</param>
        /// <param name="referenceFontSize">Font size in reference pixels.</param>
        public static float LabelHeight(string text, float width, int referenceFontSize)
        {
            EnsureStyles();
            _text.fontSize = FontSize(referenceFontSize);
            _text.fontStyle = FontStyle.Normal;
            return _text.CalcHeight(new GUIContent(text), width);
        }

        /// <summary>A one-line text field with light text at a scaled font size. Returns the edited text.</summary>
        /// <param name="rect">Field area in screen pixels.</param>
        /// <param name="text">Current text.</param>
        /// <param name="maxLength">Most characters accepted.</param>
        /// <param name="referenceFontSize">Font size in reference pixels.</param>
        public static string TextField(Rect rect, string text, int maxLength, int referenceFontSize)
        {
            EnsureStyles();
            _field.fontSize = FontSize(referenceFontSize);
            return GUI.TextField(rect, text, maxLength, _field);
        }

        /// <summary>
        /// Story 017: draws a HUD frame or plate picture (trimmed to its opaque pixels) filling <paramref name="rect"/>.
        /// The picture is scaled to the rectangle's height and 9-sliced across its width, so the ends and corners are
        /// never stretched. Draws on repaint only.
        /// </summary>
        /// <param name="rect">Frame area in screen pixels.</param>
        /// <param name="source">The frame picture (Read/Write, transparent margins).</param>
        /// <param name="border">9-slice border in trimmed-picture pixels (x left, y right, z top, w bottom).</param>
        public static void Frame(Rect rect, Texture2D source, Vector4 border)
        {
            if (Event.current.type != EventType.Repaint) return;
            var style = FrameStyle(source);
            var offset = style.border; // set in place: no RectOffset per call
            offset.left = Mathf.RoundToInt(border.x);
            offset.right = Mathf.RoundToInt(border.y);
            offset.top = Mathf.RoundToInt(border.z);
            offset.bottom = Mathf.RoundToInt(border.w);
            float height = style.normal.background.height;
            float art = rect.height / height;
            var saved = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(art, art), rect.position);
            style.Draw(new Rect(rect.x, rect.y, rect.width / art, height), false, false, false, false);
            GUI.matrix = saved;
        }

        /// <summary>
        /// Story 017: a fill bar inside a frame picture. The back colour fills the frame's inner area, the fill colour
        /// covers <paramref name="fraction"/> of it from the left, then the frame is drawn on top (<see cref="Frame"/>).
        /// Draws on repaint only.
        /// </summary>
        /// <param name="rect">Frame area in screen pixels.</param>
        /// <param name="frame">The frame picture.</param>
        /// <param name="border">9-slice border in trimmed-picture pixels.</param>
        /// <param name="fillInset">The inner area's distance from each frame edge, in trimmed-picture pixels (x left,
        /// y right, z top, w bottom). Keep x and y within <paramref name="border"/> so the fill lines up with the ends.</param>
        /// <param name="fraction">Filled share, 0 to 1.</param>
        /// <param name="fill">Fill colour.</param>
        /// <param name="back">Colour of the empty part.</param>
        public static void Bar(Rect rect, Texture2D frame, Vector4 border, Vector4 fillInset, float fraction, Color fill, Color back)
        {
            if (Event.current.type != EventType.Repaint) return;
            float art = rect.height / FrameStyle(frame).normal.background.height;
            var inner = new Rect(rect.x + fillInset.x * art, rect.y + fillInset.z * art,
                rect.width - (fillInset.x + fillInset.y) * art, rect.height - (fillInset.z + fillInset.w) * art);
            var previous = GUI.color;
            GUI.color = back;
            GUI.DrawTexture(inner, Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(inner.x, inner.y, inner.width * Mathf.Clamp01(fraction), inner.height), Texture2D.whiteTexture);
            GUI.color = previous;
            Frame(rect, frame, border);
        }

        /// <summary>
        /// Story 017: one HUD icon, cut from <see cref="GameConfig.HudIconsTexture"/> (cached; built on first call). Each
        /// icon is its cell of the <see cref="GameConfig.HudIconGrid"/> trimmed to its opaque pixels, so the cells need
        /// not be exact as long as each icon stays inside its own cell.
        /// </summary>
        /// <param name="icon">Which icon.</param>
        public static Texture2D Icon(HudIcon icon)
        {
            if (_icons == null || _icons[0] == null) _icons = CutIcons(GameConfig.Instance.HudIconsTexture, GameConfig.Instance.HudIconGrid);
            return _icons[(int)icon];
        }

        /// <summary>Draws a HUD icon keeping its aspect inside <paramref name="rect"/> (screen pixels). Draws on repaint only.</summary>
        /// <param name="rect">Icon area in screen pixels.</param>
        /// <param name="icon">Which icon.</param>
        public static void DrawIcon(Rect rect, HudIcon icon)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(rect, Icon(icon), ScaleMode.ScaleToFit);
        }

        private static GUIStyle FrameStyle(Texture2D source)
        {
            if (!Frames.TryGetValue(source, out var style) || style.normal.background == null)
            {
                style = new GUIStyle(GUIStyle.none) { normal = { background = TrimTogether(source)[0] } };
                Frames[source] = style;
            }
            return style;
        }

        // Each icon: its grid cell (row 0 at the top of the picture) cut to the opaque pixels inside it.
        private static Texture2D[] CutIcons(Texture2D sheet, Vector2Int grid)
        {
            int w = sheet.width, h = sheet.height;
            var px = sheet.GetPixels32();
            var icons = new Texture2D[IconCount];
            for (int i = 0; i < IconCount; i++)
            {
                int col = i % grid.x, row = i / grid.x;
                int x0 = col * w / grid.x, x1 = (col + 1) * w / grid.x;
                int y0 = h - (row + 1) * h / grid.y, y1 = h - row * h / grid.y; // texture rows count from the bottom
                int xMin = x1, yMin = y1, xMax = x0 - 1, yMax = y0 - 1;
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        if (px[y * w + x].a == 0) continue;
                        if (x < xMin) xMin = x;
                        if (x > xMax) xMax = x;
                        if (y < yMin) yMin = y;
                        if (y > yMax) yMax = y;
                    }
                }
                icons[i] = Copy(px, w, xMin, yMin, xMax - xMin + 1, yMax - yMin + 1, $"{sheet.name} {(HudIcon)i}");
            }
            return icons;
        }

        private static int FontSize(int referenceFontSize) => Mathf.Max(1, Mathf.RoundToInt(referenceFontSize * Scale));

        // Built on first use inside OnGUI (GUI.skin is only available there); rebuilt if the trimmed textures are gone
        // (play mode without a domain reload). Borders are refreshed every call so config edits show at once.
        private static void EnsureStyles()
        {
            var config = GameConfig.Instance;
            if (_button == null || _button.normal.background == null)
            {
                var states = TrimTogether(config.UiButtonTexture, config.UiButtonHoverTexture, config.UiButtonPressedTexture);
                _button = new GUIStyle(GUIStyle.none)
                {
                    normal = { background = states[0] },
                    hover = { background = states[1] },
                    active = { background = states[2] },
                };
                _panel = new GUIStyle(GUIStyle.none) { normal = { background = TrimTogether(config.UiPanelTexture)[0] } };
                _text = new GUIStyle(GUI.skin.label) { wordWrap = true, padding = new RectOffset(), margin = new RectOffset() };
                _field = new GUIStyle(GUI.skin.textField) { alignment = TextAnchor.MiddleLeft };
                _field.normal.textColor = _field.hover.textColor = _field.focused.textColor = config.UiTextColor;
            }
            _button.border = ToOffset(config.UiButtonBorder);
            _panel.border = ToOffset(config.UiPanelBorder);
        }

        private static RectOffset ToOffset(Vector4 border) =>
            new RectOffset(Mathf.RoundToInt(border.x), Mathf.RoundToInt(border.y), Mathf.RoundToInt(border.z), Mathf.RoundToInt(border.w));

        // Cuts every source (all the same size) to the union of their opaque pixels, so they stay aligned with each other.
        // The copies get mipmaps (they are drawn well below full size) and drop their CPU pixels after upload.
        private static Texture2D[] TrimTogether(params Texture2D[] sources)
        {
            int w = sources[0].width, h = sources[0].height;
            int xMin = w, yMin = h, xMax = -1, yMax = -1;
            var pixels = new Color32[sources.Length][];
            for (int s = 0; s < sources.Length; s++)
            {
                pixels[s] = sources[s].GetPixels32();
                var px = pixels[s];
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        if (px[row + x].a == 0) continue;
                        if (x < xMin) xMin = x;
                        if (x > xMax) xMax = x;
                        if (y < yMin) yMin = y;
                        if (y > yMax) yMax = y;
                    }
                }
            }

            int tw = xMax - xMin + 1, th = yMax - yMin + 1;
            var result = new Texture2D[sources.Length];
            for (int s = 0; s < sources.Length; s++)
            {
                var trimmed = Copy(pixels[s], w, xMin, yMin, tw, th, sources[s].name + " (trimmed)");
                Trims[sources[s]] = trimmed;
                result[s] = trimmed;
            }
            return result;
        }

        // A new texture from a rectangle of a source's pixels (w = source width), with mipmaps (drawn well below full
        // size); it drops its CPU pixels after upload.
        private static Texture2D Copy(Color32[] pixels, int w, int xMin, int yMin, int tw, int th, string name)
        {
            var region = new Color32[tw * th];
            for (int y = 0; y < th; y++)
            {
                System.Array.Copy(pixels, (yMin + y) * w + xMin, region, y * tw, tw);
            }
            var texture = new Texture2D(tw, th, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };
            texture.SetPixels32(region);
            texture.Apply(true, true);
            return texture;
        }
    }
}
