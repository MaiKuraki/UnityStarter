// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.Collections.Generic;
#endif

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// The input connector for an AudioNode
    /// </summary>
    public class AudioNodeInput : ScriptableObject
    {
        /// <summary>
        /// Perimeter of the connector object in the graph
        /// </summary>
        public Rect Window = new Rect(0, 0, ConnectorSize, ConnectorSize);
        /// <summary>
        /// The node that this connector is an input for
        /// </summary>
        [SerializeField]
        private AudioNode parentNode = null;
        /// <summary>
        /// All of the outputs that have connections to this input
        /// </summary>
        [SerializeField]
        private AudioNodeOutput[] connectedNodes = new AudioNodeOutput[0];

#if UNITY_EDITOR

        private const int MaxEditorConnections = 1024;
        private static readonly HashSet<AudioNode> CycleVisitedNodes = new HashSet<AudioNode>();
        private static readonly Stack<AudioNode> CycleTraversalStack = new Stack<AudioNode>();

        /// <summary>
        /// Whether the connector accepts more than one output to connect to it
        /// </summary>
        [SerializeField, HideInInspector]
        private bool forceSingleConnection = false;

#endif

        /// <summary>
        /// The size in pixels for the connector in the graph
        /// </summary>
        private const float ConnectorSize = 20;

        /// <summary>
        /// EDTIOR: The position of the center of the connector's Rect
        /// </summary>
        public Vector2 Center
        {
            get
            {
                Vector2 tempPos = this.Window.position;
                tempPos.x += ConnectorSize / 2;
                tempPos.y += ConnectorSize / 2;
                return tempPos;
            }
        }

        /// <summary>
        /// Public accessor for the outputs connected to this input
        /// </summary>
        public AudioNodeOutput[] ConnectedNodes
        {
            get { return this.connectedNodes; }
        }

        /// <summary>
        /// Public accessor for the node that this connector is an input for
        /// </summary>
        public AudioNode ParentNode
        {
            get { return this.parentNode; }
            set { this.parentNode = value; }
        }

#if UNITY_EDITOR

        /// <summary>
        /// EDITOR: Toggle whether this input can accept multiple connections or if a new connection will overwrite the previous one
        /// </summary>
        /// <param name="toggle"></param>
        public void SetSingleConnection(bool toggle)
        {
            this.forceSingleConnection = toggle;
        }

        /// <summary>
        /// EDITOR: Connect a new output to this input
        /// </summary>
        /// <param name="newOutput">The new output to connect</param>
        public void AddConnection(AudioNodeOutput newOutput)
        {
            TryAddConnection(newOutput);
        }

        public bool TryAddConnection(AudioNodeOutput newOutput)
        {
            if (newOutput == null || newOutput.ParentNode == null || this.parentNode == null)
                return false;

            if (WouldCreateCycle(newOutput.ParentNode, this.parentNode))
            {
                Debug.LogWarning(
                    $"Audio graph connection rejected because it would create a cycle between " +
                    $"'{this.parentNode.name}' and '{newOutput.ParentNode.name}'.");
                return false;
            }

            if (this.forceSingleConnection)
            {
                if (this.connectedNodes.Length == 1 && this.connectedNodes[0] == newOutput)
                    return false;

                Undo.RecordObject(this, "Connect Audio Nodes");
                this.connectedNodes = new[] { newOutput };
                EditorUtility.SetDirty(this);
                return true;
            }

            for (int i = 0; i < this.connectedNodes.Length; i++)
            {
                if (this.connectedNodes[i] == newOutput)
                {
                    return false;
                }
            }

            if (this.connectedNodes.Length >= MaxEditorConnections)
            {
                Debug.LogError($"Audio graph input connection limit ({MaxEditorConnections}) reached.");
                return false;
            }

            Undo.RecordObject(this, "Connect Audio Nodes");
            AudioNodeOutput[] newOutputs = new AudioNodeOutput[this.connectedNodes.Length + 1];
            this.connectedNodes.CopyTo(newOutputs, 0);
            newOutputs[newOutputs.Length - 1] = newOutput;
            this.connectedNodes = newOutputs;
            EditorUtility.SetDirty(this);
            return true;
        }

        /// <summary>
        /// EDITOR: Sort the inputs in descending vertical order in the graph
        /// </summary>
        public void SortConnections()
        {
            if (this.connectedNodes.Length <= 1)
            {
                return;
            }

            Undo.RecordObject(this, "Sort Audio Connections");

            for (int i = 1; i < this.connectedNodes.Length; i++)
            {
                AudioNodeOutput current = this.connectedNodes[i];
                AudioNode currentNode = current != null ? current.ParentNode : null;
                float currentY = currentNode != null ? currentNode.NodeRect.y : float.MaxValue;
                int insertIndex = i - 1;

                while (insertIndex >= 0)
                {
                    AudioNodeOutput previous = this.connectedNodes[insertIndex];
                    AudioNode previousNode = previous != null ? previous.ParentNode : null;
                    float previousY = previousNode != null ? previousNode.NodeRect.y : float.MaxValue;
                    if (previousY <= currentY)
                    {
                        break;
                    }

                    this.connectedNodes[insertIndex + 1] = previous;
                    insertIndex--;
                }

                this.connectedNodes[insertIndex + 1] = current;
            }
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// EDITOR: Clear an output connection
        /// </summary>
        /// <param name="outputToDelete">Output to disconnect from this input</param>
        public void RemoveConnection(AudioNodeOutput outputToDelete)
        {
            TryRemoveConnection(outputToDelete);
        }

        public bool TryRemoveConnection(AudioNodeOutput outputToDelete)
        {
            if (outputToDelete == null)
            {
                return false;
            }

            int keptCount = 0;
            for (int i = 0; i < this.connectedNodes.Length; i++)
            {
                if (this.connectedNodes[i] != outputToDelete)
                {
                    keptCount++;
                }
            }

            if (keptCount == this.connectedNodes.Length)
            {
                return false;
            }

            Undo.RecordObject(this, "Disconnect Audio Nodes");
            if (keptCount == 0)
            {
                this.connectedNodes = System.Array.Empty<AudioNodeOutput>();
                EditorUtility.SetDirty(this);
                return true;
            }

            AudioNodeOutput[] updatedNodes = new AudioNodeOutput[keptCount];
            int writeIndex = 0;
            for (int i = 0; i < this.connectedNodes.Length; i++)
            {
                AudioNodeOutput tempOutput = this.connectedNodes[i];
                if (tempOutput != outputToDelete)
                {
                    updatedNodes[writeIndex] = tempOutput;
                    writeIndex++;
                }
            }

            this.connectedNodes = updatedNodes;
            EditorUtility.SetDirty(this);
            return true;
        }

        /// <summary>
        /// EDITOR: Disconnect all output connections
        /// </summary>
        public void RemoveAllConnections()
        {
            TryRemoveAllConnections();
        }

        public bool TryRemoveAllConnections()
        {
            if (this.connectedNodes.Length == 0) return false;
            Undo.RecordObject(this, "Clear Audio Connections");
            this.connectedNodes = System.Array.Empty<AudioNodeOutput>();
            EditorUtility.SetDirty(this);
            return true;
        }

        private static bool WouldCreateCycle(AudioNode sourceNode, AudioNode targetNode)
        {
            if (sourceNode == targetNode) return true;

            CycleVisitedNodes.Clear();
            CycleTraversalStack.Clear();
            CycleTraversalStack.Push(sourceNode);

            while (CycleTraversalStack.Count > 0)
            {
                AudioNode current = CycleTraversalStack.Pop();
                if (current == null || !CycleVisitedNodes.Add(current)) continue;
                if (current == targetNode)
                {
                    CycleVisitedNodes.Clear();
                    CycleTraversalStack.Clear();
                    return true;
                }

                AudioNodeInput currentInput = current.Input;
                AudioNodeOutput[] outputs = currentInput != null ? currentInput.ConnectedNodes : null;
                if (outputs == null) continue;
                for (int i = 0; i < outputs.Length; i++)
                {
                    AudioNode connectedNode = outputs[i] != null ? outputs[i].ParentNode : null;
                    if (connectedNode != null)
                        CycleTraversalStack.Push(connectedNode);
                }
            }

            CycleVisitedNodes.Clear();
            CycleTraversalStack.Clear();
            return false;
        }

#endif
    }
}
