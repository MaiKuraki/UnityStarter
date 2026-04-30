using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Serializable snapshot of movement state for network synchronization.
    /// No dependency on any specific networking library.
    /// </summary>
    public struct MovementSnapshot
    {
        public float3 Position;
        public float3 Velocity;
        public float3 WorldUp;
        public MovementStateType StateType;
        public float VerticalVelocity;
        public bool IsGrounded;
        public int JumpCount;
        public int Tick;
        public float Timestamp;
    }

    /// <summary>
    /// Provides movement snapshots for network synchronization.
    /// Implemented by MovementComponent to expose state without Unity dependencies.
    /// </summary>
    public interface IMovementSnapshotProvider
    {
        /// <summary>Capture current movement state as a network-safe snapshot.</summary>
        MovementSnapshot GetSnapshot();

        /// <summary>Apply a remote snapshot (server-authoritative correction).</summary>
        void ApplySnapshot(in MovementSnapshot snapshot);

        /// <summary>Reset internal state from a snapshot (used on spawn/teleport).</summary>
        void ResetFromSnapshot(in MovementSnapshot snapshot);
    }

    /// <summary>
    /// Hook for server-side movement validation.
    /// Network layer or anti-cheat systems implement this to verify movement legality.
    /// </summary>
    public interface IMovementValidator
    {
        /// <summary>
        /// Validate a proposed movement delta before applying.
        /// </summary>
        /// <param name="from">Position before movement.</param>
        /// <param name="to">Position after proposed movement.</param>
        /// <param name="deltaTime">Delta time of this tick.</param>
        /// <returns>True if the movement is valid and can be applied.</returns>
        bool ValidatePosition(float3 from, float3 to, float deltaTime);

        /// <summary>
        /// Validate a proposed state transition.
        /// </summary>
        bool ValidateStateTransition(MovementStateType from, MovementStateType to);
    }
}
