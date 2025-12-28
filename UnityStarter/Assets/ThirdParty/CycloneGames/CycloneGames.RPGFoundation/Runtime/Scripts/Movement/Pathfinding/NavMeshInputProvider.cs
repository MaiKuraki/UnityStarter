using UnityEngine;

#if UNITY_AI_NAVIGATION
using UnityEngine.AI;
#endif

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Provides NavMesh-based movement input for AI characters.
    /// Converts NavMeshAgent path following into MovementComponent input.
    /// Supports OffMeshLink traversal (jumping gaps, dropping, etc.).
    /// 
    /// Requires: Unity AI Navigation package (com.unity.ai.navigation)
    /// </summary>
#if UNITY_AI_NAVIGATION
    [RequireComponent(typeof(MovementComponent))]
    [RequireComponent(typeof(NavMeshAgent))]
    [DisallowMultipleComponent]
    public class NavMeshInputProvider : MonoBehaviour, IPathfindingProvider
    {
        [Header("Movement Settings")]
        [Tooltip("Speed multiplier for NavMesh movement.")]
        [SerializeField] private float speedMultiplier = 1f;

        [Tooltip("Distance at which destination is considered reached.")]
        [SerializeField] private float stoppingDistance = 0.5f;

        [Header("OffMeshLink Settings")]
        [Tooltip("Enable automatic OffMeshLink traversal (jumping gaps, etc.).")]
        [SerializeField] private bool autoTraverseLinks = true;

        [Tooltip("Jump force for upward OffMeshLinks.")]
        [SerializeField] private float linkJumpForce = 10f;

        private MovementComponent _movement;
        private NavMeshAgent _agent;
        private bool _hasDestination;
        private bool _isTraversingLink;

        #region IPathfindingProvider Implementation

        /// <summary>
        /// Returns true if the agent has an active path and hasn't reached destination.
        /// </summary>
        public bool IsNavigating => _hasDestination && !HasReachedDestination;

        /// <summary>
        /// Returns true if the agent has reached its destination.
        /// </summary>
        public bool HasReachedDestination
        {
            get
            {
                if (_agent == null || !_hasDestination) return false;
                if (_agent.pathPending) return false;
                return _agent.remainingDistance <= stoppingDistance;
            }
        }

        /// <summary>
        /// Current navigation destination.
        /// </summary>
        public Vector3 CurrentDestination => _agent != null ? _agent.destination : Vector3.zero;

        /// <summary>
        /// Current movement direction towards next waypoint (normalized).
        /// </summary>
        public Vector3 CurrentDirection
        {
            get
            {
                if (_agent == null || !_hasDestination) return Vector3.zero;
                Vector3 dir = (_agent.steeringTarget - transform.position);
                dir.y = 0;
                return dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.zero;
            }
        }

        /// <summary>
        /// Distance remaining to destination.
        /// </summary>
        public float RemainingDistance => _agent != null ? _agent.remainingDistance : 0f;

        #endregion

        private void Awake()
        {
            _movement = GetComponent<MovementComponent>();
            _agent = GetComponent<NavMeshAgent>();

            // Disable NavMeshAgent's automatic movement - we handle it via MovementComponent
            _agent.updatePosition = false;
            _agent.updateRotation = false;
        }

        private void Update()
        {
            if (_agent == null || _movement == null) return;

            // Sync agent position to actual character position
            _agent.nextPosition = transform.position;

            if (!_hasDestination) return;

            // Handle OffMeshLink traversal
            if (_agent.isOnOffMeshLink && autoTraverseLinks)
            {
                HandleOffMeshLink();
                return;
            }

            // Normal path following
            if (!HasReachedDestination && !_isTraversingLink)
            {
                Vector3 direction = CurrentDirection;

                _movement.SetInputDirection(direction * speedMultiplier);
                _movement.SetLookDirection(direction);
            }
            else if (HasReachedDestination)
            {
                _movement.SetInputDirection(Vector3.zero);
                _hasDestination = false;
            }
        }

        /// <summary>
        /// Set a new navigation destination.
        /// </summary>
        /// <param name="destination">World position to navigate to.</param>
        /// <returns>True if path was found and navigation started.</returns>
        public bool SetDestination(Vector3 destination)
        {
            if (_agent == null) return false;

            _agent.destination = destination;
            _hasDestination = true;
            return true;
        }

        /// <summary>
        /// Stop navigation and clear destination.
        /// </summary>
        public void StopNavigation()
        {
            if (_agent != null)
            {
                _agent.ResetPath();
            }
            _hasDestination = false;
            _isTraversingLink = false;

            if (_movement != null)
            {
                _movement.SetInputDirection(Vector3.zero);
            }
        }

        /// <summary>
        /// Handle OffMeshLink traversal (jumping, dropping, etc.).
        /// </summary>
        private void HandleOffMeshLink()
        {
            if (!_agent.isOnOffMeshLink) return;

            OffMeshLinkData linkData = _agent.currentOffMeshLinkData;
            Vector3 startPos = linkData.startPos;
            Vector3 endPos = linkData.endPos;

            // Calculate if this is a jump up, jump down, or horizontal jump
            float heightDiff = endPos.y - startPos.y;
            Vector3 direction = (endPos - startPos).normalized;
            direction.y = 0;

            if (!_isTraversingLink)
            {
                _isTraversingLink = true;

                if (heightDiff > 0.5f)
                {
                    // Jump up
                    _movement.LaunchCharacter(new Vector3(direction.x * 5f, linkJumpForce, direction.z * 5f));
                }
                else if (heightDiff < -0.5f)
                {
                    // Drop down - just walk off
                    _movement.SetInputDirection(direction * speedMultiplier);
                }
                else
                {
                    // Horizontal jump
                    float jumpHeight = Mathf.Max(1f, Vector3.Distance(startPos, endPos) * 0.25f);
                    float jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);
                    _movement.LaunchCharacter(new Vector3(direction.x * 5f, jumpVelocity, direction.z * 5f));
                }
            }

            // Check if we've reached the end position
            float distToEnd = Vector3.Distance(transform.position, endPos);
            if (distToEnd < stoppingDistance || _movement.IsGrounded)
            {
                if (distToEnd < stoppingDistance * 2f)
                {
                    _agent.CompleteOffMeshLink();
                    _isTraversingLink = false;
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (_agent == null || !_hasDestination) return;

            // Draw path
            Gizmos.color = Color.green;
            NavMeshPath path = _agent.path;
            if (path != null && path.corners.Length > 1)
            {
                for (int i = 0; i < path.corners.Length - 1; i++)
                {
                    Gizmos.DrawLine(path.corners[i], path.corners[i + 1]);
                    Gizmos.DrawWireSphere(path.corners[i], 0.1f);
                }
                Gizmos.DrawWireSphere(path.corners[path.corners.Length - 1], 0.1f);
            }

            // Draw destination
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(_agent.destination, 0.3f);
        }
    }
#else
    /// <summary>
    /// Stub class when Unity AI Navigation package is not installed.
    /// </summary>
    public class NavMeshInputProvider : MonoBehaviour, IPathfindingProvider
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
            Debug.LogWarning("[NavMeshInputProvider] Unity AI Navigation package (com.unity.ai.navigation) is not installed. " +
                           "Install it via Package Manager to enable NavMesh movement.");
            enabled = false;
        }
    }
#endif
}
