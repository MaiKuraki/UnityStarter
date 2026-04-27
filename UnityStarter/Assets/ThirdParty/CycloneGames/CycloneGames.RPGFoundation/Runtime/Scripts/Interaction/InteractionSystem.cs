using System;
using System.Collections.Generic;
using VitalRouter;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    [Routes]
    public partial class InteractionSystem : MonoBehaviour, IInteractionSystem
    {
        [Header("Spatial Grid")]
        [SerializeField] private bool is2DMode;
        [SerializeField] private float cellSize = 10f;

        private IDisposable _subscription;
        private SpatialHashGrid _spatialGrid;
        private bool _initialized;
        private readonly List<DistanceMonitorEntry> _distanceMonitors = new(16);

        private struct DistanceMonitorEntry
        {
            public IInteractable Target;
            public InstigatorHandle Instigator;
            public float MaxRangeSqr;
        }

        private static InteractionSystem s_instance;

        /// <summary>Global singleton instance. Null if no InteractionSystem exists in the scene.</summary>
        public static InteractionSystem Instance => s_instance;

        public SpatialHashGrid SpatialGrid => _spatialGrid;
        public bool Is2DMode => is2DMode;

        public event Action<IInteractable, InstigatorHandle> OnAnyInteractionStarted;
        public event Action<IInteractable, InstigatorHandle, bool> OnAnyInteractionCompleted;

        private void Awake()
        {
            s_instance = this;
            Initialize();
        }

        private void Start()
        {
            _subscription = this.MapTo(Router.Default);
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            Dispose();
        }

        public void Initialize() => Initialize(is2DMode, cellSize);

        public void Initialize(bool is2DMode, float cellSize = 10f)
        {
            if (_initialized) return;

            this.is2DMode = is2DMode;
            this.cellSize = cellSize;
            _spatialGrid = new SpatialHashGrid(cellSize);
            EffectPoolSystem.Initialize();
            _initialized = true;
        }

        public void Dispose()
        {
            if (!_initialized) return;
            _subscription?.Dispose();
            _subscription = null;
            _distanceMonitors.Clear();
            _spatialGrid?.Dispose();
            _spatialGrid = null;
            EffectPoolSystem.Dispose();
            OnAnyInteractionStarted = null;
            OnAnyInteractionCompleted = null;
            _initialized = false;
        }

        public void Register(IInteractable interactable)
        {
            _spatialGrid?.Insert(interactable, is2DMode);
        }

        public void Unregister(IInteractable interactable)
        {
            _spatialGrid?.Remove(interactable);
        }

        public void UpdatePosition(IInteractable interactable)
        {
            _spatialGrid?.UpdatePosition(interactable, is2DMode);
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
                    _distanceMonitors.RemoveAt(i);
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
                    _distanceMonitors.RemoveAt(i);
                    continue;
                }
                if (!entry.Instigator.TryGetPosition(out Vector3 pos)) continue;
                if ((entry.Target.Position - pos).sqrMagnitude > entry.MaxRangeSqr)
                {
                    entry.Target.ForceEndInteraction(InteractionCancelReason.OutOfRange);
                    _distanceMonitors.RemoveAt(i);
                }
            }
        }

        public async UniTask On(InteractionCommand command)
        {
            if (command.Target == null || !command.Target.IsInteractable) return;
            OnAnyInteractionStarted?.Invoke(command.Target, command.Instigator);
            bool success = await command.Target.TryInteractAsync(command.Instigator, command.ActionId, destroyCancellationToken);
            OnAnyInteractionCompleted?.Invoke(command.Target, command.Instigator, success);
        }

        public async UniTask ProcessInteractionAsync(IInteractable target)
        {
            await ProcessInteractionAsync(target, null);
        }

        public async UniTask ProcessInteractionAsync(IInteractable target, InstigatorHandle instigator)
        {
            if (target == null || !target.IsInteractable) return;
            OnAnyInteractionStarted?.Invoke(target, instigator);
            bool success = await target.TryInteractAsync(instigator, null, destroyCancellationToken);
            OnAnyInteractionCompleted?.Invoke(target, instigator, success);
        }
    }
}