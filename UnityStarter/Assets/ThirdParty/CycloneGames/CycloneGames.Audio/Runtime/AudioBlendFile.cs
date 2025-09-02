// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    public class AudioBlendFile : AudioNode
    {
        /// <summary>
        /// The audio clip to be set on the AudioSource if this node is processed
        /// </summary>
        [SerializeField]
        private AudioClip file = null;
        [SerializeField]
        private AudioParameter parameter = null;
        [SerializeField]
        private AnimationCurve responseCurve = new AnimationCurve();
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
            if (this.file == null)
            {
                Debug.LogWarningFormat("No file in node {0}", this.name);
                return;
            }

            RandomStartTime();
            activeEvent.AddEventSource(this.file, this.parameter, this.responseCurve, this.startTime);
        }

        /// <summary>
        /// If the min and max start time are not the same, then generate a random value between min and max start time.
        /// </summary>
        /// <returns></returns>
        public void RandomStartTime()
        {
            if (this.minStartTime != this.maxStartTime)
            {
                this.startTime = UnityEngine.Random.Range(minStartTime, maxStartTime);
            }
            else
            {
                this.startTime = minStartTime;
            }

            this.startTime = file.length * startTime;
        }

#if UNITY_EDITOR

        public AudioClip File
        {
            get { return this.file; }
            set { this.file = value; }
        }

        /// <summary>
        /// The width in pixels for the node's window in the graph
        /// </summary>
        private const float NodeWidth = 300;
        private const float NodeHeight = 110;

        /// <summary>
        /// EDITOR: Initialize the node's properties when it is first created
        /// </summary>
        /// <param name="position">The position of the new node in the graph</param>
        public override void InitializeNode(Vector2 position)
        {
            this.name = "Blend File";
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
            this.file = EditorGUILayout.ObjectField(this.file, typeof(AudioClip), false) as AudioClip;
            if (this.file != null && this.name != this.file.name)
            {
                this.name = this.file.name;
            }
            this.parameter = EditorGUILayout.ObjectField(this.parameter, typeof(AudioParameter), false) as AudioParameter;
            this.responseCurve = EditorGUILayout.CurveField("Blend Curve", this.responseCurve);
            EditorGUILayout.MinMaxSlider("Start Time", ref this.minStartTime, ref this.maxStartTime, 0, 1);
        }

#endif
    }
}