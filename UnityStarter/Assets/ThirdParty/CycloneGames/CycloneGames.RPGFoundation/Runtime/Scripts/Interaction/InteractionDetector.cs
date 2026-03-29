using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Unity.Mathematics;
using R3;
using VitalRouter;
using Cysharp.Threading.Tasks;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public enum DetectionMode : byte
    {
        Physics3D = 0,
        Physics2D = 1,
        SpatialHash = 2
    }

    public class InteractionDetector : MonoBehaviour, IInteractionDetector
    {
        [Header("Detection")]
        [SerializeField] private DetectionMode detectionMode = DetectionMode.Physics3D;
        [SerializeField] private float detectionRadius = 3f;
        [SerializeField] private LayerMask interactableLayer;
        [SerializeField] private LayerMask obstructionLayer = 1;
        [SerializeField] private Vector3 detectionOffset = new(0, 1.5f, 0);
        [SerializeField] private int maxInteractables = 64;

        [Header("Channel Filter")]
        [SerializeField] private InteractionChannel channelMask = InteractionChannel.All;

        [Header("Scoring Weights")]
        [SerializeField] private float distanceWeight = 1f;
        [SerializeField] private float angleWeight = 2f;
        [Tooltip("Multiplier for Priority in the scoring formula. Higher = Priority dominates over angle/distance.")]
        [SerializeField] private float priorityWeight = 100f;

        [Header("Nearby List")]
        [Tooltip("Maximum number of candidates to track in the nearby interactables list.")]
        [SerializeField] private int maxNearbyCandidates = 16;

        [Header("LOD Settings (Time-Based)")]
        [SerializeField] private float nearDistance = 5f;
        [SerializeField] private float farDistance = 15f;
        [SerializeField] private float disableDistance = 50f;

        [Tooltip("Detection interval when target is near (milliseconds)")]
        [SerializeField] private float nearIntervalMs = 33f;
        [Tooltip("Detection interval when target is far (milliseconds)")]
        [SerializeField] private float farIntervalMs = 150f;
        [Tooltip("Detection interval when target is very far (milliseconds)")]
        [SerializeField] private float veryFarIntervalMs = 300f;
        [Tooltip("Detection interval in sleep mode (milliseconds)")]
        [SerializeField] private float sleepIntervalMs = 500f;
        [Tooltip("Time without target before entering sleep mode (milliseconds)")]
        [SerializeField] private float sleepEnterMs = 1000f;

        [Header("References")]
        [SerializeField] private Transform detectionOrigin;

        private Collider[] _colliderBuffer3D;
        private Collider2D[] _colliderBuffer2D;
        private RaycastHit[] _raycastHits3D;
        private RaycastHit2D[] _raycastHits2D;
        private bool _detectionEnabled = true;
        private float _lastDetectionTime;
        private float _noTargetStartTime;
        private IInteractionSystem _system;
        private GameObjectInstigator _cachedInstigator;

        private readonly ReactiveProperty<IInteractable> _currentInteractable = new(null);
        public ReadOnlyReactiveProperty<IInteractable> CurrentInteractable => _currentInteractable;
        public DetectionMode DetectionMode { get => detectionMode; set => detectionMode = value; }
        public InteractionChannel ChannelMask { get => channelMask; set => channelMask = value; }

        // Nearby candidates list — pre-allocated, sorted by score descending
        private readonly List<InteractionCandidate> _nearbyCandidates = new(16);
        private InteractionCandidate[] _nearbySortBuffer = new InteractionCandidate[16];
        private int _nearbyCount;
        private int _cycleIndex;
        public IReadOnlyList<InteractionCandidate> NearbyInteractables => _nearbyCandidates;
        public event Action<IReadOnlyList<InteractionCandidate>> OnNearbyInteractablesChanged;

        // Lock-free component cache using double-checked pattern
        private static Dictionary<int, IInteractable> s_componentCache = new(128);
        private static int s_cacheGeneration;
        private static float s_lastCacheCleanupTime;
        private const float CacheCleanupInterval = 10f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly System.Text.StringBuilder _debugSb = new(512);
        private string _debugStatus = string.Empty;
        [Header("Debug")]
        [SerializeField] private bool showDebugGUI;
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidInteractable(IInteractable interactable)
        {
            if (interactable == null) return false;
            if (interactable is UnityEngine.Object obj && obj == null) return false;
            return true;
        }

        private void Awake()
        {
            if (detectionOrigin == null) detectionOrigin = transform;
            _colliderBuffer3D = new Collider[maxInteractables];
            _colliderBuffer2D = new Collider2D[maxInteractables];
            _raycastHits3D = new RaycastHit[16];
            _raycastHits2D = new RaycastHit2D[16];
            _noTargetStartTime = Time.time;
            _cachedInstigator = new GameObjectInstigator(gameObject);
        }

        private void Start()
        {
            _system = InteractionSystem.Instance;
            if (_system == null) _system = FindAnyObjectByType<InteractionSystem>();
        }

        private void Update()
        {
            if (!_detectionEnabled) return;

            float currentTime = Time.time;
            float requiredInterval = CalculateUpdateInterval(currentTime);

            if (currentTime - _lastDetectionTime < requiredInterval) return;

            _lastDetectionTime = currentTime;

            // Periodic cache cleanup (infrequent, amortized)
            if (currentTime - s_lastCacheCleanupTime > CacheCleanupInterval)
            {
                CleanComponentCache();
                s_lastCacheCleanupTime = currentTime;
            }

            PerformDetection();
        }

        private void OnDestroy()
        {
            _currentInteractable?.Dispose();
            OnNearbyInteractablesChanged = null;
        }

        public void SetDetectionEnabled(bool enabled)
        {
            _detectionEnabled = enabled;
            if (!enabled && IsValidInteractable(_currentInteractable.Value))
            {
                _currentInteractable.Value.OnDefocus();
                _currentInteractable.Value = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalculateUpdateInterval(float currentTime)
        {
            const float msToSec = 0.001f;
            IInteractable current = _currentInteractable.Value;

            if (!IsValidInteractable(current))
            {
                if (current != null) _currentInteractable.Value = null;

                float noTargetDuration = currentTime - _noTargetStartTime;
                if (noTargetDuration >= sleepEnterMs * msToSec)
                    return sleepIntervalMs * msToSec;
                return nearIntervalMs * msToSec;
            }

            _noTargetStartTime = currentTime;

            float distSqr = (current.Position - detectionOrigin.position).sqrMagnitude;
            float nearSqr = nearDistance * nearDistance;
            float farSqr = farDistance * farDistance;
            float disableSqr = disableDistance * disableDistance;

            if (distSqr > disableSqr)
            {
                _currentInteractable.Value.OnDefocus();
                _currentInteractable.Value = null;
                return sleepIntervalMs * msToSec;
            }

            if (distSqr <= nearSqr) return nearIntervalMs * msToSec;
            if (distSqr <= farSqr) return farIntervalMs * msToSec;
            return veryFarIntervalMs * msToSec;
        }

        private void PerformDetection()
        {
            if (detectionMode == DetectionMode.SpatialHash && _system?.SpatialGrid != null)
                PerformSpatialHashDetection();
            else if (detectionMode == DetectionMode.Physics2D)
                PerformPhysics2DDetection();
            else
                PerformPhysics3DDetection();
        }

        private void PerformSpatialHashDetection()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugSb.Clear();
#endif
            bool is2D = _system.Is2DMode;
            Vector3 originPos = detectionOrigin.position + detectionOrigin.TransformDirection(detectionOffset);
            Vector3 originFwd = is2D ? (Vector3)((Vector2)detectionOrigin.right) : detectionOrigin.forward;

            var candidates = _system.SpatialGrid.QueryRadius(originPos, detectionRadius, is2D);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugSb.Append("[SpatialHash] ").Append(candidates.Count).AppendLine(" candidates");
#endif

            _nearbyCount = 0;
            EnsureSortBufferCapacity(candidates.Count);

            for (int i = 0, count = candidates.Count; i < count; i++)
            {
                IInteractable interactable = candidates[i];
                if (!IsValidInteractable(interactable)) continue;
                if (!interactable.IsInteractable) continue;
                if ((interactable.Channel & channelMask) == 0) continue;

                Vector3 targetPos = interactable.Position;
                Vector3 diff = targetPos - originPos;
                float distSqr = is2D
                    ? diff.x * diff.x + diff.y * diff.y
                    : diff.sqrMagnitude;

                if (interactable.AutoInteract)
                {
                    float autoDistSqr = interactable.InteractionDistance * interactable.InteractionDistance;
                    if (distSqr <= autoDistSqr)
                        interactable.TryInteractAsync().Forget();
                    continue;
                }

                float interactDistSqr = interactable.InteractionDistance * interactable.InteractionDistance;
                if (distSqr > interactDistSqr) continue;

                float dist = math.sqrt(distSqr);
                Vector3 dir = dist > 0.001f ? diff / dist : originFwd;

                // LOS check: use Physics or Physics2D based on mode
                if (is2D)
                {
                    if (IsObstructed2D(originPos, dir, dist)) continue;
                }
                else
                {
                    MonoBehaviour mb = interactable as MonoBehaviour;
                    Transform targetTransform = mb != null ? mb.transform : null;
                    if (IsObstructed3D(originPos, dir, dist, targetTransform)) continue;
                }

                float dot = Vector3.Dot(originFwd, dir);
                float score = interactable.Priority * priorityWeight + dot * angleWeight - (dist / detectionRadius) * distanceWeight;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                string name = interactable is MonoBehaviour m ? m.name : "?";
                _debugSb.Append("  [").Append(name).Append("] Score=").AppendLine(score.ToString("F1"));
#endif

                if (_nearbyCount < _nearbySortBuffer.Length)
                    _nearbySortBuffer[_nearbyCount++] = new InteractionCandidate(interactable, score, distSqr);
            }

            ApplyNearbyAndBest();
        }

        private void PerformPhysics3DDetection()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugSb.Clear();
#endif
            int count = Physics.OverlapSphereNonAlloc(
                detectionOrigin.position, detectionRadius, _colliderBuffer3D, interactableLayer);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugSb.Append("[3D Scan] ").Append(count).AppendLine(" colliders");
#endif

            _nearbyCount = 0;
            EnsureSortBufferCapacity(count);

            Vector3 originPos = detectionOrigin.position + detectionOrigin.TransformDirection(detectionOffset);
            Vector3 originFwd = detectionOrigin.forward;
            float radiusSqr = detectionRadius * detectionRadius;

            for (int i = 0; i < count; i++)
            {
                Collider col = _colliderBuffer3D[i];
                if (col == null) continue;

                IInteractable interactable = GetCachedInteractable3D(col);
                if (!IsValidInteractable(interactable)) continue;
                if (!interactable.IsInteractable) continue;
                if ((interactable.Channel & channelMask) == 0) continue;

                Vector3 targetPos = interactable.Position;
                Vector3 diff = targetPos - originPos;
                float distSqr = diff.sqrMagnitude;

                if (interactable.AutoInteract)
                {
                    float autoDistSqr = interactable.InteractionDistance * interactable.InteractionDistance;
                    if (distSqr <= autoDistSqr)
                        interactable.TryInteractAsync().Forget();
                    continue;
                }

                if (distSqr > radiusSqr) continue;

                float interactDistSqr = interactable.InteractionDistance * interactable.InteractionDistance;
                if (distSqr > interactDistSqr) continue;

                float dist = math.sqrt(distSqr);
                Vector3 dir = dist > 0.001f ? diff / dist : originFwd;

                if (IsObstructed3D(originPos, dir, dist, col.transform))
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    _debugSb.Append("  [").Append(col.name).AppendLine("] Blocked");
#endif
                    continue;
                }

                float dot = Vector3.Dot(originFwd, dir);
                float score = interactable.Priority * priorityWeight + dot * angleWeight - (dist / detectionRadius) * distanceWeight;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _debugSb.Append("  [").Append(col.name).Append("] Score=").AppendLine(score.ToString("F1"));
#endif

                if (_nearbyCount < _nearbySortBuffer.Length)
                    _nearbySortBuffer[_nearbyCount++] = new InteractionCandidate(interactable, score, distSqr);
            }

            ApplyNearbyAndBest();
        }

        private void PerformPhysics2DDetection()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugSb.Clear();
#endif
            Vector2 origin2D = detectionOrigin.position + detectionOrigin.TransformDirection(detectionOffset);
            int count = Physics2D.OverlapCircleNonAlloc(origin2D, detectionRadius, _colliderBuffer2D, interactableLayer);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugSb.Append("[2D Scan] ").Append(count).AppendLine(" colliders");
#endif

            _nearbyCount = 0;
            EnsureSortBufferCapacity(count);

            Vector2 originFwd = detectionOrigin.right; // 2D typically uses right as forward
            float radiusSqr = detectionRadius * detectionRadius;

            for (int i = 0; i < count; i++)
            {
                Collider2D col = _colliderBuffer2D[i];
                if (col == null) continue;

                IInteractable interactable = GetCachedInteractable2D(col);
                if (!IsValidInteractable(interactable)) continue;
                if (!interactable.IsInteractable) continue;
                if ((interactable.Channel & channelMask) == 0) continue;

                Vector2 targetPos = interactable.Position;
                Vector2 diff = targetPos - origin2D;
                float distSqr = diff.sqrMagnitude;

                if (interactable.AutoInteract)
                {
                    float autoDistSqr = interactable.InteractionDistance * interactable.InteractionDistance;
                    if (distSqr <= autoDistSqr)
                        interactable.TryInteractAsync().Forget();
                    continue;
                }

                if (distSqr > radiusSqr) continue;

                float interactDistSqr = interactable.InteractionDistance * interactable.InteractionDistance;
                if (distSqr > interactDistSqr) continue;

                float dist = math.sqrt(distSqr);
                Vector2 dir = dist > 0.001f ? diff / dist : originFwd;

                if (IsObstructed2D(origin2D, dir, dist))
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    _debugSb.Append("  [").Append(col.name).AppendLine("] Blocked");
#endif
                    continue;
                }

                float dot = Vector2.Dot(originFwd, dir);
                float score = interactable.Priority * priorityWeight + dot * angleWeight - (dist / detectionRadius) * distanceWeight;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _debugSb.Append("  [").Append(col.name).Append("] Score=").AppendLine(score.ToString("F1"));
#endif

                if (_nearbyCount < _nearbySortBuffer.Length)
                    _nearbySortBuffer[_nearbyCount++] = new InteractionCandidate(interactable, score, distSqr);
            }

            ApplyNearbyAndBest();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureSortBufferCapacity(int candidateCount)
        {
            if (_nearbySortBuffer.Length < candidateCount)
                _nearbySortBuffer = new InteractionCandidate[candidateCount];
        }

        private void ApplyNearbyAndBest()
        {
            // Sort candidates by score descending (insertion sort — fast for small N)
            for (int i = 1; i < _nearbyCount; i++)
            {
                var key = _nearbySortBuffer[i];
                int j = i - 1;
                while (j >= 0 && _nearbySortBuffer[j].Score < key.Score)
                {
                    _nearbySortBuffer[j + 1] = _nearbySortBuffer[j];
                    j--;
                }
                _nearbySortBuffer[j + 1] = key;
            }

            // Populate nearby list (capped at maxNearbyCandidates)
            _nearbyCandidates.Clear();
            int cap = _nearbyCount < maxNearbyCandidates ? _nearbyCount : maxNearbyCandidates;
            for (int i = 0; i < cap; i++)
                _nearbyCandidates.Add(_nearbySortBuffer[i]);

            // Clamp cycle index
            if (_cycleIndex >= _nearbyCandidates.Count)
                _cycleIndex = 0;

            // Best candidate is either the cycled target (if valid) or top scored
            IInteractable bestCandidate = _nearbyCandidates.Count > 0
                ? _nearbyCandidates[_cycleIndex].Interactable
                : null;

            if (_currentInteractable.Value != bestCandidate)
            {
                _currentInteractable.Value?.OnDefocus();
                _currentInteractable.Value = bestCandidate;
                bestCandidate?.OnFocus();
            }

            OnNearbyInteractablesChanged?.Invoke(_nearbyCandidates);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugStatus = _debugSb.ToString();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsObstructed3D(Vector3 origin, Vector3 dir, float dist, Transform targetTransform)
        {
            int hitCount = Physics.RaycastNonAlloc(origin, dir, _raycastHits3D, dist, obstructionLayer, QueryTriggerInteraction.Collide);

            for (int h = 0; h < hitCount; h++)
            {
                Transform hit = _raycastHits3D[h].transform;
                if (hit.IsChildOf(detectionOrigin)) continue;
                if (targetTransform != null && (hit == targetTransform || hit.IsChildOf(targetTransform))) continue;
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsObstructed2D(Vector2 origin, Vector2 dir, float dist)
        {
            int hitCount = Physics2D.RaycastNonAlloc(origin, dir, _raycastHits2D, dist, obstructionLayer);

            for (int h = 0; h < hitCount; h++)
            {
                Transform hit = _raycastHits2D[h].transform;
                if (hit.IsChildOf(detectionOrigin)) continue;
                return true;
            }
            return false;
        }

        // 3D cache: lock-free read, copy-on-write for thread safety
        private static IInteractable GetCachedInteractable3D(Collider col)
        {
            int id = col.GetInstanceID();
            var cache = s_componentCache;

            if (cache.TryGetValue(id, out IInteractable cached))
            {
                if (cached is UnityEngine.Object obj && obj == null)
                {
                    // Stale entry — will be cleaned up in periodic sweep
                    return null;
                }
                return cached;
            }

            IInteractable interactable = col.GetComponent<IInteractable>();
            if (interactable != null)
            {
                // Copy-on-write: create new dictionary snapshot
                var newCache = new Dictionary<int, IInteractable>(cache);
                newCache[id] = interactable;
                s_componentCache = newCache;
            }

            return interactable;
        }

        // 2D cache: same pattern as 3D
        private static IInteractable GetCachedInteractable2D(Collider2D col)
        {
            int id = col.GetInstanceID();
            var cache = s_componentCache;

            if (cache.TryGetValue(id, out IInteractable cached))
            {
                if (cached is UnityEngine.Object obj && obj == null)
                    return null;
                return cached;
            }

            IInteractable interactable = col.GetComponent<IInteractable>();
            if (interactable != null)
            {
                var newCache = new Dictionary<int, IInteractable>(cache);
                newCache[id] = interactable;
                s_componentCache = newCache;
            }

            return interactable;
        }

        private static void CleanComponentCache()
        {
            var cache = s_componentCache;
            var newCache = new Dictionary<int, IInteractable>(cache.Count);

            foreach (var kvp in cache)
            {
                if (kvp.Value is UnityEngine.Object obj && obj == null) continue;
                if (kvp.Value == null) continue;
                newCache[kvp.Key] = kvp.Value;
            }

            s_componentCache = newCache;
        }

        public static void ClearComponentCache()
        {
            s_componentCache = new Dictionary<int, IInteractable>(128);
        }

        public void TryInteract()
        {
            IInteractable target = _currentInteractable.Value;
            if (!IsValidInteractable(target)) return;
            if (target.IsInteractable)
                Router.Default.PublishAsync(new InteractionCommand(target, instigator: _cachedInstigator)).AsUniTask().Forget();
        }

        public void TryInteract(string actionId)
        {
            IInteractable target = _currentInteractable.Value;
            if (!IsValidInteractable(target)) return;
            if (target.IsInteractable)
                Router.Default.PublishAsync(new InteractionCommand(target, actionId, instigator: _cachedInstigator)).AsUniTask().Forget();
        }

        public void TryInteractAll()
        {
            for (int i = 0; i < _nearbyCandidates.Count; i++)
            {
                var interactable = _nearbyCandidates[i].Interactable;
                if (IsValidInteractable(interactable) && interactable.IsInteractable)
                    Router.Default.PublishAsync(new InteractionCommand(interactable, instigator: _cachedInstigator)).AsUniTask().Forget();
            }
        }

        public void TryInteractAll(string actionId)
        {
            for (int i = 0; i < _nearbyCandidates.Count; i++)
            {
                var interactable = _nearbyCandidates[i].Interactable;
                if (IsValidInteractable(interactable) && interactable.IsInteractable)
                    Router.Default.PublishAsync(new InteractionCommand(interactable, actionId, instigator: _cachedInstigator)).AsUniTask().Forget();
            }
        }

        public void CycleTarget(int direction)
        {
            if (_nearbyCandidates.Count <= 1) return;

            _currentInteractable.Value?.OnDefocus();

            _cycleIndex = ((_cycleIndex + direction) % _nearbyCandidates.Count + _nearbyCandidates.Count) % _nearbyCandidates.Count;

            var next = _nearbyCandidates[_cycleIndex].Interactable;
            _currentInteractable.Value = next;
            next?.OnFocus();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (detectionOrigin == null) return;

            Vector3 center = detectionOrigin.position + detectionOrigin.TransformDirection(detectionOffset);
            Gizmos.color = new Color(0, 1, 0, 0.3f);
            Gizmos.DrawWireSphere(center, detectionRadius);

            IInteractable current = _currentInteractable.Value;
            if (IsValidInteractable(current))
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(center, current.Position);
            }
        }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly GUIContent s_windowTitle = new("Interaction Debug");
        private static GUIStyle s_boxStyle;
        private static GUIStyle s_labelStyle;
        private static GUIStyle s_targetLabelStyle;
        private static bool s_stylesInitialized;
        private Rect _guiRect = new(10, 10, 380, 450);

        private static void InitStyles()
        {
            if (s_stylesInitialized) return;

            s_boxStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(8, 8, 20, 8)
            };

            s_labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true
            };

            s_targetLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.3f, 1f, 0.5f) }
            };

            s_stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (!showDebugGUI) return;

            InitStyles();

            _guiRect = GUI.Window(GetInstanceID(), _guiRect, DrawDebugWindow, s_windowTitle, s_boxStyle);
        }

        private void DrawDebugWindow(int windowId)
        {
            IInteractable target = _currentInteractable.Value;
            string targetName = target != null ? ((MonoBehaviour)target).name : "None";
            string targetPrompt = target?.InteractionPrompt ?? "-";

            GUILayout.BeginHorizontal();
            GUILayout.Label("Target: ", s_labelStyle, GUILayout.Width(50));
            GUILayout.Label(targetName, s_targetLabelStyle);
            GUILayout.EndHorizontal();

            if (target != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Prompt: ", s_labelStyle, GUILayout.Width(50));
                GUILayout.Label(targetPrompt, s_labelStyle);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("State: ", s_labelStyle, GUILayout.Width(50));
                GUILayout.Label(target.CurrentState.ToString(), s_labelStyle);
                GUILayout.Label(" | Can Interact: ", s_labelStyle);
                GUILayout.Label(target.IsInteractable ? "Yes" : "No", s_labelStyle);
                GUILayout.EndHorizontal();

                if (target.InteractionProgress > 0f)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Progress: ", s_labelStyle, GUILayout.Width(50));
                    GUILayout.Label(target.InteractionProgress.ToString("P0"), s_labelStyle);
                    GUILayout.EndHorizontal();
                }

                var actions = target.Actions;
                if (actions.Count > 0)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Actions: ", s_labelStyle, GUILayout.Width(50));
                    for (int i = 0; i < actions.Count; i++)
                    {
                        if (i > 0) GUILayout.Label(" | ", s_labelStyle);
                        var a = actions[i];
                        GUILayout.Label(string.IsNullOrEmpty(a.InputHint) ? a.DisplayText : $"{a.InputHint}: {a.DisplayText}", s_labelStyle);
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mode: ", s_labelStyle, GUILayout.Width(50));
            GUILayout.Label(detectionMode.ToString(), s_labelStyle);
            GUILayout.Label(" | Channel: ", s_labelStyle);
            GUILayout.Label(channelMask.ToString(), s_labelStyle);
            GUILayout.EndHorizontal();

            // Nearby candidates
            if (_nearbyCandidates.Count > 0)
            {
                GUILayout.Space(4);
                GUILayout.Label($"Nearby ({_nearbyCandidates.Count}):", s_labelStyle);
                int displayCount = _nearbyCandidates.Count < 8 ? _nearbyCandidates.Count : 8;
                for (int i = 0; i < displayCount; i++)
                {
                    var c = _nearbyCandidates[i];
                    string name = c.Interactable is MonoBehaviour mb ? mb.name : "?";
                    string marker = i == _cycleIndex ? " >" : "  ";
                    GUILayout.Label($"{marker} {name}  (Score: {c.Score:F1})", s_labelStyle);
                }
                if (_nearbyCandidates.Count > 8)
                    GUILayout.Label($"  ... +{_nearbyCandidates.Count - 8} more", s_labelStyle);
            }

            GUILayout.Space(8);
            GUILayout.Label("Detection Log:", s_labelStyle);
            GUILayout.TextArea(_debugStatus, s_labelStyle, GUILayout.ExpandHeight(true));

            GUI.DragWindow(new Rect(0, 0, 380, 20));
        }
#endif
    }
}
