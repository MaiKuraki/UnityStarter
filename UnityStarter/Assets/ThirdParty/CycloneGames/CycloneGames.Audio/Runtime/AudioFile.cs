// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
using System;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// An AudioNode containing a reference to an AudioClip
    /// </summary>
    public class AudioFile : AudioNode
    {
        /// <summary>
        /// The audio clip to be set on the AudioSource if this node is processed
        /// </summary>
        [SerializeField]
        private AudioClip file = null;

        [SerializeField]
        private string filePath = "";

        public AudioClip File
        {
            get { return this.file; }
            set { this.file = value; }
        }

        /// <summary>
        /// The amount of volume change to apply if this node is processed
        /// </summary>
        [SerializeField, Range(-1, 1)]
        private float volumeOffset = 0;
        /// <summary>
        /// The amount of pitch change to apply if this node is processed
        /// </summary>
        [SerializeField, Range(-1, 1)]
        private float pitchOffset = 0;
        /// <summary>
        /// The minimum start position of the node
        /// </summary>
        [Range(0, 1)]
        public float minStartTime = 0;
        /// <summary>
        /// The maximum start position of the node 
        /// </summary>
        [Range(0, 1)]
        public float maxStartTime = 0;
        /// <summary> 
        /// The Start time for the audio file to stay playing at 
        /// </summary>
        public float startTime { get; private set; }

        /// <summary>
        /// Apply all modifications to the ActiveEvent before it gets played
        /// </summary>
        /// <param name="activeEvent">The runtime event being prepared for playback</param>
        public override void ProcessNode(ActiveEvent activeEvent)
        {
            activeEvent.ModulateVolume(this.volumeOffset);
            activeEvent.ModulatePitch(this.pitchOffset);

            if (this.file != null)
            {
                if (this.file.length <= 0)
                {
                    Debug.LogWarningFormat("Invalid file length for node {0}, Event: {1}", this.name, activeEvent.rootEvent.name);
                    return;
                }
                CalculateStartTime(this.file);
                activeEvent.AddEventSource(this.file, null, null, startTime);
            }
            else if (!string.IsNullOrEmpty(this.filePath))
            {
                activeEvent.isAsync = true;
                LoadClipAsync(activeEvent).Forget();
            }
            else
            {
                Debug.LogWarningFormat("No file or path in node {0}, Event: {1}", this.name, activeEvent.rootEvent.name);
            }
        }

        private async UniTaskVoid LoadClipAsync(ActiveEvent activeEvent)
        {
            try
            {
                var audioType = GetAudioType(this.filePath);
                using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(this.filePath, audioType))
                {
                    await www.SendWebRequest().ToUniTask(cancellationToken: activeEvent.GetCancellationToken());

                    if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
                    {
                        Debug.LogError($"Error loading audio '{this.filePath}': {www.error}");
                        activeEvent.StopImmediate();
                    }
                    else
                    {
                        AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                        if (clip == null || clip.length <= 0)
                        {
                            Debug.LogWarningFormat("Invalid audio clip loaded for node {0}, Event: {1}", this.name, activeEvent.rootEvent.name);
                            activeEvent.StopImmediate();
                            return;
                        }

                        clip.name = System.IO.Path.GetFileNameWithoutExtension(this.filePath);
                        CalculateStartTime(clip);
                        activeEvent.AddEventSource(clip, null, null, startTime);
                        activeEvent.OnAsyncLoadCompleted();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // This is expected if the event is stopped while loading
            }
            catch (Exception e)
            {
                Debug.LogError($"Exception while loading audio clip from path '{this.filePath}': {e.Message}");
                activeEvent.StopImmediate();
            }
        }

        /// <summary>
        /// If the min and max start time are not the same, then generate a random value between min and max start time.
        /// </summary>
        /// <returns></returns>
        public void CalculateStartTime(AudioClip clip)
        {
            if (clip == null)
            {
                this.startTime = 0;
                return;
            }

            float startTimeRatio = 0;
            if (this.minStartTime != this.maxStartTime)
            {
                startTimeRatio = UnityEngine.Random.Range(this.minStartTime, this.maxStartTime);
            }
            else
            {
                startTimeRatio = this.minStartTime;
            }

            this.startTime = clip.length * startTimeRatio;
        }

        private AudioType GetAudioType(string path)
        {
            path = path.ToLowerInvariant();
            if (path.EndsWith(".mp3")) return AudioType.MPEG;
            if (path.EndsWith(".wav")) return AudioType.WAV;
            if (path.EndsWith(".ogg")) return AudioType.OGGVORBIS;
            if (path.EndsWith(".aiff") || path.EndsWith(".aif")) return AudioType.AIFF;
            return AudioType.UNKNOWN;
        }

#if UNITY_EDITOR

        /// <summary>
        /// The width in pixels for the node's window in the graph
        /// </summary>
        private const float NodeWidth = 300;
        private const float NodeHeight = 130;

        /// <summary>
        /// EDITOR: Initialize the node's properties when it is first created
        /// </summary>
        /// <param name="position">The position of the new node in the graph</param>
        public override void InitializeNode(Vector2 position)
        {
            this.name = "Audio File";
            this.nodeRect.position = position;
            this.nodeRect.width = NodeWidth;
            this.nodeRect.height = NodeHeight;
            AddOutput();
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// EDITOR: Display the node's properties in the graph
        /// </summary>
        protected override void DrawProperties()
        {
            this.file = EditorGUILayout.ObjectField("Audio Clip (Preloaded)", this.file, typeof(AudioClip), false) as AudioClip;
            this.filePath = EditorGUILayout.TextField("File Path (for Async)", this.filePath);

            if (this.file != null && this.name != this.file.name)
            {
                this.name = this.file.name;
            }
            this.volumeOffset = EditorGUILayout.Slider("Volume Offset", this.volumeOffset, -1, 1);
            this.pitchOffset = EditorGUILayout.Slider("Pitch Offset", this.pitchOffset, -1, 1);
            EditorGUILayout.MinMaxSlider("Start Time", ref this.minStartTime, ref this.maxStartTime, 0, 1);
        }

#endif
    }
}
