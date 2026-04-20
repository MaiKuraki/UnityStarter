// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// One layer inside an AudioBlend container — maps an AudioClip to a parameter-driven volume curve.
    /// Supports both embedded AudioClip and external AudioClipReference (Addressables, URL, StreamingAssets).
    /// </summary>
    public class AudioBlendFile : AudioNode
    {
        // ---- Source mode ----
        [SerializeField]
        private AudioFile.AudioFileSourceMode sourceMode = AudioFile.AudioFileSourceMode.EmbeddedClip;

        [SerializeField]
        private AudioClip file = null;

        [SerializeField]
        private AudioClipReference externalReference = null;

        // ---- Blend fields ----
        [SerializeField]
        private AudioParameter parameter = null;

        [SerializeField]
        private AnimationCurve responseCurve = new AnimationCurve();

        [Range(0, 1)]
        public float minStartTime = 0;
        [Range(0, 1)]
        public float maxStartTime = 0;

        public float startTime { get; private set; }

        // ---- Source mode helpers ----
        public AudioFile.AudioFileSourceMode SourceMode
        {
            get => this.sourceMode;
            set
            {
                if (this.sourceMode == value) return;
                this.sourceMode = value;
                if (value == AudioFile.AudioFileSourceMode.EmbeddedClip)
                    this.externalReference = null;
                else
                    this.file = null;
            }
        }

        private AudioFile.AudioFileSourceMode GetEffectiveSourceMode()
        {
            if (sourceMode == AudioFile.AudioFileSourceMode.ExternalReference) return AudioFile.AudioFileSourceMode.ExternalReference;
            if (file != null) return AudioFile.AudioFileSourceMode.EmbeddedClip;
            if (externalReference != null) return AudioFile.AudioFileSourceMode.ExternalReference;
            return sourceMode;
        }

        // ---- ProcessNode ----
        public override void ProcessNode(ActiveEvent activeEvent)
        {
            AudioFile.AudioFileSourceMode effectiveMode = GetEffectiveSourceMode();

            if (effectiveMode == AudioFile.AudioFileSourceMode.EmbeddedClip && this.file != null)
            {
                CalculateStartTime(this.file);
                activeEvent.AddEventSource(this.file, this.parameter, this.responseCurve, this.startTime, AudioClipResolver.CreateEmbedded(this.file));
            }
            else if (effectiveMode == AudioFile.AudioFileSourceMode.ExternalReference && this.externalReference != null)
            {
                activeEvent.isAsync = true;
                LoadClipAsync(activeEvent).Forget();
            }
            else
            {
                Debug.LogWarningFormat("No file in Blend File node {0}", this.name);
            }
        }

        private async UniTaskVoid LoadClipAsync(ActiveEvent activeEvent)
        {
            try
            {
                IAudioClipHandle handle = await AudioClipResolver.LoadExternalAsync(this.externalReference, activeEvent.GetCancellationToken());

                if (handle == null)
                {
                    Debug.LogError($"No loader found for BlendFile reference '{externalReference?.name}' in event '{activeEvent.name}'.");
                    activeEvent.StopImmediate();
                    return;
                }

                if (!handle.IsSuccess || handle.Clip == null || handle.Clip.length <= 0f)
                {
                    Debug.LogError($"Error loading blend clip '{externalReference?.ResolveLocation()}': {handle.Error}");
                    handle.Release();
                    activeEvent.StopImmediate();
                    return;
                }

                CalculateStartTime(handle.Clip);
                if (!activeEvent.AddEventSource(handle.Clip, this.parameter, this.responseCurve, this.startTime, handle))
                {
                    handle.Release();
                    return;
                }

                activeEvent.OnAsyncLoadCompleted();
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogError($"Exception loading blend clip '{externalReference?.ResolveLocation()}': {e.Message}");
                activeEvent.StopImmediate();
            }
        }

        /// <summary>Compute a random start-time offset for the given clip.</summary>
        public void CalculateStartTime(AudioClip clip)
        {
            if (clip == null) { this.startTime = 0; return; }

            float clampedMin = Mathf.Clamp01(this.minStartTime);
            float clampedMax = Mathf.Clamp01(this.maxStartTime);
            float ratio = (clampedMin != clampedMax)
                ? UnityEngine.Random.Range(clampedMin, clampedMax)
                : clampedMin;
            this.startTime = clip.length * ratio;
        }

        // Keep backwards-compatible public API
        public void RandomStartTime()
        {
            if (this.file != null) CalculateStartTime(this.file);
        }

#if UNITY_EDITOR

        private const float NodeWidth = 300f;
        private const float TitleBarH = 18f;
        private const float RowH      = 19f;
        private const float RowGap    =  2f;
        private const float BottomPad =  8f;

        public AudioClip File
        {
            get => this.file;
            set => this.file = value;
        }

        public override void InitializeNode(Vector2 position)
        {
            this.name = "Blend File";
            this.nodeRect.position = position;
            this.nodeRect.width    = NodeWidth;
            this.nodeRect.height   = CalcHeight();
            AddOutput();
            EditorUtility.SetDirty(this);
        }

        private float CalcHeight()
        {
            float R(int n) => n * (RowH + RowGap);
            // Source + clip/ref + parameter + blend curve + start time = 5 base rows
            float h = TitleBarH + R(5);
            if (GetEffectiveSourceMode() == AudioFile.AudioFileSourceMode.ExternalReference && externalReference != null)
                h += R(2);
            return h + BottomPad;
        }

        public override void DrawNode(int id)
        {
            this.nodeRect.height = CalcHeight();
            this.nodeRect = GUI.Window(id, this.nodeRect, DrawWindow, this.name);
            DrawInput();
            DrawOutput();
        }

        protected override void DrawProperties()
        {
            EditorGUI.BeginChangeCheck();

            var newMode = (AudioFile.AudioFileSourceMode)EditorGUILayout.EnumPopup("Source", GetEffectiveSourceMode());
            if (newMode != this.sourceMode) SourceMode = newMode;

            if (GetEffectiveSourceMode() == AudioFile.AudioFileSourceMode.EmbeddedClip)
            {
                this.file = EditorGUILayout.ObjectField("Audio Clip", this.file, typeof(AudioClip), false) as AudioClip;
                this.externalReference = null;
                if (this.file != null && this.name != this.file.name)
                    this.name = this.file.name;
            }
            else
            {
                this.externalReference = EditorGUILayout.ObjectField("Audio Reference", this.externalReference, typeof(AudioClipReference), false) as AudioClipReference;
                this.file = null;
                if (this.externalReference != null)
                {
                    EditorGUILayout.LabelField("Kind",     this.externalReference.LocationKind.ToString(), EditorStyles.miniLabel);
                    EditorGUILayout.LabelField("Location", this.externalReference.GetDisplayLocation(),   EditorStyles.wordWrappedMiniLabel);
                }
            }

            this.parameter     = EditorGUILayout.ObjectField("Parameter",   this.parameter,     typeof(AudioParameter),  false) as AudioParameter;
            this.responseCurve = EditorGUILayout.CurveField("Blend Curve",  this.responseCurve);
            EditorGUILayout.MinMaxSlider("Start Time", ref this.minStartTime, ref this.maxStartTime, 0, 1);

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(this);
        }

#endif
    }
}