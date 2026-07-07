using System.Collections.Generic;
using System.Globalization;
using CycloneGames.Choreography.Core;
using UnityEditor;
using UnityEngine;
using Handles = UnityEditor.Handles;

namespace CycloneGames.Choreography.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="ChoreographyAsset"/>. It keeps all data edits on the SerializedProperty path,
    /// while presenting the asset as a montage-style choreography workspace: asset overview, section order,
    /// section/track/clip/event timeline, selected element details, validation, and raw serialized fallback.
    /// </summary>
    [CustomEditor(typeof(ChoreographyAsset))]
    [CanEditMultipleObjects]
    public sealed class ChoreographyAssetEditor : UnityEditor.Editor
    {
        private static readonly GUIContent RebuildButton = new GUIContent(
            "Rebuild Runtime Model", "Rebuilds the cached engine-free runtime model from the authored data.");
        private static readonly GUIContent AddSectionButton = new GUIContent(
            "Add Section", "Adds a new choreography section at the end of the asset.");
        private static readonly GUIContent AddTrackButton = new GUIContent(
            "Add Track", "Adds a new track to the selected section.");
        private static readonly GUIContent AddClipButton = new GUIContent(
            "Add Clip", "Adds a new clip to the selected track.");
        private static readonly GUIContent AddEventButton = new GUIContent(
            "Add Event", "Adds a new timing event to the selected section.");
        private static readonly GUIContent AddEventStateButton = new GUIContent(
            "Add State", "Adds a duration-spanning event state to the selected section.");
        private static readonly GUIContent DeleteButton = new GUIContent(
            "Delete", "Deletes the selected element.");
        private static readonly GUIContent AdvancedFoldout = new GUIContent(
            "Advanced", "Fallback tools for inspecting or bulk-editing the serialized authoring model.");
        private static readonly GUIContent ResourceAssetKeyMode = new GUIContent(
            "Asset Key", "Use a loader-agnostic Location/Guid key compatible with AssetManagement-style references.");
        private static readonly GUIContent ResourceLocationMode = new GUIContent(
            "Location", "Use a raw provider location string for custom loaders or external banks.");
        private static readonly GUIContent ResourceBackendCueMode = new GUIContent(
            "Backend Cue", "Use a backend-owned cue such as a Wwise event or CycloneGames.Audio event.");
        private static readonly GUIContent ResourceAssetLabel = new GUIContent(
            "Asset", "Drag an asset here. The asset key stores its GUID and location path.");
        private static readonly GUIContent ResourceLocationLabel = new GUIContent(
            "Location", "Provider location key used when Asset Key is not active.");
        private const float PreviewTimelineHeight = 50f;
        private const float PreviewRulerHeight = 19f;
        private const float PreviewSectionStripHeight = 24f;
        private const float PreviewButtonWidth = 28f;
        private const float PreviewButtonHeight = 22f;

        private static readonly float[] PreviewTimeSteps =
        {
            0.05f, 0.1f, 0.2f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 15f, 30f, 60f, 120f, 300f
        };

        private static readonly int[] PreviewFrameSteps =
        {
            1, 2, 5, 10, 15, 30, 60, 120, 240, 600, 1200, 2400, 6000
        };

        private static readonly Vector3[] PreviewPlayheadTriangle = new Vector3[3];

        private static GUIContent _previewPlayButton;
        private static GUIContent _previewPauseButton;
        private static GUIContent _previewStopButton;
        private static GUIContent _previewStepBackButton;
        private static GUIContent _previewStepForwardButton;
        private static GUIStyle _previewRulerLabelStyle;
        private static GUIStyle _previewSectionLabelStyle;
        private static GUIStyle _previewReadoutStyle;
        private static bool _previewUiReady;

        private readonly List<string> _diagnostics = new List<string>(16);
        private readonly HashSet<string> _sectionIds = new HashSet<string>();
        private readonly HashSet<string> _trackIds = new HashSet<string>();
        private readonly HashSet<string> _clipIds = new HashSet<string>();
        private readonly List<IChoreographyPreviewTargetFactory> _previewFactories = new List<IChoreographyPreviewTargetFactory>(8);
        private readonly GUIContent _previewLabel = new GUIContent();

        private SerializedProperty _assetId;
        private SerializedProperty _sections;
        private ChoreographyTimelineView _timeline;
        private ChoreographyPreviewSession _previewSession;
        private Object _previewContext;
        private string[] _previewFactoryLabels = new string[0];
        private int _previewFactoryIndex;
        private bool _showAdvanced;
        private bool _showRawData;
        private bool _showDiagnostics = true;
        private bool _showSectionOrder = true;
        private bool _draggingPreviewTimeline;

        private void OnEnable()
        {
            _assetId = serializedObject.FindProperty("AssetId");
            _sections = serializedObject.FindProperty("Sections");
            _timeline = new ChoreographyTimelineView();
            _previewSession = new ChoreographyPreviewSession();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            if (_previewSession != null)
            {
                _previewSession.Dispose();
                _previewSession = null;
            }
        }

        private void OnEditorUpdate()
        {
            if (_previewSession == null || !_previewSession.IsPlaying)
            {
                return;
            }

            bool wasPlaying = _previewSession.IsPlaying;
            bool changed = _previewSession.Tick(EditorApplication.timeSinceStartup);
            if (_previewSession.IsPlaying)
            {
                EditorApplication.QueuePlayerLoopUpdate();
            }

            if (changed || wasPlaying != _previewSession.IsPlaying)
            {
                Repaint();
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.PropertyField(_assetId);
                EditorGUILayout.PropertyField(_sections, true);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            AuthoringStats stats = BuildStatsAndDiagnostics();
            DrawAssetHeader(stats);
            DrawSectionOrder();
            DrawPreviewTransport(stats);
            DrawWorkspace(stats);
            DrawSelectedDetails();
            DrawDiagnostics();
            DrawAdvanced();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawAssetHeader(AuthoringStats stats)
        {
            InspectorUi.DrawHeroHeader(
                "Choreography Asset",
                "Section, track, clip, and event authoring for reusable skill presentation.",
                InspectorUi.MainAccent);

            using (InspectorUi.PanelScope())
            {
                EditorGUILayout.PropertyField(_assetId);
                EditorGUILayout.Space(3f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawStat("Sections", stats.SectionCount.ToString(), InspectorUi.MainAccent);
                    DrawStat("Tracks", stats.TrackCount.ToString(), InspectorUi.BlueAccent);
                    DrawStat("Clips", stats.ClipCount.ToString(), InspectorUi.GreenAccent);
                    DrawStat("Events", stats.EventCount.ToString(), InspectorUi.WarningAccent);
                    DrawStat("States", stats.EventStateCount.ToString(), InspectorUi.RedAccent);
                    DrawStat("Duration", stats.TotalDuration.ToString("0.###") + "s", InspectorUi.NeutralAccent);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(AddSectionButton, EditorStyles.miniButtonLeft, GUILayout.Width(104f)))
                    {
                        AddSection();
                    }

                    if (GUILayout.Button(RebuildButton, EditorStyles.miniButtonRight, GUILayout.Width(160f)))
                    {
                        RebuildRuntimeModel();
                    }

                    GUILayout.FlexibleSpace();
                }
            }
        }

        private static void DrawStat(string label, string value, Color accent)
        {
            InspectorUi.DrawMetricCard(label, value, accent, GUILayout.MinWidth(72f));
        }

        private void DrawSectionOrder()
        {
            string badge = _sections != null ? _sections.arraySize.ToString() + " sections" : "0 sections";
            _showSectionOrder = InspectorUi.DrawFoldoutPanelHeader(
                "Section Order",
                "Runtime section sequence and dominant playback mode.",
                _showSectionOrder,
                InspectorUi.MainAccent,
                badge);

            if (!_showSectionOrder)
            {
                return;
            }

            if (_sections == null || _sections.arraySize == 0)
            {
                using (InspectorUi.PanelScope())
                {
                    EditorGUILayout.HelpBox("No sections authored.", MessageType.None);
                }
                return;
            }

            using (InspectorUi.PanelScope())
            {
                for (int i = 0; i < _sections.arraySize; i++)
                {
                    SerializedProperty section = _sections.GetArrayElementAtIndex(i);
                    string id = GetString(section, "Id");
                    if (string.IsNullOrEmpty(id))
                    {
                        id = "Section " + i;
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(i.ToString("00"), EditorStyles.miniLabel, GUILayout.Width(24f));
                        if (GUILayout.Button(id, EditorStyles.miniButton, GUILayout.MinWidth(80f)))
                        {
                            _timeline.SetSelection(ChoreographyTimelineSelection.Section(i));
                        }

                        GUILayout.Label(GetFloat(section, "Duration").ToString("0.###") + "s", EditorStyles.miniLabel, GUILayout.Width(52f));
                        GUILayout.Label(GetEnumName(section, "PreferredMode"), EditorStyles.miniLabel, GUILayout.Width(72f));
                        if (!GetBool(section, "Interruptible"))
                        {
                            GUILayout.Label("Locked", EditorStyles.miniBoldLabel, GUILayout.Width(48f));
                        }
                        GUILayout.FlexibleSpace();
                    }
                }
            }
        }

        private void DrawPreviewTransport(AuthoringStats stats)
        {
            EnsurePreviewUi();

            ChoreographyAsset asset = target as ChoreographyAsset;
            RefreshPreviewFactories(asset);
            IChoreographyPreviewTargetFactory factory = _previewFactories.Count > 0
                ? _previewFactories[Mathf.Clamp(_previewFactoryIndex, 0, _previewFactories.Count - 1)]
                : null;
            _previewSession.Bind(asset, factory, _previewContext);
            double duration = Mathf.Max(0f, (float)stats.TotalDuration);
            HandlePreviewKeyboardShortcuts(duration);

            InspectorUi.DrawPanelHeader(
                "Preview",
                "Backend-neutral preview transport for time or frame driven playback.",
                InspectorUi.WarningAccent);

            using (InspectorUi.PanelScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(duration <= 0d))
                    {
                        if (!_previewSession.IsPlaying)
                        {
                            if (GUILayout.Button(_previewPlayButton, EditorStyles.miniButtonLeft, GUILayout.Width(PreviewButtonWidth), GUILayout.Height(PreviewButtonHeight)))
                            {
                                TogglePreviewPlayback();
                            }
                        }
                        else if (GUILayout.Button(_previewPauseButton, EditorStyles.miniButtonLeft, GUILayout.Width(PreviewButtonWidth), GUILayout.Height(PreviewButtonHeight)))
                        {
                            TogglePreviewPlayback();
                        }

                        if (GUILayout.Button(_previewStopButton, EditorStyles.miniButtonMid, GUILayout.Width(PreviewButtonWidth), GUILayout.Height(PreviewButtonHeight)))
                        {
                            _previewSession.Stop();
                            RequestPreviewEditorUpdate();
                        }

                        if (GUILayout.Button(_previewStepBackButton, EditorStyles.miniButtonMid, GUILayout.Width(PreviewButtonWidth), GUILayout.Height(PreviewButtonHeight)))
                        {
                            _previewSession.Step(-1);
                            RequestPreviewEditorUpdate();
                        }

                        if (GUILayout.Button(_previewStepForwardButton, EditorStyles.miniButtonRight, GUILayout.Width(PreviewButtonWidth), GUILayout.Height(PreviewButtonHeight)))
                        {
                            _previewSession.Step(1);
                            RequestPreviewEditorUpdate();
                        }
                    }

                    GUILayout.Space(4f);
                    EditorGUI.BeginChangeCheck();
                    ChoreographyPreviewDriverMode mode = (ChoreographyPreviewDriverMode)EditorGUILayout.EnumPopup(
                        _previewSession.DriverMode,
                        GUILayout.Width(88f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        _previewSession.DriverMode = mode;
                        _previewSession.SetTime(_previewSession.CurrentTime);
                        RequestPreviewEditorUpdate();
                    }

                    if (_previewSession.DriverMode == ChoreographyPreviewDriverMode.Frames)
                    {
                        GUILayout.Label("FPS", EditorStyles.miniLabel, GUILayout.Width(24f));
                        EditorGUI.BeginChangeCheck();
                        double frameRate = Mathf.Max(1f, EditorGUILayout.FloatField((float)_previewSession.FrameRate, GUILayout.Width(48f)));
                        if (EditorGUI.EndChangeCheck())
                        {
                            _previewSession.FrameRate = frameRate;
                            _previewSession.SetTime(_previewSession.CurrentTime);
                            RequestPreviewEditorUpdate();
                        }
                    }

                    GUILayout.Label("Speed", EditorStyles.miniLabel, GUILayout.Width(38f));
                    EditorGUI.BeginChangeCheck();
                    float speed = EditorGUILayout.Slider(_previewSession.Speed, 0.05f, 3f, GUILayout.MinWidth(110f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        _previewSession.Speed = speed;
                        RequestPreviewEditorUpdate();
                    }

                    GUILayout.Label(FormatPreviewReadout(duration), _previewReadoutStyle, GUILayout.Width(132f));
                }

                Rect timelineRect = EditorGUILayout.GetControlRect(false, PreviewTimelineHeight);
                DrawPreviewTimeline(timelineRect, _sections, duration);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Backend", GUILayout.Width(58f));
                    EditorGUI.BeginChangeCheck();
                    _previewFactoryIndex = EditorGUILayout.Popup(_previewFactoryIndex, _previewFactoryLabels, GUILayout.MinWidth(110f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        IChoreographyPreviewTargetFactory nextFactory = _previewFactories.Count > 0
                            ? _previewFactories[Mathf.Clamp(_previewFactoryIndex, 0, _previewFactories.Count - 1)]
                            : null;
                        _previewSession.Bind(asset, nextFactory, _previewContext);
                        RequestPreviewEditorUpdate();
                    }

                    EditorGUILayout.LabelField("Target", GUILayout.Width(44f));
                    EditorGUI.BeginChangeCheck();
                    _previewContext = EditorGUILayout.ObjectField(_previewContext, typeof(Object), true, GUILayout.MinWidth(120f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        RefreshPreviewFactories(asset);
                        IChoreographyPreviewTargetFactory nextFactory = _previewFactories.Count > 0
                            ? _previewFactories[Mathf.Clamp(_previewFactoryIndex, 0, _previewFactories.Count - 1)]
                            : null;
                        _previewSession.Bind(asset, nextFactory, _previewContext);
                        RequestPreviewEditorUpdate();
                    }

                    GUILayout.Label(_previewSession.TargetName, EditorStyles.miniLabel, GUILayout.Width(84f));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (_previewSession.DriverMode == ChoreographyPreviewDriverMode.Frames)
                    {
                        int maxFrame = Mathf.Max(0, _previewSession.GetMaxFrame());
                        int frame = Mathf.Clamp(_previewSession.GetCurrentFrame(), 0, maxFrame);
                        EditorGUI.BeginChangeCheck();
                        frame = EditorGUILayout.IntField("Frame", frame);
                        if (EditorGUI.EndChangeCheck())
                        {
                            _previewSession.SetTime(Mathf.Clamp(frame, 0, maxFrame) / Mathf.Max(1f, (float)_previewSession.FrameRate));
                            RequestPreviewEditorUpdate();
                        }
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        float time = EditorGUILayout.FloatField("Time", (float)_previewSession.CurrentTime);
                        if (EditorGUI.EndChangeCheck())
                        {
                            _previewSession.SetTime(Mathf.Clamp(time, 0f, (float)duration));
                            RequestPreviewEditorUpdate();
                        }
                    }

                    GUILayout.FlexibleSpace();
                }
            }
        }

        private void HandlePreviewKeyboardShortcuts(double duration)
        {
            Event evt = Event.current;
            if (evt == null
                || evt.type != EventType.KeyDown
                || evt.keyCode != KeyCode.Space
                || duration <= 0d
                || _previewSession == null
                || serializedObject.isEditingMultipleObjects
                || EditorGUIUtility.editingTextField
                || GUIUtility.hotControl != 0
                || evt.alt
                || evt.control
                || evt.command)
            {
                return;
            }

            TogglePreviewPlayback();
            evt.Use();
        }

        private void TogglePreviewPlayback()
        {
            if (_previewSession == null)
            {
                return;
            }

            if (_previewSession.IsPlaying)
            {
                _previewSession.Pause();
            }
            else
            {
                double duration = _previewSession.GetDuration();
                if (duration > 0d && _previewSession.CurrentTime >= duration - 0.000001d)
                {
                    _previewSession.SetTime(0d);
                }

                _previewSession.Play(EditorApplication.timeSinceStartup);
            }

            RequestPreviewEditorUpdate();
        }

        private void RequestPreviewEditorUpdate()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            Repaint();
        }

        private void DrawPreviewTimeline(Rect rect, SerializedProperty sections, double duration)
        {
            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.105f, 0.105f, 0.105f, 1f)
                : new Color(0.695f, 0.695f, 0.695f, 1f);
            Color rulerBackground = EditorGUIUtility.isProSkin
                ? new Color(0.070f, 0.070f, 0.070f, 1f)
                : new Color(0.560f, 0.560f, 0.560f, 1f);
            Color stripBackground = EditorGUIUtility.isProSkin
                ? new Color(0.145f, 0.145f, 0.145f, 1f)
                : new Color(0.760f, 0.760f, 0.760f, 1f);

            EditorGUI.DrawRect(rect, background);
            Rect rulerRect = new Rect(rect.x, rect.y, rect.width, PreviewRulerHeight);
            Rect sectionRect = new Rect(rect.x, rulerRect.yMax, rect.width, PreviewSectionStripHeight);
            EditorGUI.DrawRect(rulerRect, rulerBackground);
            EditorGUI.DrawRect(sectionRect, stripBackground);

            if (duration <= 0d)
            {
                _previewLabel.text = "No preview duration";
                GUI.Label(new Rect(rect.x + 6f, rect.y + 17f, rect.width - 12f, 16f), _previewLabel, _previewReadoutStyle);
                DrawPreviewOutline(rect, new Color(0f, 0f, 0f, 0.42f), 1f);
                return;
            }

            HandlePreviewTimelineInput(rect, duration);
            DrawPreviewSections(sectionRect, sections, duration);
            DrawPreviewTicks(rect, duration);
            DrawPreviewPlayhead(rect, duration);
            DrawPreviewOutline(rect, new Color(0f, 0f, 0f, 0.42f), 1f);
        }

        private void HandlePreviewTimelineInput(Rect rect, double duration)
        {
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
            {
                _draggingPreviewTimeline = true;
                SetPreviewTimeFromMouse(rect, evt.mousePosition.x, duration);
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && _draggingPreviewTimeline)
            {
                SetPreviewTimeFromMouse(rect, evt.mousePosition.x, duration);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && _draggingPreviewTimeline)
            {
                _draggingPreviewTimeline = false;
                evt.Use();
            }
        }

        private void SetPreviewTimeFromMouse(Rect rect, float mouseX, double duration)
        {
            float normalized = Mathf.InverseLerp(rect.x, rect.xMax, Mathf.Clamp(mouseX, rect.x, rect.xMax));
            double nextTime = duration * normalized;
            if (_previewSession.DriverMode == ChoreographyPreviewDriverMode.Frames)
            {
                double frameRate = System.Math.Max(1d, _previewSession.FrameRate);
                nextTime = System.Math.Round(nextTime * frameRate) / frameRate;
            }

            _previewSession.SetTime(nextTime);
            RequestPreviewEditorUpdate();
        }

        private void DrawPreviewSections(Rect rect, SerializedProperty sections, double duration)
        {
            if (sections == null || sections.arraySize == 0)
            {
                return;
            }

            double cursor = 0d;
            for (int i = 0; i < sections.arraySize; i++)
            {
                SerializedProperty section = sections.GetArrayElementAtIndex(i);
                double sectionDuration = System.Math.Max(0d, GetFloat(section, "Duration"));
                if (sectionDuration <= 0d)
                {
                    continue;
                }

                float x = PreviewTimeToX(rect, cursor, duration);
                float width = PreviewTimeToX(rect, cursor + sectionDuration, duration) - x;
                Rect bandRect = new Rect(x, rect.y + 3f, Mathf.Max(1f, width), rect.height - 6f);
                Color bandColor = Color.Lerp(InspectorUi.MainAccent, InspectorUi.BlueAccent, i % 2 == 0 ? 0f : 0.45f);
                bandColor.a = 0.36f;
                EditorGUI.DrawRect(bandRect, bandColor);
                EditorGUI.DrawRect(new Rect(x, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.18f));

                if (width > 54f)
                {
                    string id = GetString(section, "Id");
                    _previewLabel.text = string.IsNullOrEmpty(id) ? "Section " + i : id;
                    GUI.Label(new Rect(bandRect.x + 6f, bandRect.y + 3f, bandRect.width - 12f, 16f), _previewLabel, _previewSectionLabelStyle);
                }

                cursor += sectionDuration;
            }
        }

        private void DrawPreviewTicks(Rect rect, double duration)
        {
            if (_previewSession.DriverMode == ChoreographyPreviewDriverMode.Frames)
            {
                DrawPreviewFrameTicks(rect, duration);
            }
            else
            {
                DrawPreviewTimeTicks(rect, duration);
            }
        }

        private void DrawPreviewTimeTicks(Rect rect, double duration)
        {
            float step = PickPreviewTimeStep(duration, rect.width);
            float minorStep = Mathf.Max(0.001f, step / 5f);
            int tickCount = Mathf.Min(4096, Mathf.CeilToInt((float)(duration / minorStep)) + 1);
            int decimals = step < 1f ? 2 : (step < 10f ? 1 : 0);

            for (int i = 0; i <= tickCount; i++)
            {
                double time = i * minorStep;
                if (time > duration + 0.0001d)
                {
                    break;
                }

                bool major = i % 5 == 0;
                DrawPreviewTick(rect, time, duration, major);
                if (major)
                {
                    _previewLabel.text = FormatPreviewSeconds(time, decimals);
                    GUI.Label(new Rect(PreviewTimeToX(rect, time, duration) + 4f, rect.y + 2f, 58f, 15f), _previewLabel, _previewRulerLabelStyle);
                }
            }
        }

        private void DrawPreviewFrameTicks(Rect rect, double duration)
        {
            double frameRate = System.Math.Max(1d, _previewSession.FrameRate);
            int maxFrame = Mathf.Max(0, _previewSession.GetMaxFrame());
            int majorStep = PickPreviewFrameStep(maxFrame, rect.width);
            int minorStep = Mathf.Max(1, majorStep / 5);

            for (int frame = 0; frame <= maxFrame; frame += minorStep)
            {
                double time = frame / frameRate;
                bool major = frame % majorStep == 0;
                DrawPreviewTick(rect, time, duration, major);
                if (major)
                {
                    _previewLabel.text = frame.ToString(CultureInfo.InvariantCulture) + "f";
                    GUI.Label(new Rect(PreviewTimeToX(rect, time, duration) + 4f, rect.y + 2f, 58f, 15f), _previewLabel, _previewRulerLabelStyle);
                }
            }
        }

        private void DrawPreviewTick(Rect rect, double time, double duration, bool major)
        {
            float x = PreviewTimeToX(rect, time, duration);
            float height = major ? 14f : 7f;
            Color tickColor = major ? new Color(1f, 1f, 1f, 0.32f) : new Color(1f, 1f, 1f, 0.14f);
            Color gridColor = major ? new Color(1f, 1f, 1f, 0.085f) : new Color(1f, 1f, 1f, 0.040f);
            EditorGUI.DrawRect(new Rect(x, rect.y + PreviewRulerHeight - height, 1f, height), tickColor);
            EditorGUI.DrawRect(new Rect(x, rect.y + PreviewRulerHeight, 1f, rect.height - PreviewRulerHeight), gridColor);
        }

        private void DrawPreviewPlayhead(Rect rect, double duration)
        {
            float x = PreviewTimeToX(rect, _previewSession.CurrentTime, duration);
            Color playheadColor = new Color(1f, 0.282f, 0.239f, 1f);
            Color progressColor = InspectorUi.WarningAccent;
            progressColor.a = 0.36f;

            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 4f, Mathf.Max(0f, x - rect.x), 3f), progressColor);
            EditorGUI.DrawRect(new Rect(x - 1f, rect.y, 2f, rect.height), playheadColor);

            PreviewPlayheadTriangle[0] = new Vector3(x, rect.y + 1f, 0f);
            PreviewPlayheadTriangle[1] = new Vector3(x - 5f, rect.y + 9f, 0f);
            PreviewPlayheadTriangle[2] = new Vector3(x + 5f, rect.y + 9f, 0f);
            Color oldHandlesColor = Handles.color;
            Handles.color = playheadColor;
            Handles.DrawAAConvexPolygon(PreviewPlayheadTriangle);
            Handles.color = oldHandlesColor;

            string readout = FormatPreviewCurrentValue();
            float labelWidth = Mathf.Min(84f, Mathf.Max(50f, readout.Length * 7f));
            float labelX = Mathf.Clamp(x + 6f, rect.x + 4f, rect.xMax - labelWidth - 4f);
            Rect labelRect = new Rect(labelX, rect.y + 2f, labelWidth, 15f);
            EditorGUI.DrawRect(labelRect, new Color(0f, 0f, 0f, 0.52f));
            _previewLabel.text = readout;
            GUI.Label(labelRect, _previewLabel, _previewReadoutStyle);
        }

        private float PreviewTimeToX(Rect rect, double time, double duration)
        {
            if (duration <= 0d)
            {
                return rect.x;
            }

            return rect.x + Mathf.Clamp01((float)(time / duration)) * rect.width;
        }

        private string FormatPreviewReadout(double duration)
        {
            if (_previewSession.DriverMode == ChoreographyPreviewDriverMode.Frames)
            {
                return "Frame " + _previewSession.GetCurrentFrame().ToString(CultureInfo.InvariantCulture)
                    + " / " + _previewSession.GetMaxFrame().ToString(CultureInfo.InvariantCulture);
            }

            return FormatPreviewSeconds(_previewSession.CurrentTime, 3)
                + " / " + FormatPreviewSeconds(duration, 3);
        }

        private string FormatPreviewCurrentValue()
        {
            if (_previewSession.DriverMode == ChoreographyPreviewDriverMode.Frames)
            {
                return _previewSession.GetCurrentFrame().ToString(CultureInfo.InvariantCulture) + "f";
            }

            return FormatPreviewSeconds(_previewSession.CurrentTime, 3);
        }

        private static string FormatPreviewSeconds(double value, int decimals)
        {
            string format = decimals <= 0 ? "0" : "0." + new string('0', decimals);
            return value.ToString(format, CultureInfo.InvariantCulture) + "s";
        }

        private static float PickPreviewTimeStep(double duration, float width)
        {
            float target = (float)(duration / Mathf.Max(1f, width / 84f));
            for (int i = 0; i < PreviewTimeSteps.Length; i++)
            {
                if (PreviewTimeSteps[i] >= target)
                {
                    return PreviewTimeSteps[i];
                }
            }

            return PreviewTimeSteps[PreviewTimeSteps.Length - 1];
        }

        private static int PickPreviewFrameStep(int maxFrame, float width)
        {
            float target = maxFrame / Mathf.Max(1f, width / 84f);
            for (int i = 0; i < PreviewFrameSteps.Length; i++)
            {
                if (PreviewFrameSteps[i] >= target)
                {
                    return PreviewFrameSteps[i];
                }
            }

            return PreviewFrameSteps[PreviewFrameSteps.Length - 1];
        }

        private static void DrawPreviewOutline(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static void EnsurePreviewUi()
        {
            if (_previewUiReady)
            {
                return;
            }

            _previewPlayButton = CreatePreviewIconContent(
                "Play",
                "Starts preview playback using the selected preview driver.",
                "PlayButton",
                "d_PlayButton",
                "Animation.Play",
                "d_Animation.Play");
            _previewPauseButton = CreatePreviewIconContent(
                "Pause",
                "Pauses preview playback.",
                "PauseButton",
                "d_PauseButton",
                "Animation.Pause",
                "d_Animation.Pause");
            _previewStopButton = CreatePreviewIconContent(
                "Stop",
                "Stops preview playback and rewinds to the beginning.",
                "PreMatQuad",
                "d_PreMatQuad",
                "Grid.BoxTool",
                "d_Grid.BoxTool",
                "Animation.Stop",
                "d_Animation.Stop",
                "Profiler.StopRecord",
                "d_Profiler.StopRecord");
            _previewStepBackButton = CreatePreviewIconContent(
                "Prev",
                "Steps one preview frame backward.",
                "Animation.PrevKey",
                "d_Animation.PrevKey",
                "Animation.FirstKey",
                "d_Animation.FirstKey");
            _previewStepForwardButton = CreatePreviewIconContent(
                "Next",
                "Steps one preview frame forward.",
                "StepButton",
                "d_StepButton",
                "Animation.NextKey",
                "d_Animation.NextKey");

            _previewRulerLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.82f, 0.82f, 0.82f, 1f) : new Color(0.20f, 0.20f, 0.20f, 1f) }
            };
            _previewSectionLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white }
            };
            _previewReadoutStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                normal = { textColor = EditorGUIUtility.isProSkin ? Color.white : new Color(0.14f, 0.14f, 0.14f, 1f) }
            };

            _previewUiReady = true;
        }

        private static GUIContent CreatePreviewIconContent(string fallbackText, string tooltip, params string[] iconNames)
        {
            for (int i = 0; i < iconNames.Length; i++)
            {
                GUIContent content = EditorGUIUtility.IconContent(iconNames[i]);
                if (content != null && content.image != null)
                {
                    return new GUIContent(content.image, tooltip);
                }
            }

            return new GUIContent(fallbackText, tooltip);
        }

        private void RefreshPreviewFactories(ChoreographyAsset asset)
        {
            ChoreographyPreviewRegistry.CollectFactories(asset, _previewContext, _previewFactories);
            if (_previewFactories.Count == 0)
            {
                _previewFactoryLabels = new string[] { "None" };
                _previewFactoryIndex = 0;
                return;
            }

            if (_previewFactoryLabels.Length != _previewFactories.Count)
            {
                _previewFactoryLabels = new string[_previewFactories.Count];
            }

            for (int i = 0; i < _previewFactories.Count; i++)
            {
                _previewFactoryLabels[i] = _previewFactories[i].DisplayName;
            }

            if (_previewFactoryIndex < 0 || _previewFactoryIndex >= _previewFactories.Count)
            {
                _previewFactoryIndex = 0;
            }
        }

        private void DrawWorkspace(AuthoringStats stats)
        {
            InspectorUi.DrawPanelHeader(
                "Choreography Workspace",
                "Montage-style section, track, clip, and timing visualization.",
                InspectorUi.BlueAccent);

            using (InspectorUi.PanelScope())
            {
                _timeline.SetPreviewTime(_previewSession != null ? _previewSession.CurrentTime : 0d, stats.TotalDuration > 0d);
                _timeline.Draw(_sections);
                double scrubTime;
                if (_previewSession != null && _timeline.TryConsumePreviewTimeRequest(out scrubTime))
                {
                    _previewSession.SetTime(scrubTime);
                    Repaint();
                }
            }
        }

        private void DrawSelectedDetails()
        {
            InspectorUi.DrawPanelHeader(
                "Selection Details",
                "Focused SerializedProperty editing for the selected timeline element.",
                InspectorUi.GreenAccent);

            ChoreographyTimelineSelection selection = _timeline.Selection;
            if (!selection.IsValid)
            {
                using (InspectorUi.PanelScope())
                {
                    EditorGUILayout.HelpBox("Select a section, track, clip, event, or state in the workspace.", MessageType.None);
                }
                return;
            }

            switch (selection.Kind)
            {
                case ChoreographyTimelineElementKind.Section:
                    DrawSectionDetails(selection.SectionIndex);
                    break;
                case ChoreographyTimelineElementKind.Track:
                    DrawTrackDetails(selection.SectionIndex, selection.TrackIndex);
                    break;
                case ChoreographyTimelineElementKind.Clip:
                    DrawClipDetails(selection.SectionIndex, selection.TrackIndex, selection.ClipIndex);
                    break;
                case ChoreographyTimelineElementKind.Event:
                    DrawEventDetails(selection.SectionIndex, selection.EventIndex);
                    break;
                case ChoreographyTimelineElementKind.EventState:
                    DrawEventStateDetails(selection.SectionIndex, selection.EventStateIndex);
                    break;
            }
        }

        private void DrawSectionDetails(int sectionIndex)
        {
            SerializedProperty section = GetSection(sectionIndex);
            if (section == null)
            {
                _timeline.ClearSelection();
                return;
            }

            using (InspectorUi.PanelScope())
            {
                EditorGUILayout.PropertyField(section.FindPropertyRelative("Id"));
                EditorGUILayout.PropertyField(section.FindPropertyRelative("Duration"));
                EditorGUILayout.PropertyField(section.FindPropertyRelative("Interruptible"));
                EditorGUILayout.PropertyField(section.FindPropertyRelative("PreferredMode"));
                EditorGUILayout.PropertyField(section.FindPropertyRelative("ClockSource"));
                SerializedProperty clockSource = section.FindPropertyRelative("ClockSource");
                ChoreographySectionClockSource source = clockSource != null
                    ? (ChoreographySectionClockSource)clockSource.enumValueIndex
                    : ChoreographySectionClockSource.Inherit;
                if (source == ChoreographySectionClockSource.FixedFrame)
                {
                    EditorGUILayout.PropertyField(section.FindPropertyRelative("FrameRate"));
                }
                if (source == ChoreographySectionClockSource.Audio
                    || source == ChoreographySectionClockSource.Animation
                    || source == ChoreographySectionClockSource.Timeline
                    || source == ChoreographySectionClockSource.External)
                {
                    EditorGUILayout.PropertyField(section.FindPropertyRelative("ExternalEndPolicy"));
                }
                SerializedProperty states = section.FindPropertyRelative("EventStates");
                EditorGUILayout.LabelField("Event States", states != null ? states.arraySize.ToString() : "0");

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(AddTrackButton, EditorStyles.miniButtonLeft))
                    {
                        AddTrack(sectionIndex);
                    }
                    if (GUILayout.Button(AddEventButton, EditorStyles.miniButtonMid))
                    {
                        AddEvent(sectionIndex);
                    }
                    if (GUILayout.Button(AddEventStateButton, EditorStyles.miniButtonMid))
                    {
                        AddEventState(sectionIndex);
                    }
                    if (GUILayout.Button(DeleteButton, EditorStyles.miniButtonRight, GUILayout.Width(64f)))
                    {
                        DeleteSection(sectionIndex);
                    }
                }
            }
        }

        private void DrawTrackDetails(int sectionIndex, int trackIndex)
        {
            SerializedProperty track = GetTrack(sectionIndex, trackIndex);
            if (track == null)
            {
                _timeline.ClearSelection();
                return;
            }

            SerializedProperty clips = track.FindPropertyRelative("Clips");
            using (InspectorUi.PanelScope())
            {
                EditorGUILayout.PropertyField(track.FindPropertyRelative("Id"));
                EditorGUILayout.PropertyField(track.FindPropertyRelative("Kind"));
                EditorGUILayout.LabelField("Clips", clips != null ? clips.arraySize.ToString() : "0");

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(AddClipButton, EditorStyles.miniButtonLeft))
                    {
                        AddClip(sectionIndex, trackIndex);
                    }
                    if (GUILayout.Button(DeleteButton, EditorStyles.miniButtonRight, GUILayout.Width(64f)))
                    {
                        DeleteTrack(sectionIndex, trackIndex);
                    }
                }
            }
        }

        private void DrawClipDetails(int sectionIndex, int trackIndex, int clipIndex)
        {
            SerializedProperty clip = GetClip(sectionIndex, trackIndex, clipIndex);
            if (clip == null)
            {
                _timeline.ClearSelection();
                return;
            }

            using (InspectorUi.PanelScope())
            {
                EditorGUILayout.PropertyField(clip.FindPropertyRelative("Id"));
                DrawResourceDetails(clip.FindPropertyRelative("Resource"));
                EditorGUILayout.PropertyField(clip.FindPropertyRelative("StartTime"));
                EditorGUILayout.PropertyField(clip.FindPropertyRelative("Duration"));
                EditorGUILayout.PropertyField(clip.FindPropertyRelative("Weight"));
                EditorGUILayout.PropertyField(clip.FindPropertyRelative("Channel"));
                SerializedProperty loop = clip.FindPropertyRelative("Loop");
                EditorGUILayout.PropertyField(loop);
                if (loop != null && loop.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "Loop keeps this clip active until the owning section ends. Local time repeats by Duration; EndClip is sent when the section or playback stops.",
                        MessageType.Info);
                }

                if (GUILayout.Button(DeleteButton, EditorStyles.miniButton, GUILayout.Width(64f)))
                {
                    DeleteClip(sectionIndex, trackIndex, clipIndex);
                }
            }
        }

        private void DrawResourceDetails(SerializedProperty resource)
        {
            if (resource == null)
            {
                return;
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Resource", EditorStyles.boldLabel);

            SerializedProperty source = resource.FindPropertyRelative("Source");
            SerializedProperty asset = resource.FindPropertyRelative("Asset");
            SerializedProperty address = resource.FindPropertyRelative("Address");
            SerializedProperty backend = resource.FindPropertyRelative("Backend");
            SerializedProperty bank = resource.FindPropertyRelative("Bank");
            SerializedProperty cue = resource.FindPropertyRelative("Cue");
            SerializedProperty kind = resource.FindPropertyRelative("Kind");
            SerializedProperty tag = resource.FindPropertyRelative("Tag");

            ChoreographyResourceSource currentSource = GetResourceSource(source);
            DrawResourceSourceButtons(source, asset, address, cue, currentSource);

            currentSource = GetResourceSource(source);
            if (currentSource == ChoreographyResourceSource.AssetKey)
            {
                DrawAssetKeyResourceField(asset);
            }
            else if (currentSource == ChoreographyResourceSource.BackendCue)
            {
                DrawBackendCueResourceField(backend, bank, cue);
            }
            else
            {
                DrawLocationResourceField(address);
            }

            EditorGUILayout.PropertyField(kind);
            EditorGUILayout.PropertyField(tag);
        }

        private void DrawResourceSourceButtons(
            SerializedProperty source,
            SerializedProperty asset,
            SerializedProperty address,
            SerializedProperty cue,
            ChoreographyResourceSource currentSource)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Source", GUILayout.Width(72f));

            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = currentSource == ChoreographyResourceSource.AssetKey
                ? InspectorUi.BlueAccent
                : new Color(0.50f, 0.50f, 0.50f, 0.70f);
            if (GUILayout.Button(ResourceAssetKeyMode, EditorStyles.miniButtonLeft))
            {
                SwitchResourceSource(source, asset, address, cue, ChoreographyResourceSource.AssetKey);
            }

            GUI.backgroundColor = currentSource == ChoreographyResourceSource.Location
                ? InspectorUi.WarningAccent
                : new Color(0.50f, 0.50f, 0.50f, 0.70f);
            if (GUILayout.Button(ResourceLocationMode, EditorStyles.miniButtonMid))
            {
                SwitchResourceSource(source, asset, address, cue, ChoreographyResourceSource.Location);
            }

            GUI.backgroundColor = currentSource == ChoreographyResourceSource.BackendCue
                ? InspectorUi.CyanAccent
                : new Color(0.50f, 0.50f, 0.50f, 0.70f);
            if (GUILayout.Button(ResourceBackendCueMode, EditorStyles.miniButtonRight))
            {
                SwitchResourceSource(source, asset, address, cue, ChoreographyResourceSource.BackendCue);
            }

            GUI.backgroundColor = previous;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAssetKeyResourceField(SerializedProperty asset)
        {
            Rect row = EditorGUILayout.GetControlRect();
            Rect labelRect = new Rect(row.x, row.y, 72f, row.height);
            Rect fieldRect = new Rect(row.x + 72f, row.y, row.width - 144f, row.height);
            Rect pingRect = new Rect(row.xMax - 66f, row.y, 66f, row.height);

            EditorGUI.LabelField(labelRect, ResourceAssetLabel);
            Object currentAsset = ResolveAssetKeyObject(asset);
            EditorGUI.BeginChangeCheck();
            Object selectedAsset = EditorGUI.ObjectField(fieldRect, GUIContent.none, currentAsset, typeof(Object), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (selectedAsset == null)
                {
                    ClearAssetKey(asset);
                }
                else
                {
                    SetAssetKeyFromObject(asset, selectedAsset);
                }
            }

            string location = GetAssetKeyLocation(asset);
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(location));
            if (GUI.Button(pingRect, "Ping", EditorStyles.miniButton))
            {
                PingAssetKey(asset);
            }
            EditorGUI.EndDisabledGroup();

            if (string.IsNullOrEmpty(location))
            {
                EditorGUILayout.HelpBox(
                    "Drag an asset into the Asset field. The asset key stores GUID and path; a resource provider decides how to load it.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Path", GUILayout.Width(72f));
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(location);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawLocationResourceField(SerializedProperty address)
        {
            EditorGUILayout.PropertyField(address, ResourceLocationLabel);
            if (address == null || string.IsNullOrEmpty(address.stringValue))
            {
                EditorGUILayout.HelpBox(
                    "Location is empty. Leave it empty only for pure marker clips; otherwise enter the provider key used by your resource loader.",
                    MessageType.Info);
            }
        }

        private static void DrawBackendCueResourceField(
            SerializedProperty backend,
            SerializedProperty bank,
            SerializedProperty cue)
        {
            EditorGUILayout.PropertyField(backend);
            EditorGUILayout.PropertyField(bank);
            EditorGUILayout.PropertyField(cue);

            if (backend == null || string.IsNullOrEmpty(backend.stringValue) || cue == null || string.IsNullOrEmpty(cue.stringValue))
            {
                EditorGUILayout.HelpBox(
                    "Backend Cue requires at least Backend and Cue. Use it for Wwise events, CycloneGames.Audio event names, audio bank events, or other non-Unity-object handles.",
                    MessageType.Info);
            }
        }

        private void DrawEventDetails(int sectionIndex, int eventIndex)
        {
            SerializedProperty evt = GetEvent(sectionIndex, eventIndex);
            if (evt == null)
            {
                _timeline.ClearSelection();
                return;
            }

            using (InspectorUi.PanelScope())
            {
                EditorGUILayout.PropertyField(evt.FindPropertyRelative("EventId"));
                EditorGUILayout.PropertyField(evt.FindPropertyRelative("Time"));
                EditorGUILayout.PropertyField(evt.FindPropertyRelative("Magnitude"));
                EditorGUILayout.PropertyField(evt.FindPropertyRelative("IntPayload"));
                EditorGUILayout.PropertyField(evt.FindPropertyRelative("StringPayload"));

                if (GUILayout.Button(DeleteButton, EditorStyles.miniButton, GUILayout.Width(64f)))
                {
                    DeleteEvent(sectionIndex, eventIndex);
                }
            }
        }

        private void DrawEventStateDetails(int sectionIndex, int eventStateIndex)
        {
            SerializedProperty state = GetEventState(sectionIndex, eventStateIndex);
            if (state == null)
            {
                _timeline.ClearSelection();
                return;
            }

            using (InspectorUi.PanelScope())
            {
                EditorGUILayout.PropertyField(state.FindPropertyRelative("Id"));
                EditorGUILayout.PropertyField(state.FindPropertyRelative("EventId"));
                EditorGUILayout.PropertyField(state.FindPropertyRelative("StartTime"));
                EditorGUILayout.PropertyField(state.FindPropertyRelative("EndTime"));
                EditorGUILayout.PropertyField(state.FindPropertyRelative("Magnitude"));
                EditorGUILayout.PropertyField(state.FindPropertyRelative("IntPayload"));
                EditorGUILayout.PropertyField(state.FindPropertyRelative("StringPayload"));

                if (GUILayout.Button(DeleteButton, EditorStyles.miniButton, GUILayout.Width(64f)))
                {
                    DeleteEventState(sectionIndex, eventStateIndex);
                }
            }
        }

        private void DrawDiagnostics()
        {
            _showDiagnostics = InspectorUi.DrawFoldoutPanelHeader(
                "Authoring Diagnostics",
                "Validation hints for ids, timings, and section boundaries.",
                _showDiagnostics,
                _diagnostics.Count == 0 ? InspectorUi.GreenAccent : InspectorUi.WarningAccent,
                _diagnostics.Count == 0 ? "Clean" : _diagnostics.Count + " issues");
            if (!_showDiagnostics)
            {
                return;
            }

            using (InspectorUi.PanelScope())
            {
                if (_diagnostics.Count == 0)
                {
                    EditorGUILayout.HelpBox("No authoring issues detected.", MessageType.None);
                    return;
                }

                int visibleCount = Mathf.Min(_diagnostics.Count, 6);
                for (int i = 0; i < visibleCount; i++)
                {
                    EditorGUILayout.HelpBox(_diagnostics[i], MessageType.Warning);
                }

                if (_diagnostics.Count > visibleCount)
                {
                    EditorGUILayout.LabelField((_diagnostics.Count - visibleCount) + " more issue(s).", EditorStyles.miniLabel);
                }
            }
        }

        private void DrawAdvanced()
        {
            _showAdvanced = InspectorUi.DrawFoldoutPanelHeader(
                AdvancedFoldout.text,
                "Raw serialized fallback for uncommon fields and bulk edits.",
                _showAdvanced,
                InspectorUi.NeutralAccent,
                _showRawData ? "Raw visible" : "Optional");
            if (!_showAdvanced)
            {
                return;
            }

            using (InspectorUi.PanelScope())
            {
                EditorGUILayout.HelpBox(
                    "The workspace and selection details are the primary editor. Use the raw serialized tree only for bulk edits or fields that are not exposed by the focused details panel.",
                    MessageType.None);

                _showRawData = EditorGUILayout.ToggleLeft("Show full serialized field tree", _showRawData);
                if (_showRawData)
                {
                    EditorGUILayout.PropertyField(_sections, true);
                }
            }
        }

        private AuthoringStats BuildStatsAndDiagnostics()
        {
            _diagnostics.Clear();
            _sectionIds.Clear();

            AuthoringStats stats = new AuthoringStats();
            if (_sections == null)
            {
                return stats;
            }

            stats.SectionCount = _sections.arraySize;
            for (int s = 0; s < _sections.arraySize; s++)
            {
                SerializedProperty section = _sections.GetArrayElementAtIndex(s);
                string sectionId = GetString(section, "Id");
                float sectionDuration = GetFloat(section, "Duration");
                stats.TotalDuration += Mathf.Max(0f, sectionDuration);

                if (string.IsNullOrEmpty(sectionId))
                {
                    AddDiagnostic("Section " + s + " has no id.");
                }
                else if (!_sectionIds.Add(sectionId))
                {
                    AddDiagnostic("Section '" + sectionId + "' is duplicated.");
                }

                if (sectionDuration <= 0f)
                {
                    AddDiagnostic("Section '" + DisplayId(sectionId, s) + "' has a non-positive duration.");
                }

                ValidateTracks(section, s, sectionId, sectionDuration, ref stats);
                ValidateEvents(section, s, sectionId, sectionDuration, ref stats);
                ValidateEventStates(section, s, sectionId, sectionDuration, ref stats);
            }

            return stats;
        }

        private void ValidateTracks(SerializedProperty section, int sectionIndex, string sectionId, float sectionDuration, ref AuthoringStats stats)
        {
            _trackIds.Clear();
            SerializedProperty tracks = section.FindPropertyRelative("Tracks");
            if (tracks == null)
            {
                return;
            }

            stats.TrackCount += tracks.arraySize;
            for (int t = 0; t < tracks.arraySize; t++)
            {
                SerializedProperty track = tracks.GetArrayElementAtIndex(t);
                string trackId = GetString(track, "Id");
                if (string.IsNullOrEmpty(trackId))
                {
                    AddDiagnostic("Track " + t + " in section '" + DisplayId(sectionId, sectionIndex) + "' has no id.");
                }
                else if (!_trackIds.Add(GetEnumName(track, "Kind") + "/" + trackId))
                {
                    AddDiagnostic("Track '" + trackId + "' is duplicated in section '" + DisplayId(sectionId, sectionIndex) + "'.");
                }

                ValidateClips(track, sectionIndex, sectionId, t, trackId, sectionDuration, ref stats);
            }
        }

        private void ValidateClips(
            SerializedProperty track,
            int sectionIndex,
            string sectionId,
            int trackIndex,
            string trackId,
            float sectionDuration,
            ref AuthoringStats stats)
        {
            _clipIds.Clear();
            SerializedProperty clips = track.FindPropertyRelative("Clips");
            if (clips == null)
            {
                return;
            }

            stats.ClipCount += clips.arraySize;
            for (int c = 0; c < clips.arraySize; c++)
            {
                SerializedProperty clip = clips.GetArrayElementAtIndex(c);
                string clipId = GetString(clip, "Id");
                float start = GetFloat(clip, "StartTime");
                float duration = GetFloat(clip, "Duration");
                bool loop = GetBool(clip, "Loop");

                if (string.IsNullOrEmpty(clipId))
                {
                    AddDiagnostic("Clip " + c + " in track '" + DisplayId(trackId, trackIndex) + "' has no id.");
                }
                else if (!_clipIds.Add(clipId))
                {
                    AddDiagnostic("Clip '" + clipId + "' is duplicated in track '" + DisplayId(trackId, trackIndex) + "'.");
                }

                if (start < 0f)
                {
                    AddDiagnostic("Clip '" + DisplayId(clipId, c) + "' starts before its section.");
                }

                if (duration < 0f)
                {
                    AddDiagnostic("Clip '" + DisplayId(clipId, c) + "' has a negative duration.");
                }

                if (sectionDuration > 0f && start > sectionDuration)
                {
                    AddDiagnostic("Clip '" + DisplayId(clipId, c) + "' starts after section '" + DisplayId(sectionId, sectionIndex) + "' ends.");
                }

                if (!loop && duration > 0f && sectionDuration > 0f && start + duration > sectionDuration)
                {
                    AddDiagnostic("Clip '" + DisplayId(clipId, c) + "' extends past section '" + DisplayId(sectionId, sectionIndex) + "'.");
                }

                ValidateClipResource(clip, c, clipId);
            }
        }

        private void ValidateClipResource(SerializedProperty clip, int clipIndex, string clipId)
        {
            SerializedProperty resource = clip.FindPropertyRelative("Resource");
            if (resource == null)
            {
                return;
            }

            ChoreographyResourceSource source = GetResourceSource(resource.FindPropertyRelative("Source"));
            ChoreographyResourceKind kind = GetResourceKind(resource.FindPropertyRelative("Kind"));
            string displayId = DisplayId(clipId, clipIndex);

            if (source == ChoreographyResourceSource.BackendCue)
            {
                string backend = GetString(resource, "Backend");
                string cue = GetString(resource, "Cue");
                if (string.IsNullOrEmpty(backend) || string.IsNullOrEmpty(cue))
                {
                    AddDiagnostic("Clip '" + displayId + "' uses Backend Cue but is missing Backend or Cue.");
                }

                if (kind == ChoreographyResourceKind.AudioClip)
                {
                    AddDiagnostic("Clip '" + displayId + "' uses Backend Cue with AudioClip kind. Use AudioEvent, BackendCue, or Generic for event-style audio.");
                }
            }
            else if (kind == ChoreographyResourceKind.BackendCue)
            {
                AddDiagnostic("Clip '" + displayId + "' has BackendCue kind but does not use the Backend Cue source mode.");
            }
        }

        private void ValidateEvents(SerializedProperty section, int sectionIndex, string sectionId, float sectionDuration, ref AuthoringStats stats)
        {
            SerializedProperty events = section.FindPropertyRelative("Events");
            if (events == null)
            {
                return;
            }

            stats.EventCount += events.arraySize;
            for (int e = 0; e < events.arraySize; e++)
            {
                SerializedProperty evt = events.GetArrayElementAtIndex(e);
                string eventId = GetString(evt, "EventId");
                float time = GetFloat(evt, "Time");
                if (string.IsNullOrEmpty(eventId))
                {
                    AddDiagnostic("Event " + e + " in section '" + DisplayId(sectionId, sectionIndex) + "' has no id.");
                }

                if (time < 0f || (sectionDuration > 0f && time > sectionDuration))
                {
                    AddDiagnostic("Event '" + DisplayId(eventId, e) + "' is outside section '" + DisplayId(sectionId, sectionIndex) + "'.");
                }
            }
        }

        private void ValidateEventStates(SerializedProperty section, int sectionIndex, string sectionId, float sectionDuration, ref AuthoringStats stats)
        {
            SerializedProperty states = section.FindPropertyRelative("EventStates");
            if (states == null)
            {
                return;
            }

            stats.EventStateCount += states.arraySize;
            for (int i = 0; i < states.arraySize; i++)
            {
                SerializedProperty state = states.GetArrayElementAtIndex(i);
                string id = GetString(state, "Id");
                string eventId = GetString(state, "EventId");
                float start = GetFloat(state, "StartTime");
                float end = GetFloat(state, "EndTime");

                if (string.IsNullOrEmpty(id))
                {
                    AddDiagnostic("Event state " + i + " in section '" + DisplayId(sectionId, sectionIndex) + "' has no id.");
                }

                if (string.IsNullOrEmpty(eventId))
                {
                    AddDiagnostic("Event state '" + DisplayId(id, i) + "' has no event id.");
                }

                if (end <= start)
                {
                    AddDiagnostic("Event state '" + DisplayId(id, i) + "' has a non-positive duration.");
                }

                if (start < 0f || (sectionDuration > 0f && start > sectionDuration))
                {
                    AddDiagnostic("Event state '" + DisplayId(id, i) + "' starts outside section '" + DisplayId(sectionId, sectionIndex) + "'.");
                }

                if (end < 0f || (sectionDuration > 0f && end > sectionDuration))
                {
                    AddDiagnostic("Event state '" + DisplayId(id, i) + "' ends outside section '" + DisplayId(sectionId, sectionIndex) + "'.");
                }
            }
        }

        private void AddDiagnostic(string message)
        {
            _diagnostics.Add(message);
        }

        private void AddSection()
        {
            int index = _sections.arraySize;
            _sections.InsertArrayElementAtIndex(index);
            SerializedProperty section = _sections.GetArrayElementAtIndex(index);
            InitializeSection(section, index);
            _timeline.SetSelection(ChoreographyTimelineSelection.Section(index));
        }

        private void AddTrack(int sectionIndex)
        {
            SerializedProperty section = GetSection(sectionIndex);
            if (section == null)
            {
                return;
            }

            SerializedProperty tracks = section.FindPropertyRelative("Tracks");
            int index = tracks.arraySize;
            tracks.InsertArrayElementAtIndex(index);
            SerializedProperty track = tracks.GetArrayElementAtIndex(index);
            InitializeTrack(track, index);
            _timeline.SetSelection(ChoreographyTimelineSelection.Track(sectionIndex, index));
        }

        private void AddClip(int sectionIndex, int trackIndex)
        {
            SerializedProperty track = GetTrack(sectionIndex, trackIndex);
            if (track == null)
            {
                return;
            }

            SerializedProperty clips = track.FindPropertyRelative("Clips");
            int index = clips.arraySize;
            clips.InsertArrayElementAtIndex(index);
            SerializedProperty clip = clips.GetArrayElementAtIndex(index);
            InitializeClip(clip, index);
            _timeline.SetSelection(ChoreographyTimelineSelection.Clip(sectionIndex, trackIndex, index));
        }

        private void AddEvent(int sectionIndex)
        {
            SerializedProperty section = GetSection(sectionIndex);
            if (section == null)
            {
                return;
            }

            SerializedProperty events = section.FindPropertyRelative("Events");
            int index = events.arraySize;
            events.InsertArrayElementAtIndex(index);
            SerializedProperty evt = events.GetArrayElementAtIndex(index);
            InitializeEvent(evt, index);
            _timeline.SetSelection(ChoreographyTimelineSelection.Event(sectionIndex, index));
        }

        private void AddEventState(int sectionIndex)
        {
            SerializedProperty section = GetSection(sectionIndex);
            if (section == null)
            {
                return;
            }

            SerializedProperty states = section.FindPropertyRelative("EventStates");
            if (states == null)
            {
                return;
            }

            int index = states.arraySize;
            states.InsertArrayElementAtIndex(index);
            SerializedProperty state = states.GetArrayElementAtIndex(index);
            InitializeEventState(state, index);
            _timeline.SetSelection(ChoreographyTimelineSelection.EventState(sectionIndex, index));
        }

        private void DeleteSection(int sectionIndex)
        {
            _sections.DeleteArrayElementAtIndex(sectionIndex);
            _timeline.ClearSelection();
        }

        private void DeleteTrack(int sectionIndex, int trackIndex)
        {
            SerializedProperty section = GetSection(sectionIndex);
            if (section == null)
            {
                return;
            }

            section.FindPropertyRelative("Tracks").DeleteArrayElementAtIndex(trackIndex);
            _timeline.ClearSelection();
        }

        private void DeleteClip(int sectionIndex, int trackIndex, int clipIndex)
        {
            SerializedProperty track = GetTrack(sectionIndex, trackIndex);
            if (track == null)
            {
                return;
            }

            track.FindPropertyRelative("Clips").DeleteArrayElementAtIndex(clipIndex);
            _timeline.ClearSelection();
        }

        private void DeleteEvent(int sectionIndex, int eventIndex)
        {
            SerializedProperty section = GetSection(sectionIndex);
            if (section == null)
            {
                return;
            }

            section.FindPropertyRelative("Events").DeleteArrayElementAtIndex(eventIndex);
            _timeline.ClearSelection();
        }

        private void DeleteEventState(int sectionIndex, int eventStateIndex)
        {
            SerializedProperty section = GetSection(sectionIndex);
            if (section == null)
            {
                return;
            }

            section.FindPropertyRelative("EventStates").DeleteArrayElementAtIndex(eventStateIndex);
            _timeline.ClearSelection();
        }

        private void RebuildRuntimeModel()
        {
            serializedObject.ApplyModifiedProperties();
            for (int i = 0; i < targets.Length; i++)
            {
                ChoreographyAsset asset = targets[i] as ChoreographyAsset;
                if (asset != null)
                {
                    asset.RebuildRuntimeModel();
                    EditorUtility.SetDirty(asset);
                }
            }
            serializedObject.Update();
        }

        private static void InitializeSection(SerializedProperty section, int index)
        {
            section.FindPropertyRelative("Id").stringValue = "Section_" + index;
            section.FindPropertyRelative("Duration").doubleValue = 1d;
            section.FindPropertyRelative("Interruptible").boolValue = true;
            SetEnumByName(section.FindPropertyRelative("PreferredMode"), nameof(ChoreographyPlaybackMode.Inherit));
            section.FindPropertyRelative("Tracks").ClearArray();
            section.FindPropertyRelative("Events").ClearArray();
            section.FindPropertyRelative("EventStates").ClearArray();
        }

        private static void InitializeTrack(SerializedProperty track, int index)
        {
            track.FindPropertyRelative("Id").stringValue = "Track_" + index;
            track.FindPropertyRelative("Kind").enumValueIndex = (int)ChoreographyTrackKind.Animation;
            track.FindPropertyRelative("Clips").ClearArray();
        }

        private static void InitializeClip(SerializedProperty clip, int index)
        {
            clip.FindPropertyRelative("Id").stringValue = "Clip_" + index;
            SerializedProperty resource = clip.FindPropertyRelative("Resource");
            resource.FindPropertyRelative("Source").enumValueIndex = (int)ChoreographyResourceSource.AssetKey;
            SerializedProperty asset = resource.FindPropertyRelative("Asset");
            asset.FindPropertyRelative("m_GUID").stringValue = string.Empty;
            asset.FindPropertyRelative("m_Location").stringValue = string.Empty;
            resource.FindPropertyRelative("Address").stringValue = string.Empty;
            resource.FindPropertyRelative("Backend").stringValue = string.Empty;
            resource.FindPropertyRelative("Bank").stringValue = string.Empty;
            resource.FindPropertyRelative("Cue").stringValue = string.Empty;
            resource.FindPropertyRelative("Kind").enumValueIndex = (int)ChoreographyResourceKind.Generic;
            resource.FindPropertyRelative("Tag").stringValue = string.Empty;
            clip.FindPropertyRelative("StartTime").doubleValue = 0d;
            clip.FindPropertyRelative("Duration").doubleValue = 0.5d;
            clip.FindPropertyRelative("Weight").floatValue = 1f;
            clip.FindPropertyRelative("Channel").intValue = 0;
            clip.FindPropertyRelative("Loop").boolValue = false;
        }

        private static void InitializeEvent(SerializedProperty evt, int index)
        {
            evt.FindPropertyRelative("EventId").stringValue = "Event_" + index;
            evt.FindPropertyRelative("Time").doubleValue = 0d;
            evt.FindPropertyRelative("Magnitude").floatValue = 0f;
            evt.FindPropertyRelative("IntPayload").intValue = 0;
            evt.FindPropertyRelative("StringPayload").stringValue = string.Empty;
        }

        private static void InitializeEventState(SerializedProperty state, int index)
        {
            state.FindPropertyRelative("Id").stringValue = "State_" + index;
            state.FindPropertyRelative("EventId").stringValue = "StateEvent_" + index;
            state.FindPropertyRelative("StartTime").doubleValue = 0d;
            state.FindPropertyRelative("EndTime").doubleValue = 0.25d;
            state.FindPropertyRelative("Magnitude").floatValue = 0f;
            state.FindPropertyRelative("IntPayload").intValue = 0;
            state.FindPropertyRelative("StringPayload").stringValue = string.Empty;
        }

        private SerializedProperty GetSection(int sectionIndex)
        {
            if (_sections == null || sectionIndex < 0 || sectionIndex >= _sections.arraySize)
            {
                return null;
            }
            return _sections.GetArrayElementAtIndex(sectionIndex);
        }

        private SerializedProperty GetTrack(int sectionIndex, int trackIndex)
        {
            SerializedProperty section = GetSection(sectionIndex);
            if (section == null)
            {
                return null;
            }

            SerializedProperty tracks = section.FindPropertyRelative("Tracks");
            if (tracks == null || trackIndex < 0 || trackIndex >= tracks.arraySize)
            {
                return null;
            }
            return tracks.GetArrayElementAtIndex(trackIndex);
        }

        private SerializedProperty GetClip(int sectionIndex, int trackIndex, int clipIndex)
        {
            SerializedProperty track = GetTrack(sectionIndex, trackIndex);
            if (track == null)
            {
                return null;
            }

            SerializedProperty clips = track.FindPropertyRelative("Clips");
            if (clips == null || clipIndex < 0 || clipIndex >= clips.arraySize)
            {
                return null;
            }
            return clips.GetArrayElementAtIndex(clipIndex);
        }

        private SerializedProperty GetEvent(int sectionIndex, int eventIndex)
        {
            SerializedProperty section = GetSection(sectionIndex);
            if (section == null)
            {
                return null;
            }

            SerializedProperty events = section.FindPropertyRelative("Events");
            if (events == null || eventIndex < 0 || eventIndex >= events.arraySize)
            {
                return null;
            }
            return events.GetArrayElementAtIndex(eventIndex);
        }

        private SerializedProperty GetEventState(int sectionIndex, int eventStateIndex)
        {
            SerializedProperty section = GetSection(sectionIndex);
            if (section == null)
            {
                return null;
            }

            SerializedProperty states = section.FindPropertyRelative("EventStates");
            if (states == null || eventStateIndex < 0 || eventStateIndex >= states.arraySize)
            {
                return null;
            }
            return states.GetArrayElementAtIndex(eventStateIndex);
        }

        private static string DisplayId(string id, int fallbackIndex)
        {
            return string.IsNullOrEmpty(id) ? ("#" + fallbackIndex) : id;
        }

        private static ChoreographyResourceSource GetResourceSource(SerializedProperty source)
        {
            if (source == null || source.enumValueIndex < 0)
            {
                return ChoreographyResourceSource.Location;
            }

            if (source.enumValueIndex == (int)ChoreographyResourceSource.AssetKey)
            {
                return ChoreographyResourceSource.AssetKey;
            }

            return source.enumValueIndex == (int)ChoreographyResourceSource.BackendCue
                ? ChoreographyResourceSource.BackendCue
                : ChoreographyResourceSource.Location;
        }

        private static ChoreographyResourceKind GetResourceKind(SerializedProperty kind)
        {
            if (kind == null || kind.enumValueIndex < 0)
            {
                return ChoreographyResourceKind.Generic;
            }

            return kind.enumValueIndex <= (int)ChoreographyResourceKind.BackendCue
                ? (ChoreographyResourceKind)kind.enumValueIndex
                : ChoreographyResourceKind.Generic;
        }

        private static void SwitchResourceSource(
            SerializedProperty source,
            SerializedProperty asset,
            SerializedProperty address,
            SerializedProperty cue,
            ChoreographyResourceSource targetSource)
        {
            if (source == null)
            {
                return;
            }

            ChoreographyResourceSource currentSource = GetResourceSource(source);
            if (currentSource == targetSource)
            {
                return;
            }

            if (targetSource == ChoreographyResourceSource.AssetKey)
            {
                TrySetAssetKeyFromLocation(asset, address != null ? address.stringValue : string.Empty);
            }
            else if (targetSource == ChoreographyResourceSource.Location)
            {
                string location = GetAssetKeyLocation(asset);
                if (!string.IsNullOrEmpty(location) && address != null)
                {
                    address.stringValue = location;
                }
                else if (cue != null && !string.IsNullOrEmpty(cue.stringValue) && address != null)
                {
                    address.stringValue = cue.stringValue;
                }
            }
            else if (targetSource == ChoreographyResourceSource.BackendCue && cue != null && string.IsNullOrEmpty(cue.stringValue))
            {
                string location = GetAssetKeyLocation(asset);
                cue.stringValue = !string.IsNullOrEmpty(location)
                    ? location
                    : address != null ? address.stringValue : string.Empty;
            }

            source.enumValueIndex = (int)targetSource;
        }

        private static bool TrySetAssetKeyFromLocation(SerializedProperty asset, string location)
        {
            if (asset == null || string.IsNullOrEmpty(location))
            {
                return false;
            }

            Object resolved = AssetDatabase.LoadAssetAtPath<Object>(location);
            if (resolved == null)
            {
                return false;
            }

            SetAssetKeyFromObject(asset, resolved);
            return true;
        }

        private static void SetAssetKeyFromObject(SerializedProperty asset, Object value)
        {
            if (asset == null || value == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(value);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            SerializedProperty guid = asset.FindPropertyRelative("m_GUID");
            SerializedProperty assetLocation = asset.FindPropertyRelative("m_Location");
            if (guid == null || assetLocation == null)
            {
                return;
            }

            guid.stringValue = AssetDatabase.AssetPathToGUID(path);
            assetLocation.stringValue = path;
        }

        private static void ClearAssetKey(SerializedProperty asset)
        {
            if (asset == null)
            {
                return;
            }

            SerializedProperty guid = asset.FindPropertyRelative("m_GUID");
            SerializedProperty assetLocation = asset.FindPropertyRelative("m_Location");
            if (guid != null)
            {
                guid.stringValue = string.Empty;
            }

            if (assetLocation != null)
            {
                assetLocation.stringValue = string.Empty;
            }
        }

        private static string GetAssetKeyLocation(SerializedProperty asset)
        {
            if (asset == null)
            {
                return string.Empty;
            }

            SerializedProperty location = asset.FindPropertyRelative("m_Location");
            return location != null ? location.stringValue : string.Empty;
        }

        private static Object ResolveAssetKeyObject(SerializedProperty asset)
        {
            if (asset == null)
            {
                return null;
            }

            SerializedProperty guid = asset.FindPropertyRelative("m_GUID");
            SerializedProperty location = asset.FindPropertyRelative("m_Location");
            string path = guid != null && !string.IsNullOrEmpty(guid.stringValue)
                ? AssetDatabase.GUIDToAssetPath(guid.stringValue)
                : string.Empty;

            if (string.IsNullOrEmpty(path) && location != null)
            {
                path = location.stringValue;
            }

            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Object>(path);
        }

        private static void PingAssetKey(SerializedProperty asset)
        {
            if (asset == null)
            {
                return;
            }

            SerializedProperty guid = asset.FindPropertyRelative("m_GUID");
            SerializedProperty location = asset.FindPropertyRelative("m_Location");
            string path = guid != null && !string.IsNullOrEmpty(guid.stringValue)
                ? AssetDatabase.GUIDToAssetPath(guid.stringValue)
                : string.Empty;

            if (string.IsNullOrEmpty(path) && location != null)
            {
                path = location.stringValue;
            }

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            Object targetAsset = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (targetAsset != null)
            {
                Selection.activeObject = targetAsset;
                EditorGUIUtility.PingObject(targetAsset);
            }
        }

        private static float GetFloat(SerializedProperty owner, string relative)
        {
            SerializedProperty property = owner.FindPropertyRelative(relative);
            return property != null ? (float)property.doubleValue : 0f;
        }

        private static bool GetBool(SerializedProperty owner, string relative)
        {
            SerializedProperty property = owner.FindPropertyRelative(relative);
            return property != null && property.boolValue;
        }

        private static string GetString(SerializedProperty owner, string relative)
        {
            SerializedProperty property = owner.FindPropertyRelative(relative);
            return property != null ? property.stringValue : string.Empty;
        }

        private static string GetEnumName(SerializedProperty owner, string relative)
        {
            SerializedProperty property = owner.FindPropertyRelative(relative);
            if (property == null || property.enumValueIndex < 0 || property.enumValueIndex >= property.enumDisplayNames.Length)
            {
                return string.Empty;
            }
            return property.enumDisplayNames[property.enumValueIndex];
        }

        private static void SetEnumByName(SerializedProperty property, string enumName)
        {
            if (property == null)
            {
                return;
            }

            string[] names = property.enumNames;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == enumName)
                {
                    property.enumValueIndex = i;
                    return;
                }
            }
        }

        private struct AuthoringStats
        {
            public int SectionCount;
            public int TrackCount;
            public int ClipCount;
            public int EventCount;
            public int EventStateCount;
            public double TotalDuration;
        }

        private static class InspectorUi
        {
            private const float HeroHeaderHeight = 56f;
            private const float HeaderHeight = 40f;
            private const float FoldoutHeaderHeight = 32f;
            private const float HeaderPadding = 8f;
            private const float FoldoutArrowWidth = 14f;

            private static GUIStyle _panelStyle;
            private static GUIStyle _headerTitleStyle;
            private static GUIStyle _headerSubtitleStyle;
            private static GUIStyle _foldoutTitleStyle;
            private static GUIStyle _metricLabelStyle;
            private static GUIStyle _metricValueStyle;
            private static GUIStyle _pillStyle;
            private static bool _stylesReady;

            public static Color MainAccent => new Color(0.514f, 0.376f, 0.922f, 1f);
            public static Color BlueAccent => new Color(0.231f, 0.553f, 0.851f, 1f);
            public static Color CyanAccent => new Color(0.188f, 0.737f, 0.788f, 1f);
            public static Color GreenAccent => new Color(0.188f, 0.690f, 0.741f, 1f);
            public static Color WarningAccent => new Color(0.925f, 0.576f, 0.251f, 1f);
            public static Color RedAccent => new Color(0.925f, 0.251f, 0.478f, 1f);
            public static Color NeutralAccent => new Color(0.560f, 0.560f, 0.560f, 1f);

            public static void DrawHeroHeader(string title, string subtitle, Color accent)
            {
                EnsureStyles();

                Rect rect = EditorGUILayout.GetControlRect(false, HeroHeaderHeight);
                Color background = EditorGUIUtility.isProSkin
                    ? new Color(0.145f, 0.145f, 0.145f, 1f)
                    : new Color(0.800f, 0.800f, 0.800f, 1f);

                EditorGUI.DrawRect(rect, background);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.35f));

                EditorGUI.LabelField(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 20f, 20f), title, _headerTitleStyle);
                EditorGUI.LabelField(new Rect(rect.x + 12f, rect.y + 32f, rect.width - 20f, 16f), subtitle, _headerSubtitleStyle);
            }

            public static void DrawPanelHeader(string title, string subtitle, Color accent)
            {
                EnsureStyles();

                Rect rect = EditorGUILayout.GetControlRect(false, HeaderHeight);
                Color background = EditorGUIUtility.isProSkin
                    ? new Color(0.185f, 0.185f, 0.185f, 1f)
                    : new Color(0.745f, 0.745f, 0.745f, 1f);

                EditorGUI.DrawRect(rect, background);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.28f));

                EditorGUI.LabelField(new Rect(rect.x + HeaderPadding, rect.y + 6f, rect.width - HeaderPadding * 2f, 17f), title, _foldoutTitleStyle);
                if (!string.IsNullOrEmpty(subtitle))
                {
                    EditorGUI.LabelField(new Rect(rect.x + HeaderPadding, rect.y + 25f, rect.width - HeaderPadding * 2f, 13f), subtitle, _headerSubtitleStyle);
                }
            }

            public static bool DrawFoldoutPanelHeader(string title, string subtitle, bool expanded, Color accent, string badgeText)
            {
                EnsureStyles();

                Rect rect = EditorGUILayout.GetControlRect(false, FoldoutHeaderHeight);
                Color baseColor = expanded ? accent : new Color(accent.r * 0.72f, accent.g * 0.72f, accent.b * 0.72f, 1f);
                Color background = EditorGUIUtility.isProSkin
                    ? new Color(0.180f, 0.180f, 0.180f, 1f)
                    : new Color(0.760f, 0.760f, 0.760f, 1f);

                EditorGUI.DrawRect(rect, background);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), baseColor);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.30f));

                Rect arrowRect = new Rect(rect.x + 7f, rect.y + 9f, FoldoutArrowWidth, rect.height - 18f);
                DrawFoldoutTriangle(arrowRect, expanded, baseColor);

                float badgeWidth = string.IsNullOrEmpty(badgeText) ? 0f : Mathf.Min(96f, Mathf.Max(44f, badgeText.Length * 7f + 16f));
                if (badgeWidth > 0f)
                {
                    Rect badgeRect = new Rect(rect.xMax - badgeWidth - 8f, rect.y + 7f, badgeWidth, rect.height - 14f);
                    DrawPill(badgeRect, badgeText, baseColor);
                }

                Rect titleRect = new Rect(
                    arrowRect.xMax + 5f,
                    rect.y + 5f,
                    rect.width - (arrowRect.xMax - rect.x) - badgeWidth - 18f,
                    string.IsNullOrEmpty(subtitle) ? rect.height - 10f : 15f);
                EditorGUI.LabelField(titleRect, title, _foldoutTitleStyle);

                if (!string.IsNullOrEmpty(subtitle))
                {
                    EditorGUI.LabelField(new Rect(titleRect.x, rect.y + 21f, titleRect.width, 11f), subtitle, _headerSubtitleStyle);
                }

                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    expanded = !expanded;
                    Event.current.Use();
                }

                return expanded;
            }

            public static System.IDisposable PanelScope()
            {
                EnsureStyles();
                return new EditorGUILayout.VerticalScope(_panelStyle);
            }

            public static void DrawMetricCard(string label, string value, Color accent, params GUILayoutOption[] options)
            {
                EnsureStyles();

                Rect rect = EditorGUILayout.GetControlRect(false, 46f, options);
                Color background = EditorGUIUtility.isProSkin
                    ? new Color(0.205f, 0.205f, 0.205f, 1f)
                    : new Color(0.835f, 0.835f, 0.835f, 1f);

                EditorGUI.DrawRect(rect, background);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), accent);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.18f));

                EditorGUI.LabelField(new Rect(rect.x + 9f, rect.y + 5f, rect.width - 14f, 14f), label, _metricLabelStyle);
                EditorGUI.LabelField(new Rect(rect.x + 9f, rect.y + 20f, rect.width - 14f, 20f), value, _metricValueStyle);
            }

            private static void DrawPill(Rect rect, string text, Color accent)
            {
                Color background = new Color(accent.r, accent.g, accent.b, 0.34f);
                EditorGUI.DrawRect(rect, background);
                DrawOutline(rect, new Color(accent.r, accent.g, accent.b, 0.88f), 1f);
                EditorGUI.LabelField(rect, text, _pillStyle);
            }

            private static void DrawOutline(Rect rect, Color color, float thickness)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
                EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
            }

            private static void EnsureStyles()
            {
                if (_stylesReady)
                {
                    return;
                }

                _panelStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(8, 8, 7, 7),
                    margin = new RectOffset(0, 0, 0, 7)
                };

                _headerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip,
                    fontSize = 13
                };
                _headerTitleStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.92f, 0.92f, 0.92f, 1f) : new Color(0.12f, 0.12f, 0.12f, 1f);

                _headerSubtitleStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip,
                    fontSize = 10
                };
                _headerSubtitleStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.66f, 0.66f, 0.66f, 1f) : new Color(0.30f, 0.30f, 0.30f, 1f);

                _foldoutTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip,
                    fontSize = 12
                };
                _foldoutTitleStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.88f, 0.88f, 0.88f, 1f) : new Color(0.14f, 0.14f, 0.14f, 1f);

                _metricLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip,
                    fontSize = 10
                };
                _metricLabelStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.70f, 0.70f, 0.70f, 1f) : new Color(0.34f, 0.34f, 0.34f, 1f);

                _metricValueStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip,
                    fontSize = 15
                };

                _pillStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    clipping = TextClipping.Clip,
                    fontSize = 10
                };
                _pillStyle.normal.textColor = Color.white;

                _stylesReady = true;
            }

            private static void DrawFoldoutTriangle(Rect rect, bool expanded, Color accent)
            {
                Vector2 center = rect.center;
                Vector3[] points;

                if (expanded)
                {
                    points = new[]
                    {
                        new Vector3(center.x - 4f, center.y - 2f),
                        new Vector3(center.x + 4f, center.y - 2f),
                        new Vector3(center.x, center.y + 3f)
                    };
                }
                else
                {
                    points = new[]
                    {
                        new Vector3(center.x - 2f, center.y - 4f),
                        new Vector3(center.x - 2f, center.y + 4f),
                        new Vector3(center.x + 3f, center.y)
                    };
                }

                Handles.BeginGUI();
                Color previousColor = Handles.color;
                Handles.color = new Color(accent.r, accent.g, accent.b, 0.95f);
                Handles.DrawAAConvexPolygon(points);
                Handles.color = previousColor;
                Handles.EndGUI();
            }
        }
    }
}
