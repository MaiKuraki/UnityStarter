using System;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    public class BTRunnerComponent : MonoBehaviour
    {
        private const string MESSAGE_KEY = "Message";

        public event Action OnTreeStopped;

        [Serializable]
        private class BlackBoardPassObject
        {
            public string Key;
            public Object Value;
        }

        public BehaviorTree Tree => behaviorTree;
        public BlackBoard BlackBoard => _blackBoard;
        public bool IsPaused => _isPaused;
        public bool IsStopped => _isStopped;

        [SerializeField] protected bool _startOnAwake = true;
        [SerializeField] protected BehaviorTree behaviorTree;
        [SerializeField] private BlackBoardPassObject[] _initialObjects;
        [HideInInspector][SerializeField] BlackBoard _blackBoard = new BlackBoard();

        private bool _isPaused = false;
        private bool _isStopped = false;
        private BehaviorTree _nextTree;

        private void Awake()
        {
            if (behaviorTree == null)
            {
                Debug.LogWarning($"[BTRunnerComponent] Behavior tree is null on {gameObject.name}.");
                return;
            }
            if (!_startOnAwake) return;

            if (_initialObjects != null)
            {
                for (int i = 0; i < _initialObjects.Length; i++)
                {
                    var data = _initialObjects[i];
                    if (data == null) continue;
                    if (string.IsNullOrEmpty(data.Key)) continue;
                    _blackBoard.Set(data.Key, data.Value);
                }
            }
            
            if (behaviorTree.IsCloned)
            {
                behaviorTree.OnAwake();
            }
            else
            {
                behaviorTree = (BehaviorTree)behaviorTree.Clone(gameObject);
                if (behaviorTree != null)
                {
                    behaviorTree.OnAwake();
                }
            }
        }

        private void Update()
        {
            if (behaviorTree == null) return;
            if (!behaviorTree.IsCloned) return;
            if (_isPaused) return;

            var lastState = behaviorTree.BTUpdate(_blackBoard);
            if (lastState == BTState.FAILURE || lastState == BTState.SUCCESS)
            {
                Stop();
            }
        }

        private void LateUpdate()
        {
            if (_nextTree == null) return;
            if (behaviorTree != null)
            {
                behaviorTree.Stop();
            }
            behaviorTree = _nextTree;
            _nextTree = null;
        }

        private void OnDestroy()
        {
            if (behaviorTree != null)
            {
                behaviorTree.Stop();
            }
            if (_nextTree != null)
            {
                _nextTree.Stop();
            }
        }

        public void BTSendMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _blackBoard.Set(MESSAGE_KEY, message);
        }

        public void BTSetData(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;
            _blackBoard.Set(key, value);
        }

        public void BTRemoveData(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _blackBoard.Remove(key);
        }

        public void SetTree(BehaviorTree newTree)
        {
            if (newTree == null)
            {
                Debug.LogWarning($"[BTRunnerComponent] Cannot set null behavior tree on {gameObject.name}.");
                return;
            }
            _nextTree = newTree.IsCloned ? newTree : (BehaviorTree)newTree.Clone(gameObject);
            if (_nextTree != null)
            {
                _nextTree.OnAwake();
            }
        }

        public void Stop()
        {
            if (behaviorTree != null)
            {
                behaviorTree.Stop();
            }
            _isPaused = true;
            _isStopped = true;
            OnTreeStopped?.Invoke();
        }

        public void Play()
        {
            if (behaviorTree == null)
            {
                Debug.LogWarning($"[BTRunnerComponent] Cannot play: behavior tree is null on {gameObject.name}.");
                return;
            }
            if (!_isStopped)
            {
                behaviorTree.Stop();
            }
            _isPaused = false;
            _isStopped = false;
        }

        public void Pause()
        {
            _isPaused = true;
        }

        public void Resume()
        {
            if (behaviorTree == null)
            {
                Debug.LogWarning($"[BTRunnerComponent] Cannot resume: behavior tree is null on {gameObject.name}.");
                return;
            }
            _isPaused = false;
        }

        protected virtual void OnValidate()
        {
            if (_initialObjects == null) return;

            for (int i = 0; i < _initialObjects.Length; i++)
            {
                var objectSet = _initialObjects[i];
                if (objectSet == null) continue;

                if (objectSet.Value == null)
                {
                    if (!string.IsNullOrEmpty(objectSet.Key))
                    {
                        Debug.LogWarning($"[BTRunnerComponent] Object value is null for key: {objectSet.Key}");
                    }
                    continue;
                }

                if (string.IsNullOrEmpty(objectSet.Key))
                {
                    objectSet.Key = objectSet.Value.name;
                }
            }
        }

        private void OnDrawGizmos()
        {
            if (behaviorTree == null) return;
            if (behaviorTree.Owner == null) return;
            behaviorTree.OnDrawGizmos();
        }
    }
}