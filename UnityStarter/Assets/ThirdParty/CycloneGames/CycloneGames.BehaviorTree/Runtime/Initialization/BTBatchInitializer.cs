using System;
using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Components;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Initialization
{
    /// <summary>
    /// Batch initializer for spreading AI initialization across multiple frames to prevent stuttering.
    /// Processes initialization tasks incrementally to maintain smooth frame rates.
    /// </summary>
    public class BTBatchInitializer : MonoBehaviour
    {
        private static BTBatchInitializer _instance;
        private readonly Queue<InitializationTask> _pendingTasks = new Queue<InitializationTask>(64);
        private readonly List<InitializationTask> _activeTasks = new List<InitializationTask>(32);

        [SerializeField] private int _maxInitializationsPerFrame = 10;
        [SerializeField] private bool _autoStart = true;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void Update()
        {
            if (!_autoStart) return;
            ProcessInitializations();
        }

        /// <summary>
        /// Adds an initialization task to the queue.
        /// </summary>
        public static void AddTask(BTRunnerComponent runner, Action<BTRunnerComponent> onComplete = null)
        {
            if (runner == null) return;

            EnsureInstance();

            var task = new InitializationTask
            {
                Runner = runner,
                OnComplete = onComplete,
                Status = InitializationStatus.Pending
            };

            _instance._pendingTasks.Enqueue(task);
        }

        /// <summary>
        /// Adds multiple initialization tasks to the queue.
        /// </summary>
        public static void AddTasks(IEnumerable<BTRunnerComponent> runners, Action<BTRunnerComponent> onComplete = null)
        {
            if (runners == null) return;

            EnsureInstance();

            foreach (var runner in runners)
            {
                if (runner != null)
                {
                    AddTask(runner, onComplete);
                }
            }
        }

        /// <summary>
        /// Sets the maximum number of initializations to process per frame.
        /// </summary>
        public static void SetMaxPerFrame(int count)
        {
            EnsureInstance();
            _instance._maxInitializationsPerFrame = Mathf.Max(1, count);
        }

        /// <summary>
        /// Gets the number of pending initialization tasks.
        /// </summary>
        public static int GetPendingCount()
        {
            if (_instance == null) return 0;
            return _instance._pendingTasks.Count;
        }

        /// <summary>
        /// Gets the number of active initialization tasks.
        /// </summary>
        public static int GetActiveCount()
        {
            if (_instance == null) return 0;
            return _instance._activeTasks.Count;
        }

        /// <summary>
        /// Clears all pending initialization tasks.
        /// </summary>
        public static void ClearPending()
        {
            if (_instance != null)
            {
                _instance._pendingTasks.Clear();
            }
        }

        private void ProcessInitializations()
        {
            int addedThisFrame = 0;
            while (_pendingTasks.Count > 0 && _activeTasks.Count < _maxInitializationsPerFrame && addedThisFrame < _maxInitializationsPerFrame)
            {
                var task = _pendingTasks.Dequeue();
                task.Status = InitializationStatus.Initializing;
                _activeTasks.Add(task);
                addedThisFrame++;
            }

            for (int i = _activeTasks.Count - 1; i >= 0; i--)
            {
                var task = _activeTasks[i];

                if (task.Runner == null)
                {
                    _activeTasks.RemoveAt(i);
                    continue;
                }

                if (InitializeRunner(task.Runner))
                {
                    task.Status = InitializationStatus.Completed;
                    task.OnComplete?.Invoke(task.Runner);
                    _activeTasks.RemoveAt(i);
                }
            }
        }

        private bool InitializeRunner(BTRunnerComponent runner)
        {
            if (runner == null) return true;

            if (runner.Tree != null && runner.Tree.IsCloned)
            {
                return true;
            }

            try
            {
                if (runner.Tree != null && !runner.Tree.IsCloned)
                {
                    var treeAsset = runner.Tree;
                    runner.SetTree(treeAsset);
                }

                return runner.Tree != null && runner.Tree.IsCloned;
            }
            catch (Exception e)
            {
                Debug.LogError($"[BTBatchInitializer] Failed to initialize runner on {runner.gameObject.name}: {e.Message}");
                return true;
            }
        }

        private static void EnsureInstance()
        {
            if (_instance == null)
            {
                var go = new GameObject("BTBatchInitializer");
                _instance = go.AddComponent<BTBatchInitializer>();
            }
        }

        private class InitializationTask
        {
            public BTRunnerComponent Runner;
            public Action<BTRunnerComponent> OnComplete;
            public InitializationStatus Status;
        }

        private enum InitializationStatus
        {
            Pending,
            Initializing,
            Completed,
            Failed
        }
    }
}