using LastWave.Config;
using LastWave.Player;
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
        private bool _samplePicks; // story 023 capture flag: cards with fake picks, no room

        // Story 023: character-card pictures (first idle frame of each body sheet) and taken-by labels, built once, not per frame.
        private Texture2D[] _pictureTexture;
        private Rect[] _pictureUv;
        private string[] _takenLabels;
        private string _takenFormat;
        private string[] _readyLabels;
        private string _readyFormat;
        private string _readyHint;          // the host's "Waiting for players: r/t ready", rebuilt only when the counts change
        private int _readyHintReady = -1, _readyHintTotal = -1;
        private string _readyHintFormat;
        private string[] _cardStatus;       // AC 6: per-card status line, rebuilt only when the picks change
        private bool _cardStatusDirty = true;
        private ulong _cardStatusFor;

        // Panel rows: laid out twice per event, first only measured (to size the panel), then drawn.
        private bool _measuring;
        private float _left, _top, _width, _cursor;

        private void OnEnable()
        {
            CharacterSelection.PicksChanged += MarkCardStatusDirty;
        }

        private void OnDisable()
        {
            CharacterSelection.PicksChanged -= MarkCardStatusDirty;
        }

        private void MarkCardStatusDirty()
        {
            _cardStatusDirty = true;
        }

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

        /// <summary>Story 023 capture help (<c>--page select</c>): draws the character cards with fake picks (card 1 selected by me and player 3, card 2 confirmed by player 2), no room needed. Layout check only.</summary>
        public void ShowSampleSelect()
        {
            _samplePicks = true;
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

            // Story 023: the character cards sit beside the lobby panel, in the Menu scene while in a room.
            if (_room.State == RoomState.InRoom || _samplePicks) DrawCharacterSelect(config, CharacterSelection.Instance);
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
                    // Story 023 AC 6: another player confirmed the character I had selected; shown until I pick again.
                    var lobbySelection = CharacterSelection.Instance;
                    if (lobbySelection != null && NetworkManager.Singleton != null && lobbySelection.WasTaken(NetworkManager.Singleton.LocalClientId))
                        Label(config, config.CharacterTakenAwayText, config.MenuMessageColor);
                    Label(config, "WASD to walk.", config.UiTextColor);
                    // Only the Menu scene reaches here in a room (the Game scene draws the strip above).
                    var selection = CharacterSelection.Instance;
                    bool confirmed = false;
                    if (selection != null)
                    {
                        // Story 023 AC 5/6: confirm (Ready for a client, Confirm character for the host; one flag), greyed
                        // until a pick; once confirmed the same button cancels it.
                        ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;
                        bool picked = selection.IndexFor(me) >= 0;
                        confirmed = selection.IsReady(me);
                        string text = _room.IsHost
                            ? (confirmed ? config.LobbyCancelConfirmButtonText : config.LobbyConfirmButtonText)
                            : (confirmed ? config.LobbyNotReadyButtonText : config.LobbyReadyButtonText);
                        bool wasEnabled = GUI.enabled;
                        GUI.enabled = wasEnabled && picked;
                        if (Button(config, text) && picked) selection.RequestReady(!confirmed);
                        GUI.enabled = wasEnabled;
                    }
                    if (_room.IsHost)
                    {
                        // Start once every connected player, the host included, has confirmed (alone: the host's confirm).
                        if (selection != null && !selection.ReadyToStart())
                            Label(config, HostHint(config, selection), config.UiTextColor);
                        else if (Button(config, "Start game")) _room.StartGame();
                    }
                    else if (selection == null || confirmed)
                    {
                        Label(config, config.LobbyWaitingForHostText, config.UiTextColor);
                    }
                    if (Button(config, "Leave room")) _room.LeaveRoom();
                    break;
            }

            if (!string.IsNullOrEmpty(_room.Message)) Label(config, _room.Message, config.MenuMessageColor);
        }

        /// <summary>
        /// Story 023: draws the character cards (also used by the capture flag with sample picks). The frame is the upgrade
        /// card frame: gold = my selection, blue = anything else. Selections are shared, so a card lists everyone who has
        /// it selected; a confirmed (locked) card is dimmed with "Player N - Ready" and cannot be clicked, nor can any other
        /// card while I am confirmed. A click on an open card asks the server (<see cref="CharacterSelection.RequestChoose"/>); the server decides. Sizes and
        /// positions come from <see cref="GameConfig"/>.
        /// </summary>
        private void DrawCharacterSelect(GameConfig config, CharacterSelection selection)
        {
            var characters = config.Characters;
            if ((selection == null && !_samplePicks) || characters == null || characters.Length == 0) return;
            EnsurePictures(config);

            float s = UiSkin.Scale;
            Vector2 origin = config.CharacterSelectPosition * s;
            Vector2 size = config.CharacterSelectCardSize * s;
            float gap = config.CharacterSelectCardGap * s;
            float titleHeight = config.CharacterSelectTitleHeight * s;
            float totalWidth = characters.Length * size.x + (characters.Length - 1) * gap;
            bool repaint = Event.current.type == EventType.Repaint;
            ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;
            bool meReady = selection != null && selection.IsReady(me); // AC 6: a confirmed player cannot change character
            if (selection == null) me = 0UL;
            RebuildCardStatus(config, selection, me);

            UiSkin.Label(new Rect(origin.x, origin.y, totalWidth, titleHeight), config.CharacterSelectTitleText,
                config.CharacterSelectTitleFontSize, TextAnchor.MiddleLeft, config.UiTextColor, FontStyle.Bold);

            for (int i = 0; i < characters.Length; i++)
            {
                var rect = new Rect(origin.x + i * (size.x + gap), origin.y + titleHeight, size.x, size.y);
                // Sample (capture flag): card 0 selected by me and client 2, card 1 confirmed by client 1.
                bool mine = selection != null ? selection.IndexFor(me) == i : i == 0;
                ulong lockOwner = 1UL;
                bool locked = selection != null ? selection.TryGetLockedOwner(i, out lockOwner) : i == 1;
                bool othersHold = selection != null ? HeldByOthers(selection, i, me) : i < 2;
                bool clickable = !mine && !locked && !meReady;
                if (repaint) DrawCharacterCard(rect, i, characters[i], mine, locked && lockOwner != me, locked, othersHold, config);
                // Invisible button on top receives the click; the server decides (and enforces the same rules).
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none) && clickable && selection != null) selection.RequestChoose(i);
            }
        }

        private void DrawCharacterCard(Rect card, int index, CharacterDefinition character, bool mine, bool dim, bool locked, bool othersHold, GameConfig config)
        {
            var window = Fraction(card, config.UpgradeIconWindow);
            var previous = GUI.color;
            GUI.color = config.UpgradeIconBackColor;
            GUI.DrawTexture(window, Texture2D.whiteTexture);
            GUI.color = previous;

            if (_pictureTexture[index] != null)
            {
                float padX = window.width * config.UpgradeIconPadding, padY = window.height * config.UpgradeIconPadding;
                float side = Mathf.Min(window.width - 2f * padX, window.height - 2f * padY);
                var picture = new Rect(window.center.x - side * 0.5f, window.center.y - side * 0.5f, side, side);
                GUI.DrawTextureWithTexCoords(picture, _pictureTexture[index], _pictureUv[index]);
            }

            var frame = mine
                ? UiSkin.TrimmedWith(config.UpgradeFrameHoverTexture, config.UpgradeFrameTexture)
                : UiSkin.TrimmedWith(config.UpgradeFrameTexture, config.UpgradeFrameHoverTexture);
            GUI.DrawTexture(card, frame, ScaleMode.StretchToFill);

            if (dim)
            {
                previous = GUI.color;
                GUI.color = config.CharacterTakenDimColor;
                GUI.DrawTexture(card, Texture2D.whiteTexture);
                GUI.color = previous;
            }

            var panel = Fraction(card, config.UpgradeTextPanel);
            float nameHeight = panel.height * config.UpgradeNameShare;
            UiSkin.Label(new Rect(panel.x, panel.y, panel.width, nameHeight), character.DisplayName,
                config.CharacterSelectNameFontSize, TextAnchor.MiddleCenter, config.UpgradeNameColor, FontStyle.Bold);
            Color statusColor = locked ? config.CharacterReadyColor
                : othersHold && !mine ? config.UiTextColor
                : config.UpgradeValueColor;
            UiSkin.Label(new Rect(panel.x, panel.y + nameHeight, panel.width, panel.height - nameHeight), _cardStatus[index],
                config.CharacterSelectStatusFontSize, TextAnchor.UpperCenter, statusColor);
        }

        // A rectangle given as fractions of the card, in screen pixels.
        private static Rect Fraction(Rect card, Rect f) =>
            new Rect(card.x + f.x * card.width, card.y + f.y * card.height, f.width * card.width, f.height * card.height);

        private static bool HeldByOthers(CharacterSelection selection, int index, ulong me)
        {
            for (int i = 0; i < selection.EntryCount; i++)
            {
                var entry = selection.EntryAt(i);
                if (entry.Character == index && entry.ClientId != me) return true;
            }
            return false;
        }

        // AC 6: each card's status line ("Free", "You", "Player 1, Player 3", "3 players", "Player 2 - Ready"), rebuilt
        // only when the picks change (CharacterSelection.PicksChanged), never per frame.
        private void RebuildCardStatus(GameConfig config, CharacterSelection selection, ulong me)
        {
            int count = config.Characters.Length;
            if (!_cardStatusDirty && _cardStatus != null && _cardStatus.Length == count && _cardStatusFor == me) return;
            _cardStatusDirty = false;
            _cardStatusFor = me;
            if (_cardStatus == null || _cardStatus.Length != count) _cardStatus = new string[count];

            if (selection == null)
            {
                // Capture sample: card 0 selected by me and player 3, card 1 confirmed by player 2.
                for (int i = 0; i < count; i++) _cardStatus[i] = config.CharacterFreeLabel;
                if (count > 0) _cardStatus[0] = string.Format(config.CharacterTwoNamesFormat, config.CharacterMineLabel, TakenLabel(config, 2));
                if (count > 1) _cardStatus[1] = ReadyLabel(config, 1);
                return;
            }

            for (int index = 0; index < count; index++)
            {
                if (selection.TryGetLockedOwner(index, out ulong lockOwner))
                {
                    _cardStatus[index] = lockOwner == me ? config.CharacterMineReadyLabel : ReadyLabel(config, config.PlayerSlot(lockOwner));
                    continue;
                }
                int holders = 0;
                bool includesMe = false;
                string first = null, second = null;
                for (int i = 0; i < selection.EntryCount; i++)
                {
                    var entry = selection.EntryAt(i);
                    if (entry.Character != index) continue;
                    holders++;
                    if (entry.ClientId == me) { includesMe = true; continue; }
                    string name = TakenLabel(config, config.PlayerSlot(entry.ClientId));
                    if (first == null) first = name; else if (second == null) second = name;
                }
                if (holders == 0) _cardStatus[index] = config.CharacterFreeLabel;
                else if (holders > 2) _cardStatus[index] = string.Format(config.CharacterManyPlayersFormat, holders);
                else if (includesMe) _cardStatus[index] = holders == 1 ? config.CharacterMineLabel : string.Format(config.CharacterTwoNamesFormat, config.CharacterMineLabel, first);
                else _cardStatus[index] = holders == 1 ? first : string.Format(config.CharacterTwoNamesFormat, first, second);
            }
        }

        // "Player N" per slot, built once per format string (no string building per frame).
        private string TakenLabel(GameConfig config, int slot)
        {
            int slots = Mathf.Max(1, config.MaxPlayers);
            if (_takenLabels == null || _takenLabels.Length != slots || !ReferenceEquals(_takenFormat, config.CharacterTakenLabelFormat))
            {
                _takenFormat = config.CharacterTakenLabelFormat;
                _takenLabels = new string[slots];
                for (int i = 0; i < slots; i++) _takenLabels[i] = string.Format(_takenFormat, i + 1);
            }
            return _takenLabels[Mathf.Clamp(slot, 0, slots - 1)];
        }

        // "Player N - Ready" per slot, built once per format string.
        private string ReadyLabel(GameConfig config, int slot)
        {
            int slots = Mathf.Max(1, config.MaxPlayers);
            if (_readyLabels == null || _readyLabels.Length != slots || !ReferenceEquals(_readyFormat, config.CharacterReadyLabelFormat))
            {
                _readyFormat = config.CharacterReadyLabelFormat;
                _readyLabels = new string[slots];
                for (int i = 0; i < slots; i++) _readyLabels[i] = string.Format(_readyFormat, i + 1);
            }
            return _readyLabels[Mathf.Clamp(slot, 0, slots - 1)];
        }

        // Story 023 AC 5: the host's hint in place of Start. Rebuilt only when the ready counts change.
        private string HostHint(GameConfig config, CharacterSelection selection)
        {
            if (!selection.IsReady(NetworkManager.ServerClientId)) return config.LobbyHostChooseText;
            selection.CountReady(out int ready, out int total);
            if (ready != _readyHintReady || total != _readyHintTotal || !ReferenceEquals(_readyHintFormat, config.LobbyWaitingForPlayersFormat))
            {
                _readyHintReady = ready;
                _readyHintTotal = total;
                _readyHintFormat = config.LobbyWaitingForPlayersFormat;
                _readyHint = string.Format(_readyHintFormat, ready, total);
            }
            return _readyHint;
        }

        // The standing picture of each character: idle frame 0 of its body sheet, cut like PlayerAnimator does.
        private void EnsurePictures(GameConfig config)
        {
            var characters = config.Characters;
            if (_pictureTexture != null && _pictureTexture.Length == characters.Length)
            {
                bool alive = true;
                for (int i = 0; i < _pictureTexture.Length; i++)
                    if (characters[i].BodySheet != null && _pictureTexture[i] == null) alive = false;
                if (alive) return;
            }
            _pictureTexture = new Texture2D[characters.Length];
            _pictureUv = new Rect[characters.Length];
            for (int i = 0; i < characters.Length; i++)
            {
                var sheetTexture = characters[i].BodySheet;
                if (sheetTexture == null) continue;
                var sprite = SpriteSheet.Get(sheetTexture, config.PlayerSheetCellPixels, config.PlayerSpritePixelsPerUnit).Frame(0, 0);
                var r = sprite.textureRect;
                _pictureTexture[i] = sprite.texture;
                _pictureUv[i] = new Rect(r.x / sprite.texture.width, r.y / sprite.texture.height,
                    r.width / sprite.texture.width, r.height / sprite.texture.height);
            }
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
