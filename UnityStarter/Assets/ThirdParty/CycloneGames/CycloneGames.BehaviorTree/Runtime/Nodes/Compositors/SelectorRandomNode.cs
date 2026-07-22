using CycloneGames.BehaviorTree.Runtime.Attributes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("Selector", "Randomizes child order on activation, then runs selector fallback semantics over the shuffled order.")]
    public class SelectorRandomNode : CompositeNode
    {
        [SerializeField] private int _seed;

        public int Seed => _seed;

        public override BTNode Clone()
        {
            var clone = (SelectorRandomNode)base.Clone();
            clone._seed = _seed;
            return clone;
        }

    }
}
