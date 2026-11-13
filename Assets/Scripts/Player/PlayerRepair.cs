using LastWave.Base;
using LastWave.Config;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastWave.Player
{
    /// <summary>
    /// The one place for the interact key (E): holding it revives a downed friend (brief MVP feature 5) or
    /// repairs a damaged base (feature 4). When both are in range, REVIVE WINS: a downed friend is more
    /// urgent than the base, and the revive timer would otherwise never get a chance to finish.
    /// The owner only tells the server "I am / am not interacting"; the server re-checks everything every
    /// frame with its own copy of the positions (reviver standing, friend downed and in range, or base in
    /// range), so a player pushed out of range, downed or killed simply stops.
    /// While interacting the owner cannot move or shoot (PlayerMovement and PlayerShooting read
    /// <see cref="IsInteractingLocal"/>; the server also refuses shots while <see cref="IsInteractingOnServer"/>).
    /// A downed or dead player cannot interact at all.
    /// </summary>
    /// <example>
    /// Stand within the revive range of a downed friend in the Game scene and hold E for the revive time,
    /// or stand within the repair range of the damaged base and hold E.
    /// </example>
    [DefaultExecutionOrder(-50)] // before PlayerMovement, PlayerShooting and PlayerLife read the state this frame
    public sealed class PlayerRepair : NetworkBehaviour
    {
        private BaseHealth _base;
        private PlayerLife _life;
        private bool _requested; // server: what the owner last reported

        /// <summary>Owner only: true while E is held and there is a downed friend to revive or a damaged base to repair in range. Blocks moving and shooting.</summary>
        public bool IsInteractingLocal { get; private set; }

        /// <summary>
        /// Server only: true while the owner reports interacting AND the server's copy shows the player standing
        /// with something to revive or repair in range. The server refuses shots while this is true.
        /// </summary>
        public bool IsInteractingOnServer =>
            IsServer && _requested && IsStandingOrUnknown() && (FindReviveTarget() != null || CanRepairOnServer());

        /// <summary>Clears the interact state.</summary>
        public override void OnNetworkSpawn()
        {
            _life = GetComponent<PlayerLife>();
            _requested = false;
            IsInteractingLocal = false;
        }

        /// <summary>Clears the interact state.</summary>
        public override void OnNetworkDespawn()
        {
            _requested = false;
            IsInteractingLocal = false;
        }

        /// <summary>
        /// Interact report, sent by the owner when it starts or stops interacting and run on the server.
        /// The server never trusts it for range or state: that is checked every frame in <c>Update</c>.
        /// </summary>
        /// <param name="interacting">True when the owner holds E with something to revive or repair in range.</param>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void SetInteractingRpc(bool interacting)
        {
            _requested = interacting;
        }

        private void Update()
        {
            if (!IsSpawned) return;

            // The base only exists in the Game scene (players also walk in the Menu scene).
            if (_base == null) _base = FindFirstObjectByType<BaseHealth>();

            if (IsOwner) UpdateOwner();
            if (IsServer && _requested && IsStandingOrUnknown())
            {
                var target = FindReviveTarget();
                if (target != null)
                {
                    target.ServerHoldRevive(Time.deltaTime); // revive wins over repair
                }
                else if (_base != null && InRange())
                {
                    _base.Repair(GameConfig.Instance.RepairPerSecond, Time.deltaTime);
                }
            }
        }

        private void UpdateOwner()
        {
            var keyboard = Keyboard.current;
            bool held = keyboard != null && keyboard[GameConfig.Instance.RepairKey].isPressed;
            bool interacting = held && IsStandingOrUnknown()
                && (FindReviveTarget() != null || (_base != null && _base.NeedsRepair && InRange()));
            if (interacting == IsInteractingLocal) return;

            IsInteractingLocal = interacting;
            SetInteractingRpc(interacting);
        }

        private bool IsStandingOrUnknown()
        {
            return _life == null || _life.IsStanding;
        }

        private bool CanRepairOnServer()
        {
            return _base != null && _base.NeedsRepair && InRange();
        }

        private bool InRange()
        {
            return BaseRules.IsInRange(transform.position, _base.Position, GameConfig.Instance.RepairRange);
        }

        // The nearest OTHER downed player within the revive range, or null. Uses this machine's copy of the
        // positions: the server's copy decides, the owner's copy only decides whether to stand still.
        private PlayerLife FindReviveTarget()
        {
            float range = GameConfig.Instance.ReviveRange;
            PlayerLife best = null;
            float bestSqr = float.MaxValue;
            foreach (var other in PlayerLife.All)
            {
                if (other == _life || !other.IsDowned) continue;
                if (!BaseRules.IsInRange(transform.position, other.transform.position, range)) continue;
                float sqr = ((Vector2)other.transform.position - (Vector2)transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = other;
                }
            }
            return best;
        }
    }
}
