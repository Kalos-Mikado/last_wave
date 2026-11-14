using LastWave.Config;
using LastWave.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastWave.Network
{
    /// <summary>
    /// Placeholder IMGUI room menu: create room, join by code, lobby info, host-only Start,
    /// Leave, and the last error. Placed in both the Menu and Game scenes; the UI story
    /// replaces it. Creates the NetworkManager from its prefab when none exists yet
    /// (NetworkManager keeps itself alive across scenes, so it is created only once).
    /// </summary>
    /// <example>
    /// Add to an empty GameObject and assign <c>NetworkManager.prefab</c> to the
    /// Network Manager Prefab field (done by <c>LastWaveSetup.Run</c>).
    /// </example>
    public sealed class RoomMenu : MonoBehaviour
    {
        [SerializeField] private RoomService _networkManagerPrefab;

        private RoomService _room;
        private string _codeInput = string.Empty;
        private GUIStyle _stripStyle;

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

            GUILayout.BeginArea(new Rect(20, 20, 380, 420), GUI.skin.box);
            GUILayout.Label("Last Wave");

            switch (_room.State)
            {
                case RoomState.Menu:
                    if (GUILayout.Button("Create room")) _room.CreateRoom();
                    GUILayout.Space(10);
                    GUILayout.Label("Room code:");
                    _codeInput = GUILayout.TextField(_codeInput, 16);
                    if (GUILayout.Button("Join room")) _room.JoinRoom(_codeInput);
                    break;

                case RoomState.Busy:
                    GUILayout.Label("Please wait...");
                    break;

                case RoomState.InRoom:
                    GUILayout.Label("Room code (give it to your friends):");
                    GUILayout.TextField(_room.JoinCode ?? string.Empty); // a text field so the code can be copied
                    GUILayout.Label($"Players: {_room.PlayerCount}/{config.MaxPlayers}");
                    GUILayout.Label("WASD to walk.");
                    // Only the Menu scene reaches here in a room (the Game scene draws the strip above).
                    if (_room.IsHost)
                    {
                        if (GUILayout.Button("Start game")) _room.StartGame();
                    }
                    else
                    {
                        GUILayout.Label("Waiting for the host to start...");
                    }
                    if (GUILayout.Button("Leave room")) _room.LeaveRoom();
                    break;
            }

            if (!string.IsNullOrEmpty(_room.Message))
            {
                GUILayout.Space(10);
                var previous = GUI.color;
                GUI.color = new Color(1f, 0.6f, 0.6f);
                GUILayout.Label(_room.Message);
                GUI.color = previous;
            }
            GUILayout.EndArea();
        }

        // The Game scene's room strip: room code and player count only, in a corner. Its rectangle is registered
        // so a click on it never fires a shot (UiPointerBlock). While leaving it shows the service's message instead.
        private void DrawStrip(GameConfig config)
        {
            var rect = new Rect(config.RoomStripPosition, config.RoomStripSize);
            if (Event.current.type == EventType.Repaint) UiPointerBlock.Register(rect);

            _stripStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleLeft };
            _stripStyle.fontSize = config.RoomStripFontSize;

            string text = _room.State == RoomState.InRoom
                ? $"{config.RoomStripCodeLabel}: {_room.JoinCode}   {config.RoomStripPlayersLabel}: {_room.PlayerCount}/{config.MaxPlayers}"
                : _room.Message;
            GUI.Box(rect, text, _stripStyle);
        }
    }
}
