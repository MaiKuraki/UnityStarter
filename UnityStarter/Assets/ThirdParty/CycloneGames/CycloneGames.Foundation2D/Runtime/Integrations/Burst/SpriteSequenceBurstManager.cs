using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace CycloneGames.Foundation2D.Runtime
{
    [BurstCompile]
    internal struct SpriteSequenceBatchUpdateJob : IJobParallelFor
    {
        internal NativeArray<SpriteSequencePlaybackState> States;

        [ReadOnly]
        internal NativeArray<double> DeltaTimes;

        [ReadOnly]
        internal NativeArray<int> MaxFrameAdvances;

        [WriteOnly]
        internal NativeArray<SpriteSequenceAdvanceResult> Results;

        public void Execute(int index)
        {
            SpriteSequencePlaybackState state = States[index];
            Results[index] = state.Advance(DeltaTimes[index], MaxFrameAdvances[index]);
            States[index] = state;
        }
    }

    [DisallowMultipleComponent]
    [MovedFrom(
        true,
        sourceNamespace: "CycloneGames.Foundation2D.Runtime",
        sourceAssembly: "CycloneGames.Foundation2D.Runtime",
        sourceClassName: "SpriteSequenceBurstManager")]
    public sealed class SpriteSequenceBurstManager : MonoBehaviour
    {
        [Header("Controller Sources")]
        [Tooltip("Explicit controllers owned by this manager. Duplicate entries are ignored.")]
        [SerializeField] private List<SpriteSequenceController> controllers = new();

        [Tooltip("Also collect inactive and active controllers under this transform when ownership is refreshed.")]
        [SerializeField] private bool autoCollectChildren = true;

        [Header("Scheduling")]
        [Tooltip("Inner-loop batch size used when scheduling the parallel playback-state job.")]
        [SerializeField, Min(1)] private int jobBatchSize = 32;

        [Tooltip("Controller counts below this threshold execute inline to avoid job scheduling overhead.")]
        [SerializeField, Min(1)] private int minParallelJobCount = 32;

        [Header("Capacity")]
        [Tooltip("Persistent native buffer capacity reserved on enable. Zero defers allocation until needed.")]
        [SerializeField, Min(0)] private int prewarmCapacity;

        [Tooltip("Hard ownership and native buffer limit. Additional controllers remain unclaimed and use their fallback policy.")]
        [SerializeField, Min(1)] private int maxControllerCapacity = 16384;

        private readonly List<SpriteSequenceController> _childScratch = new(256);
        private readonly List<SpriteSequenceController> _runtimeControllers = new(64);
        private readonly List<SpriteSequenceController> _ownedControllers = new(256);
        private readonly HashSet<SpriteSequenceController> _ownedControllerSet = new(256);
        private readonly List<SpriteSequenceController> _snapshotControllers = new(256);
        private readonly HashSet<SpriteSequenceController> _bulkUnregisterScratch = new(256);

        private NativeArray<SpriteSequencePlaybackState> _states;
        private NativeArray<SpriteSequenceAdvanceResult> _results;
        private NativeArray<double> _deltaTimes;
        private NativeArray<int> _maxFrameAdvances;
        private NativeArray<SpriteSequenceBatchToken> _tokens;
        private JobHandle _activeJobHandle;
        private int _capacity;
        private int _snapshotCount;
        private bool _jobScheduled;
        private bool _isUpdating;
        private bool _refreshPending;
        private bool _shutdownPending;
        private bool _shutdownComplete;
        private bool _capacityLimitWarningIssued;
        private bool _allocationWarningIssued;

        public int OwnedControllerCount => _ownedControllers.Count;
        public int BufferCapacity => _capacity;

        private void OnEnable()
        {
            _shutdownComplete = false;
            _shutdownPending = false;
            _refreshPending = false;
            _capacityLimitWarningIssued = false;
            _allocationWarningIssued = false;
            RefreshControllers();
        }

        private void OnDisable()
        {
            if (_isUpdating)
            {
                _shutdownPending = true;
                return;
            }

            Shutdown();
        }

        private void OnDestroy()
        {
            if (_isUpdating)
            {
                _shutdownPending = true;
                return;
            }

            Shutdown();
        }

        private void OnTransformChildrenChanged()
        {
            if (autoCollectChildren && isActiveAndEnabled)
            {
                RefreshControllers();
            }
        }

        private void Update()
        {
            if (_shutdownComplete || _isUpdating)
            {
                return;
            }

            _isUpdating = true;
            try
            {
                ExecuteBatch();
            }
            finally
            {
                _snapshotCount = 0;
                _snapshotControllers.Clear();
                _isUpdating = false;

                if (_shutdownPending || !isActiveAndEnabled)
                {
                    Shutdown();
                }
                else if (_refreshPending)
                {
                    _refreshPending = false;
                    RebuildOwnership();
                }
            }
        }

        public void RefreshControllers()
        {
            if (_shutdownComplete || !isActiveAndEnabled)
            {
                return;
            }

            if (_isUpdating)
            {
                _refreshPending = true;
                return;
            }

            RebuildOwnership();
        }

        /// <summary>Registers a runtime-created controller with this manager.</summary>
        public bool RegisterController(SpriteSequenceController controller)
        {
            return RegisterController(controller, out _);
        }

        /// <summary>
        /// Registers a runtime-created controller and reports whether this call created the
        /// runtime registration. A controller already owned through configured sources succeeds
        /// without creating a redundant registration.
        /// </summary>
        public bool RegisterController(SpriteSequenceController controller, out bool registrationAdded)
        {
            registrationAdded = false;
            if (controller == null)
            {
                return false;
            }

            if (_shutdownComplete || !isActiveAndEnabled)
            {
                return false;
            }

            bool isRuntimeRegistered = _runtimeControllers.Contains(controller);

            if (_ownedControllerSet.Contains(controller))
            {
                return true;
            }

            if (_isUpdating)
            {
                if (!isRuntimeRegistered)
                {
                    _runtimeControllers.Add(controller);
                    registrationAdded = true;
                }

                _refreshPending = true;
                return true;
            }

            if (!TryEnsureCapacity(_ownedControllers.Count + 1))
            {
                ReleaseOwnership();
                return false;
            }

            if (!TryAddOwnedController(controller))
            {
                return false;
            }

            if (!isRuntimeRegistered)
            {
                _runtimeControllers.Add(controller);
                registrationAdded = true;
            }

            return true;
        }

        /// <summary>Removes a runtime registration and rebuilds configured ownership.</summary>
        public bool UnregisterController(SpriteSequenceController controller)
        {
            if (controller == null || !_runtimeControllers.Remove(controller))
            {
                return false;
            }

            if (_isUpdating)
            {
                _refreshPending = true;
            }
            else if (!_shutdownComplete && isActiveAndEnabled)
            {
                RebuildOwnership();
            }

            return true;
        }

        /// <summary>Removes runtime registrations in one ownership rebuild.</summary>
        public int UnregisterControllers(IReadOnlyList<SpriteSequenceController> controllerBatch)
        {
            if (controllerBatch == null || controllerBatch.Count == 0)
            {
                return 0;
            }

            _bulkUnregisterScratch.Clear();
            for (int i = 0; i < controllerBatch.Count; i++)
            {
                SpriteSequenceController controller = controllerBatch[i];
                if (controller != null)
                {
                    _bulkUnregisterScratch.Add(controller);
                }
            }

            int removedCount = 0;
            for (int i = _runtimeControllers.Count - 1; i >= 0; i--)
            {
                if (_bulkUnregisterScratch.Contains(_runtimeControllers[i]))
                {
                    _runtimeControllers.RemoveAt(i);
                    removedCount++;
                }
            }

            _bulkUnregisterScratch.Clear();
            if (removedCount == 0)
            {
                return 0;
            }

            if (_isUpdating)
            {
                _refreshPending = true;
            }
            else if (!_shutdownComplete && isActiveAndEnabled)
            {
                RebuildOwnership();
            }

            return removedCount;
        }

        private void ExecuteBatch()
        {
            int ownedCount = _ownedControllers.Count;
            if (ownedCount == 0)
            {
                return;
            }

            if (!TryEnsureCapacity(ownedCount))
            {
                ReleaseOwnership();
                return;
            }
            double scaledDeltaTime = Time.deltaTime;
            double unscaledDeltaTime = Time.unscaledDeltaTime;
            _snapshotControllers.Clear();
            _snapshotCount = 0;

            for (int i = 0; i < ownedCount; i++)
            {
                SpriteSequenceController controller = _ownedControllers[i];
                if (controller == null)
                {
                    _refreshPending = true;
                    continue;
                }

                if (!controller.TryCaptureBatchSnapshot(
                        this,
                        scaledDeltaTime,
                        unscaledDeltaTime,
                        out SpriteSequencePlaybackState state,
                        out double deltaTime,
                        out int maxAdvances,
                        out SpriteSequenceBatchToken token))
                {
                    continue;
                }

                int index = _snapshotCount++;
                _snapshotControllers.Add(controller);
                _states[index] = state;
                _deltaTimes[index] = deltaTime;
                _maxFrameAdvances[index] = maxAdvances;
                _tokens[index] = token;
            }

            if (_snapshotCount == 0)
            {
                return;
            }

            if (_snapshotCount < Math.Max(1, minParallelJobCount))
            {
                ExecuteInline();
            }
            else
            {
                ExecuteScheduledJob();
            }

            ApplyResults();
        }

        private void ExecuteInline()
        {
            for (int i = 0; i < _snapshotCount; i++)
            {
                SpriteSequencePlaybackState state = _states[i];
                _results[i] = state.Advance(_deltaTimes[i], _maxFrameAdvances[i]);
                _states[i] = state;
            }
        }

        private void ExecuteScheduledJob()
        {
            var job = new SpriteSequenceBatchUpdateJob
            {
                States = _states,
                DeltaTimes = _deltaTimes,
                MaxFrameAdvances = _maxFrameAdvances,
                Results = _results,
            };

            _activeJobHandle = job.Schedule(_snapshotCount, Math.Max(1, jobBatchSize));
            _jobScheduled = true;
            try
            {
                _activeJobHandle.Complete();
            }
            finally
            {
                _jobScheduled = false;
            }
        }

        private void ApplyResults()
        {
            for (int i = 0; i < _snapshotCount; i++)
            {
                if (_shutdownPending)
                {
                    return;
                }

                SpriteSequenceController controller = _snapshotControllers[i];
                if (controller == null)
                {
                    _refreshPending = true;
                    continue;
                }

                try
                {
                    controller.TryApplyBatchResult(this, _tokens[i], _states[i], _results[i]);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, controller);
                }
            }
        }

        private void RebuildOwnership()
        {
            ReleaseOwnership();
            controllers ??= new List<SpriteSequenceController>();

            for (int i = 0; i < controllers.Count; i++)
            {
                TryAddOwnedController(controllers[i]);
            }

            for (int i = _runtimeControllers.Count - 1; i >= 0; i--)
            {
                if (_runtimeControllers[i] == null)
                {
                    _runtimeControllers.RemoveAt(i);
                }
            }

            for (int i = 0; i < _runtimeControllers.Count; i++)
            {
                TryAddOwnedController(_runtimeControllers[i]);
            }

            if (autoCollectChildren)
            {
                _childScratch.Clear();
                GetComponentsInChildren(true, _childScratch);
                for (int i = 0; i < _childScratch.Count; i++)
                {
                    TryAddOwnedController(_childScratch[i]);
                }

                _childScratch.Clear();
            }

            int requestedCapacity = Math.Max(_ownedControllers.Count, Math.Max(0, prewarmCapacity));
            requestedCapacity = Math.Min(requestedCapacity, ResolveMaxControllerCapacity());
            if (!TryEnsureCapacity(requestedCapacity))
            {
                ReleaseOwnership();
            }
        }

        private bool TryAddOwnedController(SpriteSequenceController controller)
        {
            if (controller == null)
            {
                return false;
            }

            if (_ownedControllerSet.Contains(controller))
            {
                return true;
            }

            if (_ownedControllers.Count >= ResolveMaxControllerCapacity())
            {
                if (!_capacityLimitWarningIssued)
                {
                    Debug.LogWarning(
                        $"SpriteSequenceBurstManager '{name}' reached maxControllerCapacity=" +
                        $"{ResolveMaxControllerCapacity()}. Additional controllers remain unclaimed and use their fallback policy.",
                        this);
                    _capacityLimitWarningIssued = true;
                }

                return false;
            }

            if (controller.TryClaimBatchOwnership(this))
            {
                _ownedControllerSet.Add(controller);
                _ownedControllers.Add(controller);
                return true;
            }

            Debug.LogError(
                $"SpriteSequenceController '{controller.name}' is already owned by another active " +
                $"SpriteSequenceBurstManager. Manager '{name}' will not update it.",
                controller);
            return false;
        }

        private void ReleaseOwnership()
        {
            for (int i = 0; i < _ownedControllers.Count; i++)
            {
                SpriteSequenceController controller = _ownedControllers[i];
                if (controller != null)
                {
                    controller.ReleaseBatchOwnership(this);
                }
            }

            _ownedControllers.Clear();
            _ownedControllerSet.Clear();
        }

        private bool TryEnsureCapacity(int count)
        {
            count = Math.Min(Math.Max(0, count), ResolveMaxControllerCapacity());
            if (count == 0)
            {
                return true;
            }

            if (count <= _capacity && _capacity <= ResolveMaxControllerCapacity() &&
                _states.IsCreated && _results.IsCreated && _deltaTimes.IsCreated &&
                _maxFrameAdvances.IsCreated && _tokens.IsCreated)
            {
                return true;
            }

            int newCapacity = _capacity > 0 ? _capacity : 16;
            while (newCapacity < count)
            {
                if (newCapacity > int.MaxValue / 2)
                {
                    newCapacity = count;
                    break;
                }

                newCapacity <<= 1;
            }

            newCapacity = Math.Min(newCapacity, ResolveMaxControllerCapacity());
            NativeArray<SpriteSequencePlaybackState> newStates = default;
            NativeArray<SpriteSequenceAdvanceResult> newResults = default;
            NativeArray<double> newDeltaTimes = default;
            NativeArray<int> newMaxFrameAdvances = default;
            NativeArray<SpriteSequenceBatchToken> newTokens = default;

            try
            {
                newStates = new NativeArray<SpriteSequencePlaybackState>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                newResults = new NativeArray<SpriteSequenceAdvanceResult>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                newDeltaTimes = new NativeArray<double>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                newMaxFrameAdvances = new NativeArray<int>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                newTokens = new NativeArray<SpriteSequenceBatchToken>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            catch (Exception exception)
            {
                DisposeTemporaryBuffers(
                    ref newStates,
                    ref newResults,
                    ref newDeltaTimes,
                    ref newMaxFrameAdvances,
                    ref newTokens);
                LogAllocationFailure(newCapacity, exception);
                return false;
            }

            try
            {
                DisposeArrays();
            }
            catch (Exception exception)
            {
                DisposeTemporaryBuffers(
                    ref newStates,
                    ref newResults,
                    ref newDeltaTimes,
                    ref newMaxFrameAdvances,
                    ref newTokens);
                LogAllocationFailure(newCapacity, exception);
                return false;
            }

            _states = newStates;
            _results = newResults;
            _deltaTimes = newDeltaTimes;
            _maxFrameAdvances = newMaxFrameAdvances;
            _tokens = newTokens;
            _capacity = newCapacity;
            return true;
        }

        private int ResolveMaxControllerCapacity()
        {
            return Math.Max(1, maxControllerCapacity);
        }

        private void LogAllocationFailure(int requestedCapacity, Exception exception)
        {
            if (_allocationWarningIssued)
            {
                return;
            }

            Debug.LogError(
                $"SpriteSequenceBurstManager '{name}' could not allocate buffers for " +
                $"{requestedCapacity} controllers. Claimed controllers will be released. Exception={exception}",
                this);
            _allocationWarningIssued = true;
        }

        private static void DisposeTemporaryBuffers(
            ref NativeArray<SpriteSequencePlaybackState> states,
            ref NativeArray<SpriteSequenceAdvanceResult> results,
            ref NativeArray<double> deltaTimes,
            ref NativeArray<int> maxFrameAdvances,
            ref NativeArray<SpriteSequenceBatchToken> tokens)
        {
            Exception ignored = null;
            DisposeAndCapture(ref states, ref ignored);
            DisposeAndCapture(ref results, ref ignored);
            DisposeAndCapture(ref deltaTimes, ref ignored);
            DisposeAndCapture(ref maxFrameAdvances, ref ignored);
            DisposeAndCapture(ref tokens, ref ignored);
        }

        private void Shutdown()
        {
            if (_shutdownComplete)
            {
                return;
            }

            _shutdownComplete = true;
            _shutdownPending = false;
            _refreshPending = false;

            try
            {
                CompleteOutstandingJob();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                ReleaseOwnership();
                try
                {
                    DisposeArrays();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
                _snapshotCount = 0;
                _snapshotControllers.Clear();
                _childScratch.Clear();
                _bulkUnregisterScratch.Clear();
            }
        }

        private void CompleteOutstandingJob()
        {
            if (!_jobScheduled)
            {
                return;
            }

            try
            {
                _activeJobHandle.Complete();
            }
            finally
            {
                _jobScheduled = false;
            }
        }

        private void DisposeArrays()
        {
            _capacity = 0;
            Exception firstException = null;
            DisposeAndCapture(ref _states, ref firstException);
            DisposeAndCapture(ref _results, ref firstException);
            DisposeAndCapture(ref _deltaTimes, ref firstException);
            DisposeAndCapture(ref _maxFrameAdvances, ref firstException);
            DisposeAndCapture(ref _tokens, ref firstException);
            if (firstException != null)
            {
                throw firstException;
            }
        }

        private static void DisposeAndCapture<T>(ref NativeArray<T> array, ref Exception firstException)
            where T : struct
        {
            if (!array.IsCreated)
            {
                return;
            }

            try
            {
                array.Dispose();
            }
            catch (Exception exception)
            {
                firstException ??= exception;
            }
            finally
            {
                array = default;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            controllers ??= new List<SpriteSequenceController>();
            jobBatchSize = Math.Max(1, jobBatchSize);
            minParallelJobCount = Math.Max(1, minParallelJobCount);
            prewarmCapacity = Math.Max(0, prewarmCapacity);
            maxControllerCapacity = Math.Max(1, maxControllerCapacity);
            prewarmCapacity = Math.Min(prewarmCapacity, maxControllerCapacity);
        }
#endif
    }
}
