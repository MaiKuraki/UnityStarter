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
        /// The distance within which the sound stays at maximum volume (inner radius).
        /// Prevents volume spikes at very close range.
        /// </summary>
        [SerializeField]
        public float MinDistance = 1;
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

        // ---- Advanced 3D Spatial Features ----

        /// <summary>
        /// Optional curve mapping normalized distance (0=MinDistance, 1=MaxDistance) to spread angle (0-360).
        /// When enabled, overrides the fixed Spread value. Allows sources to widen as the listener gets close.
        /// </summary>
        [SerializeField]
        public bool useSpreadCurve = false;
        [SerializeField]
        public AnimationCurve spreadCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 0f);

        /// <summary>
        /// Enable distance-based low-pass filtering to simulate air absorption.
        /// High frequencies attenuate faster over distance than low frequencies.
        /// </summary>
        [SerializeField]
        public bool useDistanceLowPass = false;
        /// <summary>
        /// Curve mapping normalized distance (0=MinDistance, 1=MaxDistance) to LP cutoff frequency.
        /// Default: 22000 Hz at distance 0, 800 Hz at distance 1.
        /// </summary>
        [SerializeField]
        public AnimationCurve distanceLowPassCurve = new AnimationCurve(
            new Keyframe(0f, 22000f), new Keyframe(1f, 800f));

        /// <summary>
        /// Enable directional cone attenuation. Sound is louder in the emitter's forward direction.
        /// </summary>
        [SerializeField]
        public bool useConeAttenuation = false;
        /// <summary>
        /// Inner cone angle (degrees). Sound at full volume within this angle.
        /// </summary>
        [SerializeField, Range(0f, 360f)]
        public float coneInnerAngle = 60f;
        /// <summary>
        /// Outer cone angle (degrees). Sound attenuates between inner and outer angles.
        /// </summary>
        [SerializeField, Range(0f, 360f)]
        public float coneOuterAngle = 120f;
        /// <summary>
        /// Volume multiplier applied when the listener is completely outside the outer cone.
        /// </summary>
        [SerializeField, Range(0f, 1f)]
        public float coneOuterVolume = 0.25f;

        /// <summary>
        /// The width in pixels for the node's window in the graph
        /// </summary>
        private const float NodeWidth  = 310f;
        private const float TitleBarH  = 18f;
        private const float RowH       = 19f;
        private const float RowGap     =  2f;
        private const float SectionGap =  6f;
        private const float BottomPad  = 10f;
        private const float HelpBoxH   = 38f;  // HelpBox (~2 rows)

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

            SetSourceProperties(activeEvent);
        }

        private void SetSourceProperties(ActiveEvent activeEvent)
        {
            int count = activeEvent.SourceCount;
            for (int i = 0; i < count; i++)
            {
                var es = activeEvent.GetSource(i);
                if (!es.IsValid) continue;

                AudioSource eventSource = es.source;
                eventSource.outputAudioMixerGroup = this.mixerGroup;
                eventSource.loop = this.loop;
                eventSource.spatialBlend = this.spatialBlend;
                if (this.spatialBlend > 0)
                {
                    eventSource.spatialize = this.HRTF;
                    eventSource.minDistance = this.MinDistance;
                    eventSource.maxDistance = this.MaxDistance;
                    if (this.attenuationCurve != null && this.attenuationCurve.length > 0)
                    {
                        eventSource.rolloffMode = AudioRolloffMode.Custom;
                        eventSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, this.attenuationCurve);
                    }
                    else
                    {
                        eventSource.rolloffMode = AudioRolloffMode.Logarithmic;
                        string eventName = activeEvent.rootEvent != null ? activeEvent.rootEvent.name : activeEvent.name;
                        Debug.LogWarning($"[AudioOutput] Node '{this.name}' in event '{eventName}' has Spatial Blend > 0 but an empty Attenuation curve. Falling back to Logarithmic rolloff. Fix it in the Audio Graph.");
                    }
                    eventSource.dopplerLevel = this.dopplerLevel;
                    eventSource.reverbZoneMix = this.ReverbZoneMix;
                    if (this.ReverbZoneMix == 0) eventSource.bypassReverbZones = true;
                    eventSource.spread = this.Spread;

                    // Attach AudioLowPassFilter for distance-based air absorption or occlusion
                    if (this.useDistanceLowPass || AudioManager.IsOcclusionEnabled)
                    {
                        var lpf = eventSource.GetComponent<AudioLowPassFilter>();
                        if (lpf == null) lpf = eventSource.gameObject.AddComponent<AudioLowPassFilter>();
                        lpf.cutoffFrequency = 22000f;
                        lpf.lowpassResonanceQ = 1f;
                    }
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
            this.nodeRect.width    = NodeWidth;
            this.nodeRect.height   = CalcHeight();
            AddInput();
        }

        private float CalcHeight()
        {
            float R(int n) => n * (RowH + RowGap);

            // Always-visible: MixerGroup + Volume + Pitch + Loop + SpatialBlend
            float h = TitleBarH + R(5);

            // 3D section (shown but disabled when spatialBlend == 0)
            // HRTF + MinDist + MaxDist + Attenuation + Doppler + ReverbZoneMix + Spread
            h += R(7);

            // Warning HelpBox when spatialBlend > 0 and attenuation curve is missing
            if (this.spatialBlend > 0f && (this.attenuationCurve == null || this.attenuationCurve.length == 0))
                h += HelpBoxH + RowGap;

            // Advanced 3D block — only rendered when spatialBlend > 0
            if (this.spatialBlend > 0f)
            {
                h += SectionGap + R(1); // "Advanced 3D" header
                h += R(1);              // useSpreadCurve toggle
                if (this.useSpreadCurve) h += R(1);
                h += R(1);              // useDistanceLowPass toggle
                if (this.useDistanceLowPass) h += R(1);
                h += R(1);              // useConeAttenuation toggle
                if (this.useConeAttenuation) h += R(3);
            }

            return h + BottomPad;
        }

        public override void DrawNode(int id)
        {
            this.nodeRect.height = CalcHeight();
            this.nodeRect = GUI.Window(id, this.nodeRect, DrawWindow, this.name);
            DrawInput();
            // Output node has no output connector
        }

        /// <summary>
        /// EDITOR: Draw the node's properties in the node window in the graph
        /// </summary>
        protected override void DrawProperties()
        {
            EditorGUI.BeginChangeCheck();

            // ---- Always-visible section ----
            this.mixerGroup = EditorGUILayout.ObjectField("Mixer Group", this.mixerGroup, typeof(AudioMixerGroup), false) as AudioMixerGroup;
            EditorGUILayout.MinMaxSlider("Volume", ref this.MinVolume, ref this.MaxVolume, Volume_Min, Volume_Max);
            EditorGUILayout.MinMaxSlider("Pitch",  ref this.MinPitch,  ref this.MaxPitch,  Pitch_Min,  Pitch_Max);
            this.loop         = EditorGUILayout.Toggle("Loop",          this.loop);
            this.spatialBlend = EditorGUILayout.Slider("Spatial Blend", this.spatialBlend, 0f, 1f);

            // ---- 3D section (disabled when 2D) ----
            using (new EditorGUI.DisabledScope(this.spatialBlend == 0f))
            {
                this.HRTF         = EditorGUILayout.Toggle("HRTF",          this.HRTF);
                this.MinDistance  = Mathf.Max(0.01f, EditorGUILayout.FloatField("Min Distance", this.MinDistance));
                this.MaxDistance  = Mathf.Max(this.MinDistance, EditorGUILayout.FloatField("Max Distance", this.MaxDistance));
                this.attenuationCurve = EditorGUILayout.CurveField("Attenuation", this.attenuationCurve);
                this.dopplerLevel = EditorGUILayout.FloatField("Doppler Level", this.dopplerLevel);
                this.ReverbZoneMix = EditorGUILayout.Slider("Reverb Zone Mix", this.ReverbZoneMix, 0f, 1.1f);
                this.Spread       = EditorGUILayout.Slider("Spread",          this.Spread,          0f, 360f);
            }

            // ---- Configuration warning ----
            if (this.spatialBlend > 0f && (this.attenuationCurve == null || this.attenuationCurve.length == 0))
                EditorGUILayout.HelpBox("Attenuation curve is empty — will fall back to Logarithmic rolloff at runtime.", MessageType.Warning);

            // ---- Advanced 3D sub-section (only when spatial) ----
            if (this.spatialBlend > 0f)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Advanced 3D", EditorStyles.boldLabel);

                this.useSpreadCurve = EditorGUILayout.Toggle("Spread Curve", this.useSpreadCurve);
                if (this.useSpreadCurve)
                {
                    EditorGUI.indentLevel++;
                    this.spreadCurve = EditorGUILayout.CurveField("Dist → Spread°", this.spreadCurve);
                    EditorGUI.indentLevel--;
                }

                this.useDistanceLowPass = EditorGUILayout.Toggle("Distance LP", this.useDistanceLowPass);
                if (this.useDistanceLowPass)
                {
                    EditorGUI.indentLevel++;
                    this.distanceLowPassCurve = EditorGUILayout.CurveField("Dist → LP Hz", this.distanceLowPassCurve);
                    EditorGUI.indentLevel--;
                }

                this.useConeAttenuation = EditorGUILayout.Toggle("Cone Atten.", this.useConeAttenuation);
                if (this.useConeAttenuation)
                {
                    EditorGUI.indentLevel++;
                    this.coneInnerAngle  = EditorGUILayout.Slider("Inner°", this.coneInnerAngle,  0f, 360f);
                    this.coneOuterAngle  = EditorGUILayout.Slider("Outer°", Mathf.Max(this.coneOuterAngle, this.coneInnerAngle), this.coneInnerAngle, 360f);
                    this.coneOuterVolume = EditorGUILayout.Slider("Out Vol", this.coneOuterVolume, 0f, 1f);
                    EditorGUI.indentLevel--;
                }
            }

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(this);
        }

#endif
    }
}