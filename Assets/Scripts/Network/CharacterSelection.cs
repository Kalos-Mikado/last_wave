using System;
using LastWave.Config;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastWave.Network
{
    /// <summary>
    /// Story 023 (<c>design/game-brief.md</c> Feature 23): who has selected and who has confirmed which character,
    /// server-authoritative. One dynamically spawned NetworkObject per session. <see cref="RoomService"/> spawns it when
    /// the server starts (host is client 0), so it exists in the lobby before the Game scene loads; being dynamically
    /// spawned it moves to DontDestroyOnLoad on the scene change, so the picks are still readable in the Game scene and
    /// survive Restart (which does not reload the scene). A late joiner receives the current list with the spawn.
    /// It does not use Relay or the room code: it works with any NetworkManager session whose server is started.
    /// <para>
    /// Rules (AC 6), the same for the host and every client:
    /// a selection is shared, so any number of players may select the same character while nobody has confirmed it.
    /// Confirming (Ready for a client, Confirm character for the host; one flag) locks the character: every other
    /// player who had it selected loses their pick (<see cref="WasTaken"/>). A locked character cannot be selected or
    /// confirmed by anyone else; requests are handled in arrival order, so of two confirms of one character the first
    /// wins and the second is refused with its pick cleared. A confirmed player cannot change character until they
    /// cancel, which unlocks the character (cleared picks are not given back). A disconnect removes the client's entry.
    /// </para>
    /// </summary>
    public sealed class CharacterSelection : NetworkBehaviour
    {
        /// <summary>One player's entry: a client, the selected character and whether it is confirmed.</summary>
        public struct Pick : INetworkSerializable, IEquatable<Pick>
        {
            /// <summary>Network client id of the player.</summary>
            public ulong ClientId;

            /// <summary>Index into <see cref="GameConfig.Characters"/>; -1 when another player's confirm cleared it (AC 6).</summary>
            public int Character;

            /// <summary>Confirmed (Ready for a client, Confirm character for the host). Locks the character. Cleared by a new pick.</summary>
            public bool Ready;

            /// <inheritdoc />
            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref ClientId);
                serializer.SerializeValue(ref Character);
                serializer.SerializeValue(ref Ready);
            }

            /// <inheritdoc />
            public bool Equals(Pick other) => ClientId == other.ClientId && Character == other.Character && Ready == other.Ready;
        }

        private readonly NetworkList<Pick> _picks = new NetworkList<Pick>();

        /// <summary>The live selection of the current session; null when none is spawned (not in a session).</summary>
        public static CharacterSelection Instance { get; private set; }

        /// <summary>
        /// Fires on every machine when the picks change, and once when the selection spawns (so player objects spawned
        /// earlier, e.g. in the lobby before anyone picked, re-resolve their character). Static, so listeners need not
        /// wait for <see cref="Instance"/> to exist.
        /// </summary>
        public static event Action PicksChanged;

        /// <summary>Number of locked (confirmed) characters.</summary>
        public int LockedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _picks.Count; i++) if (_picks[i].Character >= 0 && _picks[i].Ready) count++;
                return count;
            }
        }

        /// <summary>Number of entries (one per player who has picked at least once); read with <see cref="EntryAt"/>.</summary>
        public int EntryCount => _picks.Count;

        /// <summary>The entry at <paramref name="i"/>, 0 to <see cref="EntryCount"/> - 1.</summary>
        /// <param name="i">Entry position.</param>
        public Pick EntryAt(int i) => _picks[i];

        /// <inheritdoc />
        public override void OnNetworkSpawn()
        {
            Instance = this;
            _picks.OnListChanged += HandleListChanged;
            if (IsServer)
            {
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
                NetworkManager.OnClientConnectedCallback += HandleClientConnected;
            }
            PicksChanged?.Invoke();
        }

        /// <inheritdoc />
        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            _picks.OnListChanged -= HandleListChanged;
            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            }
        }

        private static void HandleListChanged(NetworkListEvent<Pick> change)
        {
            PicksChanged?.Invoke();
        }

        // A client joining after the game started has no lobby to pick in: the server gives it the first free character.
        private void HandleClientConnected(ulong clientId)
        {
            if (SceneManager.GetActiveScene().name != GameConfig.Instance.MenuSceneName) ServerAssignMissing();
        }

        /// <summary>The character index <paramref name="clientId"/> has selected, or -1 when none.</summary>
        /// <param name="clientId">Network client id.</param>
        public int IndexFor(ulong clientId)
        {
            for (int i = 0; i < _picks.Count; i++)
            {
                if (_picks[i].ClientId == clientId) return _picks[i].Character;
            }
            return -1;
        }

        /// <summary>True when someone has character <paramref name="index"/> selected (confirmed or not).</summary>
        /// <param name="index">Index into <see cref="GameConfig.Characters"/>.</param>
        public bool IsHeld(int index)
        {
            for (int i = 0; i < _picks.Count; i++) if (_picks[i].Character == index) return true;
            return false;
        }

        /// <summary>Who confirmed (locked) character <paramref name="index"/>: true and the client id, or false when nobody has.</summary>
        /// <param name="index">Index into <see cref="GameConfig.Characters"/>.</param>
        /// <param name="clientId">The confirming player, when true is returned.</param>
        public bool TryGetLockedOwner(int index, out ulong clientId)
        {
            for (int i = 0; i < _picks.Count; i++)
            {
                if (_picks[i].Character == index && _picks[i].Ready)
                {
                    clientId = _picks[i].ClientId;
                    return true;
                }
            }
            clientId = 0;
            return false;
        }

        /// <summary>True when a player has confirmed character <paramref name="index"/>; nobody else can select it then.</summary>
        /// <param name="index">Index into <see cref="GameConfig.Characters"/>.</param>
        public bool IsLocked(int index) => TryGetLockedOwner(index, out _);

        /// <summary>True when another player's confirm cleared <paramref name="clientId"/>'s pick and they have not picked again.</summary>
        /// <param name="clientId">Network client id.</param>
        public bool WasTaken(ulong clientId)
        {
            for (int i = 0; i < _picks.Count; i++)
            {
                if (_picks[i].ClientId == clientId) return _picks[i].Character < 0;
            }
            return false;
        }

        /// <summary>True when <paramref name="clientId"/> has confirmed their character (Ready, or the host's Confirm character).</summary>
        /// <param name="clientId">Network client id.</param>
        public bool IsReady(ulong clientId)
        {
            for (int i = 0; i < _picks.Count; i++)
            {
                if (_picks[i].ClientId == clientId) return _picks[i].Ready && _picks[i].Character >= 0;
            }
            return false;
        }

        /// <summary>
        /// Connected non-host players, and how many of them are ready. Call on the server (the host); clients do not
        /// know the connected list.
        /// </summary>
        /// <param name="ready">Non-host players who confirmed.</param>
        /// <param name="total">Connected non-host players.</param>
        public void CountReady(out int ready, out int total)
        {
            ready = total = 0;
            if (NetworkManager == null || !IsServer) return;
            var ids = NetworkManager.ConnectedClientsIds; // indexed loop: called from OnGUI, no enumerator allocation
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == NetworkManager.ServerClientId) continue;
                total++;
                if (IsReady(ids[i])) ready++;
            }
        }

        /// <summary>
        /// True when the host may start: every connected player, the host included, has confirmed a character; alone
        /// (story 025 solo) the host's confirm is enough. When every character is locked (more players than characters),
        /// a player who could not pick is not waited for; they get a fallback at start. Call on the server (the host).
        /// </summary>
        public bool ReadyToStart()
        {
            if (NetworkManager == null || !IsServer) return false;
            var characters = GameConfig.Instance.Characters;
            bool allLocked = characters != null && LockedCount >= characters.Length;
            var ids = NetworkManager.ConnectedClientsIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (IsReady(ids[i])) continue;
                if (allLocked) continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// The character index to draw <paramref name="clientId"/> with: their pick, or when they have none (a late safety
        /// case, until the server's assignment arrives) a character nobody holds. Several unpicked clients are spread over
        /// those by slot (<see cref="GameConfig.PlayerSlot"/>), so they differ while enough are free. When none is free
        /// the slot formula of story 020 is used. Same answer on every machine for a given list.
        /// </summary>
        /// <param name="clientId">Network client id.</param>
        /// <param name="config">The game config.</param>
        public int ResolvedIndexFor(ulong clientId, GameConfig config)
        {
            int index = IndexFor(clientId);
            if (index >= 0) return index;
            int count = Mathf.Max(1, config.Characters.Length);
            int free = 0;
            for (int i = 0; i < count; i++) if (!IsHeld(i)) free++;
            if (free == 0) return config.PlayerSlot(clientId) % count;
            int skip = config.PlayerSlot(clientId) % free; // 0 for the first unpicked slot: the first free character
            for (int i = 0; i < count; i++)
            {
                if (IsHeld(i)) continue;
                if (skip-- == 0) return i;
            }
            return 0;
        }

        /// <summary>Asks the server to select character <paramref name="index"/> for the local player. The server may refuse (locked, out of range, already confirmed).</summary>
        /// <param name="index">Index into <see cref="GameConfig.Characters"/>.</param>
        public void RequestChoose(int index)
        {
            if (!IsSpawned) return;
            ChooseRpc(index);
        }

        /// <summary>Asks the server to confirm (true) or cancel (false) the local player's character. Refused without a valid pick, or when it is locked by someone else.</summary>
        /// <param name="ready">True to confirm, false to cancel.</param>
        public void RequestReady(bool ready)
        {
            if (!IsSpawned) return;
            SetReadyRpc(ready);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SetReadyRpc(bool ready, RpcParams rpcParams = default)
        {
            ServerSetReady(rpcParams.Receive.SenderClientId, ready);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ChooseRpc(int index, RpcParams rpcParams = default)
        {
            ServerChoose(rpcParams.Receive.SenderClientId, index);
        }

        /// <summary>
        /// Server only: selects character <paramref name="index"/> for <paramref name="clientId"/>, replacing their earlier
        /// pick. Never touches anyone else's entry (selections are shared). Returns false (and changes nothing) when the
        /// index is out of range, the requester has confirmed (they must cancel first), or the character is locked.
        /// </summary>
        /// <param name="clientId">The requesting client.</param>
        /// <param name="index">Index into <see cref="GameConfig.Characters"/>.</param>
        public bool ServerChoose(ulong clientId, int index)
        {
            if (!IsServer) return false;
            var characters = GameConfig.Instance.Characters;
            if (characters == null || index < 0 || index >= characters.Length) return false;
            if (NetworkManager.ConnectedClients != null && !NetworkManager.ConnectedClients.ContainsKey(clientId)) return false;
            if (IndexFor(clientId) == index) return true; // a repeat click on your own card changes nothing
            if (IsReady(clientId)) return false;          // a confirmed player cannot change character
            if (IsLocked(index)) return false;            // someone confirmed it
            ServerSet(clientId, index, false);
            return true;
        }

        /// <summary>
        /// Server only: confirms (<paramref name="ready"/> true) or cancels <paramref name="clientId"/>'s character, host
        /// and clients alike. Confirming needs a valid pick: it locks the character and clears it for every other player
        /// who had it selected. If someone else confirmed it first, the request is refused and the requester's pick is
        /// cleared too. Cancelling unlocks it. Returns whether the flag is now <paramref name="ready"/>.
        /// </summary>
        /// <param name="clientId">The requesting client.</param>
        /// <param name="ready">True to confirm, false to cancel.</param>
        public bool ServerSetReady(ulong clientId, bool ready)
        {
            if (!IsServer) return false;
            int index = IndexFor(clientId);
            if (index < 0) return false; // no pick, or it was cleared: choose again first
            if (!ready)
            {
                if (IsReady(clientId)) ServerSet(clientId, index, false);
                return true;
            }
            if (IsReady(clientId)) return true;
            if (IsLocked(index))
            {
                ServerSet(clientId, -1, false); // lost the race: someone confirmed it first
                return false;
            }
            ServerSet(clientId, index, true);
            for (int i = 0; i < _picks.Count; i++)
            {
                var other = _picks[i];
                if (other.ClientId != clientId && other.Character == index)
                    _picks[i] = new Pick { ClientId = other.ClientId, Character = -1, Ready = false };
            }
            return true;
        }

        // Updates the client's entry in place, or adds one.
        private void ServerSet(ulong clientId, int index, bool ready)
        {
            for (int i = 0; i < _picks.Count; i++)
            {
                if (_picks[i].ClientId == clientId)
                {
                    _picks[i] = new Pick { ClientId = clientId, Character = index, Ready = ready };
                    return;
                }
            }
            _picks.Add(new Pick { ClientId = clientId, Character = index, Ready = ready });
        }

        /// <summary>
        /// Server only: gives every connected client without a pick the first character nobody holds (lowest index),
        /// already confirmed so it is locked. A client is left without one only when there are more clients than characters.
        /// </summary>
        public void ServerAssignMissing()
        {
            if (!IsServer) return;
            var characters = GameConfig.Instance.Characters;
            int count = characters == null ? 0 : characters.Length;
            var ids = NetworkManager.ConnectedClientsIds;
            for (int c = 0; c < ids.Count; c++)
            {
                ulong id = ids[c];
                if (IndexFor(id) >= 0) continue;
                for (int index = 0; index < count; index++)
                {
                    if (IsHeld(index)) continue;
                    ServerSet(id, index, true);
                    break;
                }
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            for (int i = _picks.Count - 1; i >= 0; i--)
            {
                if (_picks[i].ClientId == clientId) _picks.RemoveAt(i);
            }
        }
    }
}
