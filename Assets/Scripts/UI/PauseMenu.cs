using System;
using System.Collections;
using LastWave.Config;
using LastWave.Flow;
using LastWave.Network;
using LastWave.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace LastWave.UI
{
    /// <summary>
    /// Story 024 (<c>design/game-brief.md</c> Feature 24): the pause menu. Escape opens it on THIS machine only; the game
    /// keeps running for everyone, so it is not a real pause. Resume, Setting (the story 021 screen, coming back here),
    /// Quit and Main Menu, the last two after a Yes / No question. Leaving goes through <see cref="RoomService.LeaveRoom"/>,
    /// so a host leaving closes the room for everybody, exactly as the end screen's Back to menu does.
    /// <para>
    /// While the menu or the Setting screen is open <see cref="GameplayBlocked"/> is true: the player scripts read it and
    /// stand still, do not shoot, repair or revive, keep their aim and show the normal cursor. It stays true until the left
    /// mouse button is released after the menu closes, so the click on Resume is not a shot. Escape does nothing once the
    /// game is over or while the upgrade cards are open; the menu closes by itself when the game ends. Layout, texts and
    /// colours come from <see cref="GameConfig"/> (<c>Pause*</c> fields).
    /// </para>
    /// </summary>
    /// <example>
    /// Scene object in the Game scene (added by <c>LastWaveSetup.Run</c>); nothing to configure.
    /// </example>
    public sealed class PauseMenu : MonoBehaviour
    {
        private enum Confirm { None, MainMenu, Quit }

        private static bool _releaseWait;

        private Confirm _confirm;
        private bool _showPanel = true;
        private bool _sample;
        private RoomService _room;
        private Action _onSettingsClosed;

        /// <summary>True while the pause menu is open (including while the Setting screen opened from it is showing).</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>
        /// True while gameplay input must be ignored: the pause menu or the Setting screen is open, or the left mouse button
        /// that closed them is still held. Read by the player scripts and the crosshair.
        /// </summary>
        public static bool GameplayBlocked => IsOpen || SettingsPanel.IsOpen || _releaseWait;

        // Domain reload is off in the editor: statics survive Stop/Play, so clear them at the start of every run.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsOpen = false;
            _releaseWait = false;
        }

        /// <summary>
        /// Capture helper (<c>--open-pause</c>): creates a pause menu that is already open in any scene, ignoring Escape and
        /// the game state. Its buttons do nothing useful without a room.
        /// </summary>
        public static void ShowSample()
        {
            var go = new GameObject(nameof(PauseMenu) + " (sample)");
            var menu = go.AddComponent<PauseMenu>();
            menu._sample = true;
            IsOpen = true;
        }

        private void Awake()
        {
            _onSettingsClosed = () => _showPanel = true;
        }

        private void OnDestroy()
        {
            // The scene is going away (leaving the room or quitting): nothing may stay blocked.
            IsOpen = false;
            _releaseWait = false;
        }

        private void Update()
        {
            if (_releaseWait && (Mouse.current == null || !Mouse.current.leftButton.isPressed)) _releaseWait = false;
            if (_sample) return;

            if (IsOpen && GameOutcome.IsOver)
            {
                CloseMenu(); // the end screen must be usable
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;
            var room = Room();
            if (room != null && room.State == RoomState.Busy) return; // already leaving

            if (!IsOpen)
            {
                if (GameOutcome.IsOver) return;
                var upgrades = PlayerUpgrades.Local;
                if (upgrades != null && upgrades.HasOpenOffer) return; // do not open on top of the upgrade cards
                IsOpen = true;
                _showPanel = true;
                _confirm = Confirm.None;
                return;
            }
            if (SettingsPanel.IsOpen) { SettingsPanel.Close(); return; } // back to the pause menu
            if (_confirm != Confirm.None) { _confirm = Confirm.None; return; }
            CloseMenu();
        }

        private void CloseMenu()
        {
            IsOpen = false;
            _confirm = Confirm.None;
            _showPanel = true;
            _releaseWait = true;
            if (SettingsPanel.IsOpen) SettingsPanel.Close();
        }

        private RoomService Room()
        {
            if (_room == null && NetworkManager.Singleton != null) _room = NetworkManager.Singleton.GetComponent<RoomService>();
            return _room;
        }

        private void OnGUI()
        {
            if (!IsOpen || !_showPanel || SettingsPanel.IsOpen) return;

            var config = GameConfig.Instance;
            GUI.depth = config.PauseGuiDepth; // every OnGUI call, layout and repaint alike
            float s = UiSkin.Scale;
            bool repaint = Event.current.type == EventType.Repaint;
            var room = Room();
            bool leaving = room != null && room.State == RoomState.Busy;

            if (repaint)
            {
                var previous = GUI.color;
                GUI.color = config.PauseDimColor;
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = previous;
            }

            var padding = config.PausePanelPadding * s;
            bool dialog = leaving || _confirm != Confirm.None;
            var size = (dialog ? config.PauseConfirmPanelSize : config.PausePanelSize) * s;
            var panel = new Rect((Screen.width - size.x) * 0.5f, (Screen.height - size.y) * 0.5f, size.x, size.y);
            UiSkin.Panel(panel);
            if (repaint) UiPointerBlock.Register(panel); // a click on the menu is not a shot

            if (dialog) DrawDialog(config, panel, padding, s, leaving ? room.Message : null);
            else DrawMenu(config, panel, padding, s);

            // A click sponge drawn last (see EndScreen): the buttons above win, every other click is swallowed.
            GUI.Button(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, GUIStyle.none);
        }

        private void DrawMenu(GameConfig config, Rect panel, Vector2 padding, float s)
        {
            float y = panel.y + padding.y, gap = config.PauseButtonGap * s;
            float noticeHeight = config.PauseNoticeHeight * s;
            UiSkin.Label(new Rect(panel.x + padding.x, y, panel.width - 2f * padding.x, noticeHeight), config.PauseNoticeText,
                config.PauseNoticeFontSize, TextAnchor.MiddleCenter, config.UiTextColor);
            y += noticeHeight + gap;

            var button = config.PauseButtonSize * s;
            float x = panel.center.x - button.x * 0.5f;
            if (UiSkin.Button(new Rect(x, y, button.x, button.y), config.PauseResumeText, config.MenuButtonFontSize)) CloseMenu();
            y += button.y + gap;
            if (UiSkin.Button(new Rect(x, y, button.x, button.y), config.PauseSettingText, config.MenuButtonFontSize))
            {
                _showPanel = false; // the pause panel hides while Setting shows; Back (or Escape) shows it again
                SettingsPanel.Open(_onSettingsClosed);
            }
            y += button.y + gap;
            if (UiSkin.Button(new Rect(x, y, button.x, button.y), config.PauseQuitText, config.MenuButtonFontSize)) _confirm = Confirm.Quit;
            y += button.y + gap;
            if (UiSkin.Button(new Rect(x, y, button.x, button.y), config.PauseMainMenuText, config.MenuButtonFontSize)) _confirm = Confirm.MainMenu;
        }

        // The question with Yes / No; or, while the room is being left, only the room service's text.
        private void DrawDialog(GameConfig config, Rect panel, Vector2 padding, float s, string leavingText)
        {
            var button = config.PauseConfirmButtonSize * s;
            float gap = config.PauseConfirmButtonGap * s;
            float textHeight = panel.height - 2f * padding.y - (leavingText == null ? button.y + gap : 0f);
            string text = leavingText ?? (_confirm == Confirm.Quit ? config.PauseConfirmQuitText : config.PauseConfirmMainMenuText);
            UiSkin.Label(new Rect(panel.x + padding.x, panel.y + padding.y, panel.width - 2f * padding.x, textHeight), text,
                config.PauseConfirmFontSize, TextAnchor.MiddleCenter, config.UiTextColor, FontStyle.Bold);
            if (leavingText != null) return;

            float buttonY = panel.yMax - padding.y - button.y, left = panel.center.x - button.x - gap * 0.5f;
            if (UiSkin.Button(new Rect(left, buttonY, button.x, button.y), config.PauseYesText, config.MenuButtonFontSize))
            {
                var chosen = _confirm;
                _confirm = Confirm.None;
                if (chosen == Confirm.Quit) QuitGame(config); else LeaveToMenu(config);
            }
            if (UiSkin.Button(new Rect(left + button.x + gap, buttonY, button.x, button.y), config.PauseNoText, config.MenuButtonFontSize))
            {
                _confirm = Confirm.None;
            }
        }

        // LeaveRoom closes the room when this player is the host and then loads the menu scene itself. Without a room
        // (a debug run) there is nothing to leave, so the menu scene is loaded directly.
        private void LeaveToMenu(GameConfig config)
        {
            var room = Room();
            if (room != null && room.State == RoomState.InRoom) room.LeaveRoom();
            else if (!_sample) SceneManager.LoadScene(config.MenuSceneName);
        }

        // Leave first, quit afterwards: LeaveRoom is async and sets the Busy state at once, so the quit waits (with a
        // timeout) until it is done. This menu is destroyed by the scene change, so the wait runs on the room service.
        private void QuitGame(GameConfig config)
        {
            var room = Room();
            if (room != null && room.State == RoomState.InRoom)
            {
                room.LeaveRoom();
                room.StartCoroutine(QuitWhenLeft(room, config.PauseQuitLeaveTimeoutSeconds));
            }
            else
            {
                Quit();
            }
        }

        private static IEnumerator QuitWhenLeft(RoomService room, float timeoutSeconds)
        {
            float end = Time.unscaledTime + timeoutSeconds;
            yield return null;
            while (room != null && room.State == RoomState.Busy && Time.unscaledTime < end) yield return null;
            Quit();
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
