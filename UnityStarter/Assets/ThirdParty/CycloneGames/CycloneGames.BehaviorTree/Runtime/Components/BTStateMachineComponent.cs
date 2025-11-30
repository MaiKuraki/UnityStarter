using CycloneGames.BehaviorTree.Runtime.Data;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    [RequireComponent(typeof(BTRunnerComponent))]
    public class BTStateMachineComponent : BTRunnerComponent
    {
        [SerializeField] private string _initialState;
        [SerializeField] private BTFSMState[] _states;

        private void Start()
        {
            SetState(_initialState);
        }

        public void SetState(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            BTFSMState targetState = null;
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i].ID == id)
                {
                    targetState = _states[i];
                    break;
                }
            }

            if (targetState != null)
            {
                SetTree(targetState.GetTree(gameObject));
            }
        }
    }
}