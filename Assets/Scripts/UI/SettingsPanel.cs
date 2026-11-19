using System;
using System.Collections.Generic;
using LastWave.Config;
using UnityEngine;

namespace LastWave.UI
{
    /// <summary>
    /// Story 021 (<c>design/game-brief.md</c> Feature 21): the Setting screen, one piece shared by the Main Menu (story 022)
    /// and the pause menu (story 024). Fullscreen on/off, a resolution picked from what the display supports, and master,
    /// music and effects volume sliders, with Apply and Back.
    /// <para>
    /// Everything is local: saved with <see cref="PlayerPrefs"/> on this machine and never sent over the network. The saved
    /// display mode is applied at startup (only once something has been saved), the volumes are loaded then too. There is
    /// no sound yet, so the volumes are only stored and exposed (<see cref="MasterVolume"/>, <see cref="MusicVolume"/>,
    /// <see cref="SfxVolume"/>) for a later audio system; the master volume also drives <see cref="AudioListener.volume"/>
    /// when <see cref="GameConfig.SettingsMasterDrivesAudioListener"/> is set.
    /// </para>
    /// <para>
    /// The panel lives on its own object created before the first scene loads (kept across scenes), so it depends on no
    /// scene. While open it draws over everything (<see cref="GameConfig.SettingsGuiDepth"/>), dims the screen and swallows
    /// mouse and key events so the screen under it cannot be used. Apply applies and saves, and the panel stays open. Back
    /// (or <see cref="Close"/>) closes it, dropping display and volume changes that were not applied. The volume sliders are
    /// heard (through the listener) while dragging. Layout, texts and art come from <see cref="GameConfig"/>.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// if (UiSkin.Button(rect, "Setting", config.MenuButtonFontSize)) SettingsPanel.Open(() => _showMenu = true);
    /// // in the menu's own OnGUI: if (SettingsPanel.IsOpen) return; // optional, the panel covers it anyway
    /// </code>
    /// </example>
    public sealed class SettingsPanel : MonoBehaviour
    {
        private const string FullscreenKey = "LastWave.Settings.Fullscreen";
        private const string WidthKey = "LastWave.Settings.Width";
        private const string HeightKey = "LastWave.Settings.Height";
        private const string MasterKey = "LastWave.Settings.MasterVolume";
        private const string MusicKey = "LastWave.Settings.MusicVolume";
        private const string SfxKey = "LastWave.Settings.SfxVolume";

        private static readonly List<Vector2Int> Resolutions = new List<Vector2Int>();
        private static readonly List<string> ResolutionLabels = new List<string>();
        private static readonly string[] Percents = new string[101];

        private static Action _onClose;
        private static bool _pendingFullscreen;
        private static int _pendingResolution;
        private static float _savedMaster, _savedMusic, _savedSfx;

        /// <summary>True while the Setting screen is showing.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>Master volume, 0 to 1 (saved, or being previewed while the panel is open).</summary>
        public static float MasterVolume { get; private set; } = 1f;

        /// <summary>Music volume, 0 to 1. Nothing plays music yet; a later audio system reads it.</summary>
        public static float MusicVolume { get; private set; } = 1f;

        /// <summary>Sound effects volume, 0 to 1. Nothing plays effects yet; a later audio system reads it.</summary>
        public static float SfxVolume { get; private set; } = 1f;

        /// <summary>
        /// Shows the Setting screen with the current display mode and saved volumes. Does nothing if it is already open.
        /// </summary>
        /// <param name="onClose">Called once when the player presses Back (or <see cref="Close"/> is called); may be null.</param>
        public static void Open(Action onClose)
        {
            if (IsOpen) return;
            _onClose = onClose;
            _pendingFullscreen = Screen.fullScreenMode != FullScreenMode.Windowed;
            ListResolutions(_pendingFullscreen);
            _pendingResolution = Nearest(Screen.width, Screen.height);
            _savedMaster = MasterVolume;
            _savedMusic = MusicVolume;
            _savedSfx = SfxVolume;
            IsOpen = true;
        }

        /// <summary>
        /// Closes the Setting screen like Back: display changes not applied are dropped and the volumes go back to their
        /// saved values. Calls the <c>onClose</c> given to <see cref="Open"/>. Does nothing if it is not open.
        /// </summary>
        public static void Close()
        {
            if (!IsOpen) return;
            MasterVolume = _savedMaster;
            MusicVolume = _savedMusic;
            SfxVolume = _savedSfx;
            ApplyMaster();
            IsOpen = false;
            var callback = _onClose;
            _onClose = null;
            callback?.Invoke();
        }

        // Loads the saved values and applies them, before the first scene, then creates the drawing object.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            // Domain reload is off in the editor: statics survive Stop/Play, so start closed every run.
            IsOpen = false;
            _onClose = null;
            var config = GameConfig.Instance;
            var defaults = config.SettingsDefaultVolumes;
            MasterVolume = PlayerPrefs.GetFloat(MasterKey, defaults.x);
            MusicVolume = PlayerPrefs.GetFloat(MusicKey, defaults.y);
            SfxVolume = PlayerPrefs.GetFloat(SfxKey, defaults.z);
            ApplyMaster();
            // A saved width or height of 0 or less counts as unset. A saved windowed size too large for the desktop is
            // reduced to the nearest allowed one (ListResolutions), so the title bar never starts off-screen.
            int savedWidth = PlayerPrefs.GetInt(WidthKey, 0), savedHeight = PlayerPrefs.GetInt(HeightKey, 0);
            if (savedWidth > 0 && savedHeight > 0)
            {
                bool fullscreen = PlayerPrefs.GetInt(FullscreenKey, 1) != 0;
                ListResolutions(fullscreen);
                if (Resolutions.Count > 0)
                {
                    var size = Resolutions[Nearest(savedWidth, savedHeight)];
                    Screen.SetResolution(size.x, size.y, fullscreen ? config.SettingsFullscreenMode : FullScreenMode.Windowed);
                }
            }

            var go = new GameObject(nameof(SettingsPanel));
            DontDestroyOnLoad(go);
            go.AddComponent<SettingsPanel>();
        }

        // Applies the pending display mode at once and saves everything; the panel stays open.
        private static void Apply()
        {
            if (Resolutions.Count > 0)
            {
                var size = Resolutions[_pendingResolution];
                Screen.SetResolution(size.x, size.y, _pendingFullscreen ? GameConfig.Instance.SettingsFullscreenMode : FullScreenMode.Windowed);
                PlayerPrefs.SetInt(FullscreenKey, _pendingFullscreen ? 1 : 0);
                PlayerPrefs.SetInt(WidthKey, size.x);
                PlayerPrefs.SetInt(HeightKey, size.y);
            }
            PlayerPrefs.SetFloat(MasterKey, MasterVolume);
            PlayerPrefs.SetFloat(MusicKey, MusicVolume);
            PlayerPrefs.SetFloat(SfxKey, SfxVolume);
            PlayerPrefs.Save();
            _savedMaster = MasterVolume;
            _savedMusic = MusicVolume;
            _savedSfx = SfxVolume;
        }

        private static void ApplyMaster()
        {
            if (GameConfig.Instance.SettingsMasterDrivesAudioListener) AudioListener.volume = MasterVolume;
        }

        // The display's modes, one per width x height (refresh rates merged), smallest first, with their labels. Windowed,
        // only sizes strictly smaller than the desktop in both width and height, so the window's title bar stays on-screen.
        // If nothing qualifies (or the Editor lists nothing), the current window size is the only entry.
        private static void ListResolutions(bool fullscreen)
        {
            Resolutions.Clear();
            ResolutionLabels.Clear();
            var desktop = Screen.currentResolution;
            foreach (var mode in Screen.resolutions)
            {
                var size = new Vector2Int(mode.width, mode.height);
                if (!fullscreen && (size.x >= desktop.width || size.y >= desktop.height)) continue;
                if (!Resolutions.Contains(size)) Resolutions.Add(size);
            }
            if (Resolutions.Count == 0) Resolutions.Add(new Vector2Int(Screen.width, Screen.height));
            Resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            foreach (var size in Resolutions) ResolutionLabels.Add($"{size.x} x {size.y}");
        }

        // Index of the listed resolution closest to width x height (exact when it is listed).
        private static int Nearest(int width, int height)
        {
            int best = 0, bestDistance = int.MaxValue;
            for (int i = 0; i < Resolutions.Count; i++)
            {
                int distance = Mathf.Abs(Resolutions[i].x - width) + Mathf.Abs(Resolutions[i].y - height);
                if (distance >= bestDistance) continue;
                best = i;
                bestDistance = distance;
            }
            return best;
        }

        private static string Percent(float value)
        {
            int i = Mathf.RoundToInt(Mathf.Clamp01(value) * 100f);
            return Percents[i] ??= i + "%";
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            var config = GameConfig.Instance;
            GUI.depth = config.SettingsGuiDepth;
            float s = UiSkin.Scale;

            if (Event.current.type == EventType.Repaint)
            {
                var previous = GUI.color;
                GUI.color = config.SettingsDimColor;
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = previous;
            }

            var size = config.SettingsPanelSize * s;
            var panel = new Rect((Screen.width - size.x) * 0.5f, (Screen.height - size.y) * 0.5f, size.x, size.y);
            UiSkin.Panel(panel);

            var padding = config.SettingsPanelPadding * s;
            float left = panel.x + padding.x, width = panel.width - 2f * padding.x, y = panel.y + padding.y;
            float rowHeight = config.SettingsRowHeight * s, gap = config.SettingsRowSpacing * s;
            float labelWidth = config.SettingsLabelWidth * s;
            float controlX = left + labelWidth, controlWidth = width - labelWidth;

            float titleHeight = config.SettingsTitleHeight * s;
            UiSkin.Label(new Rect(left, y, width, titleHeight), config.SettingsTitleText, config.SettingsTitleFontSize,
                TextAnchor.MiddleCenter, config.UiTextColor, FontStyle.Bold);
            y += titleHeight + gap;

            // Fullscreen: checkbox art when both pictures exist, else an art button reading On/Off.
            RowLabel(config, left, y, labelWidth, rowHeight, config.SettingsFullscreenLabel);
            bool toggled;
            if (config.UiCheckboxOffTexture != null && config.UiCheckboxOnTexture != null)
            {
                toggled = UiSkin.ImageButton(new Rect(controlX, y, rowHeight, rowHeight),
                    _pendingFullscreen ? config.UiCheckboxOnTexture : config.UiCheckboxOffTexture);
            }
            else
            {
                var toggle = config.SettingsToggleSize * s;
                toggled = UiSkin.Button(new Rect(controlX, y + (rowHeight - toggle.y) * 0.5f, toggle.x, toggle.y),
                    _pendingFullscreen ? config.SettingsOnText : config.SettingsOffText, config.SettingsButtonFontSize);
            }
            if (toggled)
            {
                // The allowed sizes differ between fullscreen and windowed: rebuild the list and keep the nearest size.
                var current = Resolutions.Count > 0 ? Resolutions[_pendingResolution] : new Vector2Int(Screen.width, Screen.height);
                _pendingFullscreen = !_pendingFullscreen;
                ListResolutions(_pendingFullscreen);
                _pendingResolution = Nearest(current.x, current.y);
            }
            y += rowHeight + gap;

            // Resolution: < 1920 x 1080 >, stopping at both ends of the list.
            RowLabel(config, left, y, labelWidth, rowHeight, config.SettingsResolutionLabel);
            float arrow = config.SettingsArrowSize * s, arrowY = y + (rowHeight - arrow) * 0.5f;
            if (Arrow(config, new Rect(controlX, arrowY, arrow, arrow), config.UiArrowLeftTexture, "<"))
            {
                _pendingResolution = Mathf.Max(0, _pendingResolution - 1);
            }
            if (Arrow(config, new Rect(controlX + controlWidth - arrow, arrowY, arrow, arrow), config.UiArrowRightTexture, ">"))
            {
                _pendingResolution = Mathf.Min(Resolutions.Count - 1, _pendingResolution + 1);
            }
            if (ResolutionLabels.Count > 0)
            {
                UiSkin.Label(new Rect(controlX + arrow, y, controlWidth - 2f * arrow, rowHeight), ResolutionLabels[_pendingResolution],
                    config.SettingsLabelFontSize, TextAnchor.MiddleCenter, config.UiTextColor);
            }
            y += rowHeight + gap;

            float master = VolumeRow(config, left, y, labelWidth, controlWidth, rowHeight, config.SettingsMasterLabel, MasterVolume);
            if (!Mathf.Approximately(master, MasterVolume))
            {
                MasterVolume = master;
                ApplyMaster();
            }
            y += rowHeight + gap;
            MusicVolume = VolumeRow(config, left, y, labelWidth, controlWidth, rowHeight, config.SettingsMusicLabel, MusicVolume);
            y += rowHeight + gap;
            SfxVolume = VolumeRow(config, left, y, labelWidth, controlWidth, rowHeight, config.SettingsSfxLabel, SfxVolume);

            // Apply and Back, centred along the bottom of the panel.
            var button = config.SettingsButtonSize * s;
            float buttonGap = config.SettingsButtonGap * s;
            float buttonX = panel.center.x - button.x - buttonGap * 0.5f, buttonY = panel.yMax - padding.y - button.y;
            if (UiSkin.Button(new Rect(buttonX, buttonY, button.x, button.y), config.SettingsApplyText, config.SettingsButtonFontSize)) Apply();
            if (UiSkin.Button(new Rect(buttonX + button.x + buttonGap, buttonY, button.x, button.y), config.SettingsBackText, config.SettingsButtonFontSize)) Close();

            // Nothing under the panel reacts while it is open (scripts at a higher depth see the event as used).
            var e = Event.current;
            if (e.isMouse || e.isKey || e.type == EventType.ScrollWheel) e.Use();
        }

        private static void RowLabel(GameConfig config, float x, float y, float width, float height, string text)
        {
            UiSkin.Label(new Rect(x, y, width, height), text, config.SettingsLabelFontSize, TextAnchor.MiddleLeft, config.UiTextColor);
        }

        // Arrow art when set, else an art button with the fallback text.
        private static bool Arrow(GameConfig config, Rect rect, Texture2D picture, string fallback)
        {
            return picture != null ? UiSkin.ImageButton(rect, picture) : UiSkin.Button(rect, fallback, config.SettingsButtonFontSize);
        }

        // Label, slider and percentage; the slider is inset by half a knob so the knob stays inside the row at 0 and 1.
        private static float VolumeRow(GameConfig config, float left, float y, float labelWidth, float controlWidth, float rowHeight,
            string label, float value)
        {
            float s = UiSkin.Scale;
            RowLabel(config, left, y, labelWidth, rowHeight, label);
            float controlX = left + labelWidth;
            float valueWidth = config.SettingsValueWidth * s, knob = config.SettingsSliderKnobSize.x * s;
            float trackHeight = config.SettingsSliderHeight * s;
            var track = new Rect(controlX + knob * 0.5f, y + (rowHeight - trackHeight) * 0.5f, controlWidth - valueWidth - knob, trackHeight);
            value = UiSkin.Slider(track, value);
            UiSkin.Label(new Rect(controlX + controlWidth - valueWidth, y, valueWidth, rowHeight), Percent(value),
                config.SettingsLabelFontSize, TextAnchor.MiddleRight, config.UiTextColor);
            return value;
        }
    }
}
