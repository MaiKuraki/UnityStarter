// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// An AudioNode containing a reference to a voice-over AudioClip.
    /// Supports both embedded AudioClip and external AudioClipReference (Addressables, URL, StreamingAssets).
    /// </summary>
    public class AudioVoiceFile : AudioNode
    {
        // ---- Source mode ----
        [SerializeField]
        private AudioFile.AudioFileSourceMode sourceMode = AudioFile.AudioFileSourceMode.EmbeddedClip;

        /// <summary>The embedded voice clip (used in EmbeddedClip mode).</summary>
        [SerializeField]
        private AudioClip file = null;

        /// <summary>External reference (used in ExternalReference mode).</summary>
        [SerializeField]
        private AudioClipReference externalReference = null;

        // ---- Per-node offsets ----
        [SerializeField, Range(-1, 1)]
        private float volumeOffset = 0;

        [SerializeField, Range(-3, 3)]
        private float pitchOffset = 0;

        /// <summary>Language index this voice line belongs to.</summary>
        [SerializeField]
        private int language = 0;

        /// <summary>Subtitle / caption text associated with this voice clip.</summary>
        [SerializeField]
        private string text = null;

        public int Language => this.language;

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

        internal bool TryGetExternalReference(out AudioClipReference reference)
        {
            reference = GetEffectiveSourceMode() == AudioFile.AudioFileSourceMode.ExternalReference
                ? externalReference
                : null;
            return reference != null;
        }

        public AudioClipReference ExternalReference => this.externalReference;
        public AudioClip File => this.file;

        // ---- ProcessNode ----
        public override void ProcessNode(ActiveEvent activeEvent)
        {
            activeEvent.ModulateVolume(this.volumeOffset);
            activeEvent.ModulatePitch(this.pitchOffset);
            activeEvent.text = this.text;

            AudioFile.AudioFileSourceMode effectiveMode = GetEffectiveSourceMode();

            if (effectiveMode == AudioFile.AudioFileSourceMode.EmbeddedClip && this.file != null)
            {
                activeEvent.AddEventSource(this.file, null, null, 0, AudioClipResolver.CreateEmbedded(this.file));
            }
            else if (effectiveMode == AudioFile.AudioFileSourceMode.ExternalReference && this.externalReference != null)
            {
                AudioEventPreparation preparation = activeEvent.BeginAsyncPreparation();
                if (preparation != null)
                    LoadClipAsync(preparation, activeEvent.name).Forget();
            }
            else
            {
                Debug.LogWarningFormat("Empty Voice File node in event {0}", activeEvent.name);
            }
        }

        private async UniTaskVoid LoadClipAsync(AudioEventPreparation preparation, string eventName)
        {
            IAudioClipHandle handle = null;
            bool succeeded = false;
            try
            {
                handle = await AudioClipResolver.LoadExternalAsync(
                    this.externalReference,
                    preparation.CancellationToken);

                if (handle == null)
                {
                    Debug.LogError($"No loader found for VoiceFile reference '{externalReference?.name}' in event '{eventName}'.");
                    return;
                }

                if (!handle.IsSuccess || handle.Clip == null || handle.Clip.length <= 0f)
                {
                    string referenceName = externalReference != null ? externalReference.name : "<missing>";
                    Debug.LogError($"Voice audio reference '{referenceName}' failed to load.");
                    AudioClipHandleRelease.Safe(handle);
                    handle = null;
                    return;
                }

                bool sourceAccepted = preparation.TryAddSource(handle.Clip, null, null, 0f, handle);
                handle = null;
                if (!sourceAccepted)
                {
                    return;
                }

                succeeded = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                string referenceName = externalReference != null ? externalReference.name : "<missing>";
                Debug.LogError(
                    $"Voice audio reference '{referenceName}' failed with {e.GetType().Name}. Location details are omitted from logs.");
            }
            finally
            {
                try
                {
                    AudioClipHandleRelease.Safe(handle);
                }
                finally
                {
                    preparation.Complete(succeeded);
                }
            }
        }

#if UNITY_EDITOR

        private const float NodeWidth = 300f;
        private const float TitleBarH = 18f;
        private const float RowH      = 19f;
        private const float RowGap    =  2f;
        private const float BottomPad =  8f;

        public override void InitializeNode(Vector2 position)
        {
            this.name = "Voice File";
            this.nodeRect.position = position;
            this.nodeRect.width    = NodeWidth;
            this.nodeRect.height   = CalcHeight();
            AddOutput();
            EditorUtility.SetDirty(this);
        }

        private float CalcHeight()
        {
            float R(int n) => n * (RowH + RowGap);
            // Source dropdown + clip/ref field + volume + pitch + text + language
            float h = TitleBarH + R(6);
            // External mode may show 1–2 extra info label rows
            if (GetEffectiveSourceMode() == AudioFile.AudioFileSourceMode.ExternalReference && externalReference != null)
                h += R(2);
            return h + BottomPad;
        }

        public override void DrawNode(int id)
        {
            this.nodeRect.height = CalcHeight();
            base.DrawNode(id);
        }

        protected override void DrawProperties()
        {
            EditorGUI.BeginChangeCheck();

            // Source mode selector
            var newMode = (AudioFile.AudioFileSourceMode)EditorGUILayout.EnumPopup("Source", GetEffectiveSourceMode());
            if (newMode != this.sourceMode) SourceMode = newMode;

            if (GetEffectiveSourceMode() == AudioFile.AudioFileSourceMode.EmbeddedClip)
            {
                this.file = EditorGUILayout.ObjectField("Audio Clip", this.file, typeof(AudioClip), false) as AudioClip;
                this.externalReference = null;
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

            this.volumeOffset = EditorGUILayout.Slider("Volume Offset", this.volumeOffset, -1f, 1f);
            this.pitchOffset  = EditorGUILayout.Slider("Pitch Offset",  this.pitchOffset,  -3f, 3f);
            this.text         = EditorGUILayout.TextField("Text",        this.text);
            this.language     = EditorGUILayout.Popup("Language",        this.language, AudioManager.Languages);

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(this);
        }
#endif
    }
}
