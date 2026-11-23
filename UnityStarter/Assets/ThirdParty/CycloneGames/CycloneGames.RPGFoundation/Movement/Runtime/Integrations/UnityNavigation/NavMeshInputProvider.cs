using UnityEngine;
using CycloneGames.Logging;
using CycloneGames.RPGFoundation.Movement.Core;
using CycloneGames.RPGFoundation.Movement.Runtime;

#if CYCLONE_RPGFOUNDATION_HAS_UNITY_AI_NAVIGATION
using UnityEngine.AI;
#endif

namespace CycloneGames.RPGFoundation.Movement.Integrations.UnityNavigation
{
#if CYCLONE_RPGFOUNDATION_HAS_UNITY_AI_NAVIGATION
    [RequireComponent(typeof(MovementComponent))]
    [RequireComponent(typeof(NavMeshAgent))]
    [DisallowMultipleComponent]
    public class NavMeshInputProvider : MonoBehaviour, IPathfindingProvider
    {
        private static readonly LogChannel Log = RpgUnityNavigationLog.Channel;

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

        public bool IsNavigating => _hasDestination && !HasReachedDestination;

        public bool HasReachedDestination
        {
            get
            {
                if (_agent == null || !_hasDestination) return false;
                if (_agent.pathPending) return false;
                return _agent.remainingDistance <= stoppingDistance;
            }
        }

        public MovementVector3 CurrentDestination => _agent != null ? _agent.destination.ToMovementVector3() : MovementVector3.Zero;

        public MovementVector3 CurrentDirection
        {
            get
            {
                if (_agent == null || !_hasDestination) return MovementVector3.Zero;
                Vector3 dir = (_agent.steeringTarget - transform.position);
                dir.y = 0;
                return dir.sqrMagnitude > 0.001f ? dir.normalized.ToMovementVector3() : MovementVector3.Zero;
            }
        }

        public float RemainingDistance => _agent != null ? _agent.remainingDistance : 0f;

        #endregion

        private void Awake()
        {
            _movement = GetComponent<MovementComponent>();
            _agent = GetComponent<NavMeshAgent>();

            _agent.updatePosition = false;
            _agent.updateRotation = false;
        }

        private void Update()
        {
            if (_agent == null || _movement == null) return;

            _agent.nextPosition = transform.position;

            if (!_hasDestination) return;

            if (_agent.isOnOffMeshLink && autoTraverseLinks)
            {
                HandleOffMeshLink();
                return;
            }

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

        public bool SetDestination(MovementVector3 destination)
        {
            if (_agent == null) return false;

            _agent.destination = destination.ToVector3();
            _hasDestination = true;
            return true;
        }

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

        private void HandleOffMeshLink()
        {
            if (!_agent.isOnOffMeshLink) return;

            OffMeshLinkData linkData = _agent.currentOffMeshLinkData;
            Vector3 startPos = linkData.startPos;
            Vector3 endPos = linkData.endPos;

            float heightDiff = endPos.y - startPos.y;
            Vector3 direction = (endPos - startPos).normalized;
            direction.y = 0;

            if (!_isTraversingLink)
            {
                _isTraversingLink = true;

                if (heightDiff > 0.5f)
                {
                    _movement.LaunchCharacter(new Vector3(direction.x * 5f, linkJumpForce, direction.z * 5f));
                }
                else if (heightDiff < -0.5f)
                {
                    _movement.SetInputDirection(direction * speedMultiplier);
                }
                else
                {
                    float jumpHeight = Mathf.Max(1f, Vector3.Distance(startPos, endPos) * 0.25f);
                    float jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);
                    _movement.LaunchCharacter(new Vector3(direction.x * 5f, jumpVelocity, direction.z * 5f));
                }
            }

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

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(_agent.destination, 0.3f);
        }
    }
#else
    public class NavMeshInputProvider : MonoBehaviour, IPathfindingProvider
    {
        private static readonly LogChannel Log = RpgUnityNavigationLog.Channel;

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
                "[NavMeshInputProvider] Unity AI Navigation package (com.unity.ai.navigation) is not installed. " +
                "Install it via Package Manager to enable NavMesh movement.");
            enabled = false;
        }
    }
#endif
}
