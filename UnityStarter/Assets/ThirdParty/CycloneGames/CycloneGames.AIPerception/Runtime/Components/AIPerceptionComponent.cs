using UnityEngine;
using System.Text;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Component that manages AI perception sensors with runtime debug overlay.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CycloneGames/AI/AI Perception")]
    public class AIPerceptionComponent : MonoBehaviour
    {
        [Header("Sight")]
        [SerializeField] private bool _enableSight = true;
        [SerializeField] private SightSensorConfig _sightConfig = SightSensorConfig.Default;

        [Header("Hearing")]
        [SerializeField] private bool _enableHearing = false;
        [SerializeField] private HearingSensorConfig _hearingConfig = HearingSensorConfig.Default;

        [SerializeField] private bool _showDebugOverlay;

        private SightSensor _sightSensor;
        private HearingSensor _hearingSensor;
        private bool _initialized;

        // Debug overlay state - 0GC optimized
        private Rect _debugWindowRect = new Rect(10, 10, 320, 400);
        private Vector2 _scrollPosition;
        private GUIStyle _headerStyle;
        private GUIStyle _detectionStyle;
        private GUIStyle _infoStyle;
        private bool _stylesInitialized;

        // 0GC: Pre-allocated StringBuilder and cached strings
        private readonly StringBuilder _sb = new StringBuilder(256);
        private string _windowTitle;
        private static readonly GUIContent _sightHeader = new GUIContent("SIGHT");
        private static readonly GUIContent _hearingHeader = new GUIContent("HEARING");
        private static readonly GUIContent _noTargets = new GUIContent("    (No targets)");
        private static readonly GUIContent _noSounds = new GUIContent("    (No sounds)");
        private static readonly GUIContent _toggleInfo = new GUIContent("Toggle via Inspector");

        public SightSensor SightSensor => _sightSensor;
        public HearingSensor HearingSensor => _hearingSensor;

        public bool HasSightDetection => _sightSensor?.HasDetection ?? false;
        public bool HasHearingDetection => _hearingSensor?.HasDetection ?? false;
        public bool HasAnyDetection => HasSightDetection || HasHearingDetection;

        public int SightDetectedCount => _sightSensor?.DetectedCount ?? 0;
        public int HearingDetectedCount => _hearingSensor?.DetectedCount ?? 0;

        public bool ShowDebugOverlay
        {
            get => _showDebugOverlay;
            set => _showDebugOverlay = value;
        }

        private void OnEnable()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) return;

            // Ensure PerceptionManager exists to drive updates
            _ = PerceptionManagerComponent.Instance;

            if (_enableSight)
            {
                _sightSensor = new SightSensor(transform, _sightConfig);
                SensorManager.Instance?.Register(_sightSensor);
            }

            if (_enableHearing)
            {
                _hearingSensor = new HearingSensor(transform, _hearingConfig);
                SensorManager.Instance?.Register(_hearingSensor);
            }

            // Cache window title (0GC after init)
            _windowTitle = $"AI Perception - {gameObject.name}";

            _initialized = true;
        }

        protected virtual void Update()
        {
            // Debug toggle handled via Inspector button
        }

        private void OnDisable()
        {
            if (_sightSensor != null && SensorManager.HasInstance)
            {
                SensorManager.Instance.Unregister(_sightSensor);
                _sightSensor.Dispose();
                _sightSensor = null;
            }

            if (_hearingSensor != null && SensorManager.HasInstance)
            {
                SensorManager.Instance.Unregister(_hearingSensor);
                _hearingSensor.Dispose();
                _hearingSensor = null;
            }

            _initialized = false;
        }

        public IPerceptible GetClosestSightTarget()
        {
            if (_sightSensor == null || !_sightSensor.HasDetection) return null;

            DetectionResult closest = default;
            float closestDist = float.MaxValue;

            for (int i = 0; i < _sightSensor.DetectedCount; i++)
            {
                var result = _sightSensor.GetResult(i);
                if (result.Distance < closestDist)
                {
                    closestDist = result.Distance;
                    closest = result;
                }
            }

            return PerceptibleRegistry.Instance?.Get(closest.Target);
        }

        public IPerceptible GetClosestHearingTarget()
        {
            if (_hearingSensor == null || !_hearingSensor.HasDetection) return null;

            DetectionResult closest = default;
            float closestDist = float.MaxValue;

            for (int i = 0; i < _hearingSensor.DetectedCount; i++)
            {
                var result = _hearingSensor.GetResult(i);
                if (result.Distance < closestDist)
                {
                    closestDist = result.Distance;
                    closest = result;
                }
            }

            return PerceptibleRegistry.Instance?.Get(closest.Target);
        }

        private void OnGUI()
        {
            if (!_showDebugOverlay || !Application.isPlaying) return;

            InitStyles();

            _debugWindowRect = GUILayout.Window(
                GetInstanceID(),
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
            _headerStyle.normal.textColor = new Color(0.9f, 0.9f, 0.3f);

            _detectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11
            };

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

            // Sight section
            if (_enableSight)
            {
                GUILayout.Label(_sightHeader, _headerStyle);

                // 0GC: Use StringBuilder
                _sb.Clear();
                _sb.Append("  Enabled: ");
                _sb.Append(_sightSensor?.IsEnabled ?? false);
                GUILayout.Label(_sb.ToString());

                _sb.Clear();
                _sb.Append("  Detected: ");
                _sb.Append(SightDetectedCount);
                GUILayout.Label(_sb.ToString());

                if (_sightSensor != null && _sightSensor.HasDetection)
                {
                    for (int i = 0; i < _sightSensor.DetectedCount; i++)
                    {
                        var result = _sightSensor.GetResult(i);
                        var perceptible = PerceptibleRegistry.Instance?.Get(result.Target);

                        _detectionStyle.normal.textColor = Color.green;
                        _sb.Clear();
                        _sb.Append("    ► ");
                        if (perceptible != null)
                        {
                            var pc = perceptible as PerceptibleComponent;
                            _sb.Append(pc != null ? pc.gameObject.name : "Unknown");
                            _sb.Append(" (");
                            _sb.Append(PerceptibleTypes.GetTypeName(perceptible.PerceptibleTypeId));
                            _sb.Append(")");
                        }
                        else
                        {
                            _sb.Append("Invalid");
                        }
                        GUILayout.Label(_sb.ToString(), _detectionStyle);

                        _detectionStyle.normal.textColor = Color.white;
                        _sb.Clear();
                        _sb.Append("      Dist: ");
                        _sb.Append(result.Distance.ToString("F1"));
                        _sb.Append("m  Vis: ");
                        _sb.Append((result.Visibility * 100f).ToString("F0"));
                        _sb.Append("%");
                        GUILayout.Label(_sb.ToString());
                    }
                }
                else
                {
                    GUILayout.Label(_noTargets);
                }

                GUILayout.Space(8);
            }

            // Hearing section
            if (_enableHearing)
            {
                GUILayout.Label(_hearingHeader, _headerStyle);

                _sb.Clear();
                _sb.Append("  Enabled: ");
                _sb.Append(_hearingSensor?.IsEnabled ?? false);
                GUILayout.Label(_sb.ToString());

                _sb.Clear();
                _sb.Append("  Detected: ");
                _sb.Append(HearingDetectedCount);
                GUILayout.Label(_sb.ToString());

                _sb.Clear();
                _sb.Append("  Occlusion: ");
                _sb.Append(_hearingConfig.UseOcclusion);
                GUILayout.Label(_sb.ToString());

                if (_hearingSensor != null && _hearingSensor.HasDetection)
                {
                    for (int i = 0; i < _hearingSensor.DetectedCount; i++)
                    {
                        var result = _hearingSensor.GetResult(i);
                        var perceptible = PerceptibleRegistry.Instance?.Get(result.Target);

                        _detectionStyle.normal.textColor = new Color(0.5f, 1f, 0.8f);
                        _sb.Clear();
                        _sb.Append("    ♪ ");
                        if (perceptible != null)
                        {
                            var pc = perceptible as PerceptibleComponent;
                            _sb.Append(pc != null ? pc.gameObject.name : "Unknown");
                        }
                        else
                        {
                            _sb.Append("Invalid");
                        }
                        GUILayout.Label(_sb.ToString(), _detectionStyle);

                        _detectionStyle.normal.textColor = Color.white;
                        _sb.Clear();
                        _sb.Append("      Dist: ");
                        _sb.Append(result.Distance.ToString("F1"));
                        _sb.Append("m  Vol: ");
                        _sb.Append((result.Visibility * 100f).ToString("F0"));
                        _sb.Append("%");
                        GUILayout.Label(_sb.ToString());
                    }
                }
                else
                {
                    GUILayout.Label(_noSounds);
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
            DrawSightGizmo();
            DrawHearingGizmo();
        }

        private void DrawSightGizmo()
        {
            if (!_enableSight) return;

            var pos = transform.position;
            var forward = transform.forward;
            var right = transform.right;
            var up = transform.up;

            float halfAngle = _sightConfig.HalfAngle;
            float distance = _sightConfig.MaxDistance;

            bool hasDetection = _sightSensor?.HasDetection ?? false;
            Gizmos.color = hasDetection
                ? new Color(0f, 1f, 0f, 0.3f)
                : new Color(1f, 0.8f, 0f, 0.2f);

            int segments = 32;
            float angleStep = 360f / segments;

            for (int i = 0; i < segments; i++)
            {
                float angle1 = i * angleStep * Mathf.Deg2Rad;
                float angle2 = (i + 1) * angleStep * Mathf.Deg2Rad;

                Vector3 dir1 = Quaternion.AngleAxis(halfAngle, Mathf.Cos(angle1) * right + Mathf.Sin(angle1) * up) * forward;
                Vector3 dir2 = Quaternion.AngleAxis(halfAngle, Mathf.Cos(angle2) * right + Mathf.Sin(angle2) * up) * forward;

                Vector3 p1 = pos + dir1 * distance;
                Vector3 p2 = pos + dir2 * distance;

                Gizmos.DrawLine(pos, p1);
                Gizmos.DrawLine(p1, p2);
            }

            // Semi-transparent horizontal cone disc
            UnityEditor.Handles.color = hasDetection
                ? new Color(1f, 0.9f, 0.2f, 0.12f)
                : new Color(1f, 0.8f, 0.2f, 0.06f);

            Vector3 coneStart = Quaternion.AngleAxis(-halfAngle, up) * forward;
            UnityEditor.Handles.DrawSolidArc(pos, up, coneStart, halfAngle * 2f, distance);

            UnityEditor.Handles.color = new Color(1f, 0.9f, 0.3f, 0.5f);
            Vector3 leftEdge = Quaternion.AngleAxis(-halfAngle, up) * forward * distance;
            Vector3 rightEdge = Quaternion.AngleAxis(halfAngle, up) * forward * distance;
            UnityEditor.Handles.DrawLine(pos, pos + leftEdge);
            UnityEditor.Handles.DrawLine(pos, pos + rightEdge);
            UnityEditor.Handles.DrawWireArc(pos, up, coneStart, halfAngle * 2f, distance);

            Gizmos.color = new Color(1f, 1f, 0f, 0.8f);
            Gizmos.DrawLine(pos, pos + forward * distance);

            if (_sightSensor != null)
            {
                Gizmos.color = Color.green;
                for (int i = 0; i < _sightSensor.DetectedCount; i++)
                {
                    var result = _sightSensor.GetResult(i);
                    Gizmos.DrawWireSphere(result.LastKnownPosition, 0.3f);
                    Gizmos.DrawLine(pos, result.LastKnownPosition);
                }
            }
        }

        private void DrawHearingGizmo()
        {
            if (!_enableHearing) return;

            var pos = transform.position;
            float radius = _hearingConfig.Radius;

            bool hasDetection = _hearingSensor?.HasDetection ?? false;

            Gizmos.color = hasDetection
                ? new Color(0.2f, 0.8f, 1f, 0.25f)
                : new Color(0.3f, 0.5f, 1f, 0.15f);
            Gizmos.DrawWireSphere(pos, radius);

            UnityEditor.Handles.color = hasDetection
                ? new Color(0.2f, 0.8f, 1f, 0.08f)
                : new Color(0.3f, 0.5f, 1f, 0.05f);
            UnityEditor.Handles.DrawSolidDisc(pos, Vector3.up, radius);

            for (float r = 0.25f; r < 1f; r += 0.25f)
            {
                float alpha = 0.1f * (1f - r);
                Gizmos.color = new Color(0.3f, 0.5f, 1f, alpha);
                Gizmos.DrawWireSphere(pos, radius * r);
            }

            UnityEditor.Handles.color = new Color(0.4f, 0.6f, 1f, 0.3f);
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f;
                Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                UnityEditor.Handles.DrawWireArc(pos, Vector3.up, dir, 45f, radius);
            }

            if (_hearingSensor != null)
            {
                for (int i = 0; i < _hearingSensor.DetectedCount; i++)
                {
                    var result = _hearingSensor.GetResult(i);

                    float intensity = result.Visibility;
                    Gizmos.color = new Color(0f, 1f, 0.7f, 0.5f + intensity * 0.5f);
                    Gizmos.DrawWireSphere(result.LastKnownPosition, 0.25f);

                    Vector3 toTarget = (Vector3)result.LastKnownPosition - pos;
                    float dashLen = 0.5f;
                    int dashCount = Mathf.FloorToInt(toTarget.magnitude / dashLen);
                    for (int d = 0; d < dashCount; d += 2)
                    {
                        float t1 = d / (float)dashCount;
                        float t2 = Mathf.Min((d + 1) / (float)dashCount, 1f);
                        Gizmos.DrawLine(
                            pos + toTarget * t1,
                            pos + toTarget * t2
                        );
                    }
                }
            }
        }
#endif
    }
}
