using LastWave.Config;
using LastWave.Enemies;
using LastWave.Flow;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastWave.Player
{
    /// <summary>
    /// Plain upgrade arithmetic with no Unity scene or network dependency, so it can be unit tested later.
    /// Levels are packed one byte per <see cref="UpgradeKind"/> into a <c>ulong</c>.
    /// Design record: <c>design/game-brief.md</c> (Feature 12), story 012.
    /// </summary>
    public static class UpgradeRules
    {
        /// <summary>Most upgrade kinds that fit in the packed levels (one byte each).</summary>
        public const int MaxKinds = 8;

        /// <summary>Most cards one offer can hold (one byte each in the packed offer).</summary>
        public const int MaxChoices = 8;

        /// <summary>The level of <paramref name="kind"/> in a packed value (0 for a kind that does not fit).</summary>
        public static int GetLevel(ulong packed, UpgradeKind kind)
        {
            int index = (int)kind;
            if (index < 0 || index >= MaxKinds) return 0;
            return (int)((packed >> (8 * index)) & 0xFFUL);
        }

        /// <summary>The packed value with <paramref name="kind"/> raised by one level (a level never goes past 255).</summary>
        public static ulong WithLevelAdded(ulong packed, UpgradeKind kind)
        {
            int index = (int)kind;
            if (index < 0 || index >= MaxKinds) return packed;
            int level = GetLevel(packed, kind);
            if (level >= 255) return packed;
            ulong cleared = packed & ~(0xFFUL << (8 * index));
            return cleared | ((ulong)(level + 1) << (8 * index));
        }

        /// <summary>The first definition of <paramref name="kind"/>, or null when the config has none.</summary>
        public static UpgradeDefinition Find(UpgradeDefinition[] definitions, UpgradeKind kind)
        {
            if (definitions == null) return null;
            for (int i = 0; i < definitions.Length; i++)
            {
                if (definitions[i] != null && definitions[i].Kind == kind) return definitions[i];
            }
            return null;
        }

        /// <summary>Total gain at a level: value per level times level (0 when the config has no such upgrade).</summary>
        public static float Bonus(UpgradeDefinition[] definitions, UpgradeKind kind, int level)
        {
            var definition = Find(definitions, kind);
            return definition == null ? 0f : definition.Value * level;
        }

        /// <summary>Multiplier at a level: 1 plus the bonus, never below 0.01 (it is divided by for fire rate).</summary>
        public static float Multiplier(UpgradeDefinition[] definitions, UpgradeKind kind, int level)
        {
            return Mathf.Max(0.01f, 1f + Bonus(definitions, kind, level));
        }

        /// <summary>
        /// Picks up to <paramref name="wanted"/> DISTINCT kinds at random from the definitions into
        /// <paramref name="result"/> (a kind listed twice counts once; kinds that do not fit the packing are skipped).
        /// </summary>
        /// <param name="definitions">The configured upgrades.</param>
        /// <param name="wanted">Cards wanted; clamped to <see cref="MaxChoices"/> and to what is available.</param>
        /// <param name="result">Receives the kinds; at least <see cref="MaxChoices"/> long.</param>
        /// <param name="scratch">Working space, at least <see cref="MaxKinds"/> long (passed in so a deal allocates nothing).</param>
        /// <returns>How many kinds were written to <paramref name="result"/>.</returns>
        public static int Deal(UpgradeDefinition[] definitions, int wanted, byte[] result, byte[] scratch)
        {
            if (definitions == null) return 0;
            int available = 0;
            for (int i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                if (definition == null || (int)definition.Kind >= MaxKinds) continue;
                bool seen = false;
                for (int j = 0; j < available; j++)
                {
                    if (scratch[j] == (byte)definition.Kind) { seen = true; break; }
                }
                if (!seen) scratch[available++] = (byte)definition.Kind;
            }

            int count = Mathf.Clamp(wanted, 0, Mathf.Min(MaxChoices, available));
            for (int i = 0; i < count; i++)
            {
                int pick = Random.Range(i, available); // partial Fisher-Yates: scratch[0..i) are the cards dealt so far
                (scratch[i], scratch[pick]) = (scratch[pick], scratch[i]);
                result[i] = scratch[i];
            }
            return count;
        }

        /// <summary>Packs the first <paramref name="count"/> kinds of an offer, one byte each.</summary>
        public static ulong PackOffer(byte[] offer, int count)
        {
            ulong packed = 0;
            for (int i = 0; i < count && i < MaxChoices; i++) packed |= (ulong)offer[i] << (8 * i);
            return packed;
        }

        /// <summary>Reads the kind at <paramref name="index"/> from a packed offer.</summary>
        public static byte UnpackOffer(ulong packed, int index)
        {
            return (byte)((packed >> (8 * index)) & 0xFFUL);
        }
    }

    /// <summary>
    /// A player's upgrades (brief MVP feature 12). The SERVER owns everything: each wave rest it deals the
    /// player <see cref="GameConfig.UpgradeChoicesPerRest"/> distinct random cards, tells only the owner
    /// (<see cref="ShowOfferRpc"/>), checks the owner's pick (<see cref="PickRpc"/>: sender owns the player, an offer
    /// is open, index in range, game not over) and writes the new level into a server-written NetworkVariable that
    /// every machine reads. The offer closes on a pick, when the next wave starts, when the game ends or on restart;
    /// no pick means no upgrade. Levels stack, last until the game ends and are cleared by
    /// <see cref="ServerResetUpgrades"/> (Restart). Other scripts read the computed values below, which equal the
    /// plain config values at level 0.
    /// </summary>
    /// <example>
    /// <code>
    /// float interval = player.GetComponent&lt;PlayerUpgrades&gt;().FireInterval;   // config interval / fire rate multiplier
    /// </code>
    /// </example>
    public sealed class PlayerUpgrades : NetworkBehaviour
    {
        private readonly NetworkVariable<ulong> _levels = new NetworkVariable<ulong>(
            0UL, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly byte[] _serverOffer = new byte[UpgradeRules.MaxChoices];
        private readonly byte[] _scratch = new byte[UpgradeRules.MaxKinds];
        private readonly byte[] _localOffer = new byte[UpgradeRules.MaxChoices];

        private GameConfig _config;
        private PlayerHealth _health;
        private WaveManager _waves;       // server: the one whose events we listen to
        private int _serverOfferCount;    // server: cards in the open offer; 0 = none open
        private int _localOfferCount;     // owner: cards in the offer being shown; 0 = none

        /// <summary>The local player's upgrades on this machine; null before it spawns.</summary>
        public static PlayerUpgrades Local { get; private set; }

        /// <summary>True on the owner while an offer is open (cards should be shown).</summary>
        public bool HasOpenOffer => _localOfferCount > 0;

        /// <summary>Cards in the open offer on the owner; 0 when none.</summary>
        public int OfferCount => _localOfferCount;

        /// <summary>Changes every time the owner's offer opens or closes, so UI can rebuild cached text only then.</summary>
        public int OfferVersion { get; private set; }

        /// <summary>Kind of card <paramref name="index"/> in the owner's open offer.</summary>
        /// <param name="index">0 up to <see cref="OfferCount"/> minus 1.</param>
        public UpgradeKind GetOfferKind(int index)
        {
            return (UpgradeKind)_localOffer[Mathf.Clamp(index, 0, UpgradeRules.MaxChoices - 1)];
        }

        /// <summary>This player's level of an upgrade. Same value on every machine.</summary>
        /// <param name="kind">The upgrade.</param>
        public int GetLevel(UpgradeKind kind) => UpgradeRules.GetLevel(_levels.Value, kind);

        /// <summary>Bullet damage: config damage times the damage multiplier, rounded (at least 1 when the config damage is above 0).</summary>
        public int BulletDamage
        {
            get
            {
                int baseDamage = Config.BulletDamage;
                if (baseDamage <= 0) return baseDamage;
                return Mathf.Max(1, Mathf.RoundToInt(baseDamage * Multiplier(UpgradeKind.Damage)));
            }
        }

        /// <summary>Seconds between two shots: the config interval divided by the fire rate multiplier.</summary>
        public float FireInterval => Config.FireInterval / Multiplier(UpgradeKind.FireRate);

        /// <summary>Walking speed in world units per second: config speed times the move speed multiplier.</summary>
        public float MoveSpeed => Config.MoveSpeed * Multiplier(UpgradeKind.MoveSpeed);

        /// <summary>Max health in whole points: config max health plus the flat bonus.</summary>
        public int MaxHealth => Config.MaxPlayerHealth + MaxHealthBonus;

        /// <summary>Base repair per second this player contributes: config rate times the repair speed multiplier.</summary>
        public float RepairPerSecond => Config.RepairPerSecond * Multiplier(UpgradeKind.RepairSpeed);

        /// <summary>How much faster than normal this player revives a friend (1 = normal). Applied to the time they hold the key.</summary>
        public float ReviveSpeedMultiplier => Multiplier(UpgradeKind.ReviveSpeed);

        private GameConfig Config => _config != null ? _config : (_config = GameConfig.Instance);

        private int MaxHealthBonus =>
            Mathf.RoundToInt(UpgradeRules.Bonus(Config.Upgrades, UpgradeKind.MaxHealth, GetLevel(UpgradeKind.MaxHealth)));

        private float Multiplier(UpgradeKind kind) =>
            UpgradeRules.Multiplier(Config.Upgrades, kind, GetLevel(kind));

        /// <summary>Caches components and registers the local player.</summary>
        public override void OnNetworkSpawn()
        {
            _config = GameConfig.Instance;
            _health = GetComponent<PlayerHealth>();
            _serverOfferCount = 0;
            _localOfferCount = 0;
            if (IsOwner) Local = this;
        }

        /// <summary>Unsubscribes from the wave manager and unregisters the local player.</summary>
        public override void OnNetworkDespawn()
        {
            DetachWaves();
            if (Local == this) Local = null;
            _serverOfferCount = 0;
            _localOfferCount = 0;
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer) return;

            // The wave manager only exists in the Game scene; players carry over from the Menu scene.
            if (_waves == null && SceneManager.GetActiveScene().name == Config.GameSceneName)
            {
                _waves = FindFirstObjectByType<WaveManager>();
                if (_waves != null)
                {
                    _waves.OnWaveCleared += HandleWaveCleared;
                    _waves.OnWaveStarted += HandleWaveStarted;
                }
            }

            // The game ended during a rest: the offer closes.
            if (_serverOfferCount > 0 && GameOutcome.IsOver) ServerCloseOffer();
        }

        private void DetachWaves()
        {
            if (_waves == null) return;
            _waves.OnWaveCleared -= HandleWaveCleared;
            _waves.OnWaveStarted -= HandleWaveStarted;
            _waves = null;
        }

        // Server: a rest starts, unless this is the last wave, the game is over, or the wave's boss ends the game.
        private void HandleWaveCleared(int wave)
        {
            if (!IsServer || !IsSpawned || GameOutcome.IsOver) return;
            if (wave >= Config.Waves.Length) return;
            var boss = WavePlanner.FindBoss(Config.Bosses, wave);
            if (boss != null && boss.Kind == EnemyKind.Boss) return; // the win comes right after this event: no rest

            _serverOfferCount = UpgradeRules.Deal(Config.Upgrades, Config.UpgradeChoicesPerRest, _serverOffer, _scratch);
            if (_serverOfferCount == 0) return;
            ShowOfferRpc(UpgradeRules.PackOffer(_serverOffer, _serverOfferCount), _serverOfferCount);
        }

        // Server: the rest is over.
        private void HandleWaveStarted(int wave)
        {
            ServerCloseOffer();
        }

        /// <summary>Tells the owner which cards it was dealt. Server to owner only.</summary>
        /// <param name="packedKinds">Kinds, one byte each (see <see cref="UpgradeRules.PackOffer"/>).</param>
        /// <param name="count">Number of cards.</param>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        public void ShowOfferRpc(ulong packedKinds, int count)
        {
            _localOfferCount = Mathf.Clamp(count, 0, UpgradeRules.MaxChoices);
            for (int i = 0; i < _localOfferCount; i++) _localOffer[i] = UpgradeRules.UnpackOffer(packedKinds, i);
            OfferVersion++;
        }

        /// <summary>Tells the owner the offer is closed (picked, rest over, game over or restart). Server to owner only.</summary>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        public void ClearOfferRpc()
        {
            if (_localOfferCount == 0) return;
            _localOfferCount = 0;
            OfferVersion++;
        }

        /// <summary>
        /// Owner only: asks the server to take card <paramref name="index"/> of the open offer. The offer stays
        /// on screen until the server confirms by closing it. Ignored when no offer is open.
        /// </summary>
        /// <param name="index">0 up to <see cref="OfferCount"/> minus 1.</param>
        public void RequestPick(int index)
        {
            if (!IsSpawned || !IsOwner || _localOfferCount == 0) return;
            if (index < 0 || index >= _localOfferCount) return;
            PickRpc(index);
        }

        /// <summary>
        /// Pick request, sent by the owner and run on the server. The server never trusts it: the sender must own
        /// this player, an offer must be open for this player, the index must be in range and the game not over.
        /// </summary>
        /// <param name="index">Card index in the offer.</param>
        /// <param name="rpcParams">Filled in by Netcode; carries the sender.</param>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void PickRpc(int index, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if (GameOutcome.IsOver || _serverOfferCount == 0) return;
            if (index < 0 || index >= _serverOfferCount) return;

            var kind = (UpgradeKind)_serverOffer[index];
            _levels.Value = UpgradeRules.WithLevelAdded(_levels.Value, kind);
            // Own call: a max health card also heals by the gain, so a player topped up by the rest heal stays full.
            if (kind == UpgradeKind.MaxHealth && _health != null && _health.Health > 0)
            {
                var definition = UpgradeRules.Find(Config.Upgrades, kind);
                if (definition != null) _health.ServerSetHealth(_health.Health + Mathf.RoundToInt(definition.Value));
            }
            ServerCloseOffer();
        }

        /// <summary>
        /// Server only (story 007 Restart): clears every level and closes any open offer. Call it BEFORE health is
        /// reset, so the new max health applies. Ignored on a client.
        /// </summary>
        public void ServerResetUpgrades()
        {
            if (!IsServer || !IsSpawned) return;
            _levels.Value = 0UL;
            ServerCloseOffer();
        }

        private void ServerCloseOffer()
        {
            if (_serverOfferCount == 0) return;
            _serverOfferCount = 0;
            ClearOfferRpc();
        }
    }
}
