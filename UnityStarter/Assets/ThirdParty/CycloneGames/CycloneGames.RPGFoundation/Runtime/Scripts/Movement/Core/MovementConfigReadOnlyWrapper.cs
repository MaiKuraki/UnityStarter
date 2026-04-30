namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Read-only wrapper for MovementConfigBase that prevents external modification.
    /// </summary>
    public class MovementConfigReadOnlyWrapper : IMovementConfigReadOnly
    {
        private readonly MovementConfigBase _config;

        public MovementConfigReadOnlyWrapper(MovementConfigBase config)
        {
            _config = config;
        }

        public float walkSpeed => _config.WalkSpeed;
        public float runSpeed => _config.RunSpeed;
        public float sprintSpeed => _config.SprintSpeed;
        public float crouchSpeed => _config.CrouchSpeed;
        public float jumpForce => _config.JumpForce;
        public int maxJumpCount => _config.MaxJumpCount;
        public AnimationSystemType animationSystem => _config.AnimationSystem;
        public AnimancerParameterMode animancerParameterMode => _config.AnimancerParameterMode;
        public string movementSpeedParameter => _config.MovementSpeedParameter;
        public string isGroundedParameter => _config.IsGroundedParameter;
        public string jumpTrigger => _config.JumpTrigger;
    }
}