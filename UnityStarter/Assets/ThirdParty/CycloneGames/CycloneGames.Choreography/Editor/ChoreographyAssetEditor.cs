using System.Collections.Generic;
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
        private static readonly GUIContent DeleteButton = new GUIContent(
            "Delete", "Deletes the selected element.");
        private static readonly GUIContent AdvancedFoldout = new GUIContent(
            "Advanced", "Fallback tools for inspecting or bulk-editing the serialized authoring model.");
        private static readonly GUIContent ResourceAssetRefMode = new GUIContent(
            "Asset Ref", "Use CycloneGames.AssetManagement AssetRef for runtime loading.");
        private static readonly GUIContent ResourceLocationMode = new GUIContent(
            "Location", "Use a raw provider location string for custom loaders or external banks.");
        private static readonly GUIContent ResourceAssetLabel = new GUIContent(
            "Asset", "Drag an asset here. AssetRef stores its GUID and location path.");
        private static readonly GUIContent ResourceLocationLabel = new GUIContent(
            "Location", "Provider location key used when AssetRef is not active.");

        private readonly List<string> _diagnostics = new List<string>(16);
        private readonly HashSet<string> _sectionIds = new HashSet<string>();
        private readonly HashSet<string> _trackIds = new HashSet<string>();
        private readonly HashSet<string> _clipIds = new HashSet<string>();

        private SerializedProperty _assetId;
        private SerializedProperty _sections;
        private ChoreographyTimelineView _timeline;
        private bool _showAdvanced;
        private bool _showRawData;
        private bool _showDiagnostics = true;
        private bool _showSectionOrder = true;

        private void OnEnable()
        {
            _assetId = serializedObject.FindProperty("AssetId");
            _sections = serializedObject.FindProperty("Sections");
            _timeline = new ChoreographyTimelineView();
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
            DrawWorkspace();
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

        private void DrawWorkspace()
        {
            InspectorUi.DrawPanelHeader(
                "Choreography Workspace",
                "Montage-style section, track, clip, and timing visualization.",
                InspectorUi.BlueAccent);

            using (InspectorUi.PanelScope())
            {
                _timeline.Draw(_sections);
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
                    EditorGUILayout.HelpBox("Select a section, track, clip, or event in the workspace.", MessageType.None);
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
            SerializedProperty kind = resource.FindPropertyRelative("Kind");
            SerializedProperty tag = resource.FindPropertyRelative("Tag");

            ChoreographyResourceSource currentSource = GetResourceSource(source);
            DrawResourceSourceButtons(source, asset, address, currentSource);

            currentSource = GetResourceSource(source);
            if (currentSource == ChoreographyResourceSource.AssetReference)
            {
                DrawAssetRefResourceField(asset);
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
            ChoreographyResourceSource currentSource)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Source", GUILayout.Width(72f));

            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = currentSource == ChoreographyResourceSource.AssetReference
                ? InspectorUi.BlueAccent
                : new Color(0.50f, 0.50f, 0.50f, 0.70f);
            if (GUILayout.Button(ResourceAssetRefMode, EditorStyles.miniButtonLeft))
            {
                SwitchResourceSource(source, asset, address, ChoreographyResourceSource.AssetReference);
            }

            GUI.backgroundColor = currentSource == ChoreographyResourceSource.Location
                ? InspectorUi.WarningAccent
                : new Color(0.50f, 0.50f, 0.50f, 0.70f);
            if (GUILayout.Button(ResourceLocationMode, EditorStyles.miniButtonRight))
            {
                SwitchResourceSource(source, asset, address, ChoreographyResourceSource.Location);
            }

            GUI.backgroundColor = previous;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAssetRefResourceField(SerializedProperty asset)
        {
            Rect row = EditorGUILayout.GetControlRect();
            Rect labelRect = new Rect(row.x, row.y, 72f, row.height);
            Rect fieldRect = new Rect(row.x + 72f, row.y, row.width - 144f, row.height);
            Rect pingRect = new Rect(row.xMax - 66f, row.y, 66f, row.height);

            EditorGUI.LabelField(labelRect, ResourceAssetLabel);
            EditorGUI.PropertyField(fieldRect, asset, GUIContent.none);

            string location = GetAssetRefLocation(asset);
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(location));
            if (GUI.Button(pingRect, "Ping", EditorStyles.miniButton))
            {
                PingAssetRef(asset);
            }
            EditorGUI.EndDisabledGroup();

            if (string.IsNullOrEmpty(location))
            {
                EditorGUILayout.HelpBox(
                    "Drag an asset into the Asset field. AssetRef stores GUID and path, then runtime loading goes through CycloneGames.AssetManagement.",
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
            resource.FindPropertyRelative("Source").enumValueIndex = (int)ChoreographyResourceSource.AssetReference;
            SerializedProperty asset = resource.FindPropertyRelative("Asset");
            asset.FindPropertyRelative("m_GUID").stringValue = string.Empty;
            asset.FindPropertyRelative("m_Location").stringValue = string.Empty;
            resource.FindPropertyRelative("Address").stringValue = string.Empty;
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

            return source.enumValueIndex == (int)ChoreographyResourceSource.AssetReference
                ? ChoreographyResourceSource.AssetReference
                : ChoreographyResourceSource.Location;
        }

        private static void SwitchResourceSource(
            SerializedProperty source,
            SerializedProperty asset,
            SerializedProperty address,
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

            if (targetSource == ChoreographyResourceSource.AssetReference)
            {
                TrySetAssetRefFromLocation(asset, address != null ? address.stringValue : string.Empty);
            }
            else
            {
                string location = GetAssetRefLocation(asset);
                if (!string.IsNullOrEmpty(location) && address != null)
                {
                    address.stringValue = location;
                }
            }

            source.enumValueIndex = (int)targetSource;
        }

        private static bool TrySetAssetRefFromLocation(SerializedProperty asset, string location)
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

            SerializedProperty guid = asset.FindPropertyRelative("m_GUID");
            SerializedProperty assetLocation = asset.FindPropertyRelative("m_Location");
            if (guid == null || assetLocation == null)
            {
                return false;
            }

            guid.stringValue = AssetDatabase.AssetPathToGUID(location);
            assetLocation.stringValue = location;
            return true;
        }

        private static string GetAssetRefLocation(SerializedProperty asset)
        {
            if (asset == null)
            {
                return string.Empty;
            }

            SerializedProperty location = asset.FindPropertyRelative("m_Location");
            return location != null ? location.stringValue : string.Empty;
        }

        private static void PingAssetRef(SerializedProperty asset)
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
            public static Color GreenAccent => new Color(0.188f, 0.690f, 0.741f, 1f);
            public static Color WarningAccent => new Color(0.925f, 0.576f, 0.251f, 1f);
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
