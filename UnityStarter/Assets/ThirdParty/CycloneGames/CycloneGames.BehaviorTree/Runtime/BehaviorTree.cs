using System;
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

        /// <summary>
        /// Unity validation callback. Structural validation is exposed by BehaviorTreeCompiler;
        /// serialized repair is an explicit Undo-aware Editor operation.
        /// </summary>
        public void OnValidate()
        {
#if UNITY_EDITOR
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }
#endif

        }

        /// <summary>
        /// Calls OnDrawGizmos on all nodes for scene view visualization.
        /// </summary>
        public void OnDrawGizmos()
        {
            if (Nodes == null)
            {
                return;
            }

            for (int i = 0; i < Nodes.Count; i++)
            {
                Nodes[i]?.OnDrawGizmos();
            }
        }

        /// <summary>
        /// Compiles the ScriptableObject-based behavior tree into a pure C# runtime instance.
        /// Compilation allocates the runtime graph. Measure steady-state execution separately for the selected tree and target platform.
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
            EnsureEditorMutationAllowed();
            if (type == null || type.IsAbstract || !typeof(BTNode).IsAssignableFrom(type))
            {
                throw new ArgumentException("The node type must be a concrete BTNode type.", nameof(type));
            }

            const string undoName = "Behavior Tree(Create Node)";
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);

            BTNode node = null;
            try
            {
                Undo.RegisterCompleteObjectUndo(this, undoName);
                node = CreateInstance(type) as BTNode;
                if (node == null)
                {
                    throw new InvalidOperationException($"Unity could not create behavior tree node type '{type.FullName}'.");
                }

                node.Tree = this;
                node.name = type.Name;
                node.GUID = System.Guid.NewGuid().ToString();
                AttachTransientNodeToTreeAsset(node);
                Nodes ??= new List<BTNode>();
                Nodes.Add(node);
                Undo.RegisterCreatedObjectUndo(node, undoName);
                EditorUtility.SetDirty(node);
                EditorUtility.SetDirty(this);
                Undo.CollapseUndoOperations(undoGroup);
                return node;
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                CleanupFailedCreatedNode(node);
                throw;
            }
        }

        public void AddNode(BTNode node)
        {
            EnsureEditorMutationAllowed();
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            const string undoName = "Behavior Tree(Add Node)";
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);

            bool wasTransient = string.IsNullOrEmpty(AssetDatabase.GetAssetPath(node));
            bool attachedDuringTransaction = false;
            try
            {
                Undo.RegisterCompleteObjectUndo(this, undoName);
                Undo.RegisterCompleteObjectUndo(node, undoName);

                attachedDuringTransaction = AttachTransientNodeToTreeAsset(node);
                node.Tree = this;
                Nodes ??= new List<BTNode>();
                Nodes.Add(node);
                if (wasTransient)
                {
                    Undo.RegisterCreatedObjectUndo(node, undoName);
                }

                EditorUtility.SetDirty(node);
                EditorUtility.SetDirty(this);
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                CleanupFailedAddedNode(node, wasTransient, attachedDuringTransaction);
                throw;
            }
        }

        public void DeleteNode(BTNode node)
        {
            EnsureEditorMutationAllowed();
            if (node == null)
            {
                return;
            }

            const string undoName = "Behavior Tree(Delete Node)";
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);

            var affectedParents = new List<BTNode>();
            var undoTargets = new List<UnityEngine.Object> { this };
            if (Nodes != null)
            {
                for (int i = 0; i < Nodes.Count; i++)
                {
                    BTNode candidate = Nodes[i];
                    if (candidate == null || candidate == node || !ReferencesChild(candidate, node))
                    {
                        continue;
                    }

                    affectedParents.Add(candidate);
                    undoTargets.Add(candidate);
                }
            }

            Undo.RegisterCompleteObjectUndo(undoTargets.ToArray(), undoName);

            if (Root == node)
            {
                Root = null;
            }

            for (int i = 0; i < affectedParents.Count; i++)
            {
                BTNode parent = affectedParents[i];
                if (parent is BTRootNode root)
                {
                    root.Child = null;
                }
                else if (parent is DecoratorNode decorator)
                {
                    decorator.Child = null;
                }
                else if (parent is CompositeNode composite)
                {
                    composite.RemoveChildForAuthoring(node);
                }

                EditorUtility.SetDirty(parent);
            }

            if (Nodes != null)
            {
                for (int i = Nodes.Count - 1; i >= 0; i--)
                {
                    if (Nodes[i] == node)
                    {
                        Nodes.RemoveAt(i);
                    }
                }
            }

            EditorUtility.SetDirty(this);
            Undo.DestroyObjectImmediate(node);
            Undo.CollapseUndoOperations(undoGroup);
        }

        public void AddChild(BTNode parent, BTNode child)
        {
            EnsureEditorMutationAllowed();
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
                composite.AddChildForAuthoring(child);
                EditorUtility.SetDirty(composite);
            }
            parent.OnValidate();
        }

        public void RemoveChild(BTNode parent, BTNode child)
        {
            EnsureEditorMutationAllowed();
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
                composite.RemoveChildForAuthoring(child);
                EditorUtility.SetDirty(composite);
            }
            parent.OnValidate();
        }

        public void NormalizeCompositeChildren(CompositeNode composite)
        {
            EnsureEditorMutationAllowed();
            if (composite == null)
            {
                return;
            }

            Undo.RecordObject(composite, "Behavior Tree(Normalize Children)");
            composite.NormalizeChildrenForAuthoring();
            EditorUtility.SetDirty(composite);
        }

        public void NotifyTreeChanged()
        {
            EnsureEditorMutationAllowed();
            Undo.RecordObject(this, "Tree Changed");
            OnValidate();
            EditorUtility.SetDirty(this);
        }

        private static void EnsureEditorMutationAllowed()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Behavior tree authoring assets are read-only while the Editor is entering, running, or exiting Play Mode.");
            }
        }

        private bool AttachTransientNodeToTreeAsset(BTNode node)
        {
            string treePath = AssetDatabase.GetAssetPath(this);
            string nodePath = AssetDatabase.GetAssetPath(node);
            if (string.IsNullOrEmpty(treePath))
            {
                if (!string.IsNullOrEmpty(nodePath))
                {
                    throw new InvalidOperationException(
                        $"Cannot add persistent node '{node.name}' to a transient behavior tree.");
                }

                return false;
            }

            if (!string.IsNullOrEmpty(nodePath))
            {
                if (!string.Equals(nodePath, treePath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Node '{node.name}' belongs to asset '{nodePath}' and cannot be owned by behavior tree '{treePath}'.");
                }

                // AddObjectToAsset assigns the target path immediately, while IsSubAsset can remain
                // false until the asset is synchronously imported by the owning Editor transaction.
                return false;
            }

            if (node.Tree != null && node.Tree != this)
            {
                throw new InvalidOperationException(
                    $"Transient node '{node.name}' is already owned by another behavior tree.");
            }

            AssetDatabase.AddObjectToAsset(node, this);
            return true;
        }

        private static void CleanupFailedCreatedNode(BTNode node)
        {
            if (node == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(node)))
            {
                AssetDatabase.RemoveObjectFromAsset(node);
            }

            if (node != null)
            {
                DestroyImmediate(node);
            }
        }

        private static void CleanupFailedAddedNode(
            BTNode node,
            bool wasTransient,
            bool attachedDuringTransaction)
        {
            if (node != null &&
                (attachedDuringTransaction || wasTransient) &&
                !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(node)))
            {
                AssetDatabase.RemoveObjectFromAsset(node);
            }
        }

        private static bool ReferencesChild(BTNode parent, BTNode child)
        {
            if (parent is BTRootNode root)
            {
                return root.Child == child;
            }

            if (parent is DecoratorNode decorator)
            {
                return decorator.Child == child;
            }

            if (!(parent is CompositeNode composite) || composite.Children == null)
            {
                return false;
            }

            for (int i = 0; i < composite.Children.Count; i++)
            {
                if (composite.Children[i] == child)
                {
                    return true;
                }
            }

            return false;
        }
#endif
        #endregion
    }
}
