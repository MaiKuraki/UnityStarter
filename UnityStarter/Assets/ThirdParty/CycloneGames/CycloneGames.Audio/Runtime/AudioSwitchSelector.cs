// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// An AudioNode for randomly choosing one of its connected nodes
    /// </summary>
    public class AudioSwitchSelector : AudioNode
    {
        [SerializeField]
        private AudioSwitch switchObject;

        /// <summary>
        /// Randomly select a connected node
        /// </summary>
        /// <param name="activeEvent">The existing runtime audio event</param>
        public override void ProcessNode(ActiveEvent activeEvent)
        {
            if (this.input.ConnectedNodes == null || this.input.ConnectedNodes.Length == 0)
            {
                Debug.LogWarningFormat("No connected nodes for {0}", this.name);
                return;
            }

            int nodeNum = switchObject.CurrentValue;

            ProcessConnectedNode(nodeNum, activeEvent);
        }

#if UNITY_EDITOR

        /// <summary>
        /// EDITOR: Set the initial values for the node's properties
        /// </summary>
        /// <param name="position">The position of the node on the graph</param>
        public override void InitializeNode(Vector2 position)
        {
            this.name = "Switch Selector";
            this.nodeRect.height = 50;
            this.nodeRect.width = 200;
            this.nodeRect.position = position;
            AddInput();
            AddOutput();
        }

        protected override void DrawProperties()
        {
            this.switchObject = EditorGUILayout.ObjectField(this.switchObject, typeof(AudioSwitch), false) as AudioSwitch;
        }

#endif
    }
}