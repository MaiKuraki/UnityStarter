namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Read-only interface for accessing 3D movement configuration values.
    /// Extends base config with 3D-specific properties.
    /// </summary>
    public interface IMovementConfig3DReadOnly : IMovementConfigReadOnly
    {
        float rollDistance { get; }
        float rollDuration { get; }
        float climbSpeed { get; }
        float swimSpeed { get; }
        float flySpeed { get; }
        float gravity { get; }
        float airControlMultiplier { get; }
        float groundedCheckDistance { get; }
        UnityEngine.LayerMask groundLayer { get; }
        float slopeLimit { get; }
        float stepHeight { get; }
        float rotationSpeed { get; }
        string rollTrigger { get; }
    }
}