using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using VitalRouter;
using Cysharp.Threading.Tasks;
using UnityEngine;
using CycloneGames.Logging;
using CycloneGames.RPGFoundation.Interaction.Core;
using CycloneGames.Spatial.Core;

namespace CycloneGames.RPGFoundation.Interaction.Runtime
{
    [Routes]
    [DisallowMultipleComponent]
    public partial class InteractionSystem : MonoBehaviour, IInteractionSystem
    {
        private static readonly LogChannel Log = RpgInteractionLog.Channel;

        /// <summary>
        /// Priority is projected onto the 32 layer bits of the spatial index. Queries walk tiers
        /// from high to low, so a high-priority target is never dropped by a candidate budget that
        /// a low-priority target consumed first.
        /// </summary>
        public const int PriorityTierCount = 32;

        [Header("Spatial Grid")]
        [Tooltip("Local interaction world scope. Use unique values for split-screen, additive scenes, prediction worlds, or server-side simulations.")]
        [SerializeField] private int worldId;
        [SerializeField] private bool is2DMode;
        [SerializeField] private float cellSize = 10f;
        [SerializeField] private int initialCapacity = 256;

        [Header("Authority")]
        [Tooltip("Validates requests against the authority service before execution. Targets without a stable identity always bypass validation so single-player scenes keep working.")]
        [SerializeField] private bool enableAuthorityGate = true;

        private IDisposable _subscription;
        private SpatialHashGrid<int> _spatialGrid;
        private InteractionAuthorityService _authority;
        private readonly InteractionMetrics _metrics = new InteractionMetrics();
        private bool _initialized;

        // Slot table maps the grid's int payload back to the registered instance. The grid never
        // stores interface references, so query results stay value-typed and cache friendly.
        private IInteractable[] _slotItems;
        private int _slotHighWaterMark;
        private readonly Stack<int> _freeSlots = new Stack<int>(64);
        private readonly Dictionary<IInteractable, GridRegistration> _registrations =
            new Dictionary<IInteractable, GridRegistration>(256);

        private readonly int[] _tierCounts = new int[PriorityTierCount];
        private uint _occupiedTiers;

        private readonly List<DistanceMonitorEntry> _distanceMonitors = new(16);
        private readonly Dictionary<IInteractable, int> _distanceMonitorIndex = new(16);

        private int _nextRequestId;
        private int _authorityTick;

        private struct GridRegistration
        {
            public int Slot;
            public SpatialHandle Handle;
        }

        private struct DistanceMonitorEntry
        {
            public IInteractable Target;
            public InstigatorHandle Instigator;
            public float MaxRangeSqr;
        }

        private static readonly Dictionary<int, InteractionSystem> s_systemsByWorldId = new();
        private static readonly List<InteractionSystem> s_aliveSystems = new(8);
        private static InteractionSystem s_instance;
        private bool _registeredWorld;

        /// <summary>Global singleton instance. Null if no InteractionSystem exists in the scene.</summary>
        public static InteractionSystem Instance => s_instance;
        public static bool TryGetWorld(int worldId, out InteractionSystem system) => s_systemsByWorldId.TryGetValue(worldId, out system);

        /// <summary>
        /// Raised on the main thread after a new system finishes Awake initialization and is ready to
        /// accept registrations. Consumers that could not resolve a system at enable time subscribe to
        /// this event to defer binding instead of scanning the scene. Subscribers must unsubscribe
        /// after binding; the event is cleared by <see cref="ResetStaticStateForDomainReloadDisabled"/>.
        /// </summary>
        public static event Action<InteractionSystem> SystemRegistered;

        /// <summary>
        /// Resolves the default system without any scene-wide search: the alive global
        /// <see cref="Instance"/> first, otherwise the first alive registered system.
        /// Returns null when no system exists yet; callers should defer binding to
        /// <see cref="SystemRegistered"/>. Main thread only.
        /// </summary>
        public static InteractionSystem ResolveDefault()
        {
            InteractionSystem instance = s_instance;
            if (instance != null) return instance;

            for (int i = 0; i < s_aliveSystems.Count; i++)
            {
                InteractionSystem system = s_aliveSystems[i];
                if (system != null) return system;
            }

            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticStateForDomainReloadDisabled()
        {
            // With Enter Play Mode Options (Domain Reload disabled), static fields survive across
            // play sessions as destroyed object references. Rebuild a clean registry before any
            // scene loads; in a normal player the fields are already fresh, so this is a no-op.
            s_instance = null;
            s_aliveSystems.Clear();
            s_systemsByWorldId.Clear();
            SystemRegistered = null;
        }

        public SpatialHashGrid<int> SpatialGrid => _spatialGrid;
        public InteractionAuthorityService Authority => _authority;
        public InteractionMetrics Metrics => _metrics;
        public bool Is2DMode => is2DMode;
        public int WorldId => worldId;

        /// <summary>Bitmask of priority tiers that currently hold at least one registered target.</summary>
        public uint OccupiedTiers => _occupiedTiers;

        public event Action<IInteractable, InstigatorHandle> OnAnyInteractionStarted;
        public event Action<IInteractable, InstigatorHandle, bool> OnAnyInteractionCompleted;

        private void Awake()
        {
            if (s_instance == null)
            {
                s_instance = this;
            }
            else if (s_instance != this)
            {
                Log.Warning(
                    "[InteractionSystem] Multiple systems are active. Assign unique WorldId values and explicit detector/system references for production multiplayer or additive scenes.");
            }

            if (!s_systemsByWorldId.TryGetValue(worldId, out InteractionSystem existing))
            {
                s_systemsByWorldId.Add(worldId, this);
                _registeredWorld = true;
            }
            else if (existing != this)
            {
                Log.Error(
                    "[InteractionSystem] Duplicate WorldId detected. This system will not subscribe to global interaction commands.");
            }

            s_aliveSystems.Add(this);
            Initialize();
            RaiseSystemRegistered(this);
        }

        /// <summary>
        /// Notifies deferred binders. Isolated per subscriber so one throwing handler cannot
        /// break binding for the rest or abort this Awake. Cold path (scene load / late spawn).
        /// </summary>
        private static void RaiseSystemRegistered(InteractionSystem registered)
        {
            Action<InteractionSystem> handlers = SystemRegistered;
            if (handlers == null) return;

            foreach (Action<InteractionSystem> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(registered);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[InteractionSystem] A SystemRegistered subscriber failed.");
                }
            }
        }

        private void Start()
        {
            if (_registeredWorld)
            {
                _subscription = this.MapTo(Router.Default);
            }
        }

        private void OnDestroy()
        {
            Dispose();
        }

        public void Initialize() => Initialize(is2DMode, cellSize);

        public void Initialize(bool is2DMode, float cellSize = 10f)
        {
            if (_initialized) return;

            this.is2DMode = is2DMode;
            this.cellSize = cellSize > 0f ? cellSize : 10f;
            _spatialGrid = new SpatialHashGrid<int>(this.cellSize, Math.Max(initialCapacity, 64));
            _slotItems = new IInteractable[Math.Max(initialCapacity, 64)];
            _authority = new InteractionAuthorityService(new InteractionAuthorityOptions(worldId, requireStableIds: false));
            _initialized = true;
        }

        public void Dispose()
        {
            if (!_initialized) return;

            // Detach from the static registry BEFORE any teardown so a consumer re-resolving
            // during this call can never pick the dying system.
            if (s_instance == this)
            {
                s_instance = null;
            }

            if (_registeredWorld && s_systemsByWorldId.TryGetValue(worldId, out InteractionSystem registeredSystem) && registeredSystem == this)
            {
                s_systemsByWorldId.Remove(worldId);
                _registeredWorld = false;
            }

            for (int i = 0; i < s_aliveSystems.Count; i++)
            {
                if (s_aliveSystems[i] == this)
                {
                    s_aliveSystems.RemoveAt(i);
                    break;
                }
            }

            _subscription?.Dispose();
            _subscription = null;
            _distanceMonitors.Clear();
            _distanceMonitorIndex.Clear();
            _registrations.Clear();
            _freeSlots.Clear();
            _slotHighWaterMark = 0;
            Array.Clear(_slotItems, 0, _slotItems.Length);
            Array.Clear(_tierCounts, 0, _tierCounts.Length);
            _occupiedTiers = 0;
            _spatialGrid?.Dispose();
            _spatialGrid = null;
            _authority?.Clear();
            _authority = null;
            _metrics.Reset();
            OnAnyInteractionStarted = null;
            OnAnyInteractionCompleted = null;
            _initialized = false;
        }

        /// <summary>
        /// Advances the authoritative tick used by <see cref="enableAuthorityGate"/>. Call once per
        /// server frame; leave at zero to skip the tick-window checks entirely.
        /// </summary>
        public void AdvanceAuthorityTick(int tick)
        {
            _authorityTick = tick;
        }

        public void Register(IInteractable interactable)
        {
            if (interactable == null || _spatialGrid == null) return;
            if (_registrations.ContainsKey(interactable)) return;

            Vector3 position = ToGridPosition(interactable.Position);
            uint layer = PriorityToLayer(interactable.Priority);

            int slot = AllocateSlot();
            if (!_spatialGrid.TryInsert(slot, position.x, position.y, position.z, layer, out SpatialHandle handle))
            {
                FreeSlot(slot);
                return;
            }

            _slotItems[slot] = interactable;
            _registrations[interactable] = new GridRegistration { Slot = slot, Handle = handle };
            AddTier(layer);
            UpsertAuthoritySnapshot(interactable);
        }

        public void Unregister(IInteractable interactable)
        {
            if (interactable == null) return;

            if (_registrations.TryGetValue(interactable, out GridRegistration registration))
            {
                if (_spatialGrid != null && _spatialGrid.TryGetLayer(registration.Handle, out uint layer))
                {
                    RemoveTier(layer);
                }

                _spatialGrid?.TryRemove(registration.Handle);
                _slotItems[registration.Slot] = null;
                _registrations.Remove(interactable);
                FreeSlot(registration.Slot);
            }

            if (interactable is IInteractionStableIdentity identity && identity.HasStableId)
            {
                _authority?.UnregisterTarget(identity.StableIdHash);
            }
        }

        public void UpdatePosition(IInteractable interactable)
        {
            if (interactable == null || _spatialGrid == null) return;
            if (!_registrations.TryGetValue(interactable, out GridRegistration registration)) return;

            Vector3 position = ToGridPosition(interactable.Position);
            if (!_spatialGrid.TryUpdate(registration.Handle, position.x, position.y, position.z))
            {
                return;
            }

            uint desiredLayer = PriorityToLayer(interactable.Priority);
            if (_spatialGrid.TryGetLayer(registration.Handle, out uint currentLayer) && currentLayer != desiredLayer)
            {
                _spatialGrid.TrySetLayer(registration.Handle, desiredLayer);
                RemoveTier(currentLayer);
                AddTier(desiredLayer);
            }

            UpsertAuthoritySnapshot(interactable);
        }

        /// <summary>Resolves a grid payload back to its registered instance. Returns null for stale slots.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IInteractable GetInteractable(int slot)
        {
            return (uint)slot < (uint)_slotItems.Length ? _slotItems[slot] : null;
        }

        /// <summary>
        /// Runs a radius query in grid space (world X/Z, or world X/Y in 2D mode) and writes slot
        /// identifiers into <paramref name="results"/>. No managed allocation occurs.
        /// </summary>
        public int QuerySlots(Vector3 center, float radius, int[] results, int maxResults, uint layerMask)
        {
            if (_spatialGrid == null) return 0;

            Vector3 position = ToGridPosition(center);
            return _spatialGrid.QuerySphere(position.x, position.y, position.z, radius, results, maxResults, layerMask);
        }

        public void RegisterDistanceMonitor(IInteractable target, InstigatorHandle instigator, float maxRange)
        {
            if (target == null || maxRange <= 0f) return;
            if (_distanceMonitorIndex.ContainsKey(target)) return;

            _distanceMonitorIndex[target] = _distanceMonitors.Count;
            _distanceMonitors.Add(new DistanceMonitorEntry
            {
                Target = target,
                Instigator = instigator,
                MaxRangeSqr = maxRange * maxRange
            });
        }

        public void UnregisterDistanceMonitor(IInteractable target)
        {
            if (target == null) return;
            if (!_distanceMonitorIndex.TryGetValue(target, out int index)) return;

            RemoveDistanceMonitorAtSwapBack(index);
        }

        private void LateUpdate()
        {
            for (int i = _distanceMonitors.Count - 1; i >= 0; i--)
            {
                var entry = _distanceMonitors[i];
                if (entry.Target == null || entry.Instigator == null || !entry.Target.IsInteracting)
                {
                    RemoveDistanceMonitorAtSwapBack(i);
                    continue;
                }
                if (!entry.Instigator.TryGetPosition(out Vector3 pos)) continue;
                if ((entry.Target.Position - pos).sqrMagnitude > entry.MaxRangeSqr)
                {
                    entry.Target.ForceEndInteraction(InteractionCancelReason.OutOfRange);
                    RemoveDistanceMonitorAtSwapBack(i);
                }
            }
        }

        private void RemoveDistanceMonitorAtSwapBack(int index)
        {
            IInteractable removed = _distanceMonitors[index].Target;
            _distanceMonitorIndex.Remove(removed);

            int lastIndex = _distanceMonitors.Count - 1;
            if (index < lastIndex)
            {
                DistanceMonitorEntry moved = _distanceMonitors[lastIndex];
                _distanceMonitors[index] = moved;
                if (moved.Target != null)
                {
                    _distanceMonitorIndex[moved.Target] = index;
                }
            }

            _distanceMonitors.RemoveAt(lastIndex);
        }

        public async UniTask On(InteractionCommand command)
        {
            if (command.WorldId != worldId) return;
            if (command.Target == null || !command.Target.CanInteract(command.Instigator))
            {
                _metrics.RecordDroppedCommand();
                return;
            }

            if (command.Target is Interactable interactable && !interactable.CanExecuteAction(command.ActionId))
            {
                _metrics.RecordDroppedCommand();
                return;
            }

            if (!TryAuthorize(command.Target, command.Instigator, command.ActionId))
            {
                return;
            }

            RaiseInteractionStarted(command.Target, command.Instigator);
            _metrics.RecordStarted();
            bool success = await command.Target.TryInteractAsync(command.Instigator, command.ActionId, destroyCancellationToken);
            _metrics.RecordCompleted(success, GetLastCancelReason(command.Target));
            RaiseInteractionCompleted(command.Target, command.Instigator, success);
        }

        public async UniTask ProcessInteractionAsync(IInteractable target)
        {
            await ProcessInteractionAsync(target, null);
        }

        public async UniTask ProcessInteractionAsync(IInteractable target, InstigatorHandle instigator)
        {
            if (target == null || !target.CanInteract(instigator))
            {
                _metrics.RecordDroppedCommand();
                return;
            }

            if (!TryAuthorize(target, instigator, null))
            {
                return;
            }

            RaiseInteractionStarted(target, instigator);
            _metrics.RecordStarted();
            bool success = await target.TryInteractAsync(instigator, null, destroyCancellationToken);
            _metrics.RecordCompleted(success, GetLastCancelReason(target));
            RaiseInteractionCompleted(target, instigator, success);
        }

        public InteractionMetricsSnapshot GetMetricsSnapshot()
        {
            return _metrics.GetSnapshot();
        }

        public void ResetMetrics()
        {
            _metrics.Reset();
        }

        /// <summary>
        /// Runs the authority gate for one request. Targets without a stable identity always pass:
        /// they cannot be addressed by a remote peer, so authority checks do not apply to them.
        /// </summary>
        private bool TryAuthorize(IInteractable target, InstigatorHandle instigator, string actionId)
        {
            if (!enableAuthorityGate || _authority == null) return true;
            if (target is not Interactable unityTarget || !unityTarget.HasStableId) return true;

            int requestId = _nextRequestId + 1;
            _nextRequestId = requestId <= 0 ? 1 : requestId;

            ulong instigatorStableId = instigator != null ? instigator.StableId : InteractionStableId.None;
            Vector3 instigatorPosition = default;
            instigator?.TryGetPosition(out instigatorPosition);

            var request = new InteractionRequest(
                requestId,
                instigatorStableId,
                unityTarget.StableIdHash,
                actionId,
                _authorityTick,
                worldId);

            var position = new InteractionVector3(instigatorPosition.x, instigatorPosition.y, instigatorPosition.z);
            InteractionValidationResult result = _authority.ValidateRequest(request, position, _authorityTick);
            if (result.IsAccepted)
            {
                return true;
            }

            _metrics.RecordValidation(result);
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint PriorityToLayer(int priority)
        {
            int tier = priority < 0 ? 0 : (priority >= PriorityTierCount ? PriorityTierCount - 1 : priority);
            return 1u << tier;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector3 ToGridPosition(Vector3 world)
        {
            return is2DMode ? new Vector3(world.x, world.y, 0f) : world;
        }

        private void AddTier(uint layer)
        {
            int tier = TierOf(layer);
            if (_tierCounts[tier]++ == 0)
            {
                _occupiedTiers |= layer;
            }
        }

        private void RemoveTier(uint layer)
        {
            int tier = TierOf(layer);
            if (_tierCounts[tier] > 0 && --_tierCounts[tier] == 0)
            {
                _occupiedTiers &= ~layer;
            }
        }

        private static int TierOf(uint layer)
        {
            // Layer values are single-bit by construction; reuse the same projection for the index.
            int tier = 0;
            while ((layer >>= 1) != 0)
            {
                tier++;
            }

            return tier;
        }

        private int AllocateSlot()
        {
            if (_freeSlots.Count > 0)
            {
                return _freeSlots.Pop();
            }

            int slot = _slotHighWaterMark++;
            if (slot >= _slotItems.Length)
            {
                Array.Resize(ref _slotItems, _slotItems.Length * 2);
            }

            return slot;
        }

        private void FreeSlot(int slot)
        {
            _freeSlots.Push(slot);
        }

        private void UpsertAuthoritySnapshot(IInteractable interactable)
        {
            if (_authority == null || interactable is not Interactable unityInteractable || !unityInteractable.HasStableId)
            {
                return;
            }

            _authority.TryRegisterTarget(unityInteractable.CreateAuthoritySnapshot(worldId));
        }

        private static InteractionCancelReason GetLastCancelReason(IInteractable target)
        {
            return target is Interactable interactable ? interactable.LastCancelReason : InteractionCancelReason.Manual;
        }

        private void RaiseInteractionStarted(IInteractable target, InstigatorHandle instigator)
        {
            try
            {
                OnAnyInteractionStarted?.Invoke(target, instigator);
            }
            catch (Exception ex)
            {
                Log.Error(
                    ex,
                    "[InteractionSystem] An interaction-started subscriber failed.");
            }
        }

        private void RaiseInteractionCompleted(IInteractable target, InstigatorHandle instigator, bool success)
        {
            try
            {
                OnAnyInteractionCompleted?.Invoke(target, instigator, success);
            }
            catch (Exception ex)
            {
                Log.Error(
                    ex,
                    "[InteractionSystem] An interaction-completed subscriber failed.");
            }
        }
    }
}
