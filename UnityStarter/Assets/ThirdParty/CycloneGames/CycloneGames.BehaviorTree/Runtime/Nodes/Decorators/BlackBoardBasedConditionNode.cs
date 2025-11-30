using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("BlackBoard", "Checks if a key exists in the blackboard and runs the child node if it does.")]
    public class BlackBoardBasedConditionNode : DecoratorNode
    {
        [SerializeField] private string _key;
        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            if (!blackBoard.Contains(_key))
            {
                //Debug.LogError("Key not found in blackboard with key");
                return BTState.FAILURE;
            }
            object result = blackBoard.Get(_key);
            if (result == null)
            {
                return BTState.FAILURE;
            }
            return Child.Run(blackBoard);
        }
        public override BTNode Clone()
        {
            var clone = (BlackBoardBasedConditionNode)base.Clone();
            clone._key = _key;
            return clone;
        }
    }
}