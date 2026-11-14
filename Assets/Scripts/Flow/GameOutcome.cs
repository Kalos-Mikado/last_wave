using System;
using LastWave.Base;
using LastWave.Config;
using LastWave.Enemies;
using LastWave.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace LastWave.Flow
{
    /// <summary>How the game ended. <see cref="None"/> while it is still being played.</summary>
    public enum GameResult : byte
    {
        /// <summary>Not over.</summary>
        None = 0,
        /// <summary>The boss of the last wave died.</summary>
        Win = 1,
        /// <summary>The base reached 0 health.</summary>
        LoseBase = 2,
        /// <summary>Every player is dead.</summary>
        LoseEveryoneDown = 3,
    }

    /// <summary>
    /// Plain end-of-game rules with no Unity scene or network dependency, so they can be unit tested later.
    /// </summary>
    public static class OutcomeRules
    {
        /// <summary>Command-line argument that forces an outcome on the host (debug), followed by <c>win</c>, <c>base</c> or <c>everyone</c>.</summary>
        public const string ForceArgument = "--force-outcome";

        /// <summary>
        /// First result wins: once a result is recorded, a later one is ignored. An incoming
        /// <see cref="GameResult.None"/> never changes anything.
        /// </summary>
        /// <param name="current">The result recorded so far.</param>
        /// <param name="incoming">The result that has just been reported.</param>
        /// <returns>The result that stands.</returns>
        public static GameResult Decide(GameResult current, GameResult incoming)
        {
            return current != GameResult.None ? current : incoming;
        }

        /// <summary>Maps the value after <see cref="ForceArgument"/> to a result; <see cref="GameResult.None"/> when unknown or empty.</summary>
        public static GameResult ParseForced(string value)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "win": return GameResult.Win;
                case "base": return GameResult.LoseBase;
                case "everyone": return GameResult.LoseEveryoneDown;
                default: return GameResult.None;
            }
        }
    }

    /// <summary>
    /// The one authority on how the game ends (brief MVP feature 7). Scene object in the Game scene, not a
    /// NetworkObject (same pattern as BaseHealth and WaveManager). ONLY the server listens to the win and lose
    /// events (<see cref="WaveManager.OnAllWavesCleared"/>, <see cref="BaseHealth.OnBaseDestroyed"/>,
    /// <see cref="TeamDefeat.OnAllPlayersDead"/>); the first one that arrives decides
    /// (<see cref="OutcomeRules.Decide"/>), the second is ignored. The server then stops the wave runner,
    /// despawns every enemy and tells every machine through a Netcode named message; clients never decide on
    /// their own. A client asks for the current result in <c>Start</c>, so a late joiner is caught up.
    /// <para>
    /// While the game is over (<see cref="IsOver"/>): no more spawns or enemy attacks, no respawn, revive, repair
    /// or shooting effects, and downed countdowns are frozen. Movement is NOT frozen (PlayerMovement is outside
    /// this story's files): players can still walk around behind the end screen.
    /// </para>
    /// <para>
    /// Restart (<see cref="ServerRestart"/>, host only) resets in place WITHOUT reloading the scene: waves back to
    /// before wave 1 with the first-wave delay, base at full health, all enemies gone, every player standing at
    /// full health at the start position. Everyone stays in the same room and the same scene.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// GameOutcome.Current.OnOutcomeChanged += result => Debug.Log(result);   // every machine
    /// if (GameOutcome.IsOver) return;                                        // gameplay code asks this
    /// </code>
    /// Debug: start the game with <c>--force-outcome win|base|everyone</c> to end it on the host after a moment.
    /// </example>
    public sealed class GameOutcome : MonoBehaviour
    {
        private const string OutcomeMessage = "LastWave.GameOutcome";
        private const string OutcomeRequestMessage = "LastWave.GameOutcomeRequest";

        private GameConfig _config;
        private NetworkManager _network;
        private WaveManager _waves;
        private BaseHealth _base;
        private TeamDefeat _defeat;
        private GameResult _result;   // every machine; on the server it is set at the moment of the decision
        private bool _online;         // a NetworkManager is listening

        /// <summary>The game outcome object of the Game scene on this machine; null in other scenes.</summary>
        public static GameOutcome Current { get; private set; }

        /// <summary>True once the game has ended and until a restart. False when there is no outcome object (Menu scene).</summary>
        public static bool IsOver => Current != null && Current._result != GameResult.None;

        /// <summary>How the game ended; <see cref="GameResult.None"/> while it is being played.</summary>
        public GameResult Result => _result;

        /// <summary>Raised on every machine when <see cref="Result"/> changes (new result; None after a restart).</summary>
        public event Action<GameResult> OnOutcomeChanged;

        private void Start()
        {
            Current = this;
            _config = GameConfig.Instance;
            _waves = FindFirstObjectByType<WaveManager>();
            _base = FindFirstObjectByType<BaseHealth>();
            _defeat = FindFirstObjectByType<TeamDefeat>();

            _network = NetworkManager.Singleton;
            _online = _network != null && _network.IsListening;

            if (_online)
            {
                _network.CustomMessagingManager.RegisterNamedMessageHandler(OutcomeMessage, HandleOutcome);
                if (_network.IsServer)
                {
                    _network.CustomMessagingManager.RegisterNamedMessageHandler(OutcomeRequestMessage, HandleOutcomeRequest);
                    if (_waves != null) _waves.OnAllWavesCleared += HandleWin;
                    if (_base != null) _base.OnBaseDestroyed += HandleBaseDestroyed;
                    if (_defeat != null) _defeat.OnAllPlayersDead += HandleEveryoneDown;
                }
                else
                {
                    // Ask for the current result: this client may have joined after the game ended.
                    using var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
                    writer.WriteValueSafe(0);
                    _network.CustomMessagingManager.SendNamedMessage(OutcomeRequestMessage, NetworkManager.ServerClientId, writer);
                }
            }

            // DEBUG: inert unless --force-outcome is on the command line. Offline (no room) it only shows the screen.
            var forced = OutcomeRules.ParseForced(ReadArgument(OutcomeRules.ForceArgument));
            if (forced != GameResult.None && (!_online || _network.IsServer))
            {
                StartCoroutine(ForceAfterDelay(forced));
            }
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_waves != null) _waves.OnAllWavesCleared -= HandleWin;
            if (_base != null) _base.OnBaseDestroyed -= HandleBaseDestroyed;
            if (_defeat != null) _defeat.OnAllPlayersDead -= HandleEveryoneDown;

            // During shutdown the messaging manager may already be gone.
            var messaging = _network != null ? _network.CustomMessagingManager : null;
            if (messaging == null) return;
            messaging.UnregisterNamedMessageHandler(OutcomeMessage);
            if (_network.IsServer) messaging.UnregisterNamedMessageHandler(OutcomeRequestMessage);
        }

        /// <summary>
        /// Host only: starts a new game in the same room, in the same scene. Does nothing unless the game is over.
        /// Offline (debug preview, no room) it only clears the screen. Ignored with a warning on a client.
        /// </summary>
        public void ServerRestart()
        {
            if (_result == GameResult.None) return;
            if (!_online)
            {
                SetResult(GameResult.None);
                return;
            }
            if (!_network.IsServer)
            {
                Debug.LogWarning("GameOutcome.ServerRestart is server-only; call ignored.");
                return;
            }

            // The result is cleared FIRST: the base, wave and player resets below are refused while the game is over.
            SetResult(GameResult.None);
            if (_waves != null) _waves.ServerReset();
            if (_base != null) _base.ServerReset();
            foreach (var life in PlayerLife.All) life.ServerResetForNewGame();
            Broadcast(GameResult.None);
        }

        private void HandleWin() => ServerDecide(GameResult.Win);

        private void HandleBaseDestroyed() => ServerDecide(GameResult.LoseBase);

        private void HandleEveryoneDown() => ServerDecide(GameResult.LoseEveryoneDown);

        // Server: the single place that decides. The first result wins; later ones return here and do nothing.
        private void ServerDecide(GameResult incoming)
        {
            var decided = OutcomeRules.Decide(_result, incoming);
            if (decided == _result) return;

            SetResult(decided); // IsOver is true from here on
            if (_waves != null) _waves.ServerStopAndClear();
            Broadcast(decided);
        }

        private void Broadcast(GameResult result)
        {
            using var writer = CreateWriter(result);
            _network.CustomMessagingManager.SendNamedMessageToAll(OutcomeMessage, writer); // host: also loops back, then changes nothing
        }

        private void HandleOutcomeRequest(ulong senderClientId, FastBufferReader payload)
        {
            using var writer = CreateWriter(_result);
            _network.CustomMessagingManager.SendNamedMessage(OutcomeMessage, senderClientId, writer);
        }

        private static FastBufferWriter CreateWriter(GameResult result)
        {
            var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
            writer.WriteValueSafe((int)result);
            return writer;
        }

        // Every machine. The host's own broadcast arrives here too, finds the result already set and stops.
        private void HandleOutcome(ulong senderClientId, FastBufferReader payload)
        {
            payload.ReadValueSafe(out int value);
            bool known = value >= 0 && value <= byte.MaxValue && Enum.IsDefined(typeof(GameResult), (byte)value);
            SetResult(known ? (GameResult)value : GameResult.None);
        }

        private void SetResult(GameResult result)
        {
            if (_result == result) return;
            _result = result;
            // Everyone re-arms the "everyone is dead" check on a restart (its flag would block the next game).
            if (result == GameResult.None && _defeat != null) _defeat.ResetLocal();
            OnOutcomeChanged?.Invoke(result);
        }

        private System.Collections.IEnumerator ForceAfterDelay(GameResult forced)
        {
            yield return new WaitForSecondsRealtime(_config.DebugForceOutcomeDelaySeconds);
            if (_online) ServerDecide(forced);
            else SetResult(forced);
        }

        private static string ReadArgument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}
