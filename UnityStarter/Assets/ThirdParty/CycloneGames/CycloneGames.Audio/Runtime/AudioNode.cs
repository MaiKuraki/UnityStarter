// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// A generic container for audio assets and containers in an AudioEvent
    /// </summary>
    [System.Serializable]
    public class AudioNode : ScriptableObject
    {
        /// <summary>
        /// The visual size of the node in the graph
        /// </summary>
        [SerializeField]
        protected Rect nodeRect = new Rect(0, 0, 200, 100);
        /// <summary>
        /// The left connector for the node that can be connected to an AudioNodeOutput
        /// </summary>
        [SerializeField]
        protected AudioNodeInput input;
        /// <summary>
        /// The right connector for the node that can be connected to an AudioNodeInput
        /// </summary>
        [SerializeField, HideInInspector]
        protected AudioNodeOutput output;

        internal AudioNodeInput RuntimeInput => input;

        /// <summary>
        /// The minimum possible value for an AudioSource's volume property
        /// </summary>
        protected const float Volume_Min = 0;
        /// <summary>
        /// The maximum possible value for an AudioSource's volume property
        /// </summary>
        protected const float Volume_Max = 1;
        /// <summary>
        /// The minimum possible value for an AudioSource's pitch property
        /// </summary>
        protected const float Pitch_Min = 0.01f;
        /// <summary>
        /// The maximum possible value for an AudioSource's pitch property
        /// </summary>
        protected const float Pitch_Max = 3;

        /// <summary>
        /// Base function for node functionality when the AudioEvent is played
        /// </summary>
        /// <param name="activeEvent">The existing runtime instance of an event</param>
        public virtual void ProcessNode(ActiveEvent activeEvent)
        {
            return;
        }

        /// <summary>
        /// Get the connected node of index nodeNum and process it on the ActiveEvent
        /// </summary>
        /// <param name="nodeNum">The index of the connected node to process</param>
        /// <param name="activeEvent">The existing runtime instance of an AudioEvent</param>
        protected void ProcessConnectedNode(int nodeNum, ActiveEvent activeEvent)
        {
            if (this.input == null)
            {
                Debug.LogWarningFormat("{0} does not have an input on node {1}", activeEvent, this.name);
                return;
            }

            AudioNodeOutput[] connectedNodes = this.input.ConnectedNodes;
            if (connectedNodes == null || nodeNum < 0 || nodeNum >= connectedNodes.Length)
            {
                Debug.LogWarningFormat("{0} tried to access invalid connected node {1}", this.name, nodeNum);
                return;
            }

            AudioNodeOutput output = connectedNodes[nodeNum];
            AudioNode parentNode = output != null ? output.ParentNode : null;
            if (parentNode == null)
            {
                Debug.LogWarningFormat("{0} tried to process a missing connected node at index {1}", this.name, nodeNum);
                return;
            }

            if (!activeEvent.TryEnterGraphNode(parentNode))
            {
                return;
            }

            try
            {
                parentNode.ProcessNode(activeEvent);
            }
            finally
            {
                activeEvent.ExitGraphNode(parentNode);
            }
        }

        /// <summary>
        /// Reset runtime properties associated with the node
        /// </summary>
        public virtual void Reset()
        {

        }

#if UNITY_EDITOR

        /// <summary>
        /// EDITOR: Public accessor for the visual size of the node in the graph
        /// </summary>
        public Rect NodeRect
        {
            get { return this.nodeRect; }
            set { this.nodeRect = value; }
        }

        /// <summary>
        /// EDITOR: Public accessor for the left connector of the node
        /// </summary>
        public AudioNodeInput Input
        {
            get { return this.input; }
        }

        /// <summary>
        /// EDITOR: Public accessor for the right connector of the node
        /// </summary>
        public AudioNodeOutput Output
        {
            get { return this.output; }
        }

        /// <summary>
        /// EDITOR: Initialize required properties of the node when it is first created
        /// </summary>
        /// <param name="position"></param>
        public virtual void InitializeNode(Vector2 position)
        {
            this.name = "Blank Node";
            this.nodeRect.position = position;
        }

        /// <summary>
        /// EDITOR: Delete input and output connectors
        /// </summary>
        public virtual void DeleteConnections()
        {
            Undo.RecordObject(this, "Delete Audio Node Connections");
            if (this.input != null)
            {
                Undo.DestroyObjectImmediate(this.input);
                this.input = null;
            }
            if (this.output != null)
            {
                Undo.DestroyObjectImmediate(this.output);
                this.output = null;
            }
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// EDITOR: Draw draggable GUI window in the graph
        /// </summary>
        /// <param name="id"></param>
        public virtual void DrawNode(int id)
        {
            Rect previousRect = this.nodeRect;
            bool interactionStarted = Event.current.type == EventType.MouseDown
                && previousRect.Contains(Event.current.mousePosition);
            if (interactionStarted)
            {
                Undo.RecordObject(this, "Edit Audio Node");
            }

            EditorGUI.BeginChangeCheck();
            Rect nextRect = GUI.Window(id, previousRect, DrawWindow, this.name);
            bool windowChanged = EditorGUI.EndChangeCheck();
            if (nextRect.position != previousRect.position)
            {
                if (!interactionStarted)
                {
                    Undo.RecordObject(this, "Move Audio Node");
                }
                this.nodeRect = nextRect;
                windowChanged = true;
            }

            if (windowChanged)
            {
                EditorUtility.SetDirty(this);
            }
            DrawInput();
            DrawOutput();
        }

        /// <summary>
        /// EDITOR: Set the position of the node in the graph
        /// </summary>
        /// <param name="newPosition"></param>
        public void SetPosition(Vector2 newPosition)
        {
            if (this.nodeRect.position == newPosition) return;
            Undo.RecordObject(this, "Move Audio Node");
            this.nodeRect.position = newPosition;
            EditorUtility.SetDirty(this);
        }

        public void MoveBy(Vector2 offset)
        {
            EditorUtilityCache.MoveConnectedTree(this, offset);
        }

        /// <summary>
        /// EDITOR: Add a left connector to the node when it is first created
        /// </summary>
        /// <param name="singleConnection"></param>
        protected void AddInput(bool singleConnection = false)
        {
            Undo.RecordObject(this, "Create Audio Node Input");
            this.input = ScriptableObject.CreateInstance<AudioNodeInput>();
            AssetDatabase.AddObjectToAsset(this.input, this);
            Undo.RegisterCreatedObjectUndo(this.input, "Create Audio Node Input");
            this.input.name = this.name + "Input";
            this.input.ParentNode = this;
            this.input.SetSingleConnection(singleConnection);
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// EDITOR: Add a right connector to the node when it is first created
        /// </summary>
        protected void AddOutput()
        {
            Undo.RecordObject(this, "Create Audio Node Output");
            this.output = ScriptableObject.CreateInstance<AudioNodeOutput>();
            AssetDatabase.AddObjectToAsset(this.output, this);
            Undo.RegisterCreatedObjectUndo(this.output, "Create Audio Node Output");
            this.output.name = this.name + "Output";
            this.output.ParentNode = this;
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// EDITOR: Draw the properties of the node in the graph and the drag window
        /// </summary>
        /// <param name="id"></param>
        protected void DrawWindow(int id)
        {
            DrawProperties();
            GUI.DragWindow();
        }

        /// <summary>
        /// EDITOR: Draw associated properties in the node draggable window
        /// </summary>
        protected virtual void DrawProperties() {}

        /// <summary>
        /// EDITOR: Draw the left connector on the node in the graph
        /// </summary>
        protected virtual void DrawInput()
        {
            if (this.input == null)
            {
                return;
            }

            Vector2 tempPos = new Vector2(this.nodeRect.x, this.nodeRect.y);
            tempPos.x -= this.input.Window.width;
            tempPos.y += (this.nodeRect.height / 2) - 5;
            this.input.Window.position = tempPos;

            GUI.DrawTexture(this.input.Window, EditorUtilityCache.ConnectorTexture);

            for (int i = 0; i < this.input.ConnectedNodes.Length; i++)
            {
                AudioNodeOutput tempOutput = this.input.ConnectedNodes[i];
                if (tempOutput == null)
                {
                    continue;
                }

                DrawCurve(tempOutput.Center, this.input.Center);
            }
        }

        /// <summary>
        /// EDITOR: Draw the right connector on the node in the graph
        /// </summary>
        protected virtual void DrawOutput()
        {
            if (this.output == null)
            {
                return;
            }

            Vector2 tempPos = new Vector2(this.nodeRect.x, this.nodeRect.y);
            tempPos.x += this.nodeRect.width;
            tempPos.y += (this.nodeRect.height / 2) - 10;
            this.output.Window.position = tempPos;

            GUI.DrawTexture(this.output.Window, EditorUtilityCache.ConnectorTexture);
        }

        /// <summary>
        /// EDITOR: Draw the line connecting two nodes using a Bezier curve
        /// </summary>
        /// <param name="start">Position of the input node</param>
        /// <param name="end">Position of the output node</param>
        public static void DrawCurve(Vector2 start, Vector2 end)
        {
            Handles.BeginGUI();
            
            Vector3 startPosition = new Vector3(start.x, start.y);
            Vector3 endPosition = new Vector3(end.x, end.y);
            Vector3 startTangent = startPosition + (Vector3.right * 50);
            Vector3 endTangent = endPosition + (Vector3.left * 50);

            // Use a visible color for the connection line and restore it afterward.
            Color originalColor = Handles.color;
            Handles.color = new Color(0f, 1f, 0f, 1f); // Bright green
            Handles.DrawBezier(startPosition, endPosition, startTangent, endTangent, Handles.color, null, 6);
            Handles.color = originalColor;

            Handles.EndGUI();
        }

        protected static class EditorUtilityCache
        {
            private static readonly HashSet<AudioNode> MoveVisitedNodes = new HashSet<AudioNode>(64);
            private static Texture2D connectorTexture;

            public static Texture2D ConnectorTexture
            {
                get
                {
                    if (connectorTexture == null)
                    {
                        connectorTexture = EditorGUIUtility.Load("icons/animationkeyframe.png") as Texture2D;
                    }

                    return connectorTexture;
                }
            }

            public static bool NeedsSortByNodeY(AudioNodeInput input)
            {
                AudioNodeOutput[] connectedNodes = input != null ? input.ConnectedNodes : null;
                if (connectedNodes == null || connectedNodes.Length < 2)
                {
                    return false;
                }

                float previousY = float.NegativeInfinity;
                for (int i = 0; i < connectedNodes.Length; i++)
                {
                    AudioNodeOutput output = connectedNodes[i];
                    float y = output != null && output.ParentNode != null ? output.ParentNode.NodeRect.y : previousY;
                    if (y < previousY)
                    {
                        return true;
                    }

                    previousY = y;
                }

                return false;
            }

            public static void MoveConnectedTree(AudioNode root, Vector2 offset)
            {
                MoveVisitedNodes.Clear();
                MoveConnectedTreeInternal(root, offset);
                MoveVisitedNodes.Clear();
            }

            private static void MoveConnectedTreeInternal(AudioNode node, Vector2 offset)
            {
                if (node == null || !MoveVisitedNodes.Add(node))
                {
                    return;
                }

                Undo.RecordObject(node, "Move Audio Node Tree");
                node.nodeRect.position -= offset;
                EditorUtility.SetDirty(node);

                AudioNodeInput input = node.input;
                AudioNodeOutput[] outputs = input != null ? input.ConnectedNodes : null;
                if (outputs == null)
                {
                    return;
                }

                for (int i = 0; i < outputs.Length; i++)
                {
                    AudioNodeOutput output = outputs[i];
                    if (output != null)
                    {
                        MoveConnectedTreeInternal(output.ParentNode, offset);
                    }
                }
            }
        }

#endif
    }
}
