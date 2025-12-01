using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using System.Linq;
using System.Reflection;
using CycloneGames.BehaviorTree.Runtime;
using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Conditions;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Editor
{
    public class BehaviorTreeView : GraphView
    {
        //sort node position by top to bottom
        private const float NODE_X_GAP = 100;
        private const float NODE_Y_GAP = 145;
        public new class UxmlFactory : UxmlFactory<BehaviorTreeView, GraphView.UxmlTraits> { }
        public Runtime.BehaviorTree Tree => _tree;
        public Action<BTNodeView> OnNodeSelectionChanged;
        private Runtime.BehaviorTree _tree;
        private List<BTNode> _copiedNodes = new List<BTNode>();
        private Vector2 _copiedTreePosition;
        public BehaviorTreeView()
        {
            Insert(0, new GridBackground());
            var contentZoomer = new ContentZoomer();
            contentZoomer.maxScale = 2.5f;
            contentZoomer.minScale = 0.1f;
            this.AddManipulator(contentZoomer);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());


            var styleSheet = Resources.Load<StyleSheet>("BT_Editor_Style");
            styleSheets.Add(styleSheet);

            Undo.undoRedoPerformed += OnUndoRedo;
        }
        public void ClearView()
        {
            graphViewChanged -= OnGraphViewChanged;
            DeleteElements(graphElements);
            _tree = null;
        }
        public void PopulateView(Runtime.BehaviorTree tree)
        {
            this._tree = tree;
            DrawGraph();
        }

        public void UpdateNodeStates()
        {
            nodes.ForEach(n =>
            {
                if (n is BTNodeView nodeView)
                {
                    nodeView.UpdateState();
                }
            });

        }
        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            return ports.ToList().Where(endPort => endPort.direction != startPort.direction && endPort.node != startPort.node).ToList();
        }

        public override EventPropagation DeleteSelection()
        {
            var nodes = selection.OfType<BTNodeView>().ToList();
            //check is contains root node
            if (nodes.Any(n => n.Node is BTRootNode))
            {
                EditorUtility.DisplayDialog("Error", "Cannot delete root node", "OK");
                return EventPropagation.Stop;
            }
            //set dirty
            EditorUtility.SetDirty(_tree);
            return base.DeleteSelection();
        }
        private void OnUndoRedo()
        {
            DrawGraph();
            _tree.OnValidate();
            EditorUtility.SetDirty(_tree);
        }
        private void DrawGraph()
        {
            graphViewChanged -= OnGraphViewChanged;
            DeleteElements(graphElements);
            graphViewChanged += OnGraphViewChanged;
            if (_tree.Root == null)
            {
                _tree.Root = _tree.CreateNode(typeof(BTRootNode));
                EditorUtility.SetDirty(_tree);
                AssetDatabase.SaveAssets();
            }

            _tree.Nodes.ForEach(CreateNodeView);
            _tree.Nodes.ForEach(n =>
            {
                var children = _tree.GetChildren(n);
                children.ForEach(c =>
                {
                    var parentView = FindNodeView(n);
                    var childView = FindNodeView(c);
                    var edge = parentView.OutputPort.ConnectTo(childView.InputPort);
                    AddElement(edge);
                });
            });
        }
        private GraphViewChange OnGraphViewChanged(GraphViewChange graphviewChange)
        {
            if (graphviewChange.elementsToRemove != null)
            {
                graphviewChange.elementsToRemove.ForEach(elem =>
                {
                    if (elem is BTNodeView nodeView)
                    {
                        _tree.DeleteNode(nodeView.Node);
                    }
                    Edge edge = elem as Edge;
                    if (edge != null)
                    {
                        if (edge.input.node is BTNodeView inputNodeView && edge.output.node is BTNodeView outputNodeView)
                        {
                            _tree.RemoveChild(outputNodeView.Node, inputNodeView.Node);
                        }
                    }
                });
            }
            if (graphviewChange.edgesToCreate != null)
            {
                graphviewChange.edgesToCreate.ForEach(edge =>
                {
                    if (edge.input.node is BTNodeView inputNodeView && edge.output.node is BTNodeView outputNodeView)
                    {
                        _tree.AddChild(outputNodeView.Node, inputNodeView.Node);
                    }
                });
            }
            return graphviewChange;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            var mousePosition = viewTransform.matrix.inverse.MultiplyPoint(evt.localMousePosition);

            var types = TypeCache.GetTypesDerivedFrom<ActionNode>().OrderBy(t => t.Name);
            foreach (var type in types)
            {
                if (type.IsAbstract)
                    continue;
                var category = GetNodeCategory(type);
                evt.menu.AppendAction($"ActionNode/{category}/{type.Name}", a => CreateNode(type, mousePosition));
            }
            types = TypeCache.GetTypesDerivedFrom<ConditionNode>().OrderBy(t => t.Name);
            foreach (var type in types)
            {
                if (type.IsAbstract) continue;
                var category = GetNodeCategory(type);
                evt.menu.AppendAction($"ConditionNode/{category}/{type.Name}", a => CreateNode(type, mousePosition));
            }
            types = TypeCache.GetTypesDerivedFrom<CompositeNode>().OrderBy(t => t.Name);
            foreach (var type in types)
            {
                if (type.IsAbstract) continue;
                evt.menu.AppendAction($"CompositeNode/{type.Name}", a => CreateNode(type, mousePosition));
            }
            types = TypeCache.GetTypesDerivedFrom<DecoratorNode>().OrderBy(t => t.Name);
            foreach (var type in types)
            {
                if (type.IsAbstract) continue;
                var category = GetNodeCategory(type);
                evt.menu.AppendAction($"DecoratorNode/{category}/{type.Name}", a => CreateNode(type, mousePosition));
            }
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Sort Nodes", a => SortNodes());
            evt.menu.AppendAction("Return to Root", a => ReturnToRoot());
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Find Tree", a => EditorGUIUtility.PingObject(_tree));

            //copy all selected nodes
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Copy", a => CopySelectedNodes(mousePosition));
            if (_copiedNodes.Count > 0)
            {
                evt.menu.AppendAction("Paste", a => PasteCopiedNodes(mousePosition));
            }
        }
        /// <summary>
        /// Copy selected nodes to the tree
        /// </summary>
        /// <param name="mousePosition"> The position of the mouse. </param>
        private void CopySelectedNodes(Vector2 mousePosition)
        {
            _copiedNodes.Clear();
            _copiedTreePosition = mousePosition;
            var selectedNodes = selection.OfType<BTNodeView>().Select(n => n.Node).ToList();
            foreach (var node in selectedNodes)
            {
                //check is root node
                if (node is BTRootNode)
                {
                    EditorUtility.DisplayDialog("Error", "Cannot copy root node", "OK");
                    return;
                }
                _copiedNodes.Add(node);
            }
        }
        /// <summary>
        /// Paste copied nodes to the tree
        /// </summary>
        /// <param name="mousePosition"> The position of the mouse. </param>
        private void PasteCopiedNodes(Vector2 mousePosition)
        {
            Vector2 offset = mousePosition - _copiedTreePosition;
            List<BTNode> newNodes = new List<BTNode>();

            //create all node for copy
            foreach (var copiedNode in _copiedNodes)
            {
                var newNode = copiedNode.Clone();
                newNode.Tree = _tree;
                newNode.Position = copiedNode.Position + offset;
                newNode.GUID = copiedNode.GUID;
                newNodes.Add(newNode);
                _tree.AddNode(newNode);
            }
            List<CompositeNode> compositeNodes = new List<CompositeNode>();
            List<DecoratorNode> decoratorNodes = new List<DecoratorNode>();

            foreach (var newNode in newNodes)
            {
                if (newNode is CompositeNode compositeNode)
                {
                    compositeNodes.Add(compositeNode);
                }
                if (newNode is DecoratorNode decoratorNode)
                {
                    decoratorNodes.Add(decoratorNode);
                }
            }

            foreach (var compositeNode in compositeNodes)
            {
                //find same composite node from copied nodes
                var copiedCompositeNode = _copiedNodes.Find(n => n.GUID == compositeNode.GUID) as CompositeNode;
                if (copiedCompositeNode == null) continue;
                //swap children by new nodes using guid
                for (int i = 0; i < copiedCompositeNode.Children.Count; i++)
                {
                    var result = newNodes.Find(n => n.GUID == copiedCompositeNode.Children[i].GUID);
                    if (result != null)
                    {
                        compositeNode.Children.Add(result);
                    }
                }
            }

            foreach (var decoratorNode in decoratorNodes)
            {
                //find same decorator node from copied nodes
                var copiedDecoratorNode = _copiedNodes.Find(n => n.GUID == decoratorNode.GUID) as DecoratorNode;
                if (copiedDecoratorNode == null) continue;
                //swap child by new nodes using guid
                var result = newNodes.Find(n => n.GUID == copiedDecoratorNode.Child.GUID);
                if (result != null)
                {
                    decoratorNode.Child = result;
                }
            }

            //replace all guid in new nodes
            foreach (var newNode in newNodes)
            {
                newNode.GUID = System.Guid.NewGuid().ToString();
            }

            DrawGraph();
        }
        public void ReturnToRoot()
        {
            var root = GetNodeByGuid(_tree.Root.GUID) as BTNodeView;
            if (root != null)
            {
                viewTransform.scale = Vector3.one;
                viewTransform.position = -root.GetPosition().position;
            }
        }
        private BTNodeView FindNodeView(BTNode node)
        {
            return GetNodeByGuid(node.GUID) as BTNodeView;
        }

        #region Sort Nodes
        /// <summary>
        /// Sort all nodes in the tree
        /// </summary>
        public void SortNodes()
        {
            if (_tree.Root == null) return;
            SortNodes(Vector3.zero, _tree.Root);
            //redraw
            _tree.Nodes.ForEach(n =>
            {
                var nodeView = FindNodeView(n);
                if (nodeView != null)
                {
                    nodeView.SetPosition(new Rect(n.Position, Vector2.zero));
                }
            });
        }
        /// <summary>
        /// sort nodes by top to bottom
        /// </summary>
        /// <param name="position"> current position</param>
        /// <param name="node"> current node</param>
        /// <returns> (Next X, Next Y, Total Width) </returns>
        private Vector3 SortNodes(Vector3 position, BTNode node)
        {
            // Get the children of the current node
            List<BTNode> children = _tree.GetChildren(node);
            // Create a copy of the children list to avoid "Collection was modified" exception
            // when the collection is modified during enumeration
            List<BTNode> childrenCopy = new List<BTNode>(children);
            //Debug.Log("Position : " + position + " : " + node.GetType().Name + " : " + childrenCopy.Count);
            // If the node has no children, set its position to the input position and return
            if (childrenCopy.Count == 0)
            {
                node.Position = position;
                Vector3 result = new Vector3(position.x, position.y, NODE_X_GAP);
                return result;
            }

            // If the node has one child, recursively sort the child and set the node's position
            if (childrenCopy.Count == 1)
            {
                Vector3 result = SortNodes(new Vector3(position.x, position.y + NODE_Y_GAP, position.z + NODE_X_GAP), childrenCopy[0]);
                node.Position = new Vector2(position.x + (result.z - NODE_X_GAP) / 2, position.y);
                return result;
            }

            // If the node has multiple children, recursively sort each child and calculate the node's position
            float nextX = position.x;
            float nextY = position.y + NODE_Y_GAP;
            float totalWidth = 0;

            foreach (var child in childrenCopy)
            {
                Vector3 childPosition = SortNodes(new Vector3(nextX, nextY, totalWidth), child);
                nextX = childPosition.x + NODE_X_GAP;
                totalWidth += childPosition.z;
            }

            // Calculate the average position and set the node's position
            Vector3 currentPosition = new Vector3(position.x + (totalWidth - NODE_X_GAP) / 2, position.y, totalWidth);
            node.Position = currentPosition;

            Vector3 nextPosition = new Vector3(position.x + totalWidth - NODE_X_GAP, currentPosition.y, totalWidth);
            return nextPosition;
        }
        #endregion
        #region Create Node Methods
        private void CreateNode(Type type, Vector2 position)
        {
            var node = _tree.CreateNode(type);
            //set dirty
            EditorUtility.SetDirty(_tree);
            CreateNodeView(node, position);
        }
        private void CreateNodeView(BTNode node, Vector2 position)
        {
            BTNodeView nodeView = new BTNodeView(node, position);
            nodeView.OnNodeSelected += OnNodeSelectionChanged;
            AddElement(nodeView);
        }
        private void CreateNodeView(BTNode node)
        {
            BTNodeView nodeView = new BTNodeView(node);
            nodeView.OnNodeSelected += OnNodeSelectionChanged;
            AddElement(nodeView);
        }
        #endregion

        #region Attribute Methods
        private T GetAttribute<T>(Type type) where T : Attribute
        {
            var attributes = type.GetCustomAttributes<T>().FirstOrDefault();
            if (attributes == null)
            {
                return null;
            }
            return attributes;
        }

        private string GetNodeCategory(Type type)
        {
            var attribute = GetAttribute<BTInfoAttribute>(type);
            return attribute != null ? attribute.Category : "Base";
        }
        #endregion
    }
}