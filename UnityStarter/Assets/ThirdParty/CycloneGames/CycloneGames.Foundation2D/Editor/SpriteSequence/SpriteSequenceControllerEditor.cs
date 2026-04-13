#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CycloneGames.Foundation2D.Runtime;

namespace CycloneGames.Foundation2D.Editor
{
    [CustomEditor(typeof(SpriteSequenceController))]
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

        private static readonly NaturalSpriteNameComparer SpriteNameComparer = new();
        private static readonly List<Sprite> DragSpriteBuffer = new(128);
        private static readonly List<Sprite> SortSpriteBuffer = new(256);
        private static readonly HashSet<Texture> TextureSetBuffer = new();
        private static readonly List<GUIContent> ElementLabelCache = new(128);

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

        private const string PreviewSourcePrefsKey = "SpriteSequenceControllerEditor.PreviewSource";
        private static bool _previewSourceLoaded;
        private static PreviewSourceMode _previewSourceMode = PreviewSourceMode.Auto;

        private SerializedProperty _frames;
        private SerializedProperty _frameRate;
        private SerializedProperty _playMode;
        private SerializedProperty _playDirection;
        private SerializedProperty _updateDriver;
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
        private bool _foldLoop = true;
        private bool _foldRenderer = true;
        private bool _foldPreview = true;
        private bool _foldSpritesList = true;

        private Vector2 _thumbScrollPos;
        private readonly List<string> _frameIndexLabelCache = new();

        private bool _previewPlaying;
        private int _previewFrame;
        private double _previewLastTime;
        private double _previewAccumulator;
        private bool _previewInInterval;
        private bool _previewBlankDuringInterval;
        private double _previewIntervalTimer;
        private int _previewDirection = 1;
        private int _previewLoopCompleteCount;

        private const float ThumbSize = 48f;
        private const float ThumbSpacing = 2f;
        private const float DropZoneHeight = 38f;

        private static GUIStyle _sectionStyle;
        private static GUIStyle SectionStyle => _sectionStyle ??= new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(10, 10, 8, 8)
        };

        private static PreviewSourceMode PreviewSource
        {
            get
            {
                if (!_previewSourceLoaded)
                {
                    _previewSourceMode = (PreviewSourceMode)EditorPrefs.GetInt(PreviewSourcePrefsKey, (int)PreviewSourceMode.Auto);
                    _previewSourceLoaded = true;
                }

                return _previewSourceMode;
            }
            set
            {
                _previewSourceMode = value;
                _previewSourceLoaded = true;
                EditorPrefs.SetInt(PreviewSourcePrefsKey, (int)value);
            }
        }

        private void OnEnable()
        {
            _frames = serializedObject.FindProperty("frames");
            _frameRate = serializedObject.FindProperty("frameRate");
            _playMode = serializedObject.FindProperty("playMode");
            _playDirection = serializedObject.FindProperty("playDirection");
            _updateDriver = serializedObject.FindProperty("updateDriver");
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

            EditorApplication.update -= OnEditorPreviewTick;
            _previewPlaying = false;
            _previewFrame = 0;
            _previewAccumulator = 0d;
            _previewInInterval = false;
            _previewBlankDuringInterval = false;
            _previewIntervalTimer = 0d;
            _previewDirection = 1;
            _previewLoopCompleteCount = 0;
        }

        private void OnDisable()
        {
            StopPreview();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawFramesSection();
            DrawPlaybackSection();
            DrawLoopSection();
            DrawRendererSection();
            DrawPreviewSection();
            DrawRuntimeControls();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawFramesSection()
        {
            _foldFrames = EditorGUILayout.Foldout(_foldFrames, "Frames", true);
            if (!_foldFrames)
            {
                return;
            }

            EditorGUILayout.BeginVertical(SectionStyle);
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

            int count = _frames.arraySize;
            if (count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Sort by Name", EditorStyles.miniButtonLeft))
                {
                    SortFramesByName();
                }

                if (GUILayout.Button("Reverse", EditorStyles.miniButtonMid))
                {
                    ReverseFrames();
                }

                if (GUILayout.Button("Clear", EditorStyles.miniButtonRight))
                {
                    if (EditorUtility.DisplayDialog("Clear Frame Sequence", $"Clear all {count} frames?", "Clear", "Cancel"))
                    {
                        _frames.ClearArray();
                    }
                }
                EditorGUILayout.EndHorizontal();

                int drawCount = _frames.arraySize;
                if (drawCount > 0)
                {
                    DrawFrameStrip(drawCount);
                }
            }

            count = _frames.arraySize;
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

            EditorGUILayout.EndVertical();
        }

        private void DrawPlaybackSection()
        {
            _foldPlayback = EditorGUILayout.Foldout(_foldPlayback, "Playback", true);
            if (!_foldPlayback)
            {
                return;
            }

            EditorGUILayout.BeginVertical(SectionStyle);
            EditorGUILayout.PropertyField(_frameRate, LabelFrameRate);
            EditorGUILayout.PropertyField(_speedMultiplier, LabelSpeedMultiplier);
            EditorGUILayout.PropertyField(_useDiscreteSpeedMultiplier, LabelUseDiscreteSpeedMultiplier);
            if (_useDiscreteSpeedMultiplier.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_discreteSpeedStepCount, LabelDiscreteStepCount);
                EditorGUILayout.PropertyField(_discreteSpeedMultiplierRange, LabelDiscreteRange);
                EditorGUILayout.PropertyField(_warnWhenSpeedOutOfRange, LabelWarnWhenSpeedOutOfRange);
                EditorGUILayout.HelpBox("This maps speedMultiplier to the nearest evenly-spaced step in the configured range. Start with 5-7 steps, then tune by visual feel and batching results.", MessageType.Info);

                Vector2 range = _discreteSpeedMultiplierRange.vector2Value;
                float min = Mathf.Min(range.x, range.y);
                float max = Mathf.Max(range.x, range.y);
                float raw = _speedMultiplier.floatValue;
                if (raw < min || raw > max)
                {
                    EditorGUILayout.HelpBox($"Current Speed Multiplier ({raw:F3}) is outside configured range [{min:F3}, {max:F3}] and will be clamped before quantization.", MessageType.Warning);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.PropertyField(_playMode, LabelPlayMode);
            EditorGUILayout.PropertyField(_playDirection, LabelDirection);
            EditorGUILayout.PropertyField(_updateDriver, LabelUpdateDriver);
            EditorGUILayout.PropertyField(_playOnEnable, LabelPlayOnEnable);
            EditorGUILayout.PropertyField(_ignoreTimeScale, LabelIgnoreTimeScale);

            if (_updateDriver.enumValueIndex == (int)SpriteSequenceController.UpdateDriver.BurstManaged)
            {
                EditorGUILayout.HelpBox("BurstManaged requires SpriteSequenceBurstManager in scene and compile define BURST_JOBS. If unavailable, controller can fallback to MonoUpdate.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLoopSection()
        {
            _foldLoop = EditorGUILayout.Foldout(_foldLoop, "Loop Controls", true);
            if (!_foldLoop)
            {
                return;
            }

            EditorGUILayout.BeginVertical(SectionStyle);
            EditorGUILayout.PropertyField(_loopInterval, LabelLoopInterval);
            EditorGUILayout.PropertyField(_intervalHoldFrame, LabelIntervalHoldFrame);
            EditorGUILayout.PropertyField(_useFiniteLoopCount, LabelUseFiniteLoopCount);

            if (_useFiniteLoopCount.boolValue)
            {
                EditorGUILayout.PropertyField(_maxLoopCount, LabelMaxLoopCount);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRendererSection()
        {
            _foldRenderer = EditorGUILayout.Foldout(_foldRenderer, "Renderer Binding", true);
            if (!_foldRenderer)
            {
                return;
            }

            EditorGUILayout.BeginVertical(SectionStyle);
            EditorGUILayout.PropertyField(_rendererComponent, LabelRendererComponent);
            if (_rendererComponent.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Assign a component implementing ISpriteSequenceRenderer, such as UGUISequenceRenderer or SpriteRendererSequenceRenderer.", MessageType.None);
            }
            else
            {
                Component rendererComponent = _rendererComponent.objectReferenceValue as Component;
                string rendererTypeName = rendererComponent != null ? rendererComponent.GetType().Name : _rendererComponent.objectReferenceValue.GetType().Name;
                EditorGUILayout.HelpBox($"Renderer-specific settings are edited in the {rendererTypeName} component inspector below. Controller preview and playback settings stay here.", MessageType.None);

                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(rendererComponent == null))
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
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPreviewSection()
        {
            _foldPreview = EditorGUILayout.Foldout(_foldPreview, "Animation Preview", true);
            if (!_foldPreview)
            {
                return;
            }

            EditorGUILayout.BeginVertical(SectionStyle);

            int count = _frames.arraySize;
            if (count <= 0)
            {
                EditorGUILayout.HelpBox("No frames available for preview.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _previewFrame = Mathf.Clamp(_previewFrame, 0, count - 1);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_previewPlaying ? "Pause" : "Play", GUILayout.Width(60f)))
            {
                if (_previewPlaying)
                {
                    StopPreview();
                }
                else
                {
                    StartPreview();
                }
            }

            if (GUILayout.Button("Stop", GUILayout.Width(60f)))
            {
                StopPreview();
                _previewFrame = 0;
                Repaint();
            }

            EditorGUILayout.LabelField($"Frame {_previewFrame + 1}/{count}", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            PreviewSourceMode mode = PreviewSource;
            EditorGUI.BeginChangeCheck();
            mode = (PreviewSourceMode)EditorGUILayout.EnumPopup(LabelPreviewSource, mode);
            if (EditorGUI.EndChangeCheck())
            {
                PreviewSource = mode;
                Repaint();
            }

            if (mode == PreviewSourceMode.AssetPreview)
            {
                EditorGUILayout.HelpBox("AssetPreview prioritizes visual parity with Unity-rendered sprites. Preview thumbnails are generated asynchronously and may update after a short delay.", MessageType.None);
            }
            else if (mode == PreviewSourceMode.RawUV)
            {
                EditorGUILayout.HelpBox("RawUV previews textureRect extraction directly. Useful for low-overhead or UV-level debugging when atlas packing behavior is under review.", MessageType.None);
            }

            int newFrame = EditorGUILayout.IntSlider("Timeline", _previewFrame, 0, count - 1);
            if (newFrame != _previewFrame)
            {
                _previewFrame = newFrame;
                _previewAccumulator = 0d;
                Repaint();
            }

            Rect previewRect = GUILayoutUtility.GetRect(0f, 180f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.12f, 0.12f));

            if (_previewBlankDuringInterval)
            {
                EditorGUI.LabelField(previewRect, "Interval Blank Frame", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                var sprite = _frames.GetArrayElementAtIndex(_previewFrame).objectReferenceValue as Sprite;
                if (sprite != null)
                {
                    DrawSprite(previewRect, sprite);
                }
            }

            float fps = Mathf.Max(0.01f, _frameRate.floatValue);
            float speed = GetEffectivePreviewSpeed();
            float effectiveFps = fps * speed;
            float total = count / Mathf.Max(0.01f, effectiveFps);
            float current = _previewFrame / Mathf.Max(0.01f, effectiveFps);
            EditorGUILayout.LabelField($"{current:F2}s / {total:F2}s @ {effectiveFps:F1} fps", EditorStyles.miniLabel);

            if (_previewInInterval)
            {
                float interval = Mathf.Max(0f, _loopInterval.floatValue);
                EditorGUILayout.LabelField($"In interval: {_previewIntervalTimer:F2}s / {interval:F2}s", EditorStyles.miniLabel);
            }

            if (_useFiniteLoopCount.boolValue)
            {
                EditorGUILayout.LabelField($"Loop count: {_previewLoopCompleteCount}/{Mathf.Max(1, _maxLoopCount.intValue)}", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRuntimeControls()
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
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
                EditorGUILayout.EndHorizontal();
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
                        int addedCount = CollectSpritesFromDrag(DragSpriteBuffer);
                        if (addedCount > 0)
                        {
                            Undo.RecordObject(target, "Add Sprites To Sequence");
                            AppendSprites(DragSpriteBuffer);
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
            }

            for (int i = 0; i < _frames.arraySize; i++)
            {
                SerializedProperty p = _frames.GetArrayElementAtIndex(i);
                EditorGUILayout.PropertyField(p, GetElementLabel(i));
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
            foreach (Object obj in DragAndDrop.objectReferences)
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
            foreach (Object obj in DragAndDrop.objectReferences)
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

                    Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                    bool foundSprite = false;
                    foreach (Object sub in subAssets)
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
        }

        private void SortFramesByName()
        {
            int count = _frames.arraySize;
            if (count <= 1)
            {
                return;
            }

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

            float stripWidth = count * (ThumbSize + ThumbSpacing) - ThumbSpacing;
            float viewWidth = EditorGUIUtility.currentViewWidth - 40f;
            bool needsScroll = stripWidth > viewWidth;

            if (needsScroll)
            {
                _thumbScrollPos = EditorGUILayout.BeginScrollView(_thumbScrollPos, GUILayout.Height(ThumbSize + 24f));
            }

            Rect strip = GUILayoutUtility.GetRect(needsScroll ? stripWidth : viewWidth, ThumbSize + 14f);
            float startX = needsScroll ? strip.x : strip.x + (viewWidth - stripWidth) * 0.5f;

            for (int i = 0; i < count; i++)
            {
                float x = startX + i * (ThumbSize + ThumbSpacing);
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
                EditorGUI.LabelField(labelRect, GetFrameIndexLabel(i), EditorStyles.centeredGreyMiniLabel);
            }

            if (needsScroll)
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private string GetFrameIndexLabel(int index)
        {
            while (_frameIndexLabelCache.Count <= index)
            {
                _frameIndexLabelCache.Add(_frameIndexLabelCache.Count.ToString());
            }

            return _frameIndexLabelCache[index];
        }

        private static GUIContent GetElementLabel(int index)
        {
            while (ElementLabelCache.Count <= index)
            {
                ElementLabelCache.Add(new GUIContent("Element " + ElementLabelCache.Count));
            }

            return ElementLabelCache[index];
        }

        private static void DrawSprite(Rect position, Sprite sprite)
        {
            if (sprite == null)
            {
                return;
            }

            PreviewSourceMode mode = PreviewSource;
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

            Rect spriteRect;
            try
            {
                spriteRect = sprite.textureRect;
            }
            catch
            {
                spriteRect = sprite.rect;
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

            float aspect = width / Mathf.Max(height, 0.001f);
            Rect drawRect;
            if (aspect > 1f)
            {
                float h = position.height / aspect;
                drawRect = new Rect(position.x, position.y + (position.height - h) * 0.5f, position.width, h);
            }
            else
            {
                float w = position.width * aspect;
                drawRect = new Rect(position.x + (position.width - w) * 0.5f, position.y, w, position.height);
            }

            if (uv.HasValue)
            {
                GUI.DrawTextureWithTexCoords(drawRect, texture, uv.Value);
            }
            else
            {
                GUI.DrawTexture(drawRect, texture, ScaleMode.ScaleToFit, true);
            }
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
            if (_previewPlaying)
            {
                return;
            }

            _previewPlaying = true;
            _previewLastTime = EditorApplication.timeSinceStartup;
            _previewAccumulator = 0d;
            _previewInInterval = false;
            _previewBlankDuringInterval = false;
            _previewIntervalTimer = 0d;
            _previewLoopCompleteCount = 0;
            _previewDirection = _playDirection.enumValueIndex == (int)SpriteSequenceController.PlayDirection.Forward ? 1 : -1;
            EditorApplication.update += OnEditorPreviewTick;
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
            if (!_previewPlaying)
            {
                return;
            }

            int count = _frames.arraySize;
            if (count <= 1)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            double dt = now - _previewLastTime;
            _previewLastTime = now;

            float interval = Mathf.Max(0f, _loopInterval.floatValue);
            if (_previewInInterval)
            {
                _previewIntervalTimer += dt;
                if (_previewIntervalTimer < interval)
                {
                    Repaint();
                    return;
                }

                _previewInInterval = false;
                _previewBlankDuringInterval = false;
                _previewIntervalTimer = 0d;
                if (_playMode.enumValueIndex != (int)SpriteSequenceController.PlayMode.PingPong)
                {
                    _previewDirection = _playDirection.enumValueIndex == (int)SpriteSequenceController.PlayDirection.Forward ? 1 : -1;
                }
                _previewFrame = _previewDirection == 1 ? 0 : count - 1;
            }

            float fps = Mathf.Max(0.01f, _frameRate.floatValue);
            float speed = GetEffectivePreviewSpeed();
            float effectiveFps = fps * speed;
            _previewAccumulator += dt * effectiveFps;
            int step = Mathf.FloorToInt((float)_previewAccumulator);
            if (step <= 0)
            {
                return;
            }

            _previewAccumulator -= step;
            for (int i = 0; i < step; i++)
            {
                if (!AdvancePreviewFrame(count))
                {
                    break;
                }
            }

            Repaint();
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

        private bool AdvancePreviewFrame(int frameCount)
        {
            int nextFrame = _previewFrame + _previewDirection;
            int mode = _playMode.enumValueIndex;

            switch (mode)
            {
                case (int)SpriteSequenceController.PlayMode.Once:
                    if (nextFrame < 0 || nextFrame >= frameCount)
                    {
                        _previewPlaying = false;
                        EditorApplication.update -= OnEditorPreviewTick;
                        return false;
                    }
                    break;
                case (int)SpriteSequenceController.PlayMode.Loop:
                    if (nextFrame >= frameCount || nextFrame < 0)
                    {
                        if (!OnPreviewLoopBoundary())
                        {
                            return false;
                        }

                        if (_previewInInterval)
                        {
                            return false;
                        }

                        nextFrame = nextFrame >= frameCount ? 0 : frameCount - 1;
                    }
                    break;
                case (int)SpriteSequenceController.PlayMode.PingPong:
                    if (nextFrame >= frameCount)
                    {
                        _previewDirection = -1;
                        nextFrame = frameCount - 2;
                        if (nextFrame < 0) nextFrame = 0;

                        if (!OnPreviewLoopBoundary())
                        {
                            return false;
                        }

                        if (_previewInInterval)
                        {
                            _previewFrame = nextFrame;
                            return false;
                        }
                    }
                    else if (nextFrame < 0)
                    {
                        _previewDirection = 1;
                        nextFrame = 1;
                        if (nextFrame >= frameCount) nextFrame = 0;

                        if (!OnPreviewLoopBoundary())
                        {
                            return false;
                        }

                        if (_previewInInterval)
                        {
                            _previewFrame = nextFrame;
                            return false;
                        }
                    }
                    break;
            }

            _previewFrame = Mathf.Clamp(nextFrame, 0, frameCount - 1);
            return true;
        }

        private bool OnPreviewLoopBoundary()
        {
            if (_useFiniteLoopCount.boolValue)
            {
                _previewLoopCompleteCount++;
                if (_previewLoopCompleteCount >= Mathf.Max(1, _maxLoopCount.intValue))
                {
                    _previewPlaying = false;
                    EditorApplication.update -= OnEditorPreviewTick;
                    return false;
                }
            }

            float interval = Mathf.Max(0f, _loopInterval.floatValue);
            if (interval <= 0f)
            {
                return true;
            }

            _previewInInterval = true;
            _previewBlankDuringInterval = false;
            _previewIntervalTimer = 0d;
            _previewAccumulator = 0d;

            switch (_intervalHoldFrame.enumValueIndex)
            {
                case (int)SpriteSequenceController.IntervalHoldFrame.First:
                    _previewFrame = _previewDirection == 1 ? 0 : Mathf.Max(0, _frames.arraySize - 1);
                    break;
                case (int)SpriteSequenceController.IntervalHoldFrame.Blank:
                    _previewBlankDuringInterval = true;
                    break;
            }

            return true;
        }

    }
}
#endif
