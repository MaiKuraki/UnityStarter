// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    public class AudioBlendContainer : AudioNode
    {
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

            for (int i = 0; i < this.input.ConnectedNodes.Length; i++)
            {
                ProcessConnectedNode(i, activeEvent);
            }
        }

#if UNITY_EDITOR

        /// <summary>
        /// EDITOR: Set the initial values for the node's properties
        /// </summary>
        /// <param name="position">The position of the node on the graph</param>
        public override void InitializeNode(Vector2 position)
        {
            this.name = "Blend Container";
            this.nodeRect.height = 50;
            this.nodeRect.width = 150;
            this.nodeRect.position = position;
            AddInput();
            AddOutput();
        }

#endif
    }
}