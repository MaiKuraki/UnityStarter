using UnityEngine;
using CycloneGames.Logging;
using CycloneGames.RPGFoundation.Movement.Core;
using CycloneGames.RPGFoundation.Movement.Runtime;

namespace CycloneGames.RPGFoundation.Movement.Integrations.AgentsNavigation
{
#if CYCLONE_RPGFOUNDATION_HAS_AGENTS_NAVIGATION
    [DisallowMultipleComponent]
    public class AgentsNavigationProvider : MonoBehaviour, IPathfindingProvider
    {
        private static readonly LogChannel Log = RpgAgentsNavigationLog.Channel;

        [Header("Movement Settings")]
        [Tooltip("Speed multiplier for navigation movement.")]
        [SerializeField] private float speedMultiplier = 1f;

        [Tooltip("Distance at which destination is considered reached.")]
        [SerializeField] private float stoppingDistance = 0.5f;

        private MovementComponent _movement3D;
        private bool _hasDestination;
        private Vector3 _destination;

        #region IPathfindingProvider Implementation

        public bool IsNavigating => _hasDestination && !HasReachedDestination;

        public bool HasReachedDestination
        {
            get
            {
                if (!_hasDestination) return false;
                float dist = Vector3.Distance(transform.position, _destination);
                return dist <= stoppingDistance;
            }
        }

        public MovementVector3 CurrentDestination => _destination.ToMovementVector3();

        public MovementVector3 CurrentDirection
        {
            get
            {
                if (!_hasDestination) return MovementVector3.Zero;
                Vector3 dir = _destination - transform.position;
                dir.y = 0;
                return dir.sqrMagnitude > 0.001f ? dir.normalized.ToMovementVector3() : MovementVector3.Zero;
            }
        }

        public float RemainingDistance => Vector3.Distance(transform.position, _destination);

        #endregion

        private void Awake()
        {
            _movement3D = GetComponent<MovementComponent>();
        }

        private void Update()
        {
            if (!_hasDestination) return;

            if (!HasReachedDestination)
            {
                Vector3 direction = CurrentDirection;
                if (_movement3D != null)
                {
                    _movement3D.SetInputDirection(direction * speedMultiplier);
                    _movement3D.SetLookDirection(direction);
                }
            }
            else
            {
                StopMovement();
                _hasDestination = false;
            }
        }

        public bool SetDestination(MovementVector3 destination)
        {
            _destination = destination.ToVector3();
            _hasDestination = true;
            return true;
        }

        public void StopNavigation()
        {
            _hasDestination = false;
            StopMovement();
        }

        private void StopMovement()
        {
            if (_movement3D != null)
            {
                _movement3D.SetInputDirection(Vector3.zero);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!_hasDestination) return;

            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position, _destination);
            Gizmos.DrawWireSphere(_destination, 0.3f);
        }
    }
#else
    public class AgentsNavigationProvider : MonoBehaviour, IPathfindingProvider
    {
        private static readonly LogChannel Log = RpgAgentsNavigationLog.Channel;

        public bool IsNavigating => false;
        public bool HasReachedDestination => false;
        public MovementVector3 CurrentDestination => MovementVector3.Zero;
        public MovementVector3 CurrentDirection => MovementVector3.Zero;
        public float RemainingDistance => 0f;

        public bool SetDestination(MovementVector3 destination) => false;
        public void StopNavigation() { }

        private void Awake()
        {
            Log.Warning(
                "[AgentsNavigationProvider] Agents Navigation (com.projectdawn.navigation) is not installed. " +
                "Install it via Package Manager to enable DOTS-based navigation.");
            enabled = false;
        }
    }
#endif
}
