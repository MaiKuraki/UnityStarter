// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using CycloneGames.Logging;
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

        /// <summary>Stable voice-locale code used by voice-locale selectors.</summary>
        [SerializeField]
        private string voiceLocaleCode = string.Empty;

        [NonSerialized] private string cachedLocaleCode;
        [NonSerialized] private VoiceLocaleId cachedVoiceLocale;
        [NonSerialized] private bool cachedVoiceLocaleIsValid;
        [NonSerialized] private bool voiceLocaleCacheInitialized;

        public string VoiceLocaleCode => this.voiceLocaleCode ?? string.Empty;

        internal bool TryGetVoiceLocale(out VoiceLocaleId locale)
        {
            if (!this.voiceLocaleCacheInitialized ||
                !string.Equals(
                    this.cachedLocaleCode,
                    this.voiceLocaleCode,
                    StringComparison.Ordinal))
            {
                this.cachedLocaleCode = this.voiceLocaleCode;
                this.cachedVoiceLocaleIsValid =
                    VoiceLocaleId.TryCreate(
                        this.voiceLocaleCode,
                        out this.cachedVoiceLocale);
                this.voiceLocaleCacheInitialized = true;
            }

            locale = this.cachedVoiceLocale;
            return this.cachedVoiceLocaleIsValid;
        }

        private void InvalidateVoiceLocaleCache()
        {
            this.cachedLocaleCode = null;
            this.cachedVoiceLocale = VoiceLocaleId.Invalid;
            this.cachedVoiceLocaleIsValid = false;
            this.voiceLocaleCacheInitialized = false;
        }

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
                Log.Warning($"Empty Voice File node in event {activeEvent.name}");
            }
        }

        private async UniTask LoadClipAsync(AudioEventPreparation preparation, string eventName)
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
                    Log.Error($"No loader found for VoiceFile reference '{externalReference?.name}' in event '{eventName}'.");
                    return;
                }

                if (!handle.IsSuccess || handle.Clip == null || handle.Clip.length <= 0f)
                {
                    string referenceName = externalReference != null ? externalReference.name : "<missing>";
                    Log.Error($"Voice audio reference '{referenceName}' failed to load.");
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
            catch (Exception e) when (
                e is not OutOfMemoryException &&
                e is not AccessViolationException)
            {
                string referenceName = externalReference != null ? externalReference.name : "<missing>";
                Log.Error(
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
            // Source dropdown + clip/ref field + volume + pitch + voice locale.
            float h = TitleBarH + R(5);
            if (string.IsNullOrEmpty(this.voiceLocaleCode))
            {
                h += R(2);
            }
            else if (!TryGetVoiceLocale(out _))
            {
                h += R(2);
            }
            // External mode may show two extra info label rows.
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

            var newMode = (AudioFile.AudioFileSourceMode)EditorGUILayout.EnumPopup(
                "Source",
                GetEffectiveSourceMode());
            AudioClip newFile = this.file;
            AudioClipReference newExternalReference = this.externalReference;

            if (newMode == AudioFile.AudioFileSourceMode.EmbeddedClip)
            {
                newFile = EditorGUILayout.ObjectField(
                    "Audio Clip",
                    this.file,
                    typeof(AudioClip),
                    false) as AudioClip;
                newExternalReference = null;
            }
            else
            {
                newExternalReference = EditorGUILayout.ObjectField(
                    "Audio Reference",
                    this.externalReference,
                    typeof(AudioClipReference),
                    false) as AudioClipReference;
                newFile = null;
                if (newExternalReference != null)
                {
                    EditorGUILayout.LabelField(
                        "Kind",
                        newExternalReference.LocationKind.ToString(),
                        EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(
                        "Location",
                        newExternalReference.GetDisplayLocation(),
                        EditorStyles.wordWrappedMiniLabel);
                }
            }

            float newVolumeOffset = EditorGUILayout.Slider(
                "Volume Offset",
                this.volumeOffset,
                -1f,
                1f);
            float newPitchOffset = EditorGUILayout.Slider(
                "Pitch Offset",
                this.pitchOffset,
                -3f,
                3f);

            bool sourceNeedsNormalization = newMode != this.sourceMode;
            if (EditorGUI.EndChangeCheck() || sourceNeedsNormalization)
            {
                Undo.RecordObject(this, "Edit Voice File");
                this.sourceMode = newMode;
                this.file = newFile;
                this.externalReference = newExternalReference;
                this.volumeOffset = newVolumeOffset;
                this.pitchOffset = newPitchOffset;
                EditorUtility.SetDirty(this);
            }

            string enteredLocale = EditorGUILayout.TextField(
                "Voice Locale",
                VoiceLocaleCode);
            if (!string.Equals(
                    enteredLocale,
                    VoiceLocaleCode,
                    StringComparison.Ordinal))
            {
                Undo.RecordObject(this, "Set Voice Locale");
                if (string.IsNullOrEmpty(enteredLocale))
                {
                    this.voiceLocaleCode = string.Empty;
                    InvalidateVoiceLocaleCache();
                }
                else if (VoiceLocaleId.TryCreate(enteredLocale, out VoiceLocaleId canonicalLocale))
                {
                    this.voiceLocaleCode = canonicalLocale.Code;
                    this.cachedLocaleCode = this.voiceLocaleCode;
                    this.cachedVoiceLocale = canonicalLocale;
                    this.cachedVoiceLocaleIsValid = true;
                    this.voiceLocaleCacheInitialized = true;
                }
                else
                {
                    // Preserve invalid authoring input so validation can report and locate it.
                    this.voiceLocaleCode = enteredLocale;
                    InvalidateVoiceLocaleCache();
                }

                EditorUtility.SetDirty(this);
            }

            if (string.IsNullOrEmpty(this.voiceLocaleCode))
            {
                EditorGUILayout.HelpBox(
                    "Voice Locale is required when this node is connected to a Voice Locale Selector.",
                    MessageType.Info);
            }
            else if (!TryGetVoiceLocale(out _))
            {
                EditorGUILayout.HelpBox(
                    "Voice Locale must be a bounded BCP 47-style code such as en or pt-BR.",
                    MessageType.Error);
            }
        }
#endif
    }
}
