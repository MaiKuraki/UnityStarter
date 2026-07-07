using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Compilation;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Data;
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
    public class BehaviorTree : ScriptableObject
    {
        public GameObject Owner { get; private set; } = null;
        public BTNode Root;
        public List<BTNode> Nodes = new List<BTNode>();

        private readonly List<BTNode> _childrenCache = new List<BTNode>(8);

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

        /// <summary>
        /// Validates the behavior tree structure and removes null nodes.
        /// </summary>
        public void OnValidate()
        {
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

        /// <summary>
        /// Calls OnDrawGizmos on all nodes for scene view visualization.
        /// </summary>
        public void OnDrawGizmos()
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                Nodes[i]?.OnDrawGizmos();
            }
        }

        /// <summary>
        /// Compiles the ScriptableObject-based behavior tree into a pure C# runtime instance.
        /// This method is 0GC at runtime after the initial compilation.
        /// </summary>
        /// <returns>A new RuntimeBehaviorTree instance</returns>
        public RuntimeBehaviorTree Compile()
        {
            return Compile(new RuntimeBTContext());
        }

        /// <summary>
        /// Compiles using a strongly-typed runtime context (owner + services).
        /// </summary>
        public RuntimeBehaviorTree Compile(RuntimeBTContext context)
        {
            try
            {
                return BehaviorTreeCompiler.Compile(this, context);
            }
            catch (BehaviorTreeCompileException exception)
            {
                Debug.LogError("[BehaviorTree] " + exception.Message, this);
                return null;
            }
        }

        // Convenience overload: compile with a GameObject owner and no service resolver.
        public RuntimeBehaviorTree Compile(UnityEngine.GameObject owner)
        {
            return Compile(new RuntimeBTContext(owner));
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
