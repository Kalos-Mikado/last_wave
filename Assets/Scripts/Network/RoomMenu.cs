using LastWave.Config;
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

        private void Awake()
        {
            _room = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.GetComponent<RoomService>()
                : Instantiate(_networkManagerPrefab);
        }

        private void OnGUI()
        {
            var config = GameConfig.Instance;
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
                    bool inGame = SceneManager.GetActiveScene().name == config.GameSceneName;
                    if (!inGame)
                    {
                        if (_room.IsHost)
                        {
                            if (GUILayout.Button("Start game")) _room.StartGame();
                        }
                        else
                        {
                            GUILayout.Label("Waiting for the host to start...");
                        }
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
    }
}
