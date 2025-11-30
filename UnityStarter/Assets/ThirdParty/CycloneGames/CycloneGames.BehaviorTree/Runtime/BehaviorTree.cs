using System;
using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;

#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime
{
    [CreateAssetMenu(fileName = "BehaviorTree", menuName = "CycloneGames/AI/BehaviorTree")]
    public class BehaviorTree : ScriptableObject, IBehaviorTree
    {
        #region Static Parts
        private static event Action<BTNode> NodeModifier;

        public static void AddNodeModifier(Action<BTNode> modifier)
        {
            if (modifier == null)
                return;
            NodeModifier += modifier;
        }

        public static void RemoveNodeModifier(Action<BTNode> modifier)
        {
            if (modifier == null)
                return;
            NodeModifier -= modifier;
        }
        #endregion

        public bool IsCloned => _isCloned;
        public GameObject Owner { get; private set; } = null;
        public BTNode Root;
        public BTState TreeState { get; private set; } = BTState.RUNNING;
        public List<BTNode> Nodes = new List<BTNode>();

        private BlackBoard _lastBlackBoard = new BlackBoard();
        private bool _isCloned = false;

        // Cached collections to eliminate per-call allocations
        private readonly List<BTNode> _childrenCache = new List<BTNode>(8);
        private readonly Stack<BTNode> _traverseStack = new Stack<BTNode>(16);

        public BTState BTUpdate(BlackBoard blackBoard)
        {
            if (Root == null)
            {
                TreeState = BTState.FAILURE;
                return TreeState;
            }

            _lastBlackBoard = blackBoard;
            if (Root.State == BTState.RUNNING || Root.State == BTState.NOT_ENTERED)
            {
                TreeState = Root.Run(_lastBlackBoard);
            }
            return TreeState;
        }

        public void Inject(object container)
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                Nodes[i]?.Inject(container);
            }
        }

        /// <summary>
        /// Get all children of a node. Uses cached list - do not store reference.
        /// </summary>
        public List<BTNode> GetChildren(BTNode parent)
        {
            _childrenCache.Clear();

            BTRootNode root = parent as BTRootNode;
            if (root != null && root.Child != null)
            {
                _childrenCache.Add(root.Child);
                return _childrenCache;
            }

            DecoratorNode decorator = parent as DecoratorNode;
            if (decorator != null && decorator.Child != null)
            {
                _childrenCache.Add(decorator.Child);
                return _childrenCache;
            }

            CompositeNode composite = parent as CompositeNode;
            if (composite != null)
            {
                _childrenCache.AddRange(composite.Children);
            }

            return _childrenCache;
        }

        public IBehaviorTree Clone(GameObject owner)
        {
            return CloneTree(owner);
        }

        public BehaviorTree CloneTree(GameObject owner)
        {
            if (Root == null)
            {
                Debug.LogError("[BehaviorTree] Cannot clone tree: Root is null.");
                return null;
            }

            var tree = Instantiate(this);
            tree.Owner = owner;
            tree.Root = Root.Clone();
            if (tree.Root == null)
            {
                Debug.LogError("[BehaviorTree] Failed to clone root node.");
                return null;
            }

            tree.Nodes.Clear();
            tree._isCloned = true;

            Traverse(tree.Root, tree);

            return tree;
        }

        public void OnValidate()
        {
            // Manual loop instead of RemoveAll to avoid Lambda allocation
            for (int i = Nodes.Count - 1; i >= 0; i--)
            {
                if (Nodes[i] == null)
                {
                    Nodes.RemoveAt(i);
                }
            }

            for (int i = 0; i < Nodes.Count; i++)
            {
                Nodes[i].OnValidate();
            }
        }

        private void Traverse(BTNode node, BehaviorTree tree)
        {
            if (node == null) return;

            _traverseStack.Clear();
            _traverseStack.Push(node);

            while (_traverseStack.Count > 0)
            {
                var current = _traverseStack.Pop();
                if (current == null) continue;

                current.Tree = tree;
                tree.Nodes.Add(current);
                NodeModifier?.Invoke(current);

                var children = GetChildren(current);
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    var child = children[i];
                    if (child != null)
                    {
                        _traverseStack.Push(child);
                    }
                }
            }
        }

        public void OnAwake()
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                Nodes[i]?.OnAwake();
            }
        }

        public void Stop()
        {
            if (Root != null)
            {
                Root.BTStop(_lastBlackBoard);
            }
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i] != null)
                {
                    Nodes[i].State = BTState.NOT_ENTERED;
                }
            }
        }

        public void OnDrawGizmos()
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                Nodes[i]?.OnDrawGizmos();
            }
        }

        #region Editor Methods
#if UNITY_EDITOR
        public void SetEditorOwner(GameObject owner)
        {
            Owner = owner;
        }

        public BTNode CreateNode(System.Type type)
        {
            var node = CreateInstance(type) as BTNode;
            if (node == null) return null;
            node.Tree = this;
            node.name = type.Name;
            node.GUID = System.Guid.NewGuid().ToString();
            Undo.RecordObject(this, "Behavior Tree(Create Node)");
            Nodes.Add(node);
            if (!Application.isPlaying)
            {
                AssetDatabase.AddObjectToAsset(node, this);
                Undo.RegisterCreatedObjectUndo(node, "Behavior Tree(Create Node)");
            }

            return node;
        }

        public void AddNode(BTNode node)
        {
            Undo.RecordObject(this, "Behavior Tree(Copy Node)");
            Nodes.Add(node);
            if (!Application.isPlaying)
            {
                AssetDatabase.AddObjectToAsset(node, this);
                Undo.RegisterCreatedObjectUndo(node, "Behavior Tree(Copy Node)");
            }
        }

        public void DeleteNode(BTNode node)
        {
            Undo.RecordObject(this, "Behavior Tree(Delete Node)");
            Nodes.Remove(node);
            for (int i = 0; i < Nodes.Count; i++)
            {
                Nodes[i].OnValidate();
            }
            Undo.DestroyObjectImmediate(node);
        }

        public void AddChild(BTNode parent, BTNode child)
        {
            BTRootNode root = parent as BTRootNode;
            if (root != null)
            {
                Undo.RecordObject(root, "Behavior Tree(Add Child)");
                root.Child = child;
                root.OnValidate();
                EditorUtility.SetDirty(root);
            }
            DecoratorNode decorator = parent as DecoratorNode;
            if (decorator != null)
            {
                Undo.RecordObject(decorator, "Behavior Tree(Add Child)");
                decorator.Child = child;
                decorator.OnValidate();
                EditorUtility.SetDirty(decorator);
            }
            CompositeNode composite = parent as CompositeNode;
            if (composite != null)
            {
                Undo.RecordObject(composite, "Behavior Tree(Add Child)");
                composite.Children.Add(child);
                composite.OnValidate();
                EditorUtility.SetDirty(composite);
            }
            parent.OnValidate();
        }

        public void RemoveChild(BTNode parent, BTNode child)
        {
            BTRootNode root = parent as BTRootNode;
            if (root != null)
            {
                Undo.RecordObject(root, "Behavior Tree(Remove Child)");
                root.Child = null;
                root.OnValidate();
                EditorUtility.SetDirty(root);
            }
            DecoratorNode decorator = parent as DecoratorNode;
            if (decorator != null)
            {
                Undo.RecordObject(decorator, "Behavior Tree(Remove Child)");
                decorator.Child = null;
                decorator.OnValidate();
                EditorUtility.SetDirty(decorator);
            }
            CompositeNode composite = parent as CompositeNode;
            if (composite != null)
            {
                Undo.RecordObject(composite, "Behavior Tree(Remove Child)");
                composite.Children.Remove(child);
                composite.OnValidate();
                EditorUtility.SetDirty(composite);
            }
            parent.OnValidate();
        }

        public void NotifyTreeChanged()
        {
            Undo.RecordObject(this, "Tree Changed");
            OnValidate();
            EditorUtility.SetDirty(this);
        }
#endif
        #endregion
    }
}