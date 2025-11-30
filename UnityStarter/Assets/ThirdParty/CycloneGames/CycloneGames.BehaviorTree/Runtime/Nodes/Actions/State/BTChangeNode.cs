using CycloneGames.BehaviorTree.Runtime.Components;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Actions.State
{
    public class BTChangeNode : ActionNode
    {
        [SerializeField] private string _stateId;
        private BTStateMachineComponent _stateMachine;
        public override void OnAwake()
        {
            _stateMachine = Tree.Owner.GetComponent<BTStateMachineComponent>();
        }
        protected override void OnStart(BlackBoard blackBoard)
        {
            if (_stateMachine == null) return;
            if (string.IsNullOrEmpty(_stateId)) return;
            _stateMachine.SetState(_stateId);
        }
    }
}