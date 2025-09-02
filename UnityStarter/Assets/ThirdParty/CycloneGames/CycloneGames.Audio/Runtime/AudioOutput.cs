// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// The final node in an audio event
    /// </summary>
    public class AudioOutput : AudioNode
    {
        /// <summary>
        /// The audio bus to route this event to
        /// </summary>
        [SerializeField]
        public AudioMixerGroup mixerGroup = null;
        /// <summary>
        /// The low end of the random volume assigned when playing the event
        /// </summary>
        [Range(0, 1)]
        public float MinVolume = 1;
        /// <summary>
        /// The high end of the random volume assigned when playing the event
        /// </summary>
        [Range(0, 1)]
        public float MaxVolume = 1;
        /// <summary>
        /// The low end of the random pitch assigned when playing the event
        /// </summary>
        [Range(0.01f, 3)]
        public float MinPitch = 1;
        /// <summary>
        /// The high end of the random pitch assigned when playing the event
        /// </summary>
        [Range(0.01f, 3)]
        public float MaxPitch = 1;
        /// <summary>
        /// Whether to make the sound seamlessly loop
        /// </summary>
        [SerializeField]
        public bool loop = false;
        /// <summary>
        /// Amount of spatialization applied to the AudioSource
        /// </summary>
        [SerializeField]
        public float spatialBlend = 0;
        /// <summary>
        /// Whether to use the spatializer assigned in the project's audio settings
        /// </summary>
        [SerializeField]
        public bool HRTF = false;
        /// <summary>
        /// The distance beyond which the sound can no longer be heard
        /// </summary>
        [SerializeField]
        public float MaxDistance = 10;
        /// <summary>
        /// The response curve for how loud the sound will be at different distances
        /// </summary>
        [SerializeField]
        public AnimationCurve attenuationCurve = new AnimationCurve();
        /// <summary>
        /// The amount of doppler effect applied to the sound when moving relative to the listener
        /// </summary>
        [SerializeField]
        public float dopplerLevel = 1;
        /// <summary>
        /// Slider for the amount of reverb applied to the event from the reverb zone
        /// </summary>
        [SerializeField, Range(0,1.1f)]
        public float ReverbZoneMix = 1;
        /// <summary> 
        /// Sets the spread angle (in degrees) of a 3d stereo or multichannel sound in speaker space.
        ///</summary>
        [SerializeField, Range(0, 360)]
        public float Spread = 0;

        /// <summary>
        /// The width in pixels for the node's window in the graph
        /// </summary>
        private const float NodeWidth = 300;
        /// <summary>
        /// The height in pixels for the node's window in the graph
        /// </summary>
        private const float NodeHeight = 250;

        /// <summary>
        /// Apply all of the properties to the ActiveEvent and start processing the rest of the event's nodes
        /// </summary>
        /// <param name="activeEvent"></param>
        public override void ProcessNode(ActiveEvent activeEvent)
        {
            if (this.input.ConnectedNodes == null || this.input.ConnectedNodes.Length == 0)
            {
                Debug.LogWarningFormat("No connected nodes for {0}", this.name);
                return;
            }

            activeEvent.SetVolume(Random.Range(this.MinVolume, this.MaxVolume));
            activeEvent.SetPitch(Random.Range(this.MinPitch, this.MaxPitch));

            ProcessConnectedNode(0, activeEvent);

            SetSourceProperties(activeEvent.sources);
        }

        private void SetSourceProperties(List<EventSource> sources)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                AudioSource eventSource = sources[i].source;
                eventSource.outputAudioMixerGroup = this.mixerGroup;
                eventSource.loop = this.loop;
                eventSource.spatialBlend = this.spatialBlend;
                if (this.spatialBlend > 0)
                {
                    eventSource.spatialize = this.HRTF;
                    eventSource.maxDistance = this.MaxDistance;
                    eventSource.rolloffMode = AudioRolloffMode.Custom;
                    eventSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, this.attenuationCurve);
                    eventSource.dopplerLevel = this.dopplerLevel;
                    eventSource.reverbZoneMix = this.ReverbZoneMix;
                    if (this.ReverbZoneMix == 0)
                    {
                        eventSource.bypassReverbZones = true;
                    }
                    eventSource.spread = this.Spread;
                }
            }
        }

#if UNITY_EDITOR

        /// <summary>
        /// EDITOR: Initialize variables for output settings
        /// </summary>
        /// <param name="position">The position of the node window in the graph</param>
        public override void InitializeNode(Vector2 position)
        {
            this.name = "Output";
            this.nodeRect.position = position;
            this.nodeRect.width = NodeWidth;
            this.nodeRect.height = NodeHeight;
            AddInput();
        }

        /// <summary>
        /// EDITOR: Draw the node's properties in the node window in the graph
        /// </summary>
        protected override void DrawProperties()
        {
            this.mixerGroup = EditorGUILayout.ObjectField("Mixer Group", this.mixerGroup, typeof(AudioMixerGroup), false) as AudioMixerGroup;
            EditorGUILayout.MinMaxSlider("Volume", ref this.MinVolume, ref this.MaxVolume, Volume_Min, Volume_Max);
            EditorGUILayout.MinMaxSlider("Pitch", ref this.MinPitch, ref this.MaxPitch, Pitch_Min, Pitch_Max);
            this.loop = EditorGUILayout.Toggle("Loop", this.loop);
            this.spatialBlend = EditorGUILayout.Slider("Spatial Blend", this.spatialBlend, 0, 1);

            EditorGUI.BeginDisabledGroup(this.spatialBlend == 0);
            this.HRTF = EditorGUILayout.Toggle("HRTF", this.HRTF);
            this.MaxDistance = EditorGUILayout.FloatField("Max Distance", this.MaxDistance);
            this.attenuationCurve = EditorGUILayout.CurveField("Attenuation", this.attenuationCurve);
            this.dopplerLevel = EditorGUILayout.FloatField("Doppler Level", this.dopplerLevel);
            this.ReverbZoneMix = EditorGUILayout.Slider("Reverb Zone Mix", this.ReverbZoneMix, 0, 1.1f);
            this.Spread = EditorGUILayout.Slider("Spread", this.Spread, 0, 360f);
            EditorGUI.EndDisabledGroup();
        }

#endif
    }
}