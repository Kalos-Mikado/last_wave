using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace LastWave.Player
{
    /// <summary>
    /// The "everyone is dead" lose condition (brief, Goal and fail state). Scene object in the Game scene, not
    /// a NetworkObject (same pattern as BaseHealth and WaveManager): the server watches every player's
    /// <see cref="PlayerLife"/>; when at least one player is connected and ALL of them are Dead at the same
    /// time it sends one Netcode named message, and <see cref="OnAllPlayersDead"/> is raised on every machine
    /// (the host through loopback). Downed players do not count as dead: if everybody left is downed or
    /// dead with nobody standing, the downed ones time out to Dead and the team loses then (documented behaviour).
    /// Nothing else happens here on defeat: the lose screen is drawn by <c>GameOutcome</c> and <c>EndScreen</c> (story 007).
    /// </summary>
    /// <example>
    /// <code>
    /// var defeat = FindFirstObjectByType&lt;TeamDefeat&gt;();
    /// defeat.OnAllPlayersDead += () => Debug.Log("lose: everyone is dead");   // story 007 listens here
    /// </code>
    /// </example>
    public sealed class TeamDefeat : MonoBehaviour
    {
        private const string DefeatMessage = "LastWave.AllPlayersDead";

        private NetworkManager _network;
        private bool _sent;       // server: the message went out
        private bool _defeated;   // every machine: the event was raised

        /// <summary>True once the team has lost (every machine).</summary>
        public bool IsDefeated => _defeated;

        /// <summary>
        /// Raised once on every machine when every connected player is dead: on the server and host when the
        /// message loops back, on a client when it arrives. <c>GameOutcome</c> (story 007, server) listens.
        /// A client that joins after the defeat is not told by this class; <c>GameOutcome</c> catches it up.
        /// </summary>
        public event Action OnAllPlayersDead;

        /// <summary>
        /// Re-arms the check for a new game (story 007). <c>GameOutcome</c> calls it on every machine when a
        /// restart happens; without it the one-shot flags would block the next defeat.
        /// </summary>
        public void ResetLocal()
        {
            _sent = false;
            _defeated = false;
        }

        private void Start()
        {
            _network = NetworkManager.Singleton;
            if (_network == null || !_network.IsListening)
            {
                Debug.LogWarning("TeamDefeat: no running NetworkManager (game scene opened without a room); no lose condition.");
                return;
            }
            // The host receives its own broadcast through loopback (same assumption as BaseHealth), so it registers too.
            _network.CustomMessagingManager.RegisterNamedMessageHandler(DefeatMessage, HandleDefeat);
        }

        private void OnDestroy()
        {
            // During shutdown the messaging manager may already be gone.
            var messaging = _network != null ? _network.CustomMessagingManager : null;
            if (messaging == null) return;
            messaging.UnregisterNamedMessageHandler(DefeatMessage);
        }

        private void Update()
        {
            if (_sent || _network == null || !_network.IsListening || !_network.IsServer) return;

            int dead = 0;
            foreach (var life in PlayerLife.All)
            {
                if (life.IsDead) dead++;
            }
            if (!PlayerLifeRules.AllDead(PlayerLife.All.Count, dead)) return;

            _sent = true;
            using var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
            writer.WriteValueSafe(1);
            _network.CustomMessagingManager.SendNamedMessageToAll(DefeatMessage, writer);
        }

        private void HandleDefeat(ulong senderClientId, FastBufferReader payload)
        {
            if (_defeated) return;
            _defeated = true;
            OnAllPlayersDead?.Invoke();
        }
    }
}
