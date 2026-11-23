namespace CycloneGames.RPGFoundation.Movement.Core
{
    public interface IMovementConfigReadOnly
    {
        float walkSpeed { get; }
        float runSpeed { get; }
        float sprintSpeed { get; }
        float crouchSpeed { get; }
        float jumpForce { get; }
        int maxJumpCount { get; }
    }
}