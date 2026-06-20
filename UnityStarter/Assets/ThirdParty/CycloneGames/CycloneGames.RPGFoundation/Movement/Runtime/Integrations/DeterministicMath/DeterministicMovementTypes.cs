using CycloneGames.DeterministicMath;

namespace CycloneGames.RPGFoundation.Movement.Integrations.DeterministicMath
{
    /// <summary>
    /// Selects which movement simulation backend drives an entity.
    /// </summary>
    /// <remarks>
    /// <see cref="Float"/> uses the existing <c>float3</c> state machine (Unity-side, server-authoritative
    /// replication). <see cref="FixedPoint"/> uses <see cref="DeterministicMovementSimulator"/>, whose
    /// fixed-point math produces bit-identical results across platforms and is required for lockstep and
    /// rollback netcode. The composition root reads this to pick a backend per entity or per match.
    /// </remarks>
    public enum MovementDeterminismMode : byte
    {
        Float = 0,
        FixedPoint = 1
    }

    /// <summary>
    /// Fixed-point tuning for <see cref="DeterministicMovementSimulator"/>. All values are
    /// <see cref="FPInt64"/> so the simulation never touches floating point. Build the config from the
    /// same source values on every peer; constructing it identically is what keeps the simulation
    /// deterministic.
    /// </summary>
    public readonly struct DeterministicMovementConfig
    {
        /// <summary>Maximum horizontal speed in units/second.</summary>
        public readonly FPInt64 MaxHorizontalSpeed;

        /// <summary>Horizontal acceleration while grounded, in units/second^2 (must be &gt;= 0).</summary>
        public readonly FPInt64 GroundAcceleration;

        /// <summary>Horizontal acceleration while airborne, in units/second^2 (must be &gt;= 0).</summary>
        public readonly FPInt64 AirAcceleration;

        /// <summary>Horizontal deceleration applied when grounded with no input, in units/second^2 (must be &gt;= 0).</summary>
        public readonly FPInt64 GroundDeceleration;

        /// <summary>Gravity in units/second^2. Expected negative (e.g. -20).</summary>
        public readonly FPInt64 Gravity;

        /// <summary>Instant upward speed applied on jump, in units/second. Expected positive.</summary>
        public readonly FPInt64 JumpSpeed;

        /// <summary>Terminal fall speed in units/second. Expected negative (e.g. -40).</summary>
        public readonly FPInt64 MaxFallSpeed;

        /// <summary>Height of the deterministic flat ground plane along +Y.</summary>
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

        /// <summary>
        /// Authoring convenience: converts human-readable float tuning into fixed-point once, at setup
        /// time. The conversion runs off the deterministic hot path; callers must pass identical values on
        /// every peer.
        /// </summary>
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

        /// <summary>A reasonable default humanoid locomotion profile.</summary>
        public static DeterministicMovementConfig Default =>
            FromFloats(6f, 40f, 10f, 50f, -20f, 8f, -40f, 0f);
    }

    /// <summary>
    /// Per-tick deterministic input. <see cref="MoveDirection"/> is a horizontal intent vector; its Y
    /// component is ignored and a magnitude above 1 is clamped by the simulator.
    /// </summary>
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

        /// <summary>Authoring convenience that builds a deterministic input from float values.</summary>
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

    /// <summary>
    /// Complete deterministic movement state for one entity. Value type, cheap to copy, fully captures the
    /// simulation so it can be snapshotted and restored for rollback. <see cref="HorizontalVelocity"/> keeps
    /// its Y component at zero by contract; vertical motion is tracked separately in <see cref="VerticalVelocity"/>.
    /// </summary>
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

        /// <summary>Creates a resting state at a position with zero velocity.</summary>
        public static DeterministicMovementState Create(FPVector3 position, bool isGrounded, int tick = 0)
        {
            return new DeterministicMovementState(position, FPVector3.Zero, FPInt64.Zero, isGrounded, tick);
        }
    }
}
