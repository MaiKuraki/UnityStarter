using System.Collections.Generic;
using System.Globalization;
using CycloneGames.Choreography.Core;
using UnityEditor;
using UnityEngine;
using Handles = UnityEditor.Handles;

namespace CycloneGames.Choreography.Editor
{
    internal enum ChoreographyTimelineElementKind
    {
        None = 0,
        Section = 1,
        Track = 2,
        Clip = 3,
        Event = 4
    }

    internal struct ChoreographyTimelineSelection
    {
        public ChoreographyTimelineElementKind Kind;
        public int SectionIndex;
        public int TrackIndex;
        public int ClipIndex;
        public int EventIndex;

        public bool IsValid => Kind != ChoreographyTimelineElementKind.None && SectionIndex >= 0;

        public static ChoreographyTimelineSelection None()
        {
            return new ChoreographyTimelineSelection
            {
                Kind = ChoreographyTimelineElementKind.None,
                SectionIndex = -1,
                TrackIndex = -1,
                ClipIndex = -1,
                EventIndex = -1
            };
        }

        public static ChoreographyTimelineSelection Section(int sectionIndex)
        {
            return new ChoreographyTimelineSelection
            {
                Kind = ChoreographyTimelineElementKind.Section,
                SectionIndex = sectionIndex,
                TrackIndex = -1,
                ClipIndex = -1,
                EventIndex = -1
            };
        }

        public static ChoreographyTimelineSelection Track(int sectionIndex, int trackIndex)
        {
            return new ChoreographyTimelineSelection
            {
                Kind = ChoreographyTimelineElementKind.Track,
                SectionIndex = sectionIndex,
                TrackIndex = trackIndex,
                ClipIndex = -1,
                EventIndex = -1
            };
        }

        public static ChoreographyTimelineSelection Clip(int sectionIndex, int trackIndex, int clipIndex)
        {
            return new ChoreographyTimelineSelection
            {
                Kind = ChoreographyTimelineElementKind.Clip,
                SectionIndex = sectionIndex,
                TrackIndex = trackIndex,
                ClipIndex = clipIndex,
                EventIndex = -1
            };
        }

        public static ChoreographyTimelineSelection Event(int sectionIndex, int eventIndex)
        {
            return new ChoreographyTimelineSelection
            {
                Kind = ChoreographyTimelineElementKind.Event,
                SectionIndex = sectionIndex,
                TrackIndex = -1,
                ClipIndex = -1,
                EventIndex = eventIndex
            };
        }
    }

    /// <summary>
    /// Montage-style inspector timeline for <see cref="ChoreographyAsset"/> authoring data.
    /// The view is still drawn from SerializedProperty so selection, Undo, and prefab override behavior remain owned
    /// by the custom inspector. It groups rows by authored track id (similar to slots), places sections in a dedicated
    /// header row, and renders a timing row that combines section and event markers.
    /// </summary>
    internal sealed class ChoreographyTimelineView
    {
        private const float MinPixelsPerSecond = 24f;
        private const float MaxPixelsPerSecond = 720f;
        private const float LabelColumnWidth = 156f;
        private const float RulerHeight = 20f;
        private const float SectionRowHeight = 26f;
        private const float LaneHeight = 40f;
        private const float ScrollbarHeight = 14f;
        private const float MinClipWidth = 6f;
        private const float FitPadding = 12f;
        private const float MarkerHitPadding = 3f;
        private const int TimingMarkerLaneCount = 3;
        private const float TimingMarkerWidth = 28f;
        private const float TimingMarkerGap = 3f;
        private const float TimingConnectorGap = 7f;
        private const float TimingAnchorWidth = 2f;
        private const float TimingAnchorPinSize = 4f;
        private const float TimingMarkerTop = 4f;
        private const float TimingMarkerHeight = 16f;
        private const float TimingMarkerLaneGap = 3f;
        private const float TimingSectionTop = TimingMarkerTop + TimingMarkerLaneCount * TimingMarkerHeight + (TimingMarkerLaneCount - 1) * TimingMarkerLaneGap + 6f;
        private const float TimingSectionHeight = 19f;
        private const float TimingRowHeight = TimingSectionTop + TimingSectionHeight + 5f;
        private const float LoopBadgeWidth = 38f;
        private const float LoopBadgeHeight = 14f;

        private static readonly Color AnimationColor = new Color(0.231f, 0.553f, 0.851f, 1f);
        private static readonly Color AudioColor = new Color(0.188f, 0.690f, 0.741f, 1f);
        private static readonly Color VfxColor = new Color(0.757f, 0.353f, 0.588f, 1f);
        private static readonly Color EventTrackColor = new Color(0.784f, 0.557f, 0.894f, 1f);
        private static readonly Color CustomColor = new Color(0.560f, 0.560f, 0.560f, 1f);
        private static readonly Color SectionColor = new Color(0.514f, 0.376f, 0.922f, 1f);
        private static readonly Color EventMarkerColor = new Color(0.925f, 0.251f, 0.478f, 1f);
        private static readonly Color SelectionColor = new Color(1f, 0.820f, 0.349f, 1f);
        private static readonly Vector3[] TimingDiamondPoints = new Vector3[4];

        private static readonly float[] NiceSteps =
        {
            0.05f, 0.1f, 0.2f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 15f, 30f, 60f, 120f, 300f
        };

        private readonly List<LaneInfo> _lanes = new List<LaneInfo>(8);
        private readonly List<TimingMarkerLayout> _timingMarkers = new List<TimingMarkerLayout>(24);
        private readonly float[] _timingLaneRights = new float[TimingMarkerLaneCount];
        private readonly GUIContent _label = new GUIContent();

        private GUIStyle _clipLabelStyle;
        private GUIStyle _laneLabelStyle;
        private GUIStyle _sectionLabelStyle;
        private GUIStyle _rulerLabelStyle;
        private GUIStyle _markerLabelStyle;
        private GUIStyle _loopBadgeStyle;
        private bool _stylesReady;

        private float _pixelsPerSecond = 120f;
        private Vector2 _scroll;
        private bool _fitRequested;
        private float _totalDuration;
        private float _lastViewWidth;
        private ChoreographyTimelineSelection _selection = ChoreographyTimelineSelection.None();

        public ChoreographyTimelineSelection Selection => _selection;

        public void SetSelection(ChoreographyTimelineSelection selection)
        {
            _selection = selection;
        }

        public void ClearSelection()
        {
            _selection = ChoreographyTimelineSelection.None();
        }

        public void Draw(SerializedProperty sections)
        {
            EnsureStyles();
            DrawToolbar();
            BuildLanes(sections);

            if (sections == null || sections.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Create at least one section to build a choreography timeline.", MessageType.None);
                return;
            }

            if (_totalDuration <= 0f)
            {
                EditorGUILayout.HelpBox("Sections need positive durations before the timeline can be scaled.", MessageType.Warning);
                return;
            }

            int laneCount = _lanes.Count;
            float totalHeight = RulerHeight + SectionRowHeight + laneCount * LaneHeight + TimingRowHeight + ScrollbarHeight + 2f;
            Rect area = GUILayoutUtility.GetRect(1f, totalHeight, GUILayout.ExpandWidth(true));
            float viewWidth = Mathf.Max(48f, area.width - LabelColumnWidth);

            if (Event.current.type == EventType.Repaint && viewWidth > 48f)
            {
                _lastViewWidth = viewWidth;
            }

            if (_fitRequested && Event.current.type == EventType.Repaint)
            {
                float fitWidth = _lastViewWidth > 48f ? _lastViewWidth : viewWidth;
                float usableWidth = Mathf.Max(48f, fitWidth - FitPadding);
                _pixelsPerSecond = Mathf.Clamp(usableWidth / _totalDuration, MinPixelsPerSecond, MaxPixelsPerSecond);
                _scroll.x = 0f;
                _fitRequested = false;
            }

            float contentWidth = Mathf.Max(viewWidth, _totalDuration * _pixelsPerSecond);
            float maxScroll = Mathf.Max(0f, contentWidth - viewWidth);
            _scroll.x = Mathf.Clamp(_scroll.x, 0f, maxScroll);

            Rect timeRect = new Rect(area.x + LabelColumnWidth, area.y, viewWidth, area.height - ScrollbarHeight);
            HandleMouseDown(sections, area, timeRect);

            if (Event.current.type == EventType.Repaint)
            {
                DrawFrame(area, viewWidth);
                DrawLeftColumn(area);

                GUI.BeginClip(timeRect);
                DrawTimeAreaBackground(viewWidth, timeRect.height);
                DrawRuler(viewWidth, timeRect.height);
                DrawSections(sections, viewWidth);
                DrawTrackLanes(sections, viewWidth);
                DrawTimingRow(sections, viewWidth);
                GUI.EndClip();
            }

            Rect scrollbarRect = new Rect(area.x + LabelColumnWidth, area.yMax - ScrollbarHeight, viewWidth, ScrollbarHeight);
            if (maxScroll > 0f)
            {
                _scroll.x = GUI.HorizontalScrollbar(scrollbarRect, _scroll.x, viewWidth, 0f, contentWidth);
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Workspace", EditorStyles.miniBoldLabel, GUILayout.Width(72f));
                GUILayout.Label("Zoom", EditorStyles.miniLabel, GUILayout.Width(34f));
                _pixelsPerSecond = GUILayout.HorizontalSlider(_pixelsPerSecond, MinPixelsPerSecond, MaxPixelsPerSecond, GUILayout.MinWidth(88f));

                if (GUILayout.Button("Fit", EditorStyles.toolbarButton, GUILayout.Width(38f)))
                {
                    _fitRequested = true;
                }

                _label.text = _pixelsPerSecond.ToString("0", CultureInfo.InvariantCulture) + " px/s";
                GUILayout.Label(_label, EditorStyles.miniLabel, GUILayout.Width(66f));
            }
        }

        private void DrawFrame(Rect area, float viewWidth)
        {
            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.150f, 0.150f, 0.150f, 1f)
                : new Color(0.745f, 0.745f, 0.745f, 1f);
            EditorGUI.DrawRect(area, background);
            EditorGUI.DrawRect(new Rect(area.x + LabelColumnWidth - 1f, area.y, 1f, area.height - ScrollbarHeight), new Color(0f, 0f, 0f, 0.35f));
            EditorGUI.DrawRect(new Rect(area.x + LabelColumnWidth, area.yMax - ScrollbarHeight, viewWidth, ScrollbarHeight), new Color(0f, 0f, 0f, 0.18f));
        }

        private void DrawLeftColumn(Rect area)
        {
            Rect sectionLabel = new Rect(area.x + 5f, area.y + RulerHeight, LabelColumnWidth - 10f, SectionRowHeight);
            _label.text = "Sections";
            GUI.Label(sectionLabel, _label, _laneLabelStyle);

            float lanesTop = area.y + RulerHeight + SectionRowHeight;
            for (int i = 0; i < _lanes.Count; i++)
            {
                LaneInfo lane = _lanes[i];
                Rect rowRect = new Rect(area.x, lanesTop + i * LaneHeight, LabelColumnWidth, LaneHeight);
                if (IsSelectionOnLane(lane))
                {
                    EditorGUI.DrawRect(rowRect, new Color(SelectionColor.r, SelectionColor.g, SelectionColor.b, 0.16f));
                }

                Color color = LaneColor(lane.Kind);
                EditorGUI.DrawRect(new Rect(rowRect.x + 4f, rowRect.y + 6f, 4f, rowRect.height - 12f), color);

                _label.text = lane.Label;
                GUI.Label(new Rect(rowRect.x + 12f, rowRect.y, rowRect.width - 16f, rowRect.height), _label, _laneLabelStyle);
            }

            Rect timingLabel = new Rect(area.x + 5f, lanesTop + _lanes.Count * LaneHeight, LabelColumnWidth - 10f, TimingRowHeight);
            _label.text = "Timing Markers";
            GUI.Label(timingLabel, _label, _laneLabelStyle);
        }

        private void DrawTimeAreaBackground(float viewWidth, float viewHeight)
        {
            EditorGUI.DrawRect(new Rect(0f, 0f, viewWidth, RulerHeight), new Color(0f, 0f, 0f, 0.28f));
            EditorGUI.DrawRect(new Rect(0f, RulerHeight, viewWidth, SectionRowHeight), new Color(SectionColor.r, SectionColor.g, SectionColor.b, 0.10f));

            float lanesTop = RulerHeight + SectionRowHeight;
            for (int i = 0; i < _lanes.Count; i++)
            {
                Color tint = (i & 1) == 0 ? new Color(1f, 1f, 1f, 0.035f) : new Color(0f, 0f, 0f, 0.095f);
                EditorGUI.DrawRect(new Rect(0f, lanesTop + i * LaneHeight, viewWidth, LaneHeight), tint);
            }

            float timingTop = lanesTop + _lanes.Count * LaneHeight;
            EditorGUI.DrawRect(new Rect(0f, timingTop, viewWidth, TimingRowHeight), new Color(0f, 0f, 0f, 0.16f));
            EditorGUI.DrawRect(new Rect(0f, timingTop + TimingSectionTop - 4f, viewWidth, 1f), new Color(1f, 1f, 1f, 0.08f));
            EditorGUI.DrawRect(new Rect(0f, viewHeight - 1f, viewWidth, 1f), new Color(0f, 0f, 0f, 0.30f));
        }

        private void DrawRuler(float viewWidth, float fullHeight)
        {
            float step = PickTickStep();
            Color tickColor = new Color(1f, 1f, 1f, 0.26f);
            Color gridColor = new Color(1f, 1f, 1f, 0.065f);
            int decimals = step < 1f ? 2 : (step < 10f ? 1 : 0);

            for (float time = 0f; time <= _totalDuration + 0.0001f; time += step)
            {
                float x = time * _pixelsPerSecond - _scroll.x;
                if (x < -1f || x > viewWidth + 1f)
                {
                    continue;
                }

                EditorGUI.DrawRect(new Rect(x, 0f, 1f, RulerHeight), tickColor);
                EditorGUI.DrawRect(new Rect(x, RulerHeight, 1f, fullHeight - RulerHeight), gridColor);

                _label.text = time.ToString("0." + new string('0', decimals), CultureInfo.InvariantCulture) + "s";
                GUI.Label(new Rect(x + 3f, 1f, 52f, RulerHeight), _label, _rulerLabelStyle);
            }
        }

        private void DrawSections(SerializedProperty sections, float viewWidth)
        {
            float cursor = 0f;
            int count = sections.arraySize;
            float sectionBottom = RulerHeight + SectionRowHeight + _lanes.Count * LaneHeight + TimingRowHeight;

            for (int i = 0; i < count; i++)
            {
                SerializedProperty section = sections.GetArrayElementAtIndex(i);
                float duration = Mathf.Max(0f, GetFloat(section, "Duration"));
                float x = cursor * _pixelsPerSecond - _scroll.x;
                float width = duration * _pixelsPerSecond;
                Rect sectionRect = new Rect(x, RulerHeight + 2f, width, SectionRowHeight - 4f);
                bool selected = _selection.Kind == ChoreographyTimelineElementKind.Section && _selection.SectionIndex == i;

                Color body = SectionColor;
                body.a = selected ? 0.58f : 0.36f;
                EditorGUI.DrawRect(sectionRect, body);
                DrawOutline(sectionRect, selected ? SelectionColor : new Color(0f, 0f, 0f, 0.30f), selected ? 2f : 1f);
                EditorGUI.DrawRect(new Rect(x, RulerHeight, 1f, sectionBottom - RulerHeight), new Color(0f, 0f, 0f, 0.42f));

                if (width > 32f)
                {
                    string id = GetString(section, "Id");
                    if (string.IsNullOrEmpty(id))
                    {
                        id = "Section " + i;
                    }

                    string mode = GetEnumName(section, "PreferredMode");
                    if (!GetBool(section, "Interruptible"))
                    {
                        mode += " / Locked";
                    }

                    _label.text = id + "  " + mode;
                    GUI.Label(new Rect(sectionRect.x + 5f, sectionRect.y, Mathf.Min(sectionRect.width - 8f, viewWidth), sectionRect.height), _label, _sectionLabelStyle);
                }

                cursor += duration;
            }
        }

        private void DrawTrackLanes(SerializedProperty sections, float viewWidth)
        {
            float sectionStart = 0f;
            int sectionCount = sections.arraySize;

            for (int s = 0; s < sectionCount; s++)
            {
                SerializedProperty section = sections.GetArrayElementAtIndex(s);
                float sectionDuration = Mathf.Max(0f, GetFloat(section, "Duration"));
                SerializedProperty tracks = section.FindPropertyRelative("Tracks");

                if (tracks != null)
                {
                    for (int t = 0; t < tracks.arraySize; t++)
                    {
                        SerializedProperty track = tracks.GetArrayElementAtIndex(t);
                        int laneIndex = FindLaneIndex(GetTrackKind(track), GetString(track, "Id"));
                        if (laneIndex >= 0)
                        {
                            DrawTrackClips(sectionStart, sectionDuration, s, t, track, laneIndex, viewWidth);
                        }
                    }
                }

                sectionStart += sectionDuration;
            }
        }

        private void DrawTrackClips(
            float sectionStart,
            float sectionDuration,
            int sectionIndex,
            int trackIndex,
            SerializedProperty track,
            int laneIndex,
            float viewWidth)
        {
            SerializedProperty clips = track.FindPropertyRelative("Clips");
            if (clips == null)
            {
                return;
            }

            ChoreographyTrackKind kind = GetTrackKind(track);
            float laneY = RulerHeight + SectionRowHeight + laneIndex * LaneHeight;

            for (int c = 0; c < clips.arraySize; c++)
            {
                SerializedProperty clip = clips.GetArrayElementAtIndex(c);
                Rect clipRect = GetClipRect(clip, sectionStart, sectionDuration, laneY);
                if (clipRect.xMax < -24f || clipRect.x > viewWidth + 24f)
                {
                    continue;
                }

                bool selected = _selection.Kind == ChoreographyTimelineElementKind.Clip
                    && _selection.SectionIndex == sectionIndex
                    && _selection.TrackIndex == trackIndex
                    && _selection.ClipIndex == c;

                DrawClip(clip, clipRect, kind, selected);
            }
        }

        private void DrawClip(SerializedProperty clip, Rect clipRect, ChoreographyTrackKind kind, bool selected)
        {
            bool oneShot = GetFloat(clip, "Duration") <= 0f;
            Color color = LaneColor(kind);

            if (oneShot)
            {
                Rect marker = new Rect(clipRect.x - MinClipWidth * 0.5f, clipRect.y, MinClipWidth, clipRect.height);
                EditorGUI.DrawRect(marker, color);
                if (selected)
                {
                    DrawOutline(new Rect(marker.x - 2f, marker.y - 2f, marker.width + 4f, marker.height + 4f), SelectionColor, 2f);
                }
                return;
            }

            float weight = Mathf.Clamp01(GetFloat(clip, "Weight"));
            Color body = color;
            body.a = selected ? 0.92f : 0.56f + weight * 0.28f;
            EditorGUI.DrawRect(clipRect, body);
            EditorGUI.DrawRect(new Rect(clipRect.x, clipRect.yMax - 4f, clipRect.width * weight, 4f), color);

            bool loop = GetBool(clip, "Loop");
            if (loop)
            {
                DrawLoopOverlay(clip, clipRect);
            }

            DrawOutline(clipRect, selected ? SelectionColor : new Color(0f, 0f, 0f, 0.35f), selected ? 2f : 1f);

            if (clipRect.width > 26f)
            {
                string id = GetString(clip, "Id");
                if (string.IsNullOrEmpty(id))
                {
                    id = GetResourceLocation(clip);
                }
                if (string.IsNullOrEmpty(id))
                {
                    id = "Clip";
                }

                int channel = GetInt(clip, "Channel");
                _label.text = channel == 0 ? id : id + "  ch " + channel;
                Rect labelRect = loop
                    ? new Rect(clipRect.x + 5f, clipRect.y + 16f, clipRect.width - 10f, clipRect.height - 17f)
                    : new Rect(clipRect.x + 4f, clipRect.y, clipRect.width - 7f, clipRect.height);
                if (labelRect.width > 12f && labelRect.height > 8f)
                {
                    GUI.Label(labelRect, _label, _clipLabelStyle);
                }
            }
        }

        private void DrawLoopOverlay(SerializedProperty clip, Rect rect)
        {
            float duration = Mathf.Max(0f, GetFloat(clip, "Duration"));
            float cycleWidth = duration * _pixelsPerSecond;
            bool extendsPastCycle = cycleWidth > 0f && cycleWidth < rect.width - 2f;

            if (extendsPastCycle)
            {
                Rect cycleRect = new Rect(rect.x, rect.y + 2f, Mathf.Min(cycleWidth, rect.width), rect.height - 4f);
                Rect tailRect = new Rect(cycleRect.xMax, rect.y + 2f, rect.xMax - cycleRect.xMax, rect.height - 4f);
                EditorGUI.DrawRect(cycleRect, new Color(1f, 1f, 1f, 0.10f));
                EditorGUI.DrawRect(tailRect, new Color(0f, 0f, 0f, 0.16f));
                EditorGUI.DrawRect(new Rect(cycleRect.xMax - 1f, rect.y + 3f, 2f, rect.height - 6f), new Color(1f, 1f, 1f, 0.46f));

                if (tailRect.width > 76f)
                {
                    _label.text = "to section end";
                    GUI.Label(new Rect(tailRect.x + 5f, rect.y + 3f, tailRect.width - 8f, LoopBadgeHeight), _label, _clipLabelStyle);
                }
            }

            Rect badge = new Rect(rect.x + 5f, rect.y + 3f, Mathf.Min(LoopBadgeWidth, Mathf.Max(0f, rect.width - 10f)), LoopBadgeHeight);
            if (badge.width < 22f)
            {
                return;
            }

            EditorGUI.DrawRect(badge, new Color(0f, 0f, 0f, 0.34f));
            DrawOutline(badge, new Color(1f, 1f, 1f, 0.28f), 1f);
            _label.text = "LOOP";
            GUI.Label(badge, _label, _loopBadgeStyle);

            if (cycleWidth > 18f)
            {
                int tickCount = Mathf.Min(8, Mathf.FloorToInt(rect.width / cycleWidth));
                for (int i = 1; i < tickCount; i++)
                {
                    float x = rect.x + cycleWidth * i;
                    EditorGUI.DrawRect(new Rect(x, rect.y + 5f, 1f, rect.height - 10f), new Color(1f, 1f, 1f, 0.22f));
                }
            }
        }

        private void DrawTimingRow(SerializedProperty sections, float viewWidth)
        {
            float timingTop = RulerHeight + SectionRowHeight + _lanes.Count * LaneHeight;
            float sectionStart = 0f;
            int sectionCount = sections.arraySize;
            BuildTimingMarkers(sections, viewWidth);

            for (int s = 0; s < sectionCount; s++)
            {
                SerializedProperty section = sections.GetArrayElementAtIndex(s);
                float sectionDuration = Mathf.Max(0f, GetFloat(section, "Duration"));
                float sectionX = sectionStart * _pixelsPerSecond - _scroll.x;
                float sectionWidth = sectionDuration * _pixelsPerSecond;

                DrawTimingSectionSpan(section, s, sectionX, sectionWidth, timingTop, viewWidth);

                sectionStart += sectionDuration;
            }

            int selectedMarkerIndex = -1;
            for (int i = 0; i < _timingMarkers.Count; i++)
            {
                TimingMarkerLayout marker = _timingMarkers[i];
                if (IsTimingMarkerSelected(marker))
                {
                    selectedMarkerIndex = i;
                    continue;
                }

                if (marker.Kind == ChoreographyTimelineElementKind.Event)
                {
                    DrawEventTrackGuide(marker, false);
                }

                Rect rect = new Rect(marker.Rect.x, timingTop + marker.Rect.y, marker.Rect.width, marker.Rect.height);
                DrawTimingAnchor(marker.AnchorX, rect, timingTop, viewWidth, marker.Color, false);
            }

            for (int i = 0; i < _timingMarkers.Count; i++)
            {
                if (i == selectedMarkerIndex)
                {
                    continue;
                }

                DrawTimingMarker(_timingMarkers[i], timingTop, false);
            }

            if (selectedMarkerIndex >= 0)
            {
                TimingMarkerLayout marker = _timingMarkers[selectedMarkerIndex];
                if (marker.Kind == ChoreographyTimelineElementKind.Event)
                {
                    DrawEventTrackGuide(marker, true);
                }

                Rect rect = DrawTimingMarker(marker, timingTop, true);
                DrawTimingAnchor(marker.AnchorX, rect, timingTop, viewWidth, marker.Color, true);
            }
        }

        private void BuildTimingMarkers(SerializedProperty sections, float viewWidth)
        {
            _timingMarkers.Clear();

            float sectionStart = 0f;
            for (int s = 0; s < sections.arraySize; s++)
            {
                SerializedProperty section = sections.GetArrayElementAtIndex(s);
                float sectionDuration = Mathf.Max(0f, GetFloat(section, "Duration"));
                float sectionX = sectionStart * _pixelsPerSecond - _scroll.x;

                if (sectionX >= -TimingMarkerWidth && sectionX <= viewWidth + TimingMarkerWidth)
                {
                    _timingMarkers.Add(new TimingMarkerLayout(
                        ChoreographyTimelineElementKind.Section,
                        s,
                        -1,
                        sectionStart,
                        sectionX,
                        "S" + (s + 1),
                        SectionColor));
                }

                SerializedProperty events = section.FindPropertyRelative("Events");
                if (events != null)
                {
                    for (int e = 0; e < events.arraySize; e++)
                    {
                        SerializedProperty evt = events.GetArrayElementAtIndex(e);
                        float eventTime = Mathf.Max(0f, GetFloat(evt, "Time"));
                        float absoluteTime = sectionStart + eventTime;
                        float eventX = absoluteTime * _pixelsPerSecond - _scroll.x;
                        if (eventX < -TimingMarkerWidth || eventX > viewWidth + TimingMarkerWidth)
                        {
                            continue;
                        }

                        _timingMarkers.Add(new TimingMarkerLayout(
                            ChoreographyTimelineElementKind.Event,
                            s,
                            e,
                            absoluteTime,
                            eventX,
                            "E" + (e + 1),
                            EventMarkerColor));
                    }
                }

                sectionStart += sectionDuration;
            }

            _timingMarkers.Sort(CompareTimingMarkers);

            for (int i = 0; i < _timingLaneRights.Length; i++)
            {
                _timingLaneRights[i] = float.NegativeInfinity;
            }

            for (int i = 0; i < _timingMarkers.Count; i++)
            {
                TimingMarkerLayout marker = _timingMarkers[i];
                float chipX = Mathf.Max(2f, marker.AnchorX + TimingConnectorGap);
                int lane = FindAvailableTimingLane(chipX);
                if (lane < 0)
                {
                    lane = FindEarliestTimingLane();
                    chipX = Mathf.Max(chipX, _timingLaneRights[lane] + TimingMarkerGap);
                }

                marker.Rect = GetTimingMarkerRect(chipX, lane);
                _timingLaneRights[lane] = marker.Rect.xMax;
                _timingMarkers[i] = marker;
            }
        }

        private static int CompareTimingMarkers(TimingMarkerLayout left, TimingMarkerLayout right)
        {
            int timeCompare = left.AbsoluteTime.CompareTo(right.AbsoluteTime);
            if (timeCompare != 0)
            {
                return timeCompare;
            }

            if (left.Kind != right.Kind)
            {
                return left.Kind == ChoreographyTimelineElementKind.Section ? -1 : 1;
            }

            int sectionCompare = left.SectionIndex.CompareTo(right.SectionIndex);
            if (sectionCompare != 0)
            {
                return sectionCompare;
            }

            return left.EventIndex.CompareTo(right.EventIndex);
        }

        private int FindAvailableTimingLane(float chipX)
        {
            for (int i = 0; i < _timingLaneRights.Length; i++)
            {
                if (chipX >= _timingLaneRights[i] + TimingMarkerGap)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindEarliestTimingLane()
        {
            int lane = 0;
            float bestRight = _timingLaneRights[0];
            for (int i = 1; i < _timingLaneRights.Length; i++)
            {
                if (_timingLaneRights[i] < bestRight)
                {
                    bestRight = _timingLaneRights[i];
                    lane = i;
                }
            }

            return lane;
        }

        private bool IsTimingMarkerSelected(TimingMarkerLayout marker)
        {
            if (marker.Kind == ChoreographyTimelineElementKind.Section)
            {
                return _selection.Kind == ChoreographyTimelineElementKind.Section
                    && _selection.SectionIndex == marker.SectionIndex;
            }

            return _selection.Kind == ChoreographyTimelineElementKind.Event
                && _selection.SectionIndex == marker.SectionIndex
                && _selection.EventIndex == marker.EventIndex;
        }

        private void DrawTimingSectionSpan(SerializedProperty section, int sectionIndex, float x, float width, float timingTop, float viewWidth)
        {
            if (x > viewWidth || x + width < 0f)
            {
                return;
            }

            bool selected = _selection.Kind == ChoreographyTimelineElementKind.Section && _selection.SectionIndex == sectionIndex;
            Rect span = new Rect(x, timingTop + TimingSectionTop, width, TimingSectionHeight);
            Color color = SectionColor;
            color.a = selected ? 0.26f : 0.12f;
            EditorGUI.DrawRect(span, color);

            if (width > 72f)
            {
                string id = GetString(section, "Id");
                _label.text = string.IsNullOrEmpty(id) ? ("Section " + sectionIndex) : id;
                GUI.Label(new Rect(span.x + 6f, span.y, span.width - 12f, span.height), _label, _markerLabelStyle);
            }
        }

        private void DrawEventTrackGuide(TimingMarkerLayout marker, bool selected)
        {
            float thickness = selected ? 2f : 1f;
            Color color = selected
                ? new Color(SelectionColor.r, SelectionColor.g, SelectionColor.b, 0.82f)
                : new Color(EventMarkerColor.r, EventMarkerColor.g, EventMarkerColor.b, 0.38f);
            EditorGUI.DrawRect(new Rect(
                marker.AnchorX - thickness * 0.5f,
                RulerHeight + SectionRowHeight,
                thickness,
                _lanes.Count * LaneHeight + TimingRowHeight),
                color);
        }

        private Rect DrawTimingMarker(TimingMarkerLayout marker, float timingTop, bool selected)
        {
            Rect rect = new Rect(marker.Rect.x, timingTop + marker.Rect.y, marker.Rect.width, marker.Rect.height);

            Color body = marker.Color;
            body.a = selected ? 1f : 0.86f;
            EditorGUI.DrawRect(rect, body);
            DrawOutline(rect, selected ? SelectionColor : new Color(0f, 0f, 0f, 0.40f), selected ? 2f : 1f);

            _label.text = marker.Text;
            GUI.Label(rect, _label, _markerLabelStyle);
            return rect;
        }

        private static void DrawTimingAnchor(float x, Rect markerRect, float timingTop, float viewWidth, Color color, bool selected)
        {
            if (viewWidth <= 1f || x < -1f || x > viewWidth + 1f)
            {
                return;
            }

            float anchorX = Mathf.Clamp(x, 0f, viewWidth - 1f);
            float thickness = selected ? TimingAnchorWidth + 1f : TimingAnchorWidth;
            Color anchorColor = selected
                ? new Color(SelectionColor.r, SelectionColor.g, SelectionColor.b, 0.86f)
                : new Color(color.r, color.g, color.b, 0.76f);

            EditorGUI.DrawRect(new Rect(anchorX - thickness * 0.5f, timingTop + 2f, thickness, TimingSectionTop - 4f), anchorColor);

            float connectorY = markerRect.yMax - 3f;
            if (markerRect.xMin > anchorX + 2f)
            {
                EditorGUI.DrawRect(new Rect(anchorX, connectorY - thickness * 0.5f, markerRect.xMin - anchorX, thickness), anchorColor);
            }

            EditorGUI.DrawRect(new Rect(markerRect.xMin - 2f, connectorY - thickness * 0.5f, 3f, thickness), anchorColor);
            DrawDiamond(new Vector2(anchorX, connectorY), selected ? TimingAnchorPinSize + 1f : TimingAnchorPinSize, anchorColor);
        }

        private static void DrawDiamond(Vector2 center, float size, Color color)
        {
            TimingDiamondPoints[0] = new Vector3(center.x, center.y - size);
            TimingDiamondPoints[1] = new Vector3(center.x + size, center.y);
            TimingDiamondPoints[2] = new Vector3(center.x, center.y + size);
            TimingDiamondPoints[3] = new Vector3(center.x - size, center.y);

            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = color;
            Handles.DrawAAConvexPolygon(TimingDiamondPoints);
            Handles.color = previousColor;
            Handles.EndGUI();
        }

        private static Rect GetTimingMarkerRect(float x, int laneIndex)
        {
            return new Rect(
                x,
                TimingMarkerTop + laneIndex * (TimingMarkerHeight + TimingMarkerLaneGap),
                TimingMarkerWidth,
                TimingMarkerHeight);
        }

        private void HandleMouseDown(SerializedProperty sections, Rect area, Rect timeRect)
        {
            Event evt = Event.current;
            if (evt.type != EventType.MouseDown || evt.button != 0 || !area.Contains(evt.mousePosition))
            {
                return;
            }

            Vector2 position = evt.mousePosition;
            float lanesTop = area.y + RulerHeight + SectionRowHeight;
            float timingTop = lanesTop + _lanes.Count * LaneHeight;

            if (position.x < area.x + LabelColumnWidth)
            {
                if (position.y >= lanesTop && position.y < timingTop)
                {
                    int laneIndex = Mathf.FloorToInt((position.y - lanesTop) / LaneHeight);
                    if (laneIndex >= 0 && laneIndex < _lanes.Count)
                    {
                        LaneInfo lane = _lanes[laneIndex];
                        _selection = ChoreographyTimelineSelection.Track(lane.FirstSectionIndex, lane.FirstTrackIndex);
                        evt.Use();
                    }
                }
                return;
            }

            if (!timeRect.Contains(position))
            {
                return;
            }

            float localX = position.x - timeRect.x;
            float localY = position.y - timeRect.y;
            float time = (localX + _scroll.x) / _pixelsPerSecond;

            if (localY >= RulerHeight && localY < RulerHeight + SectionRowHeight)
            {
                int sectionIndex = FindSectionAtTime(sections, time);
                if (sectionIndex >= 0)
                {
                    _selection = ChoreographyTimelineSelection.Section(sectionIndex);
                    evt.Use();
                }
                return;
            }

            if (localY >= timingTop - timeRect.y && localY < timingTop - timeRect.y + TimingRowHeight)
            {
                float timingLocalY = localY - (timingTop - timeRect.y);
                if (!TryPickTimingMarker(sections, timeRect.width, localX, timingLocalY, out _selection))
                {
                    int sectionIndex = FindSectionAtTime(sections, time);
                    if (sectionIndex >= 0)
                    {
                        _selection = ChoreographyTimelineSelection.Section(sectionIndex);
                    }
                }
                evt.Use();
                return;
            }

            if (localY >= RulerHeight + SectionRowHeight && localY < RulerHeight + SectionRowHeight + _lanes.Count * LaneHeight)
            {
                int laneIndex = Mathf.FloorToInt((localY - RulerHeight - SectionRowHeight) / LaneHeight);
                if (laneIndex >= 0 && laneIndex < _lanes.Count)
                {
                    if (!TryPickClip(sections, _lanes[laneIndex], time, out _selection))
                    {
                        LaneInfo lane = _lanes[laneIndex];
                        _selection = ChoreographyTimelineSelection.Track(lane.FirstSectionIndex, lane.FirstTrackIndex);
                    }
                    evt.Use();
                }
            }
        }

        private bool TryPickTimingMarker(SerializedProperty sections, float viewWidth, float localX, float localY, out ChoreographyTimelineSelection selection)
        {
            float markerRailBottom = TimingMarkerTop
                + TimingMarkerLaneCount * TimingMarkerHeight
                + (TimingMarkerLaneCount - 1) * TimingMarkerLaneGap;

            if (localY < TimingMarkerTop - MarkerHitPadding || localY > markerRailBottom + MarkerHitPadding)
            {
                selection = ChoreographyTimelineSelection.None();
                return false;
            }

            BuildTimingMarkers(sections, viewWidth);
            for (int i = _timingMarkers.Count - 1; i >= 0; i--)
            {
                TimingMarkerLayout marker = _timingMarkers[i];
                Rect rect = marker.Rect;
                rect.xMin -= MarkerHitPadding;
                rect.xMax += MarkerHitPadding;
                rect.yMin -= MarkerHitPadding;
                rect.yMax += MarkerHitPadding;
                if (!rect.Contains(new Vector2(localX, localY)))
                {
                    continue;
                }

                if (marker.Kind == ChoreographyTimelineElementKind.Event)
                {
                    selection = ChoreographyTimelineSelection.Event(marker.SectionIndex, marker.EventIndex);
                    return true;
                }

                selection = ChoreographyTimelineSelection.Section(marker.SectionIndex);
                return true;
            }

            selection = ChoreographyTimelineSelection.None();
            return false;
        }

        private bool TryPickClip(SerializedProperty sections, LaneInfo lane, float time, out ChoreographyTimelineSelection selection)
        {
            float sectionStart = 0f;
            float oneShotTolerance = 7f / Mathf.Max(1f, _pixelsPerSecond);

            for (int s = 0; s < sections.arraySize; s++)
            {
                SerializedProperty section = sections.GetArrayElementAtIndex(s);
                float sectionDuration = Mathf.Max(0f, GetFloat(section, "Duration"));
                SerializedProperty tracks = section.FindPropertyRelative("Tracks");

                if (tracks != null)
                {
                    for (int t = 0; t < tracks.arraySize; t++)
                    {
                        SerializedProperty track = tracks.GetArrayElementAtIndex(t);
                        if (!LaneMatches(lane, track))
                        {
                            continue;
                        }

                        SerializedProperty clips = track.FindPropertyRelative("Clips");
                        if (clips == null)
                        {
                            continue;
                        }

                        for (int c = clips.arraySize - 1; c >= 0; c--)
                        {
                            SerializedProperty clip = clips.GetArrayElementAtIndex(c);
                            float start = sectionStart + Mathf.Max(0f, GetFloat(clip, "StartTime"));
                            float duration = GetFloat(clip, "Duration");
                            if (duration <= 0f)
                            {
                                if (Mathf.Abs(time - start) <= oneShotTolerance)
                                {
                                    selection = ChoreographyTimelineSelection.Clip(s, t, c);
                                    return true;
                                }
                            }
                            else
                            {
                                float end = GetBool(clip, "Loop")
                                    ? sectionStart + Mathf.Max(sectionDuration, GetFloat(clip, "StartTime"))
                                    : start + duration;
                                if (time >= start && time <= end)
                                {
                                    selection = ChoreographyTimelineSelection.Clip(s, t, c);
                                    return true;
                                }
                            }
                        }
                    }
                }

                sectionStart += sectionDuration;
            }

            selection = ChoreographyTimelineSelection.None();
            return false;
        }

        private bool TryPickEvent(SerializedProperty sections, float time, out ChoreographyTimelineSelection selection)
        {
            float sectionStart = 0f;
            float tolerance = MarkerHitPadding / Mathf.Max(1f, _pixelsPerSecond);

            for (int s = 0; s < sections.arraySize; s++)
            {
                SerializedProperty section = sections.GetArrayElementAtIndex(s);
                float sectionDuration = Mathf.Max(0f, GetFloat(section, "Duration"));
                SerializedProperty events = section.FindPropertyRelative("Events");

                if (events != null)
                {
                    for (int e = 0; e < events.arraySize; e++)
                    {
                        float eventTime = sectionStart + Mathf.Max(0f, GetFloat(events.GetArrayElementAtIndex(e), "Time"));
                        if (Mathf.Abs(time - eventTime) <= tolerance)
                        {
                            selection = ChoreographyTimelineSelection.Event(s, e);
                            return true;
                        }
                    }
                }

                sectionStart += sectionDuration;
            }

            selection = ChoreographyTimelineSelection.None();
            return false;
        }

        private int FindSectionAtTime(SerializedProperty sections, float time)
        {
            float cursor = 0f;
            for (int i = 0; i < sections.arraySize; i++)
            {
                float duration = Mathf.Max(0f, GetFloat(sections.GetArrayElementAtIndex(i), "Duration"));
                if (time >= cursor && time <= cursor + duration)
                {
                    return i;
                }
                cursor += duration;
            }
            return sections.arraySize > 0 && time >= cursor ? sections.arraySize - 1 : -1;
        }

        private Rect GetClipRect(SerializedProperty clip, float sectionStart, float sectionDuration, float laneY)
        {
            float start = Mathf.Max(0f, GetFloat(clip, "StartTime"));
            float duration = GetFloat(clip, "Duration");
            float end;

            if (duration <= 0f)
            {
                end = start;
            }
            else if (GetBool(clip, "Loop"))
            {
                end = Mathf.Max(sectionDuration, start);
            }
            else
            {
                end = start + duration;
            }

            float x = (sectionStart + start) * _pixelsPerSecond - _scroll.x;
            float width = Mathf.Max(MinClipWidth, (end - start) * _pixelsPerSecond);
            return new Rect(x, laneY + 4f, width, LaneHeight - 8f);
        }

        private void BuildLanes(SerializedProperty sections)
        {
            _lanes.Clear();
            _totalDuration = 0f;

            if (sections == null)
            {
                return;
            }

            for (int s = 0; s < sections.arraySize; s++)
            {
                SerializedProperty section = sections.GetArrayElementAtIndex(s);
                _totalDuration += Mathf.Max(0f, GetFloat(section, "Duration"));

                SerializedProperty tracks = section.FindPropertyRelative("Tracks");
                if (tracks == null)
                {
                    continue;
                }

                for (int t = 0; t < tracks.arraySize; t++)
                {
                    SerializedProperty track = tracks.GetArrayElementAtIndex(t);
                    ChoreographyTrackKind kind = GetTrackKind(track);
                    string id = GetString(track, "Id");
                    if (FindLaneIndex(kind, id) >= 0)
                    {
                        continue;
                    }

                    _lanes.Add(new LaneInfo(kind, NormalizeTrackId(kind, id), BuildLaneLabel(kind, id), s, t));
                }
            }
        }

        private int FindLaneIndex(ChoreographyTrackKind kind, string id)
        {
            string normalized = NormalizeTrackId(kind, id);
            for (int i = 0; i < _lanes.Count; i++)
            {
                LaneInfo lane = _lanes[i];
                if (lane.Kind == kind && lane.NormalizedId == normalized)
                {
                    return i;
                }
            }
            return -1;
        }

        private static bool LaneMatches(LaneInfo lane, SerializedProperty track)
        {
            ChoreographyTrackKind kind = GetTrackKind(track);
            return lane.Kind == kind && lane.NormalizedId == NormalizeTrackId(kind, GetString(track, "Id"));
        }

        private bool IsSelectionOnLane(LaneInfo lane)
        {
            if (_selection.Kind != ChoreographyTimelineElementKind.Track && _selection.Kind != ChoreographyTimelineElementKind.Clip)
            {
                return false;
            }

            return _selection.SectionIndex == lane.FirstSectionIndex && _selection.TrackIndex == lane.FirstTrackIndex;
        }

        private float PickTickStep()
        {
            float target = 68f / Mathf.Max(1f, _pixelsPerSecond);
            for (int i = 0; i < NiceSteps.Length; i++)
            {
                if (NiceSteps[i] >= target)
                {
                    return NiceSteps[i];
                }
            }
            return NiceSteps[NiceSteps.Length - 1];
        }

        private static string BuildLaneLabel(ChoreographyTrackKind kind, string id)
        {
            return string.IsNullOrEmpty(id) ? kind.ToString() : kind + " / " + id;
        }

        private static string NormalizeTrackId(ChoreographyTrackKind kind, string id)
        {
            return string.IsNullOrEmpty(id) ? kind.ToString() : id;
        }

        private static ChoreographyTrackKind GetTrackKind(SerializedProperty track)
        {
            int index = GetEnumIndex(track, "Kind");
            if (index < 0 || index > (int)ChoreographyTrackKind.Custom)
            {
                return ChoreographyTrackKind.Custom;
            }
            return (ChoreographyTrackKind)index;
        }

        private static Color LaneColor(ChoreographyTrackKind kind)
        {
            switch (kind)
            {
                case ChoreographyTrackKind.Animation:
                    return AnimationColor;
                case ChoreographyTrackKind.Audio:
                    return AudioColor;
                case ChoreographyTrackKind.Vfx:
                    return VfxColor;
                case ChoreographyTrackKind.Event:
                    return EventTrackColor;
                default:
                    return CustomColor;
            }
        }

        private static void DrawOutline(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static float GetFloat(SerializedProperty owner, string relative)
        {
            SerializedProperty property = owner.FindPropertyRelative(relative);
            if (property == null)
            {
                return 0f;
            }

            if (relative == "StartTime" || relative == "Duration" || relative == "Time")
            {
                return (float)property.doubleValue;
            }

            return property.floatValue;
        }

        private static int GetInt(SerializedProperty owner, string relative)
        {
            SerializedProperty property = owner.FindPropertyRelative(relative);
            return property != null ? property.intValue : 0;
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

        private static string GetNestedString(SerializedProperty owner, string relative, string nested)
        {
            SerializedProperty property = owner.FindPropertyRelative(relative);
            if (property == null)
            {
                return string.Empty;
            }
            return GetString(property, nested);
        }

        private static string GetResourceLocation(SerializedProperty clip)
        {
            SerializedProperty resource = clip.FindPropertyRelative("Resource");
            if (resource == null)
            {
                return string.Empty;
            }

            SerializedProperty source = resource.FindPropertyRelative("Source");
            if (source != null && source.enumValueIndex == (int)ChoreographyResourceSource.AssetReference)
            {
                SerializedProperty asset = resource.FindPropertyRelative("Asset");
                string assetLocation = asset != null ? GetString(asset, "m_Location") : string.Empty;
                if (!string.IsNullOrEmpty(assetLocation))
                {
                    return assetLocation;
                }
            }

            string location = GetString(resource, "Address");
            if (!string.IsNullOrEmpty(location))
            {
                return location;
            }

            if (source == null || source.enumValueIndex != (int)ChoreographyResourceSource.AssetReference)
            {
                return string.Empty;
            }

            SerializedProperty fallbackAsset = resource.FindPropertyRelative("Asset");
            return fallbackAsset != null ? GetString(fallbackAsset, "m_Location") : string.Empty;
        }

        private static int GetEnumIndex(SerializedProperty owner, string relative)
        {
            SerializedProperty property = owner.FindPropertyRelative(relative);
            return property != null ? property.enumValueIndex : -1;
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

        private void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }

            _clipLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            _clipLabelStyle.normal.textColor = Color.white;

            _laneLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };

            _sectionLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            _sectionLabelStyle.normal.textColor = Color.white;

            _rulerLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            _rulerLabelStyle.normal.textColor = new Color(0.84f, 0.84f, 0.84f, 1f);

            _markerLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip
            };
            _markerLabelStyle.normal.textColor = Color.white;

            _loopBadgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip
            };
            _loopBadgeStyle.normal.textColor = Color.white;

            _stylesReady = true;
        }

        private struct LaneInfo
        {
            public readonly ChoreographyTrackKind Kind;
            public readonly string NormalizedId;
            public readonly string Label;
            public readonly int FirstSectionIndex;
            public readonly int FirstTrackIndex;

            public LaneInfo(ChoreographyTrackKind kind, string normalizedId, string label, int firstSectionIndex, int firstTrackIndex)
            {
                Kind = kind;
                NormalizedId = normalizedId;
                Label = label;
                FirstSectionIndex = firstSectionIndex;
                FirstTrackIndex = firstTrackIndex;
            }
        }

        private struct TimingMarkerLayout
        {
            public readonly ChoreographyTimelineElementKind Kind;
            public readonly int SectionIndex;
            public readonly int EventIndex;
            public readonly float AbsoluteTime;
            public readonly float AnchorX;
            public readonly string Text;
            public readonly Color Color;
            public Rect Rect;

            public TimingMarkerLayout(
                ChoreographyTimelineElementKind kind,
                int sectionIndex,
                int eventIndex,
                float absoluteTime,
                float anchorX,
                string text,
                Color color)
            {
                Kind = kind;
                SectionIndex = sectionIndex;
                EventIndex = eventIndex;
                AbsoluteTime = absoluteTime;
                AnchorX = anchorX;
                Text = text;
                Color = color;
                Rect = default;
            }
        }
    }
}
