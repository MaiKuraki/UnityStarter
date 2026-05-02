using CycloneGames.BehaviorTree.Runtime.Components;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions.State
{
    public class RuntimeBTChangeNode : RuntimeNode
    {
        public string StateId { get; set; }

        private BTStateMachineComponent _cachedStateMachine;
        private bool _didCacheAttempt;

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (string.IsNullOrEmpty(StateId))
            {
                return RuntimeState.Failure;
            }

            var stateMachine = GetStateMachine(blackboard);
            if (stateMachine == null)
            {
                return RuntimeState.Failure;
            }

            stateMachine.SetState(StateId);
            return RuntimeState.Success;
        }

        private BTStateMachineComponent GetStateMachine(RuntimeBlackboard blackboard)
        {
            if (_cachedStateMachine != null)
                return _cachedStateMachine;

            if (_didCacheAttempt)
                return null;

            _cachedStateMachine = blackboard.GetService<BTStateMachineComponent>();
            if (_cachedStateMachine != null)
            {
                _didCacheAttempt = true;
                return _cachedStateMachine;
            }

            var ownerGameObject = blackboard.GetContextOwner<GameObject>();
            if (ownerGameObject != null)
            {
                _cachedStateMachine = ownerGameObject.GetComponent<BTStateMachineComponent>();
            }

            _didCacheAttempt = true;
            return _cachedStateMachine;
        }
    }
}