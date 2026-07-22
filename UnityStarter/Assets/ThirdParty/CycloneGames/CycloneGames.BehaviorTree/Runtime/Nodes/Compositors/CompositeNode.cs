using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Core;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    public abstract class CompositeNode : BTNode
    {
        [HideInInspector] public List<BTNode> Children = new List<BTNode>();
        public ConditionalAbortType AbortType = ConditionalAbortType.NONE;

#if UNITY_EDITOR
        private static readonly NodePositionComparer _positionComparer = new NodePositionComparer();
#endif

        public override BTNode Clone()
        {
            var clone = (CompositeNode)base.Clone();

            if (Application.isPlaying)
            {
                int childCount = Children != null ? Children.Count : 0;
                clone.Children = new List<BTNode>(childCount);
                for (int i = 0; i < childCount; i++)
                {
                    if (Children[i] != null)
                    {
                        clone.Children.Add(Children[i].Clone());
                    }
                }
            }
            else
            {
                clone.Children = new List<BTNode>();
            }

            return clone;
        }

        protected override void CheckIntegrity()
        {
            // Unity invokes OnValidate outside an explicit authoring transaction.
            // Structural repair and ordering are therefore performed only through
            // the Undo-aware authoring methods below.
        }

#if UNITY_EDITOR
        internal void NormalizeChildrenForAuthoring()
        {
            CaptureChildMetadata();

            if (Children == null)
            {
                Children = new List<BTNode>();
                RestoreChildMetadata();
                return;
            }

            for (int i = Children.Count - 1; i >= 0; i--)
            {
                if (Children[i] == null)
                {
                    Children.RemoveAt(i);
                }
            }

            Children.Sort(_positionComparer);
            RestoreChildMetadata();
        }

        internal void AddChildForAuthoring(BTNode child)
        {
            CaptureChildMetadata();
            Children ??= new List<BTNode>();
            Children.Add(child);
            NormalizeChildrenAfterMetadataCapture();
        }

        internal bool RemoveChildForAuthoring(BTNode child)
        {
            if (Children == null || child == null)
            {
                return false;
            }

            CaptureChildMetadata();
            bool removed = false;
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                if (Children[i] == child)
                {
                    Children.RemoveAt(i);
                    removed = true;
                }
            }

            if (removed)
            {
                NormalizeChildrenAfterMetadataCapture();
            }

            return removed;
        }

        protected virtual void CaptureChildMetadata()
        {
        }

        protected virtual void RestoreChildMetadata()
        {
        }

        protected static void CaptureMetadataByChild<T>(
            List<BTNode> children,
            List<T> metadata,
            ref Dictionary<BTNode, T> cache)
        {
            cache ??= new Dictionary<BTNode, T>();
            cache.Clear();
            if (children == null || metadata == null)
            {
                return;
            }

            int count = children.Count < metadata.Count ? children.Count : metadata.Count;
            for (int i = 0; i < count; i++)
            {
                BTNode child = children[i];
                if (child != null && !cache.ContainsKey(child))
                {
                    cache.Add(child, metadata[i]);
                }
            }
        }

        protected static void RestoreMetadataByChild<T>(
            List<BTNode> children,
            ref List<T> metadata,
            Dictionary<BTNode, T> cache,
            T defaultValue)
        {
            metadata ??= new List<T>();
            metadata.Clear();
            if (children == null)
            {
                cache?.Clear();
                return;
            }

            if (metadata.Capacity < children.Count)
            {
                metadata.Capacity = children.Count;
            }

            for (int i = 0; i < children.Count; i++)
            {
                BTNode child = children[i];
                metadata.Add(
                    child != null && cache != null && cache.TryGetValue(child, out T value)
                        ? value
                        : defaultValue);
            }

            cache?.Clear();
        }

        private void NormalizeChildrenAfterMetadataCapture()
        {
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                if (Children[i] == null)
                {
                    Children.RemoveAt(i);
                }
            }

            Children.Sort(_positionComparer);
            RestoreChildMetadata();
        }

        private sealed class NodePositionComparer : IComparer<BTNode>
        {
            public int Compare(BTNode a, BTNode b)
            {
                if (ReferenceEquals(a, b)) return 0;
                if (a == null) return 1;
                if (b == null) return -1;

                int xComparison = a.Position.x.CompareTo(b.Position.x);
                return xComparison != 0
                    ? xComparison
                    : string.CompareOrdinal(a.GUID ?? string.Empty, b.GUID ?? string.Empty);
            }
        }
#endif
    }
}
