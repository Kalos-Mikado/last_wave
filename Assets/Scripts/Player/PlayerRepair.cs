using LastWave.Base;
using LastWave.Config;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastWave.Player
{
    /// <summary>
    /// Holding the repair key (E) near a damaged base repairs it (brief MVP feature 4). The owner only tells
    /// the server "I am / am not repairing"; the server checks the range every frame with its own copy of the
    /// player's position and applies the repair, so a player pushed out of range simply stops repairing.
    /// While repairing the owner cannot move or shoot (PlayerMovement and PlayerShooting read
    /// <see cref="IsRepairingLocal"/>; the server also refuses shots while <see cref="IsRepairingOnServer"/>).
    /// This component is the one place for the E key: story 005 (revive) extends it, it is not built here.
    /// </summary>
    /// <example>
    /// Stand within the repair range of the damaged base in the Game scene and hold E.
    /// </example>
    [DefaultExecutionOrder(-50)] // before PlayerMovement and PlayerShooting read IsRepairingLocal this frame
    public sealed class PlayerRepair : NetworkBehaviour
    {
        private BaseHealth _base;
        private bool _requested; // server: what the owner last reported

        /// <summary>Owner only: true while E is held, the base is in range and it needs repair. Blocks moving and shooting.</summary>
        public bool IsRepairingLocal { get; private set; }

        /// <summary>
        /// Server only: true while the owner reports repairing AND the server's copy of the player is in range
        /// of a base that needs repair. The server refuses shots while this is true.
        /// </summary>
        public bool IsRepairingOnServer => IsServer && _requested && _base != null && _base.NeedsRepair && InRange();

        /// <summary>Clears the repair state.</summary>
        public override void OnNetworkSpawn()
        {
            _requested = false;
            IsRepairingLocal = false;
        }

        /// <summary>Clears the repair state.</summary>
        public override void OnNetworkDespawn()
        {
            _requested = false;
            IsRepairingLocal = false;
        }

        /// <summary>
        /// Repair report, sent by the owner when it starts or stops repairing and run on the server.
        /// The server never trusts it for range: that is checked every frame in <c>Update</c>.
        /// </summary>
        /// <param name="repairing">True when the owner holds E in range of a damaged base.</param>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void SetRepairingRpc(bool repairing)
        {
            _requested = repairing;
        }

        private void Update()
        {
            if (!IsSpawned) return;

            // The base only exists in the Game scene (players also walk in the Menu scene).
            if (_base == null) _base = FindFirstObjectByType<BaseHealth>();

            if (IsOwner) UpdateOwner();
            if (IsServer && _requested && _base != null && InRange())
            {
                _base.Repair(GameConfig.Instance.RepairPerSecond, Time.deltaTime);
            }
        }

        private void UpdateOwner()
        {
            var keyboard = Keyboard.current;
            bool held = keyboard != null && keyboard[GameConfig.Instance.RepairKey].isPressed;
            bool repairing = held && _base != null && _base.NeedsRepair && InRange();
            if (repairing == IsRepairingLocal) return;

            IsRepairingLocal = repairing;
            SetRepairingRpc(repairing);
        }

        private bool InRange()
        {
            return BaseRules.IsInRange(transform.position, _base.Position, GameConfig.Instance.RepairRange);
        }
    }
}
