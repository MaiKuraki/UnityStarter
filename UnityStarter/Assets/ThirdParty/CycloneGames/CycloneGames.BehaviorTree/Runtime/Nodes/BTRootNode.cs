using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes
{
    public class BTRootNode : BTNode
    {
        [HideInInspector] public BTNode Child;

        public override void Inject(object container)
        {
            base.Inject(container);
            Child?.Inject(container);
        }

        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            if (Child == null) return BTState.FAILURE;
            return Child.Run(blackBoard);
        }

        protected override void OnStop(IBlackBoard blackBoard)
        {
            Child?.BTStop(blackBoard);
        }

        public override BTState Evaluate(IBlackBoard blackBoard)
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