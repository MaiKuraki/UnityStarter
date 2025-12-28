using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Provides Agents Navigation (DOTS) based movement input for AI characters.
    /// This is a simplified implementation that uses direction-based movement.
    /// For full ECS integration, extend this class or create a custom implementation.
    /// 
    /// Requires: Agents Navigation package (com.projectdawn.navigation)
    /// Note: Only supports 3D movement. For 2D, use AStarInputProvider.
    /// </summary>
#if AGENTS_NAVIGATION
    [DisallowMultipleComponent]
    public class AgentsNavigationProvider : MonoBehaviour, IPathfindingProvider
    {
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

        public Vector3 CurrentDestination => _destination;

        public Vector3 CurrentDirection
        {
            get
            {
                if (!_hasDestination) return Vector3.zero;
                Vector3 dir = _destination - transform.position;
                dir.y = 0;
                return dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.zero;
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

        public bool SetDestination(Vector3 destination)
        {
            _destination = destination;
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
    /// <summary>
    /// Stub class when Agents Navigation is not installed.
    /// </summary>
    public class AgentsNavigationProvider : MonoBehaviour, IPathfindingProvider
    {
        public bool IsNavigating => false;
        public bool HasReachedDestination => false;
        public Vector3 CurrentDestination => Vector3.zero;
        public Vector3 CurrentDirection => Vector3.zero;
        public float RemainingDistance => 0f;

        public bool SetDestination(Vector3 destination) => false;
        public void StopNavigation() { }

        private void Awake()
        {
            Debug.LogWarning("[AgentsNavigationProvider] Agents Navigation (com.projectdawn.navigation) is not installed. " +
                           "Install it via Package Manager to enable DOTS-based navigation.");
            enabled = false;
        }
    }
#endif
}
