using System;
using System.Threading.Tasks;
using LastWave.Config;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastWave.Network
{
    /// <summary>Where the local player is in the room flow.</summary>
    public enum RoomState
    {
        /// <summary>Not in a room; create or join is possible.</summary>
        Menu,
        /// <summary>A service call is in flight; buttons are hidden.</summary>
        Busy,
        /// <summary>In a room (lobby or game scene).</summary>
        InRoom,
    }

    /// <summary>
    /// Owns the Multiplayer Services session (Relay + join code) for the local player.
    /// Lives on the NetworkManager prefab, so it survives scene loads with it.
    /// The SDK's default Netcode handler starts and stops the NetworkManager; this class
    /// never calls <c>NetworkManager.StartHost</c> or <c>Shutdown</c> itself.
    /// Not thread-safe: call from the Unity main thread only.
    /// </summary>
    /// <example>
    /// <code>
    /// var room = NetworkManager.Singleton.GetComponent&lt;RoomService&gt;();
    /// room.CreateRoom();          // host: show room.JoinCode to friends
    /// room.JoinRoom("ABC123");    // friend
    /// if (room.IsHost) room.StartGame();
    /// room.LeaveRoom();
    /// </code>
    /// </example>
    [RequireComponent(typeof(NetworkManager))]
    public sealed class RoomService : MonoBehaviour
    {
        private const string LinkHint =
            "Likely cause: link the project to a Unity project in Project Settings > Services.";

        [SerializeField] private CharacterSelection _characterSelectionPrefab; // story 023: spawned by the server at start

        private NetworkManager _networkManager;
        private ISession _session;
        private bool _leaving;

        /// <summary>Current step of the room flow.</summary>
        public RoomState State { get; private set; } = RoomState.Menu;

        /// <summary>Last status or error text to show the player; empty when none.</summary>
        public string Message { get; private set; } = string.Empty;

        /// <summary>True when the local player created the current room.</summary>
        public bool IsHost => _session != null && _session.IsHost;

        /// <summary>The code friends type to join; null when not in a room.</summary>
        public string JoinCode => _session?.Code;

        /// <summary>Players currently in the room, host included; 0 when not in a room.</summary>
        public int PlayerCount => _session?.PlayerCount ?? 0;

        private void Awake()
        {
            _networkManager = GetComponent<NetworkManager>();
            _networkManager.OnClientStopped += HandleClientStopped;
            _networkManager.OnServerStarted += HandleServerStarted;
        }

        private void OnDestroy()
        {
            _networkManager.OnClientStopped -= HandleClientStopped;
            _networkManager.OnServerStarted -= HandleServerStarted;
        }

        // Story 023: the character selection is spawned as soon as the server (host) starts, so it exists in the lobby.
        // Dynamically spawned objects move to DontDestroyOnLoad on the scene change, so it survives into the Game scene.
        private void HandleServerStarted()
        {
            if (_characterSelectionPrefab == null || CharacterSelection.Instance != null) return;
            var selection = Instantiate(_characterSelectionPrefab);
            selection.GetComponent<NetworkObject>().Spawn(false);
        }

        /// <summary>
        /// Creates a private Relay room and makes this machine the host (listen server).
        /// On success <see cref="State"/> becomes <see cref="RoomState.InRoom"/> and
        /// <see cref="JoinCode"/> is set; on failure <see cref="Message"/> explains why.
        /// </summary>
        public async void CreateRoom()
        {
            if (!await SignInAsync()) return;
            try
            {
                var options = new SessionOptions
                {
                    MaxPlayers = GameConfig.Instance.MaxPlayers,
                    IsPrivate = true, // join by code only, never listed (no matchmaking)
                }.WithRelayNetwork();
                _session = await MultiplayerService.Instance.CreateSessionAsync(options);
                SetState(RoomState.InRoom, string.Empty);
            }
            catch (Exception e)
            {
                SetState(RoomState.Menu, $"Could not create the room: {e.Message}");
            }
        }

        /// <summary>
        /// Joins the room with the given code as a client. A full room, a wrong code or a
        /// service failure leaves <see cref="State"/> at <see cref="RoomState.Menu"/> with
        /// the reason in <see cref="Message"/>.
        /// </summary>
        /// <param name="code">The join code shown on the host's screen.</param>
        public async void JoinRoom(string code)
        {
            code = code?.Trim();
            if (string.IsNullOrEmpty(code))
            {
                SetState(RoomState.Menu, "Enter the room code first.");
                return;
            }
            if (!await SignInAsync()) return;
            try
            {
                _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                SetState(RoomState.InRoom, string.Empty);
            }
            catch (SessionException e) when (e.Error == SessionError.SessionNotFound)
            {
                SetState(RoomState.Menu, "No room with that code.");
            }
            // UNVERIFIED: the SDK maps Lobby's LobbyFull to SessionError.Unknown and keeps only the
            // message text (LobbyConverter.ToSessionException), so "full" in the message is the only signal.
            catch (Exception e) when (e.Message.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SetState(RoomState.Menu, $"The room is full ({GameConfig.Instance.MaxPlayers} players max).");
            }
            catch (Exception e)
            {
                SetState(RoomState.Menu, $"Could not join the room: {e.Message}");
            }
        }

        /// <summary>
        /// Host only: loads the game scene for everyone through the Netcode scene manager.
        /// Does nothing on a client.
        /// </summary>
        public void StartGame()
        {
            if (!IsHost) return;
            // Story 023: anyone who has not chosen (the gated Start should prevent it) gets the first free character.
            if (CharacterSelection.Instance != null) CharacterSelection.Instance.ServerAssignMissing();
            _networkManager.SceneManager.LoadScene(GameConfig.Instance.GameSceneName, LoadSceneMode.Single);
        }

        /// <summary>
        /// Leaves the room and returns to the menu scene. When the host leaves, the room is
        /// deleted and every client is sent back to the menu with a message.
        /// </summary>
        public async void LeaveRoom()
        {
            if (_session == null || _leaving) return; // not in a room, or already leaving (a double click on a button)
            var session = _session;
            _leaving = true;
            SetState(RoomState.Busy, "Leaving...");
            try
            {
                if (session.IsHost) await session.AsHost().DeleteAsync();
                else await session.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Leaving the room failed, returning to the menu anyway: {e.Message}");
            }
            _session = null;
            _leaving = false;
            ReturnToMenu(string.Empty);
        }

        private async Task<bool> SignInAsync()
        {
            SetState(RoomState.Busy, "Connecting...");
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    // A fresh profile per launch gives each running copy its own anonymous player,
                    // so two copies on one PC can join each other while testing.
                    var profile = "p" + Guid.NewGuid().ToString("N").Substring(0, 16);
                    await UnityServices.InitializeAsync(new InitializationOptions().SetProfile(profile));
                }
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }
                return true;
            }
            catch (Exception e)
            {
                SetState(RoomState.Menu, $"Unity Services sign-in failed: {e.Message}\n{LinkHint}");
                return false;
            }
        }

        // The network stopped without LeaveRoom: on a client the host left (or the link dropped).
        // UNVERIFIED: that a host quitting fires OnClientStopped on every client through Relay,
        // and that LeaveAsync after it only throws (it is caught) rather than hanging.
        private async void HandleClientStopped(bool wasHost)
        {
            if (_leaving || _session == null) return;
            var session = _session;
            _session = null;
            ReturnToMenu(wasHost
                ? "The connection to the room was lost."
                : "The host left the room, so the game ended.");
            try
            {
                await session.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Session cleanup after the host left failed: {e.Message}");
            }
        }

        private void ReturnToMenu(string message)
        {
            SetState(RoomState.Menu, message);
            var menu = GameConfig.Instance.MenuSceneName;
            if (SceneManager.GetActiveScene().name != menu)
            {
                SceneManager.LoadScene(menu);
            }
        }

        private void SetState(RoomState state, string message)
        {
            State = state;
            Message = message;
        }
    }
}
