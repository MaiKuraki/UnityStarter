using System;
using System.Collections.Generic;
using VitalRouter;
using Cysharp.Threading.Tasks;
using UnityEngine;
using CycloneGames.Logging;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Runtime
{
    [Routes]
    [DisallowMultipleComponent]
    public partial class InteractionSystem : MonoBehaviour, IInteractionSystem
    {
        private static readonly LogChannel Log = RpgInteractionLog.Channel;

        [Header("Spatial Grid")]
        [Tooltip("Local interaction world scope. Use unique values for split-screen, additive scenes, prediction worlds, or server-side simulations.")]
        [SerializeField] private int worldId;
        [SerializeField] private bool is2DMode;
        [SerializeField] private float cellSize = 10f;

        private IDisposable _subscription;
        private SpatialHashGrid _spatialGrid;
        private InteractionAuthorityService _authority;
        private readonly InteractionMetrics _metrics = new InteractionMetrics();
        private bool _initialized;
        private readonly List<DistanceMonitorEntry> _distanceMonitors = new(16);

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

        public SpatialHashGrid SpatialGrid => _spatialGrid;
        public InteractionAuthorityService Authority => _authority;
        public InteractionMetrics Metrics => _metrics;
        public bool Is2DMode => is2DMode;
        public int WorldId => worldId;

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
            _spatialGrid = new SpatialHashGrid(this.cellSize);
            _authority = new InteractionAuthorityService(new InteractionAuthorityOptions(worldId, requireStableIds: false));
            EffectPoolSystem.Initialize();
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
            _spatialGrid?.Dispose();
            _spatialGrid = null;
            _authority?.Clear();
            _authority = null;
            _metrics.Reset();
            EffectPoolSystem.Dispose();
            OnAnyInteractionStarted = null;
            OnAnyInteractionCompleted = null;
            _initialized = false;
        }

        public void Register(IInteractable interactable)
        {
            if (interactable == null) return;
            _spatialGrid?.Insert(interactable, is2DMode);
            UpsertAuthoritySnapshot(interactable);
        }

        public void Unregister(IInteractable interactable)
        {
            if (interactable == null) return;
            _spatialGrid?.Remove(interactable);
            if (interactable is IInteractionStableIdentity identity && identity.HasStableId)
            {
                _authority?.UnregisterTarget(identity.StableIdHash);
            }
        }

        public void UpdatePosition(IInteractable interactable)
        {
            if (interactable == null) return;
            _spatialGrid?.UpdatePosition(interactable, is2DMode);
            UpsertAuthoritySnapshot(interactable);
        }

        public void RegisterDistanceMonitor(IInteractable target, InstigatorHandle instigator, float maxRange)
        {
            if (target == null || maxRange <= 0f) return;
            for (int i = 0; i < _distanceMonitors.Count; i++)
                if (_distanceMonitors[i].Target == target) return;
            _distanceMonitors.Add(new DistanceMonitorEntry
            {
                Target = target,
                Instigator = instigator,
                MaxRangeSqr = maxRange * maxRange
            });
        }

        public void UnregisterDistanceMonitor(IInteractable target)
        {
            for (int i = _distanceMonitors.Count - 1; i >= 0; i--)
            {
                if (_distanceMonitors[i].Target == target)
                {
                    RemoveDistanceMonitorAtSwapBack(i);
                    return;
                }
            }
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
            int lastIndex = _distanceMonitors.Count - 1;
            if (index < lastIndex)
                _distanceMonitors[index] = _distanceMonitors[lastIndex];
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
