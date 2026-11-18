using LastWave.Config;
using LastWave.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastWave.Network
{
    /// <summary>
    /// IMGUI room menu: create room, join by code, lobby info, host-only Start, Leave, and the last error. Placed in
    /// both the Menu and Game scenes. Creates the NetworkManager from its prefab when none exists yet (NetworkManager
    /// keeps itself alive across scenes, so it is created only once).
    /// Story 016: while not in a room the menu picture covers the screen with the logo on its dark left side, and the
    /// rows sit in the art panel with art buttons (<see cref="UiSkin"/>); layout comes from <see cref="GameConfig"/>.
    /// Story 022: outside a room the panel shows one of the local <see cref="Page"/>s (Main Menu, room page, How to Play);
    /// the page is never networked. Being in a room resets it to the Main Menu, so leaving (or the room closing) lands
    /// there with the service's message, while a failed Create/Join stays on the room page.
    /// </summary>
    /// <example>
    /// Add to an empty GameObject and assign <c>NetworkManager.prefab</c> to the
    /// Network Manager Prefab field (done by <c>LastWaveSetup.Run</c>).
    /// </example>
    public sealed class RoomMenu : MonoBehaviour
    {
        private const int JoinCodeMaxLength = 16;

        /// <summary>Story 022: the pages shown while not in a room. Story 027 adds its Campaign / Free Play page here.</summary>
        public enum Page
        {
            /// <summary>Play, How to Play, Setting, Quit.</summary>
            Main,
            /// <summary>Create room, room code, Join room, Back.</summary>
            Room,
            /// <summary>Controls and goal, Back.</summary>
            HowToPlay,
        }

        [SerializeField] private RoomService _networkManagerPrefab;

        private RoomService _room;
        private string _codeInput = string.Empty;
        private Page _page = Page.Main;

        // Panel rows: laid out twice per event, first only measured (to size the panel), then drawn.
        private bool _measuring;
        private float _left, _top, _width, _cursor;

        private void Awake()
        {
            _room = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.GetComponent<RoomService>()
                : Instantiate(_networkManagerPrefab);
        }

        /// <summary>Story 022: shows <paramref name="page"/> the next time the menu is drawn outside a room.</summary>
        /// <param name="page">The page to show.</param>
        public void ShowPage(Page page)
        {
            _page = page;
        }

        private void OnGUI()
        {
            var config = GameConfig.Instance;
            if (_room.State == RoomState.InRoom) _page = Page.Main;

            // Story 007: in the Game scene, once in a room, the big panel shrinks to a corner strip. The Leave
            // button lives on the end screen. Outside a room (Menu state) the full panel stays, as before.
            bool inGame = SceneManager.GetActiveScene().name == config.GameSceneName;
            if (inGame && _room.State != RoomState.Menu)
            {
                DrawStrip(config);
                return;
            }

            // Story 016: the menu picture and logo only while not in a room; once in one the scene shows as before.
            if (_room.State != RoomState.InRoom && Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), config.MenuBackgroundTexture, ScaleMode.ScaleAndCrop);
                GUI.DrawTexture(UiSkin.ToScreen(config.MenuLogoRect), UiSkin.Trimmed(config.MenuLogoTexture), ScaleMode.ScaleToFit);
            }

            // Story 022: the Setting screen covers the menu; its Back returns to the page underneath (the Main Menu).
            if (SettingsPanel.IsOpen) return;

            float scale = UiSkin.Scale;
            var padding = config.MenuPanelPadding * scale;
            var origin = config.MenuPanelPosition * scale;
            float panelWidth = (_room.State == RoomState.Menu && _page == Page.HowToPlay ? config.HowToPlayPanelWidth : config.MenuPanelWidth) * scale;
            _width = panelWidth - 2f * padding.x;

            _measuring = true;
            _cursor = 0f;
            DrawRows(config);

            var panel = new Rect(origin.x, origin.y, panelWidth, _cursor + 2f * padding.y);
            UiSkin.Panel(panel);

            _measuring = false;
            _cursor = 0f;
            _left = panel.x + padding.x;
            _top = panel.y + padding.y;
            DrawRows(config);
        }

        private void DrawRows(GameConfig config)
        {
            switch (_room.State)
            {
                case RoomState.Menu:
                    DrawPage(config);
                    if (_page == Page.HowToPlay) return; // the room message belongs to the menu pages, not the help text
                    break;

                case RoomState.Busy:
                    Label(config, "Please wait...", config.UiTextColor);
                    break;

                case RoomState.InRoom:
                    Label(config, "Room code (give it to your friends):", config.UiTextColor);
                    Field(config, _room.JoinCode ?? string.Empty); // a text field so the code can be copied
                    Label(config, $"Players: {_room.PlayerCount}/{config.MaxPlayers}", config.UiTextColor);
                    Label(config, "WASD to walk.", config.UiTextColor);
                    // Only the Menu scene reaches here in a room (the Game scene draws the strip above).
                    if (_room.IsHost)
                    {
                        if (Button(config, "Start game")) _room.StartGame();
                    }
                    else
                    {
                        Label(config, "Waiting for the host to start...", config.UiTextColor);
                    }
                    if (Button(config, "Leave room")) _room.LeaveRoom();
                    break;
            }

            if (!string.IsNullOrEmpty(_room.Message)) Label(config, _room.Message, config.MenuMessageColor);
        }

        // Story 022: the page rows while not in a room.
        private void DrawPage(GameConfig config)
        {
            switch (_page)
            {
                case Page.Main:
                    if (Button(config, config.MainMenuPlayText)) _page = Page.Room;
                    if (Button(config, config.MainMenuHowToPlayText)) _page = Page.HowToPlay;
                    if (Button(config, config.MainMenuSettingText)) SettingsPanel.Open(null);
                    if (Button(config, config.MainMenuQuitText)) Quit();
                    break;

                case Page.Room:
                    if (Button(config, "Create room")) _room.CreateRoom();
                    Label(config, "Room code:", config.UiTextColor);
                    _codeInput = Field(config, _codeInput);
                    if (Button(config, "Join room")) _room.JoinRoom(_codeInput);
                    if (Button(config, config.MenuBackText)) _page = Page.Main;
                    break;

                case Page.HowToPlay:
                    Label(config, config.HowToPlayTitleText, config.UiTextColor, config.HowToPlayTitleFontSize, TextAnchor.MiddleCenter, FontStyle.Bold);
                    foreach (var line in config.HowToPlayLines)
                        Label(config, line, config.UiTextColor, config.HowToPlayFontSize, TextAnchor.MiddleLeft, FontStyle.Normal);
                    if (Button(config, config.MenuBackText)) _page = Page.Main;
                    break;
            }
        }

        // Quit closes the player; in the editor Application.Quit is ignored, so play mode is stopped instead.
        private static void Quit()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // The next row's rectangle in screen pixels, below the previous one with the configured gap.
        private Rect NextRow(GameConfig config, float height)
        {
            if (_cursor > 0f) _cursor += config.MenuRowSpacing * UiSkin.Scale;
            var rect = new Rect(_left, _top + _cursor, _width, height);
            _cursor += height;
            return rect;
        }

        private bool Button(GameConfig config, string text)
        {
            var rect = NextRow(config, config.MenuButtonHeight * UiSkin.Scale);
            return !_measuring && UiSkin.Button(rect, text, config.MenuButtonFontSize);
        }

        private void Label(GameConfig config, string text, Color color)
        {
            Label(config, text, color, config.MenuLabelFontSize, TextAnchor.MiddleLeft, FontStyle.Normal);
        }

        private void Label(GameConfig config, string text, Color color, int fontSize, TextAnchor alignment, FontStyle fontStyle)
        {
            // LabelHeight measures normal weight; a bold one-line title still fits the minimum row height.
            float height = Mathf.Max(config.MenuLabelHeight * UiSkin.Scale, UiSkin.LabelHeight(text, _width, fontSize));
            var rect = NextRow(config, height);
            if (!_measuring) UiSkin.Label(rect, text, fontSize, alignment, color, fontStyle);
        }

        private string Field(GameConfig config, string text)
        {
            var rect = NextRow(config, config.MenuFieldHeight * UiSkin.Scale);
            return _measuring ? text : UiSkin.TextField(rect, text, JoinCodeMaxLength, config.MenuFieldFontSize);
        }

        // The Game scene's room strip: room code and player count only, in a corner (story 016: top-right by default, so
        // it does not cover the base). Its rectangle is registered so a click on it never fires a shot (UiPointerBlock).
        // While leaving it shows the service's message instead.
        private void DrawStrip(GameConfig config)
        {
            // Story 017: on the room-info plate, sizes in reference pixels.
            float scale = UiSkin.Scale;
            var size = config.RoomStripSize * scale;
            var anchor = config.RoomStripAnchor;
            var gap = config.RoomStripPosition * scale;
            var position = new Vector2(
                anchor.x * (Screen.width - size.x) + (1f - 2f * anchor.x) * gap.x,
                anchor.y * (Screen.height - size.y) + (1f - 2f * anchor.y) * gap.y);
            var rect = new Rect(position, size);
            if (Event.current.type != EventType.Repaint) return;
            UiPointerBlock.Register(rect);
            UiSkin.Frame(rect, config.HudRoomPlateTexture, config.HudRoomPlateBorder);

            string text = _room.State == RoomState.InRoom
                ? $"{config.RoomStripCodeLabel}: {_room.JoinCode}   {config.RoomStripPlayersLabel}: {_room.PlayerCount}/{config.MaxPlayers}"
                : _room.Message;
            float padding = config.HudRoomTextPadding * scale;
            UiSkin.Label(new Rect(rect.x + padding, rect.y, rect.width - 2f * padding, rect.height), text,
                config.RoomStripFontSize, TextAnchor.MiddleLeft, config.UiTextColor);
        }
    }
}
