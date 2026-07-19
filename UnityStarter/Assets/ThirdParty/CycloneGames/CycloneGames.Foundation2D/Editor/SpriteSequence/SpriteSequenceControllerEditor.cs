#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CycloneGames.Foundation2D.Runtime;

namespace CycloneGames.Foundation2D.Editor
{
    [CustomEditor(typeof(SpriteSequenceController))]
    [CanEditMultipleObjects]
    public sealed class SpriteSequenceControllerEditor : UnityEditor.Editor
    {
        private enum PreviewSourceMode
        {
            Auto = 0,
            AssetPreview = 1,
            RawUV = 2,
        }

        private sealed class NaturalSpriteNameComparer : IComparer<Sprite>
        {
            public int Compare(Sprite a, Sprite b)
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                return EditorUtility.NaturalCompare(a.name, b.name);
            }
        }

        private const int DefaultDragBufferCapacity = 128;
        private const int DefaultSortBufferCapacity = 256;
        private const int MaximumRetainedSpriteBufferCapacity = 1024;

        private static readonly NaturalSpriteNameComparer SpriteNameComparer = new();
        private static readonly List<Sprite> DragSpriteBuffer = new(DefaultDragBufferCapacity);
        private static readonly List<Sprite> SortSpriteBuffer = new(DefaultSortBufferCapacity);

        private static readonly GUIContent ModuleTitle = new("Sprite Sequence Controller");
        private static readonly GUIContent ModuleSubtitle = new("Frame authoring, deterministic playback settings, renderer binding, and preview.");
        private static readonly GUIContent SectionFrames = new("Frames");
        private static readonly GUIContent SectionPlayback = new("Playback");
        private static readonly GUIContent SectionLoop = new("Loop Controls");
        private static readonly GUIContent SectionRenderer = new("Renderer Binding");
        private static readonly GUIContent SectionPreview = new("Animation Preview");
        private static readonly GUIContent BadgeAuthoring = new("AUTHORING");
        private static readonly GUIContent BadgeRuntime = new("RUNTIME");
        private static readonly GUIContent BadgeBinding = new("BINDING");
        private static readonly GUIContent BadgePreview = new("EDITOR ONLY");
        private static readonly GUIContent LabelSprites = new("Sprites");
        private static readonly GUIContent LabelFrameRate = new("Frame Rate (fps)");
        private static readonly GUIContent LabelSpeedMultiplier = new("Speed Multiplier");
        private static readonly GUIContent LabelUseDiscreteSpeedMultiplier = new("Use Discrete Speed Multiplier");
        private static readonly GUIContent LabelDiscreteStepCount = new("Discrete Step Count");
        private static readonly GUIContent LabelDiscreteRange = new("Discrete Multiplier Range");
        private static readonly GUIContent LabelWarnWhenSpeedOutOfRange = new("Warn When Speed Out Of Range");
        private static readonly GUIContent LabelPlayMode = new("Play Mode");
        private static readonly GUIContent LabelDirection = new("Direction");
        private static readonly GUIContent LabelUpdateDriver = new("Update Driver");
        private static readonly GUIContent LabelPlayOnEnable = new("Play On Enable");
        private static readonly GUIContent LabelIgnoreTimeScale = new("Ignore TimeScale");
        private static readonly GUIContent LabelFallbackToMonoUpdate = new("Fallback To Mono Update");
        private static readonly GUIContent LabelMaxFrameAdvancesPerUpdate = new("Max Frame Advances Per Update");
        private static readonly GUIContent LabelLoopInterval = new("Loop Interval (sec)");
        private static readonly GUIContent LabelIntervalHoldFrame = new("Interval Hold Frame");
        private static readonly GUIContent LabelUseFiniteLoopCount = new("Use Finite Loop Count");
        private static readonly GUIContent LabelMaxLoopCount = new("Max Loop Count");
        private static readonly GUIContent LabelRendererComponent = new("Renderer Component");
        private static readonly GUIContent LabelRenderMode = new("Render Mode");
        private static readonly GUIContent LabelFlipbookMaterial = new("Flipbook Shared Material");
        private static readonly GUIContent LabelArraySize = new("Size");
        private static readonly GUIContent LabelMaterialStrategy = new("Material Strategy");
        private static readonly GUIContent LabelSharedMaterialOverride = new("Shared Material Override");
        private static readonly GUIContent LabelSpritesFoldout = new("Sprites List");
        private static readonly GUIContent LabelPreviewSource = new("Preview Source");
        private static readonly GUIContent LabelPreviousPage = new("Previous");
        private static readonly GUIContent LabelNextPage = new("Next");

        private static readonly string[] ExplicitlyDrawnProperties =
        {
            "frames",
            "frameRate",
            "playMode",
            "playDirection",
            "updateDriver",
            "fallbackToMonoUpdateWhenBurstUnavailable",
            "maxFrameAdvancesPerUpdate",
            "playOnEnable",
            "ignoreTimeScale",
            "speedMultiplier",
            "useDiscreteSpeedMultiplier",
            "discreteSpeedStepCount",
            "discreteSpeedMultiplierRange",
            "warnWhenSpeedOutOfRange",
            "loopInterval",
            "intervalHoldFrame",
            "useFiniteLoopCount",
            "maxLoopCount",
            "rendererComponent",
        };

        private SerializedProperty _frames;
        private SerializedProperty _frameRate;
        private SerializedProperty _playMode;
        private SerializedProperty _playDirection;
        private SerializedProperty _updateDriver;
        private SerializedProperty _fallbackToMonoUpdateWhenBurstUnavailable;
        private SerializedProperty _maxFrameAdvancesPerUpdate;
        private SerializedProperty _playOnEnable;
        private SerializedProperty _ignoreTimeScale;
        private SerializedProperty _speedMultiplier;
        private SerializedProperty _useDiscreteSpeedMultiplier;
        private SerializedProperty _discreteSpeedStepCount;
        private SerializedProperty _discreteSpeedMultiplierRange;
        private SerializedProperty _warnWhenSpeedOutOfRange;

        private SerializedProperty _loopInterval;
        private SerializedProperty _intervalHoldFrame;
        private SerializedProperty _useFiniteLoopCount;
        private SerializedProperty _maxLoopCount;

        private SerializedProperty _rendererComponent;

        private bool _foldFrames = true;
        private bool _foldPlayback = true;
        private bool _foldLoop;
        private bool _foldRenderer = true;
        private bool _foldPreview;
        private bool _foldSpritesList = true;

        private bool _serializedPropertiesValid;
        private string _serializedPropertiesError;

        private Vector2 _thumbScrollPos;
        private int _framePage;
        private int _cachedFrameLabelStart = -1;
        private readonly GUIContent[] _frameElementLabels = new GUIContent[FramesPerPage];
        private readonly GUIContent[] _frameIndexLabels = new GUIContent[FramesPerPage];

        private bool _previewPlaying;
        private PreviewSourceMode _previewSourceMode = PreviewSourceMode.Auto;
        private SpriteSequencePlaybackState _previewState;
        private double _previewLastTime;

        private const float ThumbSize = 48f;
        private const float ThumbSpacing = 2f;
        private const float DropZoneHeight = 38f;
        private const int FramesPerPage = 24;
        private const int DefaultPreviewCatchUpFrameBudget = 64;

        private void OnEnable()
        {
            _frames = serializedObject.FindProperty("frames");
            _frameRate = serializedObject.FindProperty("frameRate");
            _playMode = serializedObject.FindProperty("playMode");
            _playDirection = serializedObject.FindProperty("playDirection");
            _updateDriver = serializedObject.FindProperty("updateDriver");
            _fallbackToMonoUpdateWhenBurstUnavailable = serializedObject.FindProperty("fallbackToMonoUpdateWhenBurstUnavailable");
            _maxFrameAdvancesPerUpdate = serializedObject.FindProperty("maxFrameAdvancesPerUpdate");
            _playOnEnable = serializedObject.FindProperty("playOnEnable");
            _ignoreTimeScale = serializedObject.FindProperty("ignoreTimeScale");
            _speedMultiplier = serializedObject.FindProperty("speedMultiplier");
            _useDiscreteSpeedMultiplier = serializedObject.FindProperty("useDiscreteSpeedMultiplier");
            _discreteSpeedStepCount = serializedObject.FindProperty("discreteSpeedStepCount");
            _discreteSpeedMultiplierRange = serializedObject.FindProperty("discreteSpeedMultiplierRange");
            _warnWhenSpeedOutOfRange = serializedObject.FindProperty("warnWhenSpeedOutOfRange");

            _loopInterval = serializedObject.FindProperty("loopInterval");
            _intervalHoldFrame = serializedObject.FindProperty("intervalHoldFrame");
            _useFiniteLoopCount = serializedObject.FindProperty("useFiniteLoopCount");
            _maxLoopCount = serializedObject.FindProperty("maxLoopCount");

            _rendererComponent = serializedObject.FindProperty("rendererComponent");
            _serializedPropertiesValid = Foundation2DInspectorUi.ValidateRequiredProperties(
                serializedObject,
                nameof(SpriteSequenceControllerEditor),
                ExplicitlyDrawnProperties,
                out _serializedPropertiesError);

            for (int i = 0; i < FramesPerPage; i++)
            {
                _frameElementLabels[i] ??= new GUIContent();
                _frameIndexLabels[i] ??= new GUIContent();
            }

            EditorApplication.update -= OnEditorPreviewTick;
            _previewPlaying = false;
            _previewState = default;
            _framePage = 0;
            _cachedFrameLabelStart = -1;
        }

        private void OnDisable()
        {
            StopPreview();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (!_serializedPropertiesValid)
            {
                Foundation2DInspectorUi.DrawInvalidSerializedPropertyState(
                    serializedObject,
                    ModuleTitle,
                    ModuleSubtitle,
                    _serializedPropertiesError);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            Foundation2DInspectorUi.DrawModuleHeader(ModuleTitle, ModuleSubtitle);
            if (serializedObject.isEditingMultipleObjects)
            {
                Foundation2DInspectorUi.DrawMultiObjectActionNotice();
            }

            DrawFramesSection();
            DrawPlaybackSection();
            DrawLoopSection();
            DrawRendererSection();
            DrawPreviewSection();
            DrawRuntimeControls();
            Foundation2DInspectorUi.DrawRemainingProperties(serializedObject, ExplicitlyDrawnProperties);

            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed && (_previewPlaying || _previewState.IsPaused))
            {
                bool remainPaused = _previewState.IsPaused;
                ResetPreviewState(true);
                if (remainPaused)
                {
                    _previewState.Pause();
                }
            }
        }

        private void DrawFramesSection()
        {
            if (!Foundation2DInspectorUi.DrawSectionHeader(
                    ref _foldFrames,
                    SectionFrames,
                    BadgeAuthoring,
                    Foundation2DInspectorUi.BadgeTone.Neutral))
            {
                return;
            }

            using (Foundation2DInspectorUi.BeginCard())
            {
                using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects))
                {
                    DrawDropZone();

                    Rect spritesFoldoutRect = EditorGUILayout.GetControlRect();
                    spritesFoldoutRect.xMin += 8f;
                    _foldSpritesList = EditorGUI.Foldout(spritesFoldoutRect, _foldSpritesList, LabelSpritesFoldout, true);
                    if (_foldSpritesList)
                    {
                        DrawFramesArrayWithoutFoldoutArrow();
                    }
                    else
                    {
                        DrawCollapsedSpritesSummary();
                    }

                    int actionFrameCount = _frames.arraySize;
                    if (actionFrameCount > 0)
                    {
                        using (Foundation2DInspectorUi.BeginActionLayout(3, 88f))
                        {
                            if (GUILayout.Button("Sort by Name"))
                            {
                                SortFramesByName();
                            }

                            if (GUILayout.Button("Reverse"))
                            {
                                ReverseFrames();
                            }

                            if (GUILayout.Button("Clear") &&
                                EditorUtility.DisplayDialog("Clear Frame Sequence", $"Clear all {actionFrameCount} frames?", "Clear", "Cancel"))
                            {
                                _frames.ClearArray();
                                _framePage = 0;
                                OnFrameStructureChanged();
                            }
                        }

                        DrawFrameStrip(_frames.arraySize);
                    }
                }

                int count = _frames.arraySize;
                if (count > 0)
                {
                    EditorGUILayout.LabelField($"Frame Count: {count}", EditorStyles.miniLabel);

                    float fps = Mathf.Max(0.01f, _frameRate.floatValue);
                    float duration = count / fps;
                    EditorGUILayout.LabelField($"Cycle Duration: {duration:F2}s", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.HelpBox("Assign sprite frames to start playback.", MessageType.Info);
                }
            }
        }

        private void DrawPlaybackSection()
        {
            if (!Foundation2DInspectorUi.DrawSectionHeader(
                    ref _foldPlayback,
                    SectionPlayback,
                    BadgeRuntime,
                    Foundation2DInspectorUi.BadgeTone.Good))
            {
                return;
            }

            using (Foundation2DInspectorUi.BeginCard())
            {
                EditorGUILayout.PropertyField(_frameRate, LabelFrameRate, false);
                EditorGUILayout.PropertyField(_speedMultiplier, LabelSpeedMultiplier, false);
                EditorGUILayout.PropertyField(_useDiscreteSpeedMultiplier, LabelUseDiscreteSpeedMultiplier, false);
                if (!_useDiscreteSpeedMultiplier.hasMultipleDifferentValues && _useDiscreteSpeedMultiplier.boolValue)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.PropertyField(_discreteSpeedStepCount, LabelDiscreteStepCount, false);
                        EditorGUILayout.PropertyField(_discreteSpeedMultiplierRange, LabelDiscreteRange, false);
                        EditorGUILayout.PropertyField(_warnWhenSpeedOutOfRange, LabelWarnWhenSpeedOutOfRange, false);
                        EditorGUILayout.HelpBox("This maps speedMultiplier to the nearest evenly-spaced step in the configured range. Start with 5-7 steps, then tune by visual feel and batching results.", MessageType.Info);

                        Vector2 range = _discreteSpeedMultiplierRange.vector2Value;
                        float min = Mathf.Min(range.x, range.y);
                        float max = Mathf.Max(range.x, range.y);
                        float raw = _speedMultiplier.floatValue;
                        if (raw < min || raw > max)
                        {
                            EditorGUILayout.HelpBox($"Current Speed Multiplier ({raw:F3}) is outside configured range [{min:F3}, {max:F3}] and will be clamped before quantization.", MessageType.Warning);
                        }
                    }
                }

                EditorGUILayout.PropertyField(_playMode, LabelPlayMode, false);
                EditorGUILayout.PropertyField(_playDirection, LabelDirection, false);
                EditorGUILayout.PropertyField(_updateDriver, LabelUpdateDriver, false);
                EditorGUILayout.PropertyField(_playOnEnable, LabelPlayOnEnable, false);
                EditorGUILayout.PropertyField(_ignoreTimeScale, LabelIgnoreTimeScale, false);

                if (_maxFrameAdvancesPerUpdate != null)
                {
                    EditorGUILayout.PropertyField(_maxFrameAdvancesPerUpdate, LabelMaxFrameAdvancesPerUpdate, false);
                    EditorGUILayout.HelpBox("This budget bounds catch-up work after a stall. Excess accumulated animation time is discarded deterministically after the configured number of frame advances.", MessageType.None);
                }

                bool driverIsMixed = _updateDriver.hasMultipleDifferentValues;
                bool usesBurstDriver = !driverIsMixed &&
                                       _updateDriver.enumValueIndex == (int)SpriteSequenceController.UpdateDriver.BurstManaged;
                if ((driverIsMixed || usesBurstDriver) && _fallbackToMonoUpdateWhenBurstUnavailable != null)
                {
                    EditorGUILayout.PropertyField(_fallbackToMonoUpdateWhenBurstUnavailable, LabelFallbackToMonoUpdate, false);
                }

                if (usesBurstDriver)
                {
                    EditorGUILayout.HelpBox("BurstManaged requires the optional Foundation2D Burst integration and an active SpriteSequenceBurstManager. The fallback setting controls behavior when that integration is unavailable.", MessageType.Info);
                }
            }

        }

        private void DrawLoopSection()
        {
            if (!Foundation2DInspectorUi.DrawSectionHeader(
                    ref _foldLoop,
                    SectionLoop,
                    BadgeRuntime,
                    Foundation2DInspectorUi.BadgeTone.Neutral))
            {
                return;
            }

            using (Foundation2DInspectorUi.BeginCard())
            {
                EditorGUILayout.PropertyField(_loopInterval, LabelLoopInterval, false);
                EditorGUILayout.PropertyField(_intervalHoldFrame, LabelIntervalHoldFrame, false);
                EditorGUILayout.PropertyField(_useFiniteLoopCount, LabelUseFiniteLoopCount, false);

                if (!_useFiniteLoopCount.hasMultipleDifferentValues && _useFiniteLoopCount.boolValue)
                {
                    EditorGUILayout.PropertyField(_maxLoopCount, LabelMaxLoopCount, false);
                }
            }
        }

        private void DrawRendererSection()
        {
            if (!Foundation2DInspectorUi.DrawSectionHeader(
                    ref _foldRenderer,
                    SectionRenderer,
                    BadgeBinding,
                    Foundation2DInspectorUi.BadgeTone.Neutral))
            {
                return;
            }

            using (Foundation2DInspectorUi.BeginCard())
            {
                EditorGUILayout.PropertyField(_rendererComponent, LabelRendererComponent, false);
                if (_rendererComponent.hasMultipleDifferentValues)
                {
                    EditorGUILayout.HelpBox("Selected controllers use different renderer bindings. Set a common binding or inspect one controller at a time to navigate to its renderer.", MessageType.Info);
                    return;
                }

                Component rendererComponent = _rendererComponent.objectReferenceValue as Component;
                if (rendererComponent == null && target is SpriteSequenceController controller)
                {
                    rendererComponent = controller.GetComponent<ISpriteSequenceRenderer>() as Component;
                }

                if (_rendererComponent.objectReferenceValue != null &&
                    _rendererComponent.objectReferenceValue is not ISpriteSequenceRenderer)
                {
                    EditorGUILayout.HelpBox("The assigned component does not implement ISpriteSequenceRenderer. Runtime playback cannot use this binding.", MessageType.Error);
                }
                else if (_rendererComponent.objectReferenceValue == null && rendererComponent != null)
                {
                    EditorGUILayout.HelpBox($"Runtime will automatically resolve {rendererComponent.GetType().Name} on this GameObject. Assign it explicitly when more than one renderer adapter is present.", MessageType.Info);
                }
                else if (rendererComponent == null)
                {
                    EditorGUILayout.HelpBox("No ISpriteSequenceRenderer is assigned or available on this GameObject. Add UGUISequenceRenderer or SpriteRendererSequenceRenderer.", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox($"Renderer-specific settings are edited in the {rendererComponent.GetType().Name} component Inspector. Controller preview and playback settings stay here.", MessageType.None);
                }

                if (rendererComponent != null)
                {
                    using (Foundation2DInspectorUi.BeginActionLayout(2, 110f))
                    {
                        if (GUILayout.Button("Ping Renderer"))
                        {
                            EditorGUIUtility.PingObject(rendererComponent);
                        }

                        if (GUILayout.Button("Select Renderer"))
                        {
                            Selection.activeObject = rendererComponent;
                        }
                    }
                }
            }
        }

        private void DrawPreviewSection()
        {
            if (!Foundation2DInspectorUi.DrawSectionHeader(
                    ref _foldPreview,
                    SectionPreview,
                    BadgePreview,
                    Foundation2DInspectorUi.BadgeTone.Neutral))
            {
                return;
            }

            using (Foundation2DInspectorUi.BeginCard())
            {
                if (serializedObject.isEditingMultipleObjects)
                {
                    StopPreview();
                    EditorGUILayout.HelpBox("Animation preview is available when exactly one controller is selected.", MessageType.Info);
                    return;
                }

                int count = _frames.arraySize;
                if (count <= 0)
                {
                    EditorGUILayout.HelpBox("No frames available for preview.", MessageType.Info);
                    return;
                }

                _previewState.TotalFrameCount = count;
                _previewState.CurrentFrameIndex = Mathf.Clamp(_previewState.CurrentFrameIndex, 0, count - 1);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(_previewPlaying ? "Pause" : "Play", GUILayout.Width(60f)))
                    {
                        if (_previewPlaying)
                        {
                            PausePreview();
                        }
                        else
                        {
                            StartPreview();
                        }
                    }

                    if (GUILayout.Button("Stop", GUILayout.Width(60f)))
                    {
                        StopPreview();
                        _previewState.Stop(0);
                        Repaint();
                    }

                    EditorGUILayout.LabelField($"Frame {_previewState.CurrentFrameIndex + 1}/{count}", EditorStyles.miniLabel);
                }

                using (EditorGUI.ChangeCheckScope change = new())
                {
                    PreviewSourceMode selectedMode = (PreviewSourceMode)EditorGUILayout.EnumPopup(LabelPreviewSource, _previewSourceMode);
                    if (change.changed)
                    {
                        _previewSourceMode = selectedMode;
                        Repaint();
                    }
                }

                PreviewSourceMode mode = _previewSourceMode;

                if (mode == PreviewSourceMode.AssetPreview)
                {
                    EditorGUILayout.HelpBox("AssetPreview prioritizes visual parity with Unity-rendered sprites. Preview thumbnails are generated asynchronously and may update after a short delay.", MessageType.None);
                }
                else if (mode == PreviewSourceMode.RawUV)
                {
                    EditorGUILayout.HelpBox("RawUV previews textureRect extraction directly. Useful for low-overhead or UV-level debugging when atlas packing behavior is under review.", MessageType.None);
                }

                int newFrame = EditorGUILayout.IntSlider("Timeline", _previewState.CurrentFrameIndex, 0, count - 1);
                if (newFrame != _previewState.CurrentFrameIndex)
                {
                    _previewState.Seek(newFrame);
                    Repaint();
                }

                float previewHeight = Mathf.Clamp(EditorGUIUtility.currentViewWidth * 0.42f, 120f, 180f);
                Rect previewRect = GUILayoutUtility.GetRect(0f, previewHeight, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.12f, 0.12f));

                bool previewIsBlank = _previewState.IsInInterval &&
                                      _previewState.IntervalHoldMode == SpriteSequenceIntervalHoldMode.Blank;
                if (previewIsBlank)
                {
                    EditorGUI.LabelField(previewRect, "Interval Blank Frame", EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    var sprite = _frames.GetArrayElementAtIndex(_previewState.CurrentFrameIndex).objectReferenceValue as Sprite;
                    if (sprite != null)
                    {
                        DrawSprite(previewRect, sprite);
                    }
                }

                float fps = Mathf.Max(0.01f, _frameRate.floatValue);
                float speed = GetEffectivePreviewSpeed();
                float effectiveFps = fps * speed;
                float total = count / Mathf.Max(0.01f, effectiveFps);
                float current = _previewState.CurrentFrameIndex / Mathf.Max(0.01f, effectiveFps);
                EditorGUILayout.LabelField($"{current:F2}s / {total:F2}s @ {effectiveFps:F1} fps", EditorStyles.miniLabel);

                if (_previewState.IsInInterval)
                {
                    float interval = Mathf.Max(0f, _loopInterval.floatValue);
                    EditorGUILayout.LabelField($"In interval: {_previewState.LoopIntervalElapsed:F2}s / {interval:F2}s", EditorStyles.miniLabel);
                }

                if (_useFiniteLoopCount.boolValue)
                {
                    EditorGUILayout.LabelField($"Loop count: {_previewState.CurrentLoopCount}/{Mathf.Max(1, _maxLoopCount.intValue)}", EditorStyles.miniLabel);
                }
            }
        }

        private void DrawRuntimeControls()
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(!Application.isPlaying || serializedObject.isEditingMultipleObjects))
            {
                using (Foundation2DInspectorUi.BeginActionLayout(4, 60f))
                {
                    if (GUILayout.Button("Play"))
                    {
                        ((SpriteSequenceController)target).Play();
                    }

                    if (GUILayout.Button("Pause"))
                    {
                        ((SpriteSequenceController)target).Pause();
                    }

                    if (GUILayout.Button("Resume"))
                    {
                        ((SpriteSequenceController)target).Resume();
                    }

                    if (GUILayout.Button("Stop"))
                    {
                        ((SpriteSequenceController)target).Stop();
                    }
                }
            }
        }

        private void DrawDropZone()
        {
            var dropArea = GUILayoutUtility.GetRect(0f, DropZoneHeight, GUILayout.ExpandWidth(true));
            bool isDragging = Event.current.type == EventType.DragUpdated || Event.current.type == EventType.DragPerform;
            bool isHovering = isDragging && dropArea.Contains(Event.current.mousePosition);

            Color bg = isHovering ? new Color(0.2f, 0.4f, 0.2f, 0.3f) : new Color(0.25f, 0.25f, 0.25f, 0.3f);
            EditorGUI.DrawRect(dropArea, bg);
            DrawDashedBorder(dropArea, isHovering ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.5f, 0.5f, 0.5f, 0.6f));

            string hint = _frames.arraySize > 0
                ? "Drag Sprites here to append (natural sort by name)"
                : "Drag Sprites here to build sequence";
            EditorGUI.LabelField(dropArea, hint, EditorStyles.centeredGreyMiniLabel);

            if (serializedObject.isEditingMultipleObjects)
            {
                return;
            }

            if (!dropArea.Contains(Event.current.mousePosition))
            {
                return;
            }

            switch (Event.current.type)
            {
                case EventType.DragUpdated:
                    if (HasSpriteInDrag())
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        Event.current.Use();
                    }
                    break;
                case EventType.DragPerform:
                    {
                        DragAndDrop.AcceptDrag();
                        try
                        {
                            int addedCount = CollectSpritesFromDrag(DragSpriteBuffer);
                            if (addedCount > 0)
                            {
                                AppendSprites(DragSpriteBuffer);
                            }
                        }
                        finally
                        {
                            ReleaseSpriteBuffer(DragSpriteBuffer, DefaultDragBufferCapacity);
                        }
                        Event.current.Use();
                        break;
                    }
            }
        }

        private void DrawFramesArrayWithoutFoldoutArrow()
        {
            EditorGUILayout.LabelField(LabelSprites, EditorStyles.boldLabel);

            int size = Mathf.Max(0, EditorGUILayout.IntField(LabelArraySize, _frames.arraySize));
            if (size != _frames.arraySize)
            {
                _frames.arraySize = size;
                _cachedFrameLabelStart = -1;
            }

            int count = _frames.arraySize;
            if (count <= 0)
            {
                return;
            }

            DrawFramePageControls(count);
            GetFramePageRange(count, out int start, out int end);
            EnsureFrameLabels(start, end);
            for (int i = start; i < end; i++)
            {
                SerializedProperty property = _frames.GetArrayElementAtIndex(i);
                EditorGUILayout.PropertyField(property, _frameElementLabels[i - start], false);
            }
        }

        private void DrawCollapsedSpritesSummary()
        {
            int count = _frames.arraySize;
            EditorGUILayout.LabelField($"Size: {count}", EditorStyles.miniLabel);

            if (count <= 0)
            {
                return;
            }

            float fps = Mathf.Max(0.01f, _frameRate.floatValue);
            float duration = count / fps;
            EditorGUILayout.LabelField($"Frame Count: {count}    Cycle Duration: {duration:F2}s", EditorStyles.miniLabel);
        }

        private static bool HasSpriteInDrag()
        {
            foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
            {
                if (obj is Sprite)
                {
                    return true;
                }

                if (obj is Texture2D tex)
                {
                    string path = AssetDatabase.GetAssetPath(tex);
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null && importer.spriteImportMode != SpriteImportMode.None)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int CollectSpritesFromDrag(List<Sprite> result)
        {
            result.Clear();
            foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
            {
                if (obj is Sprite s)
                {
                    result.Add(s);
                    continue;
                }

                if (obj is Texture2D tex)
                {
                    string path = AssetDatabase.GetAssetPath(tex);
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                    bool foundSprite = false;
                    foreach (UnityEngine.Object sub in subAssets)
                    {
                        if (sub is Sprite sprite)
                        {
                            result.Add(sprite);
                            foundSprite = true;
                        }
                    }

                    if (!foundSprite)
                    {
                        Sprite single = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                        if (single != null)
                        {
                            result.Add(single);
                        }
                    }
                }
            }

            result.Sort(SpriteNameComparer);
            return result.Count;
        }

        private void AppendSprites(List<Sprite> sprites)
        {
            serializedObject.Update();
            int startIndex = _frames.arraySize;
            _frames.arraySize += sprites.Count;
            for (int i = 0; i < sprites.Count; i++)
            {
                _frames.GetArrayElementAtIndex(startIndex + i).objectReferenceValue = sprites[i];
            }

            serializedObject.ApplyModifiedProperties();
            OnFrameStructureChanged();
        }

        private void OnFrameStructureChanged()
        {
            _cachedFrameLabelStart = -1;
            if (!_previewPlaying && !_previewState.IsPaused)
            {
                return;
            }

            if (_frames.arraySize <= 0)
            {
                StopPreview();
                _previewState = default;
                return;
            }

            bool remainPaused = _previewState.IsPaused;
            ResetPreviewState(true);
            if (remainPaused)
            {
                _previewState.Pause();
            }
        }

        private void SortFramesByName()
        {
            int count = _frames.arraySize;
            if (count <= 1)
            {
                return;
            }

            try
            {
                SortSpriteBuffer.Clear();
                if (SortSpriteBuffer.Capacity < count)
                {
                    SortSpriteBuffer.Capacity = count;
                }

                for (int i = 0; i < count; i++)
                {
                    SortSpriteBuffer.Add(_frames.GetArrayElementAtIndex(i).objectReferenceValue as Sprite);
                }

                SortSpriteBuffer.Sort(SpriteNameComparer);

                for (int i = 0; i < count; i++)
                {
                    _frames.GetArrayElementAtIndex(i).objectReferenceValue = SortSpriteBuffer[i];
                }
            }
            finally
            {
                ReleaseSpriteBuffer(SortSpriteBuffer, DefaultSortBufferCapacity);
            }
        }

        private void ReverseFrames()
        {
            int count = _frames.arraySize;
            if (count <= 1)
            {
                return;
            }

            for (int i = 0; i < count / 2; i++)
            {
                int j = count - 1 - i;
                var a = _frames.GetArrayElementAtIndex(i);
                var b = _frames.GetArrayElementAtIndex(j);
                (a.objectReferenceValue, b.objectReferenceValue) = (b.objectReferenceValue, a.objectReferenceValue);
            }
        }

        private void DrawFrameStrip(int count)
        {
            count = Mathf.Min(count, _frames.arraySize);
            if (count <= 0)
            {
                return;
            }

            if (!_foldSpritesList)
            {
                DrawFramePageControls(count);
            }

            GetFramePageRange(count, out int start, out int end);
            EnsureFrameLabels(start, end);
            int visibleCount = end - start;
            float stripWidth = visibleCount * (ThumbSize + ThumbSpacing) - ThumbSpacing;
            float viewWidth = Mathf.Max(ThumbSize, EditorGUIUtility.currentViewWidth - 70f);
            bool needsScroll = stripWidth > viewWidth;

            bool scrollViewOpened = false;
            try
            {
                if (needsScroll)
                {
                    _thumbScrollPos = EditorGUILayout.BeginScrollView(_thumbScrollPos, GUILayout.Height(ThumbSize + 24f));
                    scrollViewOpened = true;
                }

                Rect strip = GUILayoutUtility.GetRect(needsScroll ? stripWidth : viewWidth, ThumbSize + 14f);
                float startX = needsScroll ? strip.x : strip.x + (viewWidth - stripWidth) * 0.5f;

                for (int i = start; i < end; i++)
                {
                    int localIndex = i - start;
                    float x = startX + localIndex * (ThumbSize + ThumbSpacing);
                    var thumbRect = new Rect(x, strip.y, ThumbSize, ThumbSize);

                    EditorGUI.DrawRect(thumbRect, new Color(0.18f, 0.18f, 0.18f));

                    var spriteProp = _frames.GetArrayElementAtIndex(i);
                    if (spriteProp == null)
                    {
                        continue;
                    }

                    var sprite = spriteProp.objectReferenceValue as Sprite;
                    if (sprite != null)
                    {
                        DrawSprite(thumbRect, sprite);
                    }

                    var labelRect = new Rect(x, strip.y + ThumbSize, ThumbSize, 14f);
                    EditorGUI.LabelField(labelRect, _frameIndexLabels[localIndex], EditorStyles.centeredGreyMiniLabel);
                }
            }
            finally
            {
                if (scrollViewOpened)
                {
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawFramePageControls(int count)
        {
            int pageCount = Mathf.Max(1, (count + FramesPerPage - 1) / FramesPerPage);
            _framePage = Mathf.Clamp(_framePage, 0, pageCount - 1);

            bool narrow = EditorGUIUtility.currentViewWidth < 360f;
            if (!narrow)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawPreviousPageButton();
                    DrawPageSummary(count, pageCount);
                    DrawNextPageButton(pageCount);
                }
                return;
            }

            using (Foundation2DInspectorUi.BeginActionLayout(2, 90f))
            {
                DrawPreviousPageButton();
                DrawNextPageButton(pageCount);
            }
            DrawPageSummary(count, pageCount);
        }

        private void DrawPreviousPageButton()
        {
            using (new EditorGUI.DisabledScope(_framePage <= 0))
            {
                if (GUILayout.Button(LabelPreviousPage))
                {
                    _framePage--;
                    _thumbScrollPos = Vector2.zero;
                    _cachedFrameLabelStart = -1;
                }
            }
        }

        private void DrawNextPageButton(int pageCount)
        {
            using (new EditorGUI.DisabledScope(_framePage >= pageCount - 1))
            {
                if (GUILayout.Button(LabelNextPage))
                {
                    _framePage++;
                    _thumbScrollPos = Vector2.zero;
                    _cachedFrameLabelStart = -1;
                }
            }
        }

        private void DrawPageSummary(int count, int pageCount)
        {
            int firstFrame = _framePage * FramesPerPage + 1;
            int lastFrame = Mathf.Min(count, firstFrame + FramesPerPage - 1);
            EditorGUILayout.LabelField($"Page {_framePage + 1}/{pageCount}  |  Frames {firstFrame}-{lastFrame}", EditorStyles.centeredGreyMiniLabel);
        }

        private void GetFramePageRange(int count, out int start, out int end)
        {
            int pageCount = Mathf.Max(1, (count + FramesPerPage - 1) / FramesPerPage);
            _framePage = Mathf.Clamp(_framePage, 0, pageCount - 1);
            start = _framePage * FramesPerPage;
            end = Mathf.Min(count, start + FramesPerPage);
        }

        private void EnsureFrameLabels(int start, int end)
        {
            if (_cachedFrameLabelStart == start)
            {
                return;
            }

            int visibleCount = end - start;
            for (int i = 0; i < visibleCount; i++)
            {
                int frameIndex = start + i;
                _frameElementLabels[i].text = $"Element {frameIndex}";
                _frameIndexLabels[i].text = frameIndex.ToString();
            }

            _cachedFrameLabelStart = start;
        }

        private void DrawSprite(Rect position, Sprite sprite)
        {
            if (sprite == null)
            {
                return;
            }

            PreviewSourceMode mode = _previewSourceMode;
            if (mode != PreviewSourceMode.RawUV)
            {
                if (TryDrawSpriteUsingAssetPreview(position, sprite))
                {
                    return;
                }

                if (mode == PreviewSourceMode.AssetPreview)
                {
                    return;
                }
            }

            DrawSpriteUsingRawUv(position, sprite);
        }

        private static bool TryDrawSpriteUsingAssetPreview(Rect position, Sprite sprite)
        {
            // Atlas packing (tight/rotated) can make raw UV preview deviate from final appearance.
            // Prefer Unity's generated sprite preview texture in editor for visual consistency.
            Texture previewTexture = AssetPreview.GetAssetPreview(sprite);
            if (previewTexture == null)
            {
                previewTexture = AssetPreview.GetMiniThumbnail(sprite);
            }

            if (previewTexture == null)
            {
                return false;
            }

            DrawTextureWithAspect(position, previewTexture, null);
            return true;
        }

        private static void DrawSpriteUsingRawUv(Rect position, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null)
            {
                return;
            }

            if (sprite.packed &&
                (sprite.packingMode != SpritePackingMode.Rectangle || sprite.packingRotation != SpritePackingRotation.None))
            {
                TryDrawSpriteUsingAssetPreview(position, sprite);
                return;
            }

            Rect spriteRect;
            try
            {
                spriteRect = sprite.textureRect;
            }
            catch
            {
                TryDrawSpriteUsingAssetPreview(position, sprite);
                return;
            }

            Texture tex = sprite.texture;
            var uv = new Rect(
                spriteRect.x / tex.width,
                spriteRect.y / tex.height,
                spriteRect.width / tex.width,
                spriteRect.height / tex.height);

            DrawTextureWithAspect(position, tex, uv);
        }

        private static void DrawTextureWithAspect(Rect position, Texture texture, Rect? uv)
        {
            if (texture == null)
            {
                return;
            }

            float width = texture.width;
            float height = texture.height;

            if (uv.HasValue)
            {
                Rect uvRect = uv.Value;
                width *= Mathf.Abs(uvRect.width);
                height *= Mathf.Abs(uvRect.height);
            }

            Rect drawRect = CalculateAspectFitRect(position, width, height);

            if (uv.HasValue)
            {
                GUI.DrawTextureWithTexCoords(drawRect, texture, uv.Value);
            }
            else
            {
                GUI.DrawTexture(drawRect, texture, ScaleMode.ScaleToFit, true);
            }
        }

        private static Rect CalculateAspectFitRect(Rect position, float contentWidth, float contentHeight)
        {
            float safeWidth = Mathf.Max(contentWidth, 0.001f);
            float safeHeight = Mathf.Max(contentHeight, 0.001f);
            float contentAspect = safeWidth / safeHeight;
            float containerAspect = position.width / Mathf.Max(position.height, 0.001f);
            if (contentAspect >= containerAspect)
            {
                float height = position.width / contentAspect;
                return new Rect(position.x, position.y + (position.height - height) * 0.5f, position.width, height);
            }

            float width = position.height * contentAspect;
            return new Rect(position.x + (position.width - width) * 0.5f, position.y, width, position.height);
        }

        private static void DrawDashedBorder(Rect rect, Color color)
        {
            Color oldColor = Handles.color;
            Handles.color = color;
            Handles.DrawDottedLine(new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMax, rect.yMin), 4f);
            Handles.DrawDottedLine(new Vector3(rect.xMin, rect.yMax), new Vector3(rect.xMax, rect.yMax), 4f);
            Handles.DrawDottedLine(new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMin, rect.yMax), 4f);
            Handles.DrawDottedLine(new Vector3(rect.xMax, rect.yMin), new Vector3(rect.xMax, rect.yMax), 4f);
            Handles.color = oldColor;
        }

        private void StartPreview()
        {
            if (_previewPlaying || serializedObject.isEditingMultipleObjects || _frames.arraySize <= 0)
            {
                return;
            }

            if (_previewState.IsPaused && _previewState.TotalFrameCount == _frames.arraySize)
            {
                _previewState.Resume();
                _previewLastTime = EditorApplication.timeSinceStartup;
            }
            else
            {
                ResetPreviewState(false);
            }

            _previewPlaying = true;
            EditorApplication.update += OnEditorPreviewTick;
        }

        private void PausePreview()
        {
            if (!_previewPlaying)
            {
                return;
            }

            _previewState.Pause();
            StopPreview();
        }

        private void StopPreview()
        {
            if (!_previewPlaying)
            {
                return;
            }

            _previewPlaying = false;
            EditorApplication.update -= OnEditorPreviewTick;
        }

        private void OnEditorPreviewTick()
        {
            if (!_previewPlaying || target == null)
            {
                StopPreview();
                return;
            }

            serializedObject.UpdateIfRequiredOrScript();
            int count = _frames.arraySize;
            if (count <= 0)
            {
                StopPreview();
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            double deltaTime = Math.Max(0d, now - _previewLastTime);
            _previewLastTime = now;

            int catchUpBudget = _maxFrameAdvancesPerUpdate != null
                ? Mathf.Max(1, _maxFrameAdvancesPerUpdate.intValue)
                : DefaultPreviewCatchUpFrameBudget;
            SpriteSequenceAdvanceResult result = _previewState.Advance(deltaTime, catchUpBudget);
            if (!_previewState.IsPlaying)
            {
                _previewPlaying = false;
                EditorApplication.update -= OnEditorPreviewTick;
            }

            if (result.VisualCommitRequired || _previewState.IsInInterval || !_previewPlaying)
            {
                Repaint();
            }
        }

        private void ResetPreviewState(bool preserveCurrentFrame)
        {
            int frameCount = _frames != null ? _frames.arraySize : 0;
            int previousFrame = preserveCurrentFrame ? _previewState.CurrentFrameIndex : -1;
            if (frameCount <= 0)
            {
                _previewState = default;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            SpriteSequencePlaybackDirection direction = _playDirection.enumValueIndex == (int)SpriteSequenceController.PlayDirection.Forward
                ? SpriteSequencePlaybackDirection.Forward
                : SpriteSequencePlaybackDirection.Reverse;
            _previewState.Initialize(
                direction,
                Mathf.Max(0.01f, _frameRate.floatValue),
                (SpriteSequencePlaybackMode)_playMode.enumValueIndex,
                frameCount,
                GetEffectivePreviewSpeed(),
                _useFiniteLoopCount.boolValue ? Mathf.Max(1, _maxLoopCount.intValue) : 0,
                Mathf.Max(0f, _loopInterval.floatValue),
                (SpriteSequenceIntervalHoldMode)_intervalHoldFrame.enumValueIndex);
            if (previousFrame >= 0)
            {
                _previewState.Seek(Mathf.Clamp(previousFrame, 0, frameCount - 1));
            }
            _previewLastTime = now;
        }

        private float GetEffectivePreviewSpeed()
        {
            float raw = Mathf.Max(0f, _speedMultiplier.floatValue);
            if (!_useDiscreteSpeedMultiplier.boolValue)
            {
                return raw;
            }

            int steps = Mathf.Max(2, _discreteSpeedStepCount.intValue);
            Vector2 range = _discreteSpeedMultiplierRange.vector2Value;
            float min = Mathf.Max(0f, Mathf.Min(range.x, range.y));
            float max = Mathf.Max(min, Mathf.Max(range.x, range.y));

            float v = Mathf.Clamp(raw, min, max);
            float span = max - min;
            if (span <= 0.000001f)
            {
                return min;
            }

            float normalized = (v - min) / span;
            int index = Mathf.RoundToInt(normalized * (steps - 1));
            float t = index / (float)(steps - 1);
            return min + t * span;
        }

        private static void ReleaseSpriteBuffer(List<Sprite> buffer, int retainedCapacity)
        {
            buffer.Clear();
            if (buffer.Capacity > MaximumRetainedSpriteBufferCapacity)
            {
                buffer.Capacity = retainedCapacity;
            }
        }

    }
}
#endif
