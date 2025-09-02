// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CycloneGames.Audio.Runtime;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    public class AudioSnapshotTransition : AudioNode
    {
        [SerializeField]
        private AudioMixerSnapshot snapshot;
        [SerializeField]
        private float transitionTime = 0f;
        [SerializeField]
        private bool useParameter = false;
        [SerializeField]
        private AudioParameter parameter = null;
        [SerializeField]
        private AnimationCurve responseCurve = null;

        /// <summary>
        /// Play the snapshot transition on the mixer
        /// </summary>
        /// <param name="activeEvent">The existing runtime audio event</param>
        public override void ProcessNode(ActiveEvent activeEvent)
        {
            if (this.snapshot == null)
            {
                Debug.LogWarningFormat("No snapshot in transition trigger {0}", this.name);
                return;
            }

            activeEvent.hasSnapshotTransition = true;
            float effectiveTransitionTime = 0f;

            if (this.useParameter && this.parameter != null && this.responseCurve != null)
            {
                effectiveTransitionTime = this.responseCurve.Evaluate(this.parameter.CurrentValue);
            }
            else
            {
                effectiveTransitionTime = this.transitionTime;
            }

            this.snapshot.TransitionTo(effectiveTransitionTime);
            AudioManager.DelayRemoveActiveEvent(activeEvent, effectiveTransitionTime);

            if (this.input.ConnectedNodes != null && this.input.ConnectedNodes.Length != 0)
            {
                int nodeNum = Random.Range(0, this.input.ConnectedNodes.Length);
                ProcessConnectedNode(nodeNum, activeEvent);
            }
        }

#if UNITY_EDITOR

        /// <summary>
        /// EDITOR: Set the initial values for the node's properties
        /// </summary>
        /// <param name="position">The position of the node on the graph</param>
        public override void InitializeNode(Vector2 position)
        {
            this.name = "Audio Snapshot Transition";
            this.nodeRect.height = 170;
            this.nodeRect.width = 300;
            this.nodeRect.position = position;
            AddInput();
            AddOutput();
        }

        protected override void DrawProperties()
        {
            this.snapshot = EditorGUILayout.ObjectField("Snapshot", this.snapshot, typeof(AudioMixerSnapshot), false) as AudioMixerSnapshot;
            if (this.snapshot != null)
            {
                this.name = this.snapshot.name;
            }
            EditorGUILayout.BeginToggleGroup("Timer", !this.useParameter);
            this.transitionTime = EditorGUILayout.FloatField("Transition Time", this.transitionTime);
            EditorGUILayout.EndToggleGroup();
            this.useParameter = EditorGUILayout.Toggle("Use Parameter", this.useParameter);
            EditorGUILayout.BeginToggleGroup("Parameter", this.useParameter);
            this.parameter = EditorGUILayout.ObjectField("Parameter", this.parameter, typeof(AudioParameter), false) as AudioParameter;
            this.responseCurve = EditorGUILayout.CurveField("Response Curve", this.responseCurve);
            EditorGUILayout.EndToggleGroup();
        }

#endif
    }
}