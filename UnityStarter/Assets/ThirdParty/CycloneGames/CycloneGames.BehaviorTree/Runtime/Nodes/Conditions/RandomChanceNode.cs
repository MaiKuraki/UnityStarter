using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Conditions
{
    [BTInfo("Random", "Returns Success with the configured chance ratio. A non-zero seed uses a deterministic local generator.")]
    public class RandomChanceNode : ConditionNode
    {
        [SerializeField] private float _chance = 1f;
        [SerializeField] private float _outOf = 1f;
        [SerializeField] private int _seed;

        public override BTNode Clone()
        {
            var clone = (RandomChanceNode)base.Clone();
            clone._chance = _chance;
            clone._outOf = _outOf;
            clone._seed = _seed;
            return clone;
        }

    }
}
