using CycloneGames.BehaviorTree.Runtime.Data;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes
{
    public class BTRootNode : BTNode
    {
        [HideInInspector] public BTNode Child;

        protected override BTState OnRun(BlackBoard blackBoard)
        {
            if (Child == null) return BTState.FAILURE;
            return Child.Run(blackBoard);
        }

        protected override void OnStop(BlackBoard blackBoard)
        {
            Child?.BTStop(blackBoard);
        }

        public override BTState Evaluate(BlackBoard blackBoard)
        {
            if (Child == null) return BTState.FAILURE;
            return Child.Evaluate(blackBoard);
        }

        public override BTNode Clone()
        {
            var clone = (BTRootNode)base.Clone();
            if (Child != null)
            {
                clone.Child = Child.Clone();
            }
            return clone;
        }
    }
}