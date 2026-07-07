using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Core;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    public abstract class CompositeNode : BTNode
    {
        [HideInInspector] public List<BTNode> Children = new List<BTNode>();
        public ConditionalAbortType AbortType = ConditionalAbortType.NONE;

        private static readonly NodePositionComparer _positionComparer = new NodePositionComparer();

        public override BTNode Clone()
        {
            var clone = (CompositeNode)base.Clone();

            if (Application.isPlaying)
            {
                clone.Children = new List<BTNode>(Children.Count);
                for (int i = 0; i < Children.Count; i++)
                {
                    clone.Children.Add(Children[i].Clone());
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
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                if (Children[i] == null)
                {
                    Children.RemoveAt(i);
                }
            }

            Children.Sort(_positionComparer);
        }

        protected void AddRuntimeChildren(RuntimeCompositeNode runtimeNode)
        {
            runtimeNode.AbortType = (RuntimeAbortType)(int)AbortType;
            for (int i = 0; i < Children.Count; i++)
            {
                BTNode child = Children[i];
                runtimeNode.AddChild(CreateRequiredRuntimeNode(child, $"child[{i}]"));
            }
        }

        private class NodePositionComparer : IComparer<BTNode>
        {
            public int Compare(BTNode a, BTNode b)
            {
                if (a == null || b == null) return 0;
                return a.Position.x.CompareTo(b.Position.x);
            }
        }
    }
}
