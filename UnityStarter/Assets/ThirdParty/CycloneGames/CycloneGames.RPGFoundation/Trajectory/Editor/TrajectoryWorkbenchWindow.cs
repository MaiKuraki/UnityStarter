using System;
using System.Diagnostics;
using CycloneGames.RPGFoundation.Trajectory.Core;
using CycloneGames.Compat;
using CycloneGames.Logging;
using CycloneGames.RPGFoundation.Trajectory.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Trajectory.Editor
{
    /// <summary>
    /// Scene-wide authoring workbench for trajectory probes.
    ///
    /// Complements the per-component inspector: this window lists every probe in the open scenes,
    /// shows the recorded segments and hits as tables, times a full re-trace across all probes, and
    /// applies preset shapes. The scan is manual so nothing runs per editor frame.
    /// </summary>
    public sealed class TrajectoryWorkbenchWindow : EditorWindow
    {
        private TrajectoryDebugProbe[] _probes = Array.Empty<TrajectoryDebugProbe>();
        private string[] _probeLabels = Array.Empty<string>();
        private int _selectedIndex = -1;
        private Vector2 _probeScroll;
        private Vector2 _detailScroll;
        private bool _showSegments = true;
        private bool _showHits = true;
        private bool _showSettings = true;

        // A trace is recomputed only when its inputs can have changed. OnGUI runs for Layout,
        // Repaint and every input event, so tracing there would issue a physics query per event.
        private TrajectoryDebugProbe _cachedProbe;
        private TrajectoryTraceResult _cachedResult;
        private bool _hasCachedTrace;

        private double _lastTraceAllMs;
        private int _lastTraceAllCount;
        private double _lastTraceAllMaxMs;
        private readonly TrajectoryCurvePreviewPanel _curvePanel = new TrajectoryCurvePreviewPanel();

        private static readonly LogChannel Log = RpgTrajectoryEditorLog.Channel;

        [MenuItem("Tools/CycloneGames/Trajectory/Workbench")]
        private static void Open()
        {
            var window = GetWindow<TrajectoryWorkbenchWindow>("Trajectory Workbench");
            window.minSize = new Vector2(520f, 420f);
            window.ScanProbes();
        }

        private void OnEnable()
        {
            ScanProbes();
            SceneView.duringSceneGui += _curvePanel.DrawScene;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= _curvePanel.DrawScene;
        }

        private void ScanProbes()
        {
            // Both the scene query and the identity token go through the package-local shim so the
            // EntityId / InstanceID split stays in one place across Unity versions.
            _probes = UnityObjectQuery.FindAll<TrajectoryDebugProbe>(includeInactive: false);
            _probeLabels = new string[_probes.Length];
            for (int i = 0; i < _probes.Length; i++)
            {
                _probeLabels[i] = BuildProbeLabel(_probes[i]);
            }

            if (_selectedIndex >= _probes.Length)
            {
                _selectedIndex = _probes.Length - 1;
            }

            InvalidateTraceCache();
        }

        /// <summary>
        /// Drops the cached trace. Called only when the inputs can have changed (scene scan,
        /// selection change, preset edit, explicit re-trace), so OnGUI never issues a physics query
        /// on every repaint.
        /// </summary>
        private void InvalidateTraceCache()
        {
            _hasCachedTrace = false;
            _cachedProbe = null;
        }

        /// <summary>
        /// Builds "Parent/Child #A1B2" so probes that share a name stay distinguishable. Only the low
        /// 16 bits of the identity are shown: the full token is unreadable and, on 2022 LTS, a negative
        /// instance id renders as a 16-digit hex blob.
        /// </summary>
        private static string BuildProbeLabel(TrajectoryDebugProbe probe)
        {
            if (probe == null)
            {
                return "<missing>";
            }

            Transform transform = probe.transform;
            var builder = new System.Text.StringBuilder(transform.name);
            Transform parent = transform.parent;
            while (parent != null && builder.Length < 64)
            {
                builder.Insert(0, parent.name + "/");
                parent = parent.parent;
            }

            ulong id = UnityObjectId.Of(probe);
            builder.Append(" #").Append((id & 0xFFFFUL).ToString("X4"));
            return builder.ToString();
        }

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space(2f);

            if (_probes.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No TrajectoryDebugProbe in the open scenes. Add the component to a GameObject, then press Scan.",
                    MessageType.Info);
                return;
            }

            DrawProbeList();

            if (_selectedIndex < 0 || _selectedIndex >= _probes.Length)
            {
                return;
            }

            TrajectoryDebugProbe probe = _probes[_selectedIndex];
            if (probe == null)
            {
                return;
            }

            // Anchor the curve preview to the probe the workbench is showing. Without this the
            // preview sat at the world origin and looked like a second, unexplained trace.
            _curvePanel.SetAnchor(probe.Origin);

            EditorGUILayout.Space(2f);
            DrawProbeActions(probe);
            EditorGUILayout.Space(2f);

            _showSettings = EditorGUILayout.Foldout(_showSettings, "Scene view display", true);
            if (_showSettings)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    TrajectoryGizmoSettings.DrawMenuItems();

                    EditorGUILayout.LabelField(
                        "Settings file: " + TrajectoryGizmoSettings.SettingsFileRelativePath,
                        EditorStyles.miniLabel);
                    string settingsError = TrajectoryGizmoSettings.LastLoadError;
                    if (!string.IsNullOrEmpty(settingsError))
                    {
                        EditorGUILayout.HelpBox("Settings file error: " + settingsError, MessageType.Warning);
                    }

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Reload from file", EditorStyles.miniButton))
                    {
                        TrajectoryGizmoSettings.ReloadFromDisk();
                    }

                    if (GUILayout.Button("Reset to defaults", EditorStyles.miniButton))
                    {
                        TrajectoryGizmoSettings.ResetToDefaults();
                    }

                    if (GUILayout.Button("Reveal file", EditorStyles.miniButton))
                    {
                        EditorUtility.RevealInFinder(TrajectoryGizmoSettings.SettingsFilePath);
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space(2f);
            DrawTraceDetails(probe);

            EditorGUILayout.Space(2f);
            _curvePanel.DrawPanel();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Scan", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            {
                ScanProbes();
            }

            if (GUILayout.Button("Trace All", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                TraceAll();
            }

            GUILayout.FlexibleSpace();

            if (_lastTraceAllCount > 0)
            {
                GUILayout.Label(
                    "last: " + _lastTraceAllCount + " probes, sum " + _lastTraceAllMs.ToString("F3")
                    + " ms, max " + _lastTraceAllMaxMs.ToString("F3") + " ms",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawProbeList()
        {
            EditorGUILayout.LabelField("Probes (" + _probes.Length + ")", EditorStyles.boldLabel);

            // Fixed height left a large empty band when only one probe exists; size to content with
            // a ceiling so a big scene still scrolls.
            float listHeight = Mathf.Clamp(_probes.Length * 20f + 8f, 48f, 160f);
            _probeScroll = EditorGUILayout.BeginScrollView(_probeScroll, GUILayout.Height(listHeight));
            for (int i = 0; i < _probes.Length; i++)
            {
                bool selected = i == _selectedIndex;
                if (GUILayout.Toggle(selected, _probeLabels[i], EditorStyles.miniButton) != selected)
                {
                    _selectedIndex = i;
                    InvalidateTraceCache();
                    if (_probes[i] != null)
                    {
                        Selection.activeGameObject = _probes[i].gameObject;
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawProbeActions(TrajectoryDebugProbe probe)
        {
            EditorGUILayout.LabelField("Selected: " + probe.name, EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Select in Hierarchy"))
            {
                Selection.activeGameObject = probe.gameObject;
            }

            using (new EditorGUI.DisabledScope(probe.Preset == null))
            {
                if (GUILayout.Button("Hitscan")) ApplyPreset(probe, 60f, TrajectoryShape.Ray, 0f, 0f, 0, 0, 1, 1);
                if (GUILayout.Button("Ricochet")) ApplyPreset(probe, 80f, TrajectoryShape.Sphere, 0.03f, 0f, 4, 0, 8, 8);
                if (GUILayout.Button("Piercing")) ApplyPreset(probe, 70f, TrajectoryShape.Sphere, 0.04f, 0f, 0, 8, 12, 12);
                if (GUILayout.Button("Capsule")) ApplyPreset(probe, 40f, TrajectoryShape.Capsule, 0.3f, 1.8f, 0, 4, 16, 16);
                if (GUILayout.Button("Box")) ApplyPreset(probe, 40f, TrajectoryShape.Box, 0f, 0f, 0, 4, 16, 16);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "query: " + DescribeQuery(probe),
                EditorStyles.miniLabel);
        }

        private static string DescribeQuery(TrajectoryDebugProbe probe)
        {
            TrajectoryQueryPresetAsset preset = probe.Preset;
            if (preset == null)
            {
                return "<no preset assigned>";
            }

            var text = new System.Text.StringBuilder();
            text.Append("mask=").Append(preset.CollisionMask)
                .Append(" dist=").Append(preset.Distance.ToString("F2"))
                .Append(" shape=").Append(preset.QueryShape);

            switch (preset.QueryShape)
            {
                case TrajectoryShape.Sphere:
                    text.Append(" radius=").Append(preset.RadiusValue.ToString("F3"));
                    break;
                case TrajectoryShape.Capsule:
                    text.Append(" radius=").Append(preset.RadiusValue.ToString("F3"))
                        .Append(" height=").Append(preset.HeightValue.ToString("F2"));
                    break;
                case TrajectoryShape.Box:
                    TrajectoryVector3 half = preset.HalfExtentsValue;
                    text.Append(" half=(")
                        .Append(half.X.ToString("F2")).Append(',')
                        .Append(half.Y.ToString("F2")).Append(',')
                        .Append(half.Z.ToString("F2")).Append(')');
                    break;
            }

            TrajectoryTraceBuffer buffer = probe.Buffer;
            text.Append("  seg=").Append(buffer.SegmentCount).Append('/').Append(buffer.SegmentCapacity)
                .Append(" hit=").Append(buffer.HitCount).Append('/').Append(buffer.HitCapacity)
                .Append("  reflect=").Append(probe.ReflectionMask.value != 0 ? "on" : "off")
                .Append(" pierce=").Append(probe.PierceMask.value != 0 ? "on" : "off");

            return text.ToString();
        }

        private void DrawTraceDetails(TrajectoryDebugProbe probe)
        {
            if (!_hasCachedTrace || _cachedProbe != probe)
            {
                if (!probe.TryTrace(out _cachedResult))
                {
                    _hasCachedTrace = false;
                    EditorGUILayout.HelpBox("Trace failed: assign a preset and make sure the query is valid.", MessageType.Warning);
                    return;
                }

                _cachedProbe = probe;
                _hasCachedTrace = true;
            }

            TrajectoryTraceResult result = _cachedResult;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Trace details (cached)", EditorStyles.miniLabel);
            if (GUILayout.Button("Re-trace selected", EditorStyles.miniButton))
            {
                InvalidateTraceCache();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Flags", result.Flags.ToString());
            EditorGUILayout.LabelField(
                "Segments " + result.SegmentCount
                + "   Hits " + result.HitCount
                + "   Travel " + result.TravelDistance.ToString("F3")
                + "   Nearest " + result.NearestHitIndex);

            if (result.Flags != TrajectoryTraceFlags.None)
            {
                EditorGUILayout.HelpBox(ExplainFlags(result.Flags), MessageType.None);
            }

            _showSegments = EditorGUILayout.Foldout(_showSegments, "Segments", true);
            if (_showSegments)
            {
                DrawSegmentTable(probe);
            }

            _showHits = EditorGUILayout.Foldout(_showHits, "Hits", true);
            if (_showHits)
            {
                DrawHitTable(probe);
            }
        }

        private void DrawSegmentTable(TrajectoryDebugProbe probe)
        {
            TrajectoryTraceBuffer buffer = probe.Buffer;
            if (buffer.SegmentCount == 0)
            {
                EditorGUILayout.LabelField("(none)", EditorStyles.miniLabel);
                return;
            }

            float segmentHeight = Mathf.Clamp(buffer.SegmentCount * 18f + 8f, 26f, 140f);
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, GUILayout.Height(segmentHeight));
            for (int i = 0; i < buffer.SegmentCount; i++)
            {
                TrajectorySegment segment = buffer.GetSegment(i);
                EditorGUILayout.LabelField(
                    "#" + segment.Index
                    + "  hit=" + segment.HitIndex
                    + "  d=" + segment.Distance.ToString("F3")
                    + "  to=(" + segment.To.X.ToString("F2") + "," + segment.To.Y.ToString("F2") + "," + segment.To.Z.ToString("F2") + ")",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHitTable(TrajectoryDebugProbe probe)
        {
            TrajectoryTraceBuffer buffer = probe.Buffer;
            if (buffer.HitCount == 0)
            {
                EditorGUILayout.LabelField("(none)", EditorStyles.miniLabel);
                return;
            }

            float hitHeight = Mathf.Clamp(buffer.HitCount * 18f + 8f, 26f, 140f);
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, GUILayout.Height(hitHeight));
            for (int i = 0; i < buffer.HitCount; i++)
            {
                TrajectoryHit hit = buffer.GetHit(i);
                EditorGUILayout.LabelField(
                    "#" + i
                    + "  seg=" + hit.SegmentIndex
                    + "  d=" + hit.Distance.ToString("F3")
                    + "  " + hit.Response
                    + "  obj=" + hit.TargetObjectId
                    + "  ent=" + hit.TargetEntityId,
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        private static string ExplainFlags(TrajectoryTraceFlags flags)
        {
            var text = new System.Text.StringBuilder();
            AppendFlag(text, flags, TrajectoryTraceFlags.InvalidQuery, "query rejected by IsValid");
            AppendFlag(text, flags, TrajectoryTraceFlags.DegenerateDirection, "direction collapsed to zero length");
            AppendFlag(text, flags, TrajectoryTraceFlags.MissingCollisionWorld, "no collision world; drew the unblocked path");
            AppendFlag(text, flags, TrajectoryTraceFlags.HitCapacityReached, "hit buffer full; raise HitCapacity");
            AppendFlag(text, flags, TrajectoryTraceFlags.SegmentCapacityReached, "segment buffer full; raise SegmentCapacity");
            AppendFlag(text, flags, TrajectoryTraceFlags.IterationLimitReached, "iteration budget consumed; raise MaxIterationCount");
            AppendFlag(text, flags, TrajectoryTraceFlags.MaxHitCountReached, "MaxHitCount reached; the trace stopped early");
            return text.ToString();
        }

        private static void AppendFlag(
            System.Text.StringBuilder text,
            TrajectoryTraceFlags flags,
            TrajectoryTraceFlags flag,
            string meaning)
        {
            if ((flags & flag) != 0)
            {
                text.Append("- ").Append(meaning).Append('\n');
            }
        }

        private void TraceAll()
        {
            ScanProbes();

            if (_probes.Length == 0)
            {
                _lastTraceAllCount = 0;
                return;
            }

            double sum = 0;
            double max = 0;
            int traced = 0;

            for (int i = 0; i < _probes.Length; i++)
            {
                TrajectoryDebugProbe probe = _probes[i];
                if (probe == null)
                {
                    continue;
                }

                long start = Stopwatch.GetTimestamp();
                bool ok = probe.TryTrace(out _);
                long elapsed = Stopwatch.GetTimestamp() - start;
                double ms = elapsed * 1000.0 / Stopwatch.Frequency;

                if (!ok)
                {
                    continue;
                }

                traced++;
                sum += ms;
                if (ms > max)
                {
                    max = ms;
                }
            }

            _lastTraceAllCount = traced;
            _lastTraceAllMs = sum;
            _lastTraceAllMaxMs = max;
            Log.Info("[Trajectory Workbench] traced " + traced + " probes, sum " + sum.ToString("F3")
                     + " ms, max " + max.ToString("F3") + " ms");

            InvalidateTraceCache();
        }

        private void ApplyPreset(
            TrajectoryDebugProbe probe,
            float maxDistance,
            TrajectoryShape shape,
            float radius,
            float height,
            int reflectionCount,
            int pierceCount,
            int hitCount,
            int iterationCount)
        {
            TrajectoryQueryPresetAsset preset = probe.Preset;
            if (preset == null)
            {
                return;
            }

            var serialized = new SerializedObject(preset);
            Undo.RecordObject(preset, "Apply Trajectory Preset");

            SetFloat(serialized, "MaxDistance", maxDistance);
            SetInt(serialized, "Shape", (int)shape);
            SetFloat(serialized, "Radius", radius);
            SetFloat(serialized, "Height", height);
            SetInt(serialized, "MaxReflectionCount", reflectionCount);
            SetInt(serialized, "MaxPierceCount", pierceCount);
            SetInt(serialized, "MaxHitCount", hitCount);
            SetInt(serialized, "MaxIterationCount", iterationCount);
            SetFloat(serialized, "SurfaceOffset", TrajectoryQuery.DEFAULT_SURFACE_OFFSET);

            SerializedProperty layerMask = serialized.FindProperty("CollisionLayerMask");
            if (layerMask != null)
            {
                layerMask.intValue = -1;
            }

            SerializedProperty halfExtents = serialized.FindProperty("HalfExtents");
            if (halfExtents != null)
            {
                halfExtents.vector3Value = new Vector3(0.5f, 0.5f, 0.5f);
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(preset);
            InvalidateTraceCache();
        }

        private static void SetFloat(SerializedObject serialized, string propertyName, float value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetInt(SerializedObject serialized, string propertyName, int value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                return;
            }

            if (property.propertyType == SerializedPropertyType.Enum)
            {
                property.enumValueIndex = value;
            }
            else
            {
                property.intValue = value;
            }
        }
    }
}
