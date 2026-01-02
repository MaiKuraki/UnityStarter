using Unity.Mathematics;
using UnityEngine;
using System.Collections.Generic;
using System.Text;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Component that marks a GameObject as perceptible by AI sensors.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CycloneGames/AI/Perceptible")]
    public class PerceptibleComponent : MonoBehaviour, IPerceptible
    {
        [Header("Type")]
        [SerializeField] private int _typeId = PerceptibleTypes.Default;
        [SerializeField] private string _tag = "";

        [Header("Detection")]
        [SerializeField] private float _detectionRadius = 1f;
        [SerializeField] private bool _isDetectable = true;
        [SerializeField] private Transform _losPoint;

        [Header("Sound (for Hearing)")]
        [SerializeField] private float _loudness = 1f;
        [SerializeField] private bool _isSoundSource;

        [SerializeField] private bool _showDebugOverlay;

        private PerceptibleHandle _handle;
        private int _perceptibleId;

        // Track who detected us
        private readonly List<(AIPerceptionComponent detector, DetectionReason reason, float time)> _detectedBy = new();

        // GUI state - 0GC optimized
        private Rect _debugWindowRect = new Rect(350, 10, 280, 320);
        private Vector2 _scrollPosition;
        private GUIStyle _headerStyle;
        private GUIStyle _infoStyle;
        private bool _stylesInitialized;

        // 0GC: Pre-allocated StringBuilder and cached content
        private readonly StringBuilder _sb = new StringBuilder(128);
        private string _windowTitle;
        private static readonly GUIContent _detectedByHeader = new GUIContent("DETECTED BY:");
        private static readonly GUIContent _noOne = new GUIContent("  (No one)");
        private static readonly GUIContent _toggleInfo = new GUIContent("Toggle via Inspector");

        public int PerceptibleId => _perceptibleId;
        public int PerceptibleTypeId => _typeId;
        public float DetectionRadius => _detectionRadius;
        public float Loudness => _loudness;
        public bool IsSoundSource => _isSoundSource;
        public string Tag => _tag;

        public bool IsDetectable
        {
            get => _isDetectable && isActiveAndEnabled;
            set
            {
                if (_isDetectable != value)
                {
                    _isDetectable = value;
                    if (PerceptibleRegistry.HasInstance)
                        PerceptibleRegistry.Instance.MarkDirty();
                }
            }
        }

        public float3 Position => transform.position;

        public PerceptibleHandle Handle => _handle;

        public bool ShowDebugOverlay
        {
            get => _showDebugOverlay;
            set => _showDebugOverlay = value;
        }

        public float3 GetLOSPoint()
        {
            return _losPoint != null
                ? (float3)_losPoint.position
                : (float3)transform.position;
        }

        private void OnEnable()
        {
            _perceptibleId = GetInstanceID();
            _handle = PerceptibleRegistry.Instance.Register(this);

            // Cache window title (0GC after init)
            _windowTitle = $"Perceptible - {gameObject.name}";
        }

        private void Update()
        {
            // Scan for who is detecting us (only when debug overlay is shown)
            if (_showDebugOverlay)
            {
                UpdateDetectors();
            }
        }

        private void UpdateDetectors()
        {
            _detectedBy.Clear();

            var perceptions = FindObjectsByType<AIPerceptionComponent>(FindObjectsSortMode.None);

            foreach (var perception in perceptions)
            {
                if (perception.gameObject == gameObject) continue;

                // Check sight
                if (perception.SightSensor != null)
                {
                    for (int i = 0; i < perception.SightSensor.DetectedCount; i++)
                    {
                        var result = perception.SightSensor.GetResult(i);
                        if (result.Target == _handle)
                        {
                            _detectedBy.Add((perception, DetectionReason.VisualContact, result.DetectionTime));
                            break;
                        }
                    }
                }

                // Check hearing
                if (perception.HearingSensor != null)
                {
                    for (int i = 0; i < perception.HearingSensor.DetectedCount; i++)
                    {
                        var result = perception.HearingSensor.GetResult(i);
                        if (result.Target == _handle)
                        {
                            _detectedBy.Add((perception, DetectionReason.SoundHeard, result.DetectionTime));
                            break;
                        }
                    }
                }
            }
        }

        private void OnDisable()
        {
            if (PerceptibleRegistry.HasInstance)
            {
                PerceptibleRegistry.Instance.Unregister(_handle);
            }
            _handle = PerceptibleHandle.Invalid;
        }

        public void SetTypeId(int typeId)
        {
            _typeId = typeId;
            if (PerceptibleRegistry.HasInstance)
                PerceptibleRegistry.Instance.MarkDirty();
        }

        public void SetLoudness(float loudness)
        {
            _loudness = Mathf.Max(0f, loudness);
        }

        /// <summary>
        /// Gets list of AI that detected this perceptible.
        /// </summary>
        public IReadOnlyList<(AIPerceptionComponent detector, DetectionReason reason, float time)> GetDetectors() => _detectedBy;

        private void OnGUI()
        {
            if (!_showDebugOverlay || !Application.isPlaying) return;

            InitStyles();

            _debugWindowRect = GUI.Window(
                GetInstanceID() + 1000,
                _debugWindowRect,
                DrawDebugWindow,
                _windowTitle
            );
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };
            _headerStyle.normal.textColor = new Color(0.3f, 0.9f, 0.7f);

            _infoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10
            };
            _infoStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);

            _stylesInitialized = true;
        }

        private void DrawDebugWindow(int windowId)
        {
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            // 0GC: Use StringBuilder
            _sb.Clear();
            _sb.Append("Type: ");
            _sb.Append(PerceptibleTypes.GetTypeName(_typeId));
            GUILayout.Label(_sb.ToString());

            _sb.Clear();
            _sb.Append("Detectable: ");
            _sb.Append(IsDetectable);
            GUILayout.Label(_sb.ToString());

            _sb.Clear();
            _sb.Append("Loudness: ");
            _sb.Append(_loudness.ToString("F2"));
            GUILayout.Label(_sb.ToString());

            GUILayout.Space(8);

            GUILayout.Label(_detectedByHeader, _headerStyle);

            if (_detectedBy.Count == 0)
            {
                GUILayout.Label(_noOne);
            }
            else
            {
                for (int i = 0; i < _detectedBy.Count; i++)
                {
                    var (detector, reason, time) = _detectedBy[i];

                    _sb.Clear();
                    _sb.Append("  ");

                    // Reason icon
                    switch (reason)
                    {
                        case DetectionReason.VisualContact:
                            _sb.Append("👁 ");
                            break;
                        case DetectionReason.SoundHeard:
                            _sb.Append("👂 ");
                            break;
                        case DetectionReason.ProximityAlert:
                            _sb.Append("⚠ ");
                            break;
                        default:
                            _sb.Append("? ");
                            break;
                    }

                    _sb.Append(detector.gameObject.name);
                    GUILayout.Label(_sb.ToString());

                    _sb.Clear();
                    _sb.Append("      Reason: ");
                    _sb.Append(reason.ToString());
                    GUILayout.Label(_sb.ToString());
                }
            }

            GUILayout.Space(8);
            GUILayout.Label(_toggleInfo, _infoStyle);

            GUILayout.EndScrollView();

            GUI.DragWindow();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _detectionRadius);

            if (_isSoundSource)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
                Gizmos.DrawWireSphere(transform.position, _detectionRadius * _loudness);
            }

            if (_losPoint != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, _losPoint.position);
                Gizmos.DrawWireSphere(_losPoint.position, 0.1f);
            }

            // Draw detection lines from detectors
            if (Application.isPlaying)
            {
                for (int i = 0; i < _detectedBy.Count; i++)
                {
                    var (detector, reason, _) = _detectedBy[i];
                    Gizmos.color = reason == DetectionReason.VisualContact
                        ? new Color(1f, 0.8f, 0f, 0.6f)
                        : new Color(0.5f, 0.8f, 1f, 0.6f);
                    Gizmos.DrawLine(transform.position, detector.transform.position);
                }
            }
        }
#endif
    }
}
