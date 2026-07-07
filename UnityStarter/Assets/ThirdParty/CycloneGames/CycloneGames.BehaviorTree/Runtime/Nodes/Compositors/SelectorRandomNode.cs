using CycloneGames.BehaviorTree.Runtime.Attributes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("Selector", "Randomizes child order on activation, then runs selector fallback semantics over the shuffled order.")]
    public class SelectorRandomNode : CompositeNode
    {
        [SerializeField] private int _seed;

        public override BTNode Clone()
        {
            var clone = (SelectorRandomNode)base.Clone();
            clone._seed = _seed;
            return clone;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeSelectorRandom((uint)_seed);
            node.GUID = GUID;
            AddRuntimeChildren(node);
            return node;
        }
    }
}
