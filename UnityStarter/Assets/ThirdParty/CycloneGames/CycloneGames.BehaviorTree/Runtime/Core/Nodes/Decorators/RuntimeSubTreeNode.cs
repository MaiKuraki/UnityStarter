using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    [Flags]
    public enum RuntimeSubTreePortDirection : byte
    {
        Input = 1 << 0,
        Output = 1 << 1,
        InOut = Input | Output,
    }

    /// <summary>
    /// Executes a subtree against an owned scoped blackboard and optionally mirrors typed ports.
    /// Input ports are synchronized before each child step. InOut ports take one input snapshot at
    /// activation start. Output and InOut ports are committed once, only after normal Success or
    /// Failure completion; aborted and faulted activations discard local output.
    /// </summary>
    public class RuntimeSubTreeNode : RuntimeDecoratorNode
    {
        private const byte PORT_AUTO = 0;
        private const byte PORT_INT = 1;
        private const byte PORT_FLOAT = 2;
        private const byte PORT_BOOL = 3;
        private const byte PORT_VECTOR3 = 4;
        private const byte PORT_OBJECT = 5;
        private const byte PORT_LONG = 6;
        private const byte PORT_LONG2 = 7;
        private const byte PORT_LONG3 = 8;

        private RuntimeBlackboard _scopedBlackboard;
        private int[] _localKeys = Array.Empty<int>();
        private int[] _parentKeys = Array.Empty<int>();
        private byte[] _portTypes = Array.Empty<byte>();
        private RuntimeSubTreePortDirection[] _portDirections = Array.Empty<RuntimeSubTreePortDirection>();
        private RuntimeBlackboardMutation[] _mutationScratch = Array.Empty<RuntimeBlackboardMutation>();
        private int _remapCount;
        private bool _needsInitialInputSync;

        public override void OnAwake()
        {
            if (_scopedBlackboard != null)
            {
                throw new InvalidOperationException("The subtree node was awakened more than once.");
            }

            _scopedBlackboard = new RuntimeBlackboard(null, 8);
            base.OnAwake();
        }

        /// <summary>
        /// Configures automatically detected port mappings. Arrays are copied during setup.
        /// </summary>
        public void SetPortRemapping(int[] localKeys, int[] parentKeys)
        {
            SetPortRemapping(localKeys, parentKeys, null, null);
        }

        /// <summary>
        /// Configures typed port mappings. Type IDs are: 0 auto, 1 int, 2 float, 3 bool,
        /// 4 Vector3, 5 object, 6 long, 7 long2, and 8 long3. Arrays are copied during setup.
        /// </summary>
        public void SetPortRemapping(int[] localKeys, int[] parentKeys, byte[] portTypes)
        {
            SetPortRemapping(localKeys, parentKeys, portTypes, null);
        }

        /// <summary>
        /// Configures typed, directional port mappings. A null direction array preserves the legacy
        /// overload as InOut: capture parent input once at activation start and commit local output on
        /// normal completion. Use Input for values that must refresh before every child step, and
        /// Output when the mapped parent value must not be copied into the local key. Normal scoped
        /// blackboard parent fallback still applies to any absent local key with the same hash.
        /// </summary>
        public void SetPortRemapping(
            int[] localKeys,
            int[] parentKeys,
            byte[] portTypes,
            RuntimeSubTreePortDirection[] portDirections)
        {
            ThrowIfSetupFrozen();
            if (IsStarted)
            {
                throw new InvalidOperationException("Port remapping cannot change during an active subtree execution.");
            }

            ValidatePortRemapping(localKeys, parentKeys, portTypes, portDirections);

            if (localKeys == null)
            {
                _localKeys = Array.Empty<int>();
                _parentKeys = Array.Empty<int>();
                _portTypes = Array.Empty<byte>();
                _portDirections = Array.Empty<RuntimeSubTreePortDirection>();
                _mutationScratch = Array.Empty<RuntimeBlackboardMutation>();
                _remapCount = 0;
                return;
            }

            _localKeys = (int[])localKeys.Clone();
            _parentKeys = (int[])parentKeys.Clone();
            _remapCount = localKeys.Length;
            _portTypes = portTypes != null
                ? (byte[])portTypes.Clone()
                : new byte[_remapCount];
            if (portDirections != null)
            {
                _portDirections = (RuntimeSubTreePortDirection[])portDirections.Clone();
            }
            else
            {
                _portDirections = new RuntimeSubTreePortDirection[_remapCount];
                for (int i = 0; i < _remapCount; i++)
                {
                    _portDirections[i] = RuntimeSubTreePortDirection.InOut;
                }
            }

            _mutationScratch = new RuntimeBlackboardMutation[_remapCount];
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            BindScope(blackboard);
            _needsInitialInputSync = true;
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null)
            {
                return RuntimeState.Failure;
            }

            SyncInputs(blackboard, _needsInitialInputSync);
            _needsInitialInputSync = false;
            return Child.Run(_scopedBlackboard);
        }

        protected override void OnExit(
            RuntimeBlackboard blackboard,
            RuntimeNodeExitReason reason,
            Exception exception)
        {
            try
            {
                if (Child != null && Child.IsStarted)
                {
                    Child.Abort(_scopedBlackboard);
                }

                if (reason == RuntimeNodeExitReason.Completed)
                {
                    SyncOutputs(blackboard);
                }
            }
            finally
            {
                _needsInitialInputSync = false;
                ReleaseScope();
            }
        }

        protected override void OnReset(RuntimeBlackboard blackboard)
        {
            BindScope(blackboard);
            try
            {
                Child?.Reset(_scopedBlackboard);
            }
            finally
            {
                ReleaseScope();
            }
        }

        protected override void OnDispose(RuntimeBlackboard blackboard)
        {
            RuntimeBlackboard scopedBlackboard = _scopedBlackboard;
            _scopedBlackboard = null;
            if (scopedBlackboard == null)
            {
                Child?.DisposeNode(blackboard);
                return;
            }

            try
            {
                Child?.DisposeNode(scopedBlackboard);
            }
            finally
            {
                try
                {
                    scopedBlackboard.Parent = null;
                }
                finally
                {
                    try
                    {
                        scopedBlackboard.Context = null;
                    }
                    finally
                    {
                        scopedBlackboard.Dispose();
                    }
                }
            }
        }

        private static void ValidatePortRemapping(
            int[] localKeys,
            int[] parentKeys,
            byte[] portTypes,
            RuntimeSubTreePortDirection[] portDirections)
        {
            if (localKeys == null || parentKeys == null)
            {
                if (localKeys == null &&
                    parentKeys == null &&
                    (portTypes == null || portTypes.Length == 0) &&
                    (portDirections == null || portDirections.Length == 0))
                {
                    return;
                }

                throw new ArgumentException("Local and parent key arrays must either both be null or both be present.");
            }

            if (localKeys.Length != parentKeys.Length)
            {
                throw new ArgumentException("Local and parent key arrays must have identical lengths.");
            }

            if (portTypes != null && portTypes.Length != localKeys.Length)
            {
                throw new ArgumentException("The port type array must match the key array lengths.", nameof(portTypes));
            }

            if (portDirections != null && portDirections.Length != localKeys.Length)
            {
                throw new ArgumentException(
                    "The port direction array must match the key array lengths.",
                    nameof(portDirections));
            }

            for (int i = 0; i < localKeys.Length; i++)
            {
                for (int earlierIndex = 0; earlierIndex < i; earlierIndex++)
                {
                    if (localKeys[earlierIndex] == localKeys[i])
                    {
                        throw new ArgumentException(
                            $"Local port key {localKeys[i]} appears more than once.",
                            nameof(localKeys));
                    }

                    if (parentKeys[earlierIndex] == parentKeys[i])
                    {
                        throw new ArgumentException(
                            $"Parent port key {parentKeys[i]} appears more than once.",
                            nameof(parentKeys));
                    }
                }
            }

            if (portTypes != null)
            {
                for (int i = 0; i < portTypes.Length; i++)
                {
                    if (portTypes[i] > PORT_LONG3)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(portTypes),
                            portTypes[i],
                            $"Port type at index {i} is not supported.");
                    }
                }
            }

            if (portDirections != null)
            {
                byte supportedDirections = (byte)RuntimeSubTreePortDirection.InOut;
                for (int i = 0; i < portDirections.Length; i++)
                {
                    byte direction = (byte)portDirections[i];
                    if (direction == 0 || (direction & ~supportedDirections) != 0)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(portDirections),
                            portDirections[i],
                            $"Port direction at index {i} is not supported.");
                    }
                }
            }
        }

        private void BindScope(RuntimeBlackboard blackboard)
        {
            if (_scopedBlackboard == null)
            {
                throw new InvalidOperationException("The subtree blackboard has not been initialized.");
            }

            _scopedBlackboard.Parent = blackboard;
            _scopedBlackboard.Context = blackboard?.Context;
            if (blackboard != null)
            {
                _scopedBlackboard.StringHashFunc = blackboard.StringHashFunc;
            }
        }

        private void ReleaseScope()
        {
            if (_scopedBlackboard == null)
            {
                return;
            }

            try
            {
                _scopedBlackboard.Clear();
            }
            finally
            {
                try
                {
                    _scopedBlackboard.Parent = null;
                }
                finally
                {
                    _scopedBlackboard.Context = null;
                }
            }
        }

        private void SyncInputs(RuntimeBlackboard parent, bool includeInOut)
        {
            try
            {
                int mutationCount = CaptureMappedMutations(
                    parent,
                    _parentKeys,
                    _localKeys,
                    includeParentChain: true,
                    removeWhenMissing: true,
                    requiredDirection: RuntimeSubTreePortDirection.Input,
                    excludedDirection: includeInOut
                        ? (RuntimeSubTreePortDirection)0
                        : RuntimeSubTreePortDirection.Output);
                _scopedBlackboard.ApplyLocalBatch(_mutationScratch, mutationCount);
            }
            finally
            {
                Array.Clear(_mutationScratch, 0, _mutationScratch.Length);
            }
        }

        private void SyncOutputs(RuntimeBlackboard parent)
        {
            try
            {
                int mutationCount = CaptureMappedMutations(
                    _scopedBlackboard,
                    _localKeys,
                    _parentKeys,
                    includeParentChain: false,
                    removeWhenMissing: false,
                    requiredDirection: RuntimeSubTreePortDirection.Output,
                    excludedDirection: (RuntimeSubTreePortDirection)0);
                parent.ApplyLocalBatch(_mutationScratch, mutationCount);
            }
            finally
            {
                Array.Clear(_mutationScratch, 0, _mutationScratch.Length);
            }
        }

        private int CaptureMappedMutations(
            RuntimeBlackboard source,
            int[] sourceKeys,
            int[] destinationKeys,
            bool includeParentChain,
            bool removeWhenMissing,
            RuntimeSubTreePortDirection requiredDirection,
            RuntimeSubTreePortDirection excludedDirection)
        {
            int mutationCount = 0;
            for (int i = 0; i < _remapCount; i++)
            {
                RuntimeSubTreePortDirection direction = _portDirections[i];
                if ((direction & requiredDirection) == 0 ||
                    (excludedDirection != 0 && (direction & excludedDirection) != 0))
                {
                    continue;
                }

                int sourceKey = sourceKeys[i];
                int destinationKey = destinationKeys[i];
                bool hasValue = source.TryCaptureMutation(
                    sourceKey,
                    includeParentChain,
                    out RuntimeBlackboardMutation mutation);

                if (!hasValue)
                {
                    if (!removeWhenMissing)
                    {
                        continue;
                    }

                    mutation.Kind = RuntimeBlackboardMutationKind.Remove;
                }

                byte configuredType = _portTypes[i];
                if (hasValue &&
                    configuredType != PORT_AUTO &&
                    configuredType != (byte)mutation.Kind)
                {
                    throw new InvalidOperationException(
                        $"Subtree port {i} expected value type {configuredType}, but key {sourceKey} contains {(byte)mutation.Kind}.");
                }

                mutation.Key = destinationKey;
                _mutationScratch[mutationCount++] = mutation;
            }

            return mutationCount;
        }
    }
}
