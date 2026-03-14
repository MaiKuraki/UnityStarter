using CycloneGames.BehaviorTree.Runtime.Components;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions.State
{
    public class RuntimeBTChangeNode : RuntimeNode
    {
        public string StateId { get; set; }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (string.IsNullOrEmpty(StateId))
            {
                return RuntimeState.Failure;
            }

            var stateMachine = blackboard.GetService<BTStateMachineComponent>();
            if (stateMachine != null)
            {
                stateMachine.SetState(StateId);
                return RuntimeState.Success;
            }

            var ownerGameObject = blackboard.GetContextOwner<GameObject>();
            if (ownerGameObject == null)
            {
                return RuntimeState.Failure;
            }

            stateMachine = ownerGameObject.GetComponent<BTStateMachineComponent>();
            if (stateMachine == null)
            {
                return RuntimeState.Failure;
            }

            stateMachine.SetState(StateId);
            return RuntimeState.Success;
        }
    }
}