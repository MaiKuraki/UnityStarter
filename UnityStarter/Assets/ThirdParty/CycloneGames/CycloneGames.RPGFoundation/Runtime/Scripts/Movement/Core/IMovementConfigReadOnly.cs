namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Read-only interface for accessing movement configuration values.
    /// Prevents external code from modifying the shared ScriptableObject asset.
    /// </summary>
    public interface IMovementConfigReadOnly
    {
        float walkSpeed { get; }
        float runSpeed { get; }
        float sprintSpeed { get; }
        float crouchSpeed { get; }
        float jumpForce { get; }
        int maxJumpCount { get; }
        AnimationSystemType animationSystem { get; }
        AnimancerParameterMode animancerParameterMode { get; }
        string movementSpeedParameter { get; }
        string isGroundedParameter { get; }
        string jumpTrigger { get; }
    }
}