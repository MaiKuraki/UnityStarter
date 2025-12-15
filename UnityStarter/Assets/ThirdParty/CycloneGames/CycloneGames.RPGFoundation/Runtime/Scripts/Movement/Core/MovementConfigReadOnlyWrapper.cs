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

        public float walkSpeed => _config.walkSpeed;
        public float runSpeed => _config.runSpeed;
        public float sprintSpeed => _config.sprintSpeed;
        public float crouchSpeed => _config.crouchSpeed;
        public float jumpForce => _config.jumpForce;
        public int maxJumpCount => _config.maxJumpCount;
        public AnimationSystemType animationSystem => _config.animationSystem;
        public AnimancerParameterMode animancerParameterMode => _config.animancerParameterMode;
        public string movementSpeedParameter => _config.movementSpeedParameter;
        public string isGroundedParameter => _config.isGroundedParameter;
        public string jumpTrigger => _config.jumpTrigger;
    }
}