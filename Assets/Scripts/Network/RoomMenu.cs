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
    /// </summary>
    /// <example>
    /// Add to an empty GameObject and assign <c>NetworkManager.prefab</c> to the
    /// Network Manager Prefab field (done by <c>LastWaveSetup.Run</c>).
    /// </example>
    public sealed class RoomMenu : MonoBehaviour
    {
        private const int JoinCodeMaxLength = 16;

        [SerializeField] private RoomService _networkManagerPrefab;

        private RoomService _room;
        private string _codeInput = string.Empty;

        // Panel rows: laid out twice per event, first only measured (to size the panel), then drawn.
        private bool _measuring;
        private float _left, _top, _width, _cursor;

        private void Awake()
        {
            _room = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.GetComponent<RoomService>()
                : Instantiate(_networkManagerPrefab);
        }

        private void OnGUI()
        {
            var config = GameConfig.Instance;

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

            float scale = UiSkin.Scale;
            var padding = config.MenuPanelPadding * scale;
            var origin = config.MenuPanelPosition * scale;
            _width = config.MenuPanelWidth * scale - 2f * padding.x;

            _measuring = true;
            _cursor = 0f;
            DrawRows(config);

            var panel = new Rect(origin.x, origin.y, config.MenuPanelWidth * scale, _cursor + 2f * padding.y);
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
                    if (Button(config, "Create room")) _room.CreateRoom();
                    Label(config, "Room code:", config.UiTextColor);
                    _codeInput = Field(config, _codeInput);
                    if (Button(config, "Join room")) _room.JoinRoom(_codeInput);
                    // TEMP (story 021): opens the Setting screen until story 022's Main Menu replaces this panel.
                    if (Button(config, "Setting") && !SettingsPanel.IsOpen) SettingsPanel.Open(null);
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
            float height = Mathf.Max(config.MenuLabelHeight * UiSkin.Scale, UiSkin.LabelHeight(text, _width, config.MenuLabelFontSize));
            var rect = NextRow(config, height);
            if (!_measuring) UiSkin.Label(rect, text, config.MenuLabelFontSize, TextAnchor.MiddleLeft, color);
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
