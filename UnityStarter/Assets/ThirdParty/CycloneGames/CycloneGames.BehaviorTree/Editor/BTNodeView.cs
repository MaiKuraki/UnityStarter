using System;
using CycloneGames.BehaviorTree.Runtime.Conditions;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace CycloneGames.BehaviorTree.Editor
{
    public class BTNodeView : Node
    {
        public Action<BTNodeView> OnNodeSelected;
        public BTNode Node => _node;
        private BTNode _node;
        public Port InputPort;
        public Port OutputPort;
        public static string ConvertToReadableName(string name)
        {
            name = name.Replace("Node", "").Replace("(Clone)", "").Replace("BT", "");
            //separate the words by capital letters
            for (int i = 1; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]))
                {
                    name = name.Insert(i, "#");
                    i++;
                }
            }
            name = name.Replace("#", "<br>");
            return name;
        }
        public BTNodeView(BTNode node) : base(AssetDatabase.GetAssetPath(Resources.Load<VisualTreeAsset>("BT_Node_Layout")))
        {
            this._node = node;
            //separate the words by capital letters
            this.title = ConvertToReadableName(node.name);
            this.viewDataKey = node.GUID;
            style.left = node.Position.x;
            style.top = node.Position.y;
            CreateInputPorts();
            CreateOutputPorts();
            SetUpClasses();
        }
        public BTNodeView(BTNode node, Vector2 position) : base(AssetDatabase.GetAssetPath(Resources.Load<VisualTreeAsset>("BT_Node_Layout")))
        {
            this._node = node;
            this.title = ConvertToReadableName(node.name);
            this.viewDataKey = node.GUID;
            node.Position = position;

            style.left = position.x;
            style.top = position.y;

            CreateInputPorts();
            CreateOutputPorts();
            SetUpClasses();
        }
        public override void OnSelected()
        {
            base.OnSelected();
            OnNodeSelected?.Invoke(this);
        }
        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            Undo.RecordObject(_node, "Behavior Tree(Set Position)");
            _node.Position = new Vector2(newPos.xMin, newPos.yMin);
            EditorUtility.SetDirty(_node);
        }

        public void UpdateState()
        {
            RemoveFromClassList("success");
            RemoveFromClassList("failure");
            RemoveFromClassList("running");
            RemoveFromClassList("not-entered");
            if (!Application.isPlaying) return;
            if (_node.IsStarted)
            {
                AddToClassList("running");
            }
            switch (_node.State)
            {
                case BTState.SUCCESS:
                    AddToClassList("success");
                    break;
                case BTState.FAILURE:
                    AddToClassList("failure");
                    break;
                case BTState.NOT_ENTERED:
                    AddToClassList("not-entered");
                    break;
            }
        }
        private void CreateInputPorts()
        {
            if (_node is DecoratorNode or CompositeNode or ActionNode or ConditionNode)
            {
                InputPort = InstantiatePort(Orientation.Vertical, Direction.Input, Port.Capacity.Single, typeof(bool));
            }
            if (InputPort != null)
            {
                InputPort.portName = "";
                InputPort.style.flexDirection = FlexDirection.Column;
                //center the port
                InputPort.style.alignItems = Align.Center;
                inputContainer.Add(InputPort);
            }
        }

        private void CreateOutputPorts()
        {
            if (_node is CompositeNode)
            {
                OutputPort = InstantiatePort(Orientation.Vertical, Direction.Output, Port.Capacity.Multi, typeof(bool));
            }
            else if (_node is DecoratorNode or BTRootNode)
            {
                OutputPort = InstantiatePort(Orientation.Vertical, Direction.Output, Port.Capacity.Single, typeof(bool));
            }
            if (OutputPort != null)
            {
                OutputPort.portName = "";
                OutputPort.style.flexDirection = FlexDirection.ColumnReverse;
                //center the port
                OutputPort.style.alignItems = Align.Center;
                outputContainer.Add(OutputPort);
            }
        }
        private void SetUpClasses()
        {
            if (_node is CompositeNode)
            {
                AddToClassList("composite");
                return;
            }
            if (_node is BTRootNode)
            {
                AddToClassList("root");
                return;
            }
            if (_node is ActionNode)
            {
                AddToClassList("action");
                return;
            }
            if (_node is ConditionNode)
            {
                AddToClassList("condition");
                return;
            }
            if (_node is DecoratorNode)
            {
                AddToClassList("decorator");
                return;
            }
        }

    }
}