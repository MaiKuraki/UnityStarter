using CycloneGames.DeterministicMath;

namespace CycloneGames.RPGFoundation.Movement.Integrations.DeterministicMath
{
    public enum MovementDeterminismMode : byte
    {
        Float = 0,
        FixedPoint = 1
    }

    public readonly struct DeterministicMovementConfig
    {
        public readonly FPInt64 MaxHorizontalSpeed;

        public readonly FPInt64 GroundAcceleration;

        public readonly FPInt64 AirAcceleration;

        public readonly FPInt64 GroundDeceleration;

        public readonly FPInt64 Gravity;

        public readonly FPInt64 JumpSpeed;

        public readonly FPInt64 MaxFallSpeed;

        public readonly FPInt64 GroundHeight;

        public DeterministicMovementConfig(
            FPInt64 maxHorizontalSpeed,
            FPInt64 groundAcceleration,
            FPInt64 airAcceleration,
            FPInt64 groundDeceleration,
            FPInt64 gravity,
            FPInt64 jumpSpeed,
            FPInt64 maxFallSpeed,
            FPInt64 groundHeight)
        {
            MaxHorizontalSpeed = maxHorizontalSpeed;
            GroundAcceleration = groundAcceleration;
            AirAcceleration = airAcceleration;
            GroundDeceleration = groundDeceleration;
            Gravity = gravity;
            JumpSpeed = jumpSpeed;
            MaxFallSpeed = maxFallSpeed;
            GroundHeight = groundHeight;
        }

        public static DeterministicMovementConfig FromFloats(
            float maxHorizontalSpeed,
            float groundAcceleration,
            float airAcceleration,
            float groundDeceleration,
            float gravity,
            float jumpSpeed,
            float maxFallSpeed,
            float groundHeight = 0f)
        {
            return new DeterministicMovementConfig(
                FPInt64.FromFloat(maxHorizontalSpeed),
                FPInt64.FromFloat(groundAcceleration),
                FPInt64.FromFloat(airAcceleration),
                FPInt64.FromFloat(groundDeceleration),
                FPInt64.FromFloat(gravity),
                FPInt64.FromFloat(jumpSpeed),
                FPInt64.FromFloat(maxFallSpeed),
                FPInt64.FromFloat(groundHeight));
        }

        public static DeterministicMovementConfig Default =>
            FromFloats(6f, 40f, 10f, 50f, -20f, 8f, -40f, 0f);
    }

    public readonly struct DeterministicMovementInput
    {
        public readonly FPVector3 MoveDirection;
        public readonly FPInt64 DeltaTime;
        public readonly bool JumpRequested;

        public DeterministicMovementInput(FPVector3 moveDirection, FPInt64 deltaTime, bool jumpRequested)
        {
            MoveDirection = moveDirection;
            DeltaTime = deltaTime;
            JumpRequested = jumpRequested;
        }

        public static DeterministicMovementInput FromFloats(
            float moveX,
            float moveZ,
            float deltaTime,
            bool jumpRequested)
        {
            return new DeterministicMovementInput(
                new FPVector3(FPInt64.FromFloat(moveX), FPInt64.Zero, FPInt64.FromFloat(moveZ)),
                FPInt64.FromFloat(deltaTime),
                jumpRequested);
        }
    }

    public readonly struct DeterministicMovementState
    {
        public readonly FPVector3 Position;
        public readonly FPVector3 HorizontalVelocity;
        public readonly FPInt64 VerticalVelocity;
        public readonly bool IsGrounded;
        public readonly int Tick;

        public DeterministicMovementState(
            FPVector3 position,
            FPVector3 horizontalVelocity,
            FPInt64 verticalVelocity,
            bool isGrounded,
            int tick)
        {
            Position = position;
            HorizontalVelocity = horizontalVelocity;
            VerticalVelocity = verticalVelocity;
            IsGrounded = isGrounded;
            Tick = tick;
        }

        public static DeterministicMovementState Create(FPVector3 position, bool isGrounded, int tick = 0)
        {
            return new DeterministicMovementState(position, FPVector3.Zero, FPInt64.Zero, isGrounded, tick);
        }
    }
}
