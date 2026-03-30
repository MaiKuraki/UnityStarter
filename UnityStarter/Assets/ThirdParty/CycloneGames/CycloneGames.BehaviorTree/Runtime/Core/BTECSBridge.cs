namespace CycloneGames.BehaviorTree.Runtime.Core
{
    /// <summary>
    /// ECS bridge interface for data-oriented behavior tree execution.
    /// Implement this to drive behavior trees from an ECS system (e.g., Unity DOTS ISystem)
    /// while keeping the tree structure in managed code.
    /// 
    /// Usage pattern (in an ISystem):
    ///   foreach (var (btRef, transform) in SystemAPI.Query<BTEntityRef, LocalTransform>())
    ///   {
    ///       var ctx = new ECSBTContext { Entity = entity, EntityManager = mgr };
    ///       btRef.RuntimeTree.Tick();
    ///   }
    /// </summary>
    public interface IBTEntityBridge
    {
        /// <summary>
        /// Read a component value from the entity. Implementation should use
        /// EntityManager.GetComponentData or equivalent.
        /// </summary>
        T ReadComponent<T>(int entityId) where T : struct;

        /// <summary>
        /// Write a component value to the entity.
        /// </summary>
        void WriteComponent<T>(int entityId, T value) where T : struct;

        /// <summary>
        /// Check if entity has a specific component type.
        /// </summary>
        bool HasComponent<T>(int entityId) where T : struct;
    }

    /// <summary>
    /// Lightweight per-entity handle for ECS integration.
    /// Store this in an IComponentData to associate entities with behavior trees.
    ///
    /// Supports two execution modes:
    ///   Managed mode: TreeIndex → BTTreePool (per-entity managed instances)
    ///   DOD mode:     DODAgentId → BTTickScheduler (Burst parallel tick, 10000+ agents)
    /// </summary>
    public struct BTEntityRef
    {
        /// <summary>Index into BTTreePool managed instance array.</summary>
        public int TreeIndex;

        /// <summary>Index into managed RuntimeBlackboard array.</summary>
        public int BlackboardIndex;

        /// <summary>Agent ID in BTTickScheduler (DOD mode). -1 if using managed mode.</summary>
        public int DODAgentId;

        /// <summary>Priority level for tick scheduling.</summary>
        public int Priority;

        /// <summary>Frames remaining until next tick.</summary>
        public int TickCountdown;

        public static readonly BTEntityRef Invalid = new BTEntityRef { TreeIndex = -1, BlackboardIndex = -1, DODAgentId = -1 };
        public bool IsValid => TreeIndex >= 0 || DODAgentId >= 0;
        public bool IsDOD => DODAgentId >= 0;
    }

    /// <summary>
    /// Pool of RuntimeBehaviorTree instances for high-density AI scenarios.
    /// Caches compiled BehaviorTree assets and provides per-entity instances
    /// with individual node state and blackboards.
    /// 
    /// For ECS: store BTEntityRef in IComponentData, tick via pool in ISystem.
    /// For MonoBehaviour: use when spawning/despawning hundreds of identical AI agents.
    /// </summary>
    public class BTTreePool
    {
        // Template trees (compiled once, cloned per entity)
        private readonly CycloneGames.BehaviorTree.Runtime.BehaviorTree[] _templates;
        private int _templateCount;

        // Per-entity instances
        private RuntimeBehaviorTree[] _instances;
        private int _instanceCount;
        private int _instanceCapacity;

        // Free list for O(1) recycle
        private int[] _freeList;
        private int _freeCount;

        public BTTreePool(int maxTemplates = 32, int initialInstanceCapacity = 256)
        {
            _templates = new BehaviorTree[maxTemplates];
            _instanceCapacity = initialInstanceCapacity;
            _instances = new RuntimeBehaviorTree[_instanceCapacity];
            _freeList = new int[_instanceCapacity];
        }

        /// <summary>
        /// Register a BehaviorTree asset as a template. Returns template index.
        /// </summary>
        public int RegisterTemplate(CycloneGames.BehaviorTree.Runtime.BehaviorTree template)
        {
            int idx = _templateCount++;
            _templates[idx] = template;
            return idx;
        }

        /// <summary>
        /// Allocate a new tree instance from a template. Returns instance index.
        /// Each instance has its own node graph and blackboard.
        /// </summary>
        public int Allocate(int templateIndex, RuntimeBTContext context = null)
        {
            var template = _templates[templateIndex];
            if (template == null) return -1;

            var instance = template.Compile(context ?? new RuntimeBTContext());
            if (instance == null) return -1;

            int idx;
            if (_freeCount > 0)
            {
                idx = _freeList[--_freeCount];
            }
            else
            {
                if (_instanceCount >= _instanceCapacity)
                {
                    int newCap = _instanceCapacity * 2;
                    var newArr = new RuntimeBehaviorTree[newCap];
                    System.Array.Copy(_instances, newArr, _instanceCount);
                    _instances = newArr;

                    var newFree = new int[newCap];
                    System.Array.Copy(_freeList, newFree, _freeCount);
                    _freeList = newFree;
                    _instanceCapacity = newCap;
                }
                idx = _instanceCount++;
            }

            _instances[idx] = instance;
            return idx;
        }

        /// <summary>
        /// Recycle a tree instance (stop and return to pool).
        /// </summary>
        public void Release(int instanceIndex)
        {
            if (instanceIndex < 0 || instanceIndex >= _instanceCount) return;
            var instance = _instances[instanceIndex];
            if (instance != null)
            {
                instance.Stop();
                _instances[instanceIndex] = null;
                _freeList[_freeCount++] = instanceIndex;
            }
        }

        public RuntimeBehaviorTree GetInstance(int instanceIndex) => _instances[instanceIndex];

        public RuntimeState Tick(int instanceIndex)
        {
            var instance = _instances[instanceIndex];
            if (instance == null) return RuntimeState.Failure;
            return instance.Tick();
        }

        /// <summary>
        /// Batch tick all active instances. For MonoBehaviour usage with hundreds of agents.
        /// Respects per-tree ShouldTick() LOD gating.
        /// </summary>
        public void TickAll()
        {
            for (int i = 0; i < _instanceCount; i++)
            {
                var inst = _instances[i];
                if (inst != null && inst.ShouldTick())
                    inst.Tick();
            }
        }

        /// <summary>
        /// Batch tick with results buffer (avoids per-call overhead).
        /// Caller provides a pre-allocated results array.
        /// </summary>
        public int TickAll(RuntimeState[] results, int maxResults)
        {
            int count = 0;
            for (int i = 0; i < _instanceCount && count < maxResults; i++)
            {
                var inst = _instances[i];
                if (inst != null && inst.ShouldTick())
                {
                    results[count++] = inst.Tick();
                }
            }
            return count;
        }

        public int InstanceCount => _instanceCount;

        public void Clear()
        {
            for (int i = 0; i < _instanceCount; i++)
            {
                _instances[i]?.Stop();
                _instances[i] = null;
            }
            _instanceCount = 0;
            _freeCount = 0;
        }
    }
}
