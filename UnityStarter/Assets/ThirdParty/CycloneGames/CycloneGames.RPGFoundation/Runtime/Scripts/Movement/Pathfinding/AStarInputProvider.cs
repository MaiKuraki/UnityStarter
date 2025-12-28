using UnityEngine;

#if ASTAR_PATHFINDING
using Pathfinding;
#endif

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Provides A* Pathfinding Project based movement input for AI characters.
    /// Supports both 3D and 2D pathfinding (grid graphs, navmesh graphs, etc.).
    /// 
    /// Requires: A* Pathfinding Project package (com.arongranberg.astar)
    /// </summary>
#if ASTAR_PATHFINDING
    [RequireComponent(typeof(Seeker))]
    [DisallowMultipleComponent]
    public class AStarInputProvider : MonoBehaviour, IPathfindingProvider
    {
        [Header("Movement Settings")]
        [Tooltip("Speed multiplier for A* movement.")]
        [SerializeField] private float speedMultiplier = 1f;

        [Tooltip("Distance at which destination is considered reached.")]
        [SerializeField] private float stoppingDistance = 0.5f;

        [Tooltip("How often to recalculate path (seconds). 0 = never auto-recalculate.")]
        [SerializeField] private float repathRate = 0.5f;

        [Header("2D Settings")]
        [Tooltip("Enable 2D mode (uses XY plane instead of XZ).")]
        [SerializeField] private bool is2DMode = false;

        private Seeker _seeker;
        private MovementComponent _movement3D;
        private object _movement2D; // Use object to avoid direct reference to MovementComponent2D
        private System.Reflection.MethodInfo _setInputDirection2D;
        private Path _currentPath;
        private int _currentWaypoint;
        private bool _hasDestination;
        private Vector3 _destination;
        private float _lastRepathTime;

        #region IPathfindingProvider Implementation

        public bool IsNavigating => _hasDestination && !HasReachedDestination;

        public bool HasReachedDestination
        {
            get
            {
                if (!_hasDestination) return false;
                float dist = is2DMode
                    ? Vector2.Distance(transform.position, _destination)
                    : Vector3.Distance(transform.position, _destination);
                return dist <= stoppingDistance;
            }
        }

        public Vector3 CurrentDestination => _destination;

        public Vector3 CurrentDirection
        {
            get
            {
                if (_currentPath == null || _currentWaypoint >= _currentPath.vectorPath.Count)
                    return Vector3.zero;

                Vector3 dir = _currentPath.vectorPath[_currentWaypoint] - transform.position;
                if (is2DMode) dir.z = 0;
                else dir.y = 0;

                return dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.zero;
            }
        }

        public float RemainingDistance
        {
            get
            {
                if (_currentPath == null) return 0f;

                float dist = 0f;
                for (int i = _currentWaypoint; i < _currentPath.vectorPath.Count - 1; i++)
                {
                    dist += Vector3.Distance(_currentPath.vectorPath[i], _currentPath.vectorPath[i + 1]);
                }
                dist += Vector3.Distance(transform.position,
                    _currentWaypoint < _currentPath.vectorPath.Count
                        ? _currentPath.vectorPath[_currentWaypoint]
                        : _destination);
                return dist;
            }
        }

        #endregion

        private void Awake()
        {
            _seeker = GetComponent<Seeker>();
            _movement3D = GetComponent<MovementComponent>();

            // Try to get MovementComponent2D via reflection to avoid compile-time dependency
            var movement2DType = System.Type.GetType("CycloneGames.RPGFoundation.Runtime.Movement.MovementComponent2D, CycloneGames.RPGFoundation.Runtime");
            if (movement2DType != null)
            {
                _movement2D = GetComponent(movement2DType);
                if (_movement2D != null)
                {
                    _setInputDirection2D = movement2DType.GetMethod("SetInputDirection", new[] { typeof(Vector2) });
                }
            }
        }

        private void Update()
        {
            if (!_hasDestination) return;

            // Auto repath
            if (repathRate > 0 && Time.time - _lastRepathTime > repathRate)
            {
                _seeker.StartPath(transform.position, _destination, OnPathComplete);
                _lastRepathTime = Time.time;
            }

            // Follow path
            if (_currentPath != null && !HasReachedDestination)
            {
                FollowPath();
            }
            else if (HasReachedDestination)
            {
                StopMovement();
                _hasDestination = false;
            }
        }

        public bool SetDestination(Vector3 destination)
        {
            if (_seeker == null) return false;

            _destination = destination;
            _hasDestination = true;
            _currentWaypoint = 0;
            _lastRepathTime = Time.time;

            _seeker.StartPath(transform.position, destination, OnPathComplete);
            return true;
        }

        public void StopNavigation()
        {
            _seeker.CancelCurrentPathRequest();
            _currentPath = null;
            _hasDestination = false;
            _currentWaypoint = 0;
            StopMovement();
        }

        private void OnPathComplete(Path p)
        {
            if (p.error)
            {
                Debug.LogWarning($"[AStarInputProvider] Path error: {p.errorLog}");
                return;
            }

            _currentPath = p;
            _currentWaypoint = 0;
        }

        private void FollowPath()
        {
            if (_currentPath == null || _currentWaypoint >= _currentPath.vectorPath.Count)
                return;

            Vector3 targetWaypoint = _currentPath.vectorPath[_currentWaypoint];
            Vector3 direction = CurrentDirection;

            // Check if reached current waypoint
            float waypointDist = is2DMode
                ? Vector2.Distance(transform.position, targetWaypoint)
                : Vector3.Distance(transform.position, targetWaypoint);

            if (waypointDist < stoppingDistance * 0.5f)
            {
                _currentWaypoint++;
                if (_currentWaypoint >= _currentPath.vectorPath.Count)
                {
                    StopMovement();
                    return;
                }
            }

            // Apply movement
            if (is2DMode && _movement2D != null && _setInputDirection2D != null)
            {
                _setInputDirection2D.Invoke(_movement2D, new object[] { new Vector2(direction.x, direction.y) * speedMultiplier });
            }
            else if (_movement3D != null)
            {
                _movement3D.SetInputDirection(direction * speedMultiplier);
                _movement3D.SetLookDirection(direction);
            }
        }

        private void StopMovement()
        {
            if (is2DMode && _movement2D != null && _setInputDirection2D != null)
            {
                _setInputDirection2D.Invoke(_movement2D, new object[] { Vector2.zero });
            }
            else if (_movement3D != null)
            {
                _movement3D.SetInputDirection(Vector3.zero);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (_currentPath == null || _currentPath.vectorPath == null) return;

            Gizmos.color = Color.cyan;
            for (int i = _currentWaypoint; i < _currentPath.vectorPath.Count - 1; i++)
            {
                Gizmos.DrawLine(_currentPath.vectorPath[i], _currentPath.vectorPath[i + 1]);
                Gizmos.DrawWireSphere(_currentPath.vectorPath[i], 0.1f);
            }

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(_destination, 0.3f);
        }
    }
#else
    /// <summary>
    /// Stub class when A* Pathfinding Project is not installed.
    /// </summary>
    public class AStarInputProvider : MonoBehaviour, IPathfindingProvider
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
            Debug.LogWarning("[AStarInputProvider] A* Pathfinding Project (com.arongranberg.astar) is not installed. " +
                           "Install it via Package Manager to enable A* pathfinding.");
            enabled = false;
        }
    }
#endif
}
