using CycloneGames.BehaviorTree.Runtime.Data;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    public class BTStateMachineComponent : BTRunnerComponent
    {
        [SerializeField] private string _initialState;
        [SerializeField] private BTFSMState[] _states;

        protected override void Awake()
        {
            BTFSMState initial = FindState(_initialState);
            if (initial != null && initial.GetTree() != null)
            {
                behaviorTree = initial.GetTree();
            }

            base.Awake();
        }

        public void SetState(string id)
        {
            BTFSMState targetState = FindState(id);

            if (targetState != null && targetState.GetTree() != null)
            {
                SetTree(targetState.GetTree());
            }
        }

        private BTFSMState FindState(string id)
        {
            if (string.IsNullOrEmpty(id) || _states == null)
            {
                return null;
            }

            for (int i = 0; i < _states.Length; i++)
            {
                BTFSMState state = _states[i];
                if (state != null && string.Equals(state.ID, id, System.StringComparison.Ordinal))
                {
                    return state;
                }
            }

            return null;
        }
    }
}
