using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using R3;
using VitalRouter;
using Cysharp.Threading.Tasks;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public class InteractionDetector : MonoBehaviour, IInteractionDetector
    {
        [Header("Detection")]
        [SerializeField] private float detectionRadius = 3f;
        [SerializeField] private LayerMask interactableLayer;
        [SerializeField] private LayerMask obstructionLayer = 1;
        [SerializeField] private Vector3 detectionOffset = new(0, 1.5f, 0);
        [SerializeField] private int maxInteractables = 32;

        [Header("Scoring Weights")]
        [SerializeField] private float distanceWeight = 1f;
        [SerializeField] private float angleWeight = 2f;

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

        private Collider[] _colliderBuffer;
        private RaycastHit[] _raycastHits;
        private bool _detectionEnabled = true;
        private float _lastDetectionTime; // Time of last detection
        private float _noTargetTime; // Accumulated time without a target
        private bool _isSleeping; // Sleep mode flag

        private readonly ReactiveProperty<IInteractable> _currentInteractable = new(null);
        public ReadOnlyReactiveProperty<IInteractable> CurrentInteractable => _currentInteractable;

        // Hybrid cache: static for cross-detector sharing, instance WeakReference for safety
        private static readonly Dictionary<int, WeakReference<IInteractable>> s_componentCache = new(64);
        private static readonly object s_cacheLock = new();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly System.Text.StringBuilder _debugSb = new(512);
        private string _debugStatus = string.Empty;
        [Header("Debug")]
        [SerializeField] private bool showDebugGUI;
#endif

        /// <summary>
        /// Checks if an IInteractable reference is valid (not null and not destroyed).
        /// Handles the Unity quirk where interface references don't become null when the underlying object is destroyed.
        /// </summary>
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
            _colliderBuffer = new Collider[maxInteractables];
            _raycastHits = new RaycastHit[16];
        }

        private void Update()
        {
            if (!_detectionEnabled) return;

            float currentTime = Time.time;
            float requiredInterval = CalculateUpdateInterval();

            if (currentTime - _lastDetectionTime < requiredInterval) return;

            _lastDetectionTime = currentTime;
            PerformDetection();
        }

        private void OnDestroy()
        {
            _currentInteractable?.Dispose();
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
        private float CalculateUpdateInterval()
        {
            const float msToSec = 0.001f;
            IInteractable current = _currentInteractable.Value;

            // Sleep mode: no target for extended period
            if (!IsValidInteractable(current))
            {
                // Clear the reference if it's a destroyed object
                if (current != null)
                {
                    _currentInteractable.Value = null;
                }

                _noTargetTime += Time.deltaTime;
                if (_noTargetTime >= sleepEnterMs * msToSec)
                {
                    _isSleeping = true;
                    return sleepIntervalMs * msToSec;
                }
                return nearIntervalMs * msToSec; // Keep searching actively at first
            }

            // Found target, reset sleep tracking
            _noTargetTime = 0f;
            _isSleeping = false;

            float distSqr = (current.Position - detectionOrigin.position).sqrMagnitude;
            float nearSqr = nearDistance * nearDistance;
            float farSqr = farDistance * farDistance;
            float disableSqr = disableDistance * disableDistance;

            // Beyond disable distance: defocus and enter sleep mode
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugSb.Clear();
#endif
            int count = Physics.OverlapSphereNonAlloc(
                detectionOrigin.position, detectionRadius, _colliderBuffer, interactableLayer);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugSb.Append("[Scan] ").Append(count).AppendLine(" colliders");
#endif

            IInteractable bestCandidate = null;
            float bestScore = float.MinValue;

            Vector3 originPos = detectionOrigin.position + detectionOrigin.TransformDirection(detectionOffset);
            Vector3 originFwd = detectionOrigin.forward;
            float radiusSqr = detectionRadius * detectionRadius;

            for (int i = 0; i < count; i++)
            {
                Collider col = _colliderBuffer[i];
                if (col == null) continue;

                IInteractable interactable = GetCachedInteractable(col);

                if (!IsValidInteractable(interactable))
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    _debugSb.Append("  [").Append(col.name).AppendLine("] Skip: Null/Destroyed");
#endif
                    continue;
                }

                if (!interactable.IsInteractable)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    _debugSb.Append("  [").Append(col.name).AppendLine("] Skip: Not Interactable");
#endif
                    continue;
                }

                Vector3 targetPos = interactable.Position;
                Vector3 diff = targetPos - originPos;
                float distSqr = diff.sqrMagnitude;

                // Auto-interact bypass
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

                float dist = FastSqrt(distSqr);
                Vector3 dir = dist > 0.001f ? diff / dist : originFwd;

                if (IsObstructed(originPos, dir, dist, col.transform))
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    _debugSb.Append("  [").Append(col.name).AppendLine("] Blocked");
#endif
                    continue;
                }

                float dot = Vector3.Dot(originFwd, dir);
                float score = interactable.Priority * 100f + dot * angleWeight - (dist / detectionRadius) * distanceWeight;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _debugSb.Append("  [").Append(col.name).Append("] Score=").AppendLine(score.ToString("F1"));
#endif

                if (score > bestScore)
                {
                    bestScore = score;
                    bestCandidate = interactable;
                }
            }

            if (_currentInteractable.Value != bestCandidate)
            {
                _currentInteractable.Value?.OnDefocus();
                _currentInteractable.Value = bestCandidate;
                bestCandidate?.OnFocus();
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugStatus = _debugSb.ToString();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsObstructed(Vector3 origin, Vector3 dir, float dist, Transform targetTransform)
        {
            int hitCount = Physics.RaycastNonAlloc(origin, dir, _raycastHits, dist, obstructionLayer, QueryTriggerInteraction.Collide);

            for (int h = 0; h < hitCount; h++)
            {
                Transform hit = _raycastHits[h].transform;
                if (hit.IsChildOf(detectionOrigin)) continue;
                if (hit == targetTransform || hit.IsChildOf(targetTransform)) continue;
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float FastSqrt(float value)
        {
            // Carmack's fast inverse sqrt approximation, then invert
            if (value <= 0f) return 0f;
            return Mathf.Sqrt(value);
        }

        private static IInteractable GetCachedInteractable(Collider col)
        {
            int id = col.GetInstanceID();

            lock (s_cacheLock)
            {
                if (s_componentCache.TryGetValue(id, out WeakReference<IInteractable> weakRef))
                {
                    if (weakRef.TryGetTarget(out IInteractable cached))
                    {
                        // Verify Unity object is still valid
                        if (cached is UnityEngine.Object obj && obj == null)
                        {
                            s_componentCache.Remove(id);
                            return null;
                        }
                        return cached;
                    }
                    s_componentCache.Remove(id);
                }

                IInteractable interactable = col.GetComponent<IInteractable>();
                if (interactable != null)
                    s_componentCache[id] = new WeakReference<IInteractable>(interactable);

                return interactable;
            }
        }

        public static void ClearComponentCache()
        {
            lock (s_cacheLock)
            {
                s_componentCache.Clear();
            }
        }

        public void TryInteract()
        {
            IInteractable target = _currentInteractable.Value;
            if (!IsValidInteractable(target)) return;
            if (target.IsInteractable)
                Router.Default.PublishAsync(new InteractionCommand(target)).AsUniTask().Forget();
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
        // Pre-allocated GUI content to avoid GC
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
            }

            GUILayout.Space(8);
            GUILayout.Label("Detection Log:", s_labelStyle);
            GUILayout.TextArea(_debugStatus, s_labelStyle, GUILayout.ExpandHeight(true));

            GUI.DragWindow(new Rect(0, 0, 380, 20));
        }
#endif
    }
}