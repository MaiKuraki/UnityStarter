using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Unified interface for different pathfinding providers.
    /// Allows MovementComponent to work with any navigation system.
    /// </summary>
    public interface IPathfindingProvider
    {
        /// <summary>
        /// Set a navigation destination.
        /// </summary>
        /// <param name="destination">World position to navigate to.</param>
        /// <returns>True if path calculation started successfully.</returns>
        bool SetDestination(Vector3 destination);

        /// <summary>
        /// Stop current navigation and clear path.
        /// </summary>
        void StopNavigation();

        /// <summary>
        /// Returns true if actively navigating to a destination.
        /// </summary>
        bool IsNavigating { get; }

        /// <summary>
        /// Returns true if destination has been reached.
        /// </summary>
        bool HasReachedDestination { get; }

        /// <summary>
        /// Current navigation destination.
        /// </summary>
        Vector3 CurrentDestination { get; }

        /// <summary>
        /// Current movement direction towards next waypoint (normalized).
        /// </summary>
        Vector3 CurrentDirection { get; }

        /// <summary>
        /// Distance remaining to destination.
        /// </summary>
        float RemainingDistance { get; }
    }
}
