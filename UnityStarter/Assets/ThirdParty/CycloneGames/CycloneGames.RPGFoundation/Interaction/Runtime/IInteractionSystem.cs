using System;
using UnityEngine;
using Cysharp.Threading.Tasks;
using CycloneGames.RPGFoundation.Interaction.Core;
using CycloneGames.Spatial.Core;

namespace CycloneGames.RPGFoundation.Interaction.Runtime
{
    /// <summary>
    /// Central management hub for the interaction subsystem.
    /// Handles spatial registration, command routing, and interaction lifecycle processing.
    /// One instance per scene.
    /// </summary>
    public interface IInteractionSystem : IDisposable
    {
        /// <summary>
        /// Spatial index payload is a slot identifier, never an <see cref="IInteractable"/> reference.
        /// Resolve slots with <see cref="GetInteractable"/> after a query returns.
        /// </summary>
        SpatialHashGrid<int> SpatialGrid { get; }

        /// <summary>Resolves a slot identifier returned by a grid query. Returns null for stale slots.</summary>
        IInteractable GetInteractable(int slot);

        /// <summary>Bitmask of priority tiers that currently hold at least one registered target.</summary>
        uint OccupiedTiers { get; }

        /// <summary>Whether the system is operating in 2D mode (positions project onto X/Y with Z pinned to zero).</summary>
        bool Is2DMode { get; }

        /// <summary>
        /// Local interaction world scope. Commands from detectors are ignored by systems with a different world ID.
        /// Use unique values for split-screen, additive scenes, prediction worlds, or server-side simulations.
        /// </summary>
        int WorldId { get; }

        /// <summary>Initialize the system with default settings.</summary>
        void Initialize();

        /// <summary>Initialize the system with explicit 2D/3D mode and spatial grid cell size.</summary>
        /// <param name="is2DMode">True for 2D games (X/Y coordinates), false for 3D (X/Z).</param>
        /// <param name="cellSize">Spatial hash grid cell size. Larger values reduce cell count; smaller values increase query precision.</param>
        void Initialize(bool is2DMode, float cellSize = 10f);

        /// <summary>Register an interactable with the spatial grid. Called automatically by Interactable.OnEnable.</summary>
        void Register(IInteractable interactable);

        /// <summary>Remove an interactable from the spatial grid. Called automatically by Interactable.OnDisable.</summary>
        void Unregister(IInteractable interactable);

        /// <summary>Notify the spatial grid that an interactable's position or priority has changed.</summary>
        void UpdatePosition(IInteractable interactable);

        /// <summary>
        /// Runs a radius query and writes slot identifiers into <paramref name="results"/> without allocating.
        /// In 2D mode the position projects onto X/Y with Z pinned to zero, matching the registration projection.
        /// </summary>
        int QuerySlots(Vector3 center, float radius, int[] results, int maxResults, uint layerMask);

        /// <summary>Advances the authoritative tick used by the authority gate.</summary>
        void AdvanceAuthorityTick(int tick);

        /// <summary>Process an interaction command asynchronously, executing the target's interaction lifecycle.</summary>
        UniTask ProcessInteractionAsync(IInteractable target);

        /// <summary>Process an interaction command with a known instigator.</summary>
        /// <param name="target">The interactable to interact with.</param>
        /// <param name="instigator">The instigator initiating the interaction (e.g., player).</param>
        UniTask ProcessInteractionAsync(IInteractable target, InstigatorHandle instigator);

        /// <summary>
        /// Register an interactable for centralized distance monitoring.
        /// The system checks each frame whether the instigator has moved beyond maxRange,
        /// automatically cancelling the interaction with <see cref="InteractionCancelReason.OutOfRange"/>.
        /// Replaces per-interactable UniTask polling with a single batched loop.
        /// </summary>
        void RegisterDistanceMonitor(IInteractable target, InstigatorHandle instigator, float maxRange);

        /// <summary>Remove an interactable from distance monitoring.</summary>
        void UnregisterDistanceMonitor(IInteractable target);

        /// <summary>Fired when any interaction begins globally. Parameters: (target, instigator).</summary>
        event Action<IInteractable, InstigatorHandle> OnAnyInteractionStarted;

        /// <summary>Fired when any interaction ends globally. Parameters: (target, instigator, success).</summary>
        event Action<IInteractable, InstigatorHandle, bool> OnAnyInteractionCompleted;
    }
}
