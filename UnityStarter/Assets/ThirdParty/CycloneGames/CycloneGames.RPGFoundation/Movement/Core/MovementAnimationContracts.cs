using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.RPGFoundation.Movement.Core
{
    [Flags]
    public enum MovementAnimationFlags : uint
    {
        None = 0,
        Grounded = 1u << 0,
        Sprinting = 1u << 1,
        Crouching = 1u << 2,
        Climbing = 1u << 3,
        WallSliding = 1u << 4,
        Swimming = 1u << 5,
        Flying = 1u << 6,
        Rolling = 1u << 7,
        RootMotionDriven = 1u << 8,
        WallJumping = 1u << 9,
    }

    [Flags]
    public enum MovementStepEvent : uint
    {
        None = 0,
        JumpImpulse = 1u << 0,
        AirJumpImpulse = 1u << 1,
        RollStarted = 1u << 2,
        Landed = 1u << 3,
        ClimbEntered = 1u << 4,
        ClimbExited = 1u << 5,
        WallSlideEntered = 1u << 6,
        WallSlideExited = 1u << 7,
        SwimEntered = 1u << 8,
        SwimExited = 1u << 9,
        FlyEntered = 1u << 10,
        FlyExited = 1u << 11,
    }

    public enum MovementAnimationActionKind : byte
    {
        None = 0,
        Jump = 1,
        AirJump = 2,
        Roll = 3,
        Land = 4,
        ClimbEnter = 5,
        ClimbExit = 6,
        WallSlideEnter = 7,
        WallSlideExit = 8,
        SwimEnter = 9,
        SwimExit = 10,
        FlyEnter = 11,
        FlyExit = 12,
    }

    public readonly struct MovementAnimationState : IEquatable<MovementAnimationState>
    {
        public readonly uint Sequence;

        public readonly float DeltaTime;

        public readonly MovementStateType State;
        public readonly MovementAnimationFlags Flags;

        public readonly MovementVector3 Velocity;

        public readonly MovementVector3 Input;

        public readonly MovementVector3 WorldUp;

        public readonly MovementVector3 GroundNormal;

        public readonly MovementVector3 Facing;

        public readonly float PlanarSpeed;

        public readonly float VerticalSpeed;

        public readonly uint SemanticStateId;

        public readonly uint ExtensionFlags;
        public readonly float Extension0;
        public readonly float Extension1;
        public readonly float Extension2;
        public readonly float Extension3;

        public MovementAnimationState(
            uint sequence,
            float deltaTime,
            MovementStateType state,
            MovementAnimationFlags flags,
            MovementVector3 velocity,
            MovementVector3 input,
            MovementVector3 worldUp,
            MovementVector3 groundNormal,
            MovementVector3 facing)
            : this(sequence, deltaTime, state, flags, velocity, input, worldUp,
                   groundNormal, facing, 0f, 0f, 0u, 0u, 0f, 0f, 0f, 0f, true)
        {
        }

        public MovementAnimationState WithExtensions(
            uint extensionFlags, float e0, float e1, float e2, float e3)
        {
            return new MovementAnimationState(
                Sequence, DeltaTime, State, Flags, Velocity, Input, WorldUp, GroundNormal, Facing,
                PlanarSpeed, VerticalSpeed, SemanticStateId, extensionFlags,
                e0, e1, e2, e3, false);
        }

        public MovementAnimationState WithSemanticState(uint semanticStateId)
        {
            return new MovementAnimationState(
                Sequence, DeltaTime, State, Flags, Velocity, Input, WorldUp, GroundNormal, Facing,
                PlanarSpeed, VerticalSpeed, semanticStateId, ExtensionFlags,
                Extension0, Extension1, Extension2, Extension3, false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasExtension(uint bit)
        {
            return (ExtensionFlags & bit) != 0;
        }

        private MovementAnimationState(
            uint sequence,
            float deltaTime,
            MovementStateType state,
            MovementAnimationFlags flags,
            MovementVector3 velocity,
            MovementVector3 input,
            MovementVector3 worldUp,
            MovementVector3 groundNormal,
            MovementVector3 facing,
            float planarSpeed,
            float verticalSpeed,
            uint semanticStateId,
            uint extensionFlags,
            float e0,
            float e1,
            float e2,
            float e3,
            bool clean)
        {
            if (clean)
            {
                Sequence = sequence;
                DeltaTime = FiniteNonNegative(deltaTime);
                State = state;
                Flags = flags;
                Velocity = Finite(velocity);
                Input = Finite(input);

                MovementVector3 up = Finite(worldUp, MovementVector3.Up);
                if (up.SqrMagnitude < 1E-8f) up = MovementVector3.Up;
                WorldUp = up.Normalized;

                MovementVector3 normal = Finite(groundNormal, WorldUp);
                if (normal.SqrMagnitude < 1E-8f) normal = WorldUp;
                GroundNormal = normal.Normalized;

                Facing = Finite(facing);

                float vertical = MovementVector3.Dot(Velocity, WorldUp);
                VerticalSpeed = FiniteScalar(vertical);
                MovementVector3 planar = Velocity - WorldUp * vertical;
                PlanarSpeed = FiniteScalar(planar.Length);
            }
            else
            {
                Sequence = sequence;
                DeltaTime = deltaTime;
                State = state;
                Flags = flags;
                Velocity = velocity;
                Input = input;
                WorldUp = worldUp;
                GroundNormal = groundNormal;
                Facing = facing;
                PlanarSpeed = planarSpeed;
                VerticalSpeed = verticalSpeed;
            }

            SemanticStateId = semanticStateId;
            ExtensionFlags = extensionFlags;
            Extension0 = FiniteScalar(e0);
            Extension1 = FiniteScalar(e1);
            Extension2 = FiniteScalar(e2);
            Extension3 = FiniteScalar(e3);
        }

        public bool IsGrounded { get { return Has(MovementAnimationFlags.Grounded); } }
        public bool IsSprinting { get { return Has(MovementAnimationFlags.Sprinting); } }
        public bool IsCrouching { get { return Has(MovementAnimationFlags.Crouching); } }
        public bool IsClimbing { get { return Has(MovementAnimationFlags.Climbing); } }
        public bool IsWallSliding { get { return Has(MovementAnimationFlags.WallSliding); } }
        public bool IsSwimming { get { return Has(MovementAnimationFlags.Swimming); } }
        public bool IsFlying { get { return Has(MovementAnimationFlags.Flying); } }
        public bool IsRolling { get { return Has(MovementAnimationFlags.Rolling); } }
        public bool IsRootMotionDriven { get { return Has(MovementAnimationFlags.RootMotionDriven); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Has(MovementAnimationFlags flag)
        {
            return (Flags & flag) != 0;
        }

        public bool IsValid
        {
            get
            {
                return DeltaTime >= 0f
                    && Velocity.IsFinite
                    && Input.IsFinite
                    && WorldUp.IsFinite
                    && GroundNormal.IsFinite
                    && Facing.IsFinite
                    && PlanarSpeed >= 0f
                    && !float.IsNaN(VerticalSpeed)
                    && !float.IsInfinity(VerticalSpeed)
                    && !float.IsNaN(Extension0) && !float.IsInfinity(Extension0)
                    && !float.IsNaN(Extension1) && !float.IsInfinity(Extension1)
                    && !float.IsNaN(Extension2) && !float.IsInfinity(Extension2)
                    && !float.IsNaN(Extension3) && !float.IsInfinity(Extension3);
            }
        }

        public bool Equals(MovementAnimationState other)
        {
            return Sequence == other.Sequence
                && DeltaTime.Equals(other.DeltaTime)
                && State == other.State
                && Flags == other.Flags
                && Velocity.Equals(other.Velocity)
                && Input.Equals(other.Input)
                && WorldUp.Equals(other.WorldUp)
                && GroundNormal.Equals(other.GroundNormal)
                && Facing.Equals(other.Facing)
                && PlanarSpeed.Equals(other.PlanarSpeed)
                && VerticalSpeed.Equals(other.VerticalSpeed)
                && SemanticStateId == other.SemanticStateId
                && ExtensionFlags == other.ExtensionFlags
                && Extension0.Equals(other.Extension0)
                && Extension1.Equals(other.Extension1)
                && Extension2.Equals(other.Extension2)
                && Extension3.Equals(other.Extension3);
        }

        public override bool Equals(object obj)
        {
            return obj is MovementAnimationState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Sequence;
                hash = (hash * 397) ^ DeltaTime.GetHashCode();
                hash = (hash * 397) ^ (int)State;
                hash = (hash * 397) ^ (int)Flags;
                hash = (hash * 397) ^ Velocity.GetHashCode();
                hash = (hash * 397) ^ Input.GetHashCode();
                hash = (hash * 397) ^ WorldUp.GetHashCode();
                hash = (hash * 397) ^ GroundNormal.GetHashCode();
                hash = (hash * 397) ^ Facing.GetHashCode();
                hash = (hash * 397) ^ PlanarSpeed.GetHashCode();
                hash = (hash * 397) ^ VerticalSpeed.GetHashCode();
                hash = (hash * 397) ^ (int)SemanticStateId;
                hash = (hash * 397) ^ (int)ExtensionFlags;
                hash = (hash * 397) ^ Extension0.GetHashCode();
                hash = (hash * 397) ^ Extension1.GetHashCode();
                hash = (hash * 397) ^ Extension2.GetHashCode();
                hash = (hash * 397) ^ Extension3.GetHashCode();
                return hash;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float FiniteScalar(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float FiniteNonNegative(float value)
        {
            float result = FiniteScalar(value);
            return result < 0f ? 0f : result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static MovementVector3 Finite(MovementVector3 value, MovementVector3 fallback)
        {
            return value.IsFinite ? value : fallback;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static MovementVector3 Finite(MovementVector3 value)
        {
            return value.IsFinite ? value : MovementVector3.Zero;
        }
    }

    public readonly struct MovementAnimationAction : IEquatable<MovementAnimationAction>
    {
        public readonly uint Sequence;
        public readonly MovementAnimationActionKind Kind;

        public readonly float Magnitude;

        public readonly byte Ordinal;

        public MovementAnimationAction(uint sequence, MovementAnimationActionKind kind, float magnitude, byte ordinal)
        {
            Sequence = sequence;
            Kind = kind;
            Magnitude = float.IsNaN(magnitude) || float.IsInfinity(magnitude) ? 0f : magnitude;
            Ordinal = ordinal;
        }

        public bool IsValid
        {
            get { return Kind != MovementAnimationActionKind.None; }
        }

        public bool Equals(MovementAnimationAction other)
        {
            return Sequence == other.Sequence
                && Kind == other.Kind
                && Magnitude.Equals(other.Magnitude)
                && Ordinal == other.Ordinal;
        }

        public override bool Equals(object obj)
        {
            return obj is MovementAnimationAction other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Sequence;
                hash = (hash * 397) ^ (int)Kind;
                hash = (hash * 397) ^ Magnitude.GetHashCode();
                hash = (hash * 397) ^ Ordinal;
                return hash;
            }
        }
    }

    public static class MovementAnimationActionWriter
    {
        private const int MaximumActionsPerStepValue = 12;

        public static int MaximumActionsPerStep { get { return MaximumActionsPerStepValue; } }

        private const uint DefinedMask = 0x00000FFFu;

        public static int Count(MovementStepEvent events)
        {
            uint value = (uint)events & DefinedMask;
            value = value - ((value >> 1) & 0x55555555u);
            value = (value & 0x33333333u) + ((value >> 2) & 0x33333333u);
            value = (value + (value >> 4)) & 0x0F0F0F0Fu;
            return (int)((value * 0x01010101u) >> 24);
        }

        public static int Write(MovementStepEvent events, uint sequence, Span<MovementAnimationAction> destination)
        {
            int count = 0;
            Append(MovementStepEvent.JumpImpulse, MovementAnimationActionKind.Jump, events, sequence, destination, ref count);
            Append(MovementStepEvent.AirJumpImpulse, MovementAnimationActionKind.AirJump, events, sequence, destination, ref count);
            Append(MovementStepEvent.RollStarted, MovementAnimationActionKind.Roll, events, sequence, destination, ref count);
            Append(MovementStepEvent.Landed, MovementAnimationActionKind.Land, events, sequence, destination, ref count);
            Append(MovementStepEvent.ClimbEntered, MovementAnimationActionKind.ClimbEnter, events, sequence, destination, ref count);
            Append(MovementStepEvent.ClimbExited, MovementAnimationActionKind.ClimbExit, events, sequence, destination, ref count);
            Append(MovementStepEvent.WallSlideEntered, MovementAnimationActionKind.WallSlideEnter, events, sequence, destination, ref count);
            Append(MovementStepEvent.WallSlideExited, MovementAnimationActionKind.WallSlideExit, events, sequence, destination, ref count);
            Append(MovementStepEvent.SwimEntered, MovementAnimationActionKind.SwimEnter, events, sequence, destination, ref count);
            Append(MovementStepEvent.SwimExited, MovementAnimationActionKind.SwimExit, events, sequence, destination, ref count);
            Append(MovementStepEvent.FlyEntered, MovementAnimationActionKind.FlyEnter, events, sequence, destination, ref count);
            Append(MovementStepEvent.FlyExited, MovementAnimationActionKind.FlyExit, events, sequence, destination, ref count);
            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Append(
            MovementStepEvent bit,
            MovementAnimationActionKind kind,
            MovementStepEvent events,
            uint sequence,
            Span<MovementAnimationAction> destination,
            ref int count)
        {
            if ((events & bit) == 0) return;
            if (count >= destination.Length) return;
            destination[count] = new MovementAnimationAction(sequence, kind, 0f, (byte)count);
            count++;
        }
    }

    // Displacement and elapsed capture time are handed to Movement as one
    // value; the accumulator resamples this window onto fixed physics steps.
    public readonly struct MovementRootMotionDelta : IEquatable<MovementRootMotionDelta>
    {
        public readonly uint Sequence;

        public readonly MovementVector3 Position;

        public readonly float YawRadians;

        public readonly float DeltaTime;

        public readonly MovementVector3 WorldUp;

        public MovementRootMotionDelta(
            uint sequence,
            MovementVector3 position,
            float yawRadians,
            float deltaTime,
            MovementVector3 worldUp)
        {
            Sequence = sequence;
            Position = position.IsFinite ? position : MovementVector3.Zero;
            YawRadians = float.IsNaN(yawRadians) || float.IsInfinity(yawRadians) ? 0f : yawRadians;
            DeltaTime = float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= 0f ? 0f : deltaTime;
            MovementVector3 up = worldUp.IsFinite ? worldUp : MovementVector3.Up;
            WorldUp = up.SqrMagnitude < 1E-8f ? MovementVector3.Up : up.Normalized;
        }

        public bool IsValid
        {
            get { return DeltaTime > 0f && Position.IsFinite && WorldUp.IsFinite; }
        }

        public MovementVector3 LinearVelocity
        {
            get { return DeltaTime > 0f ? Position * (1f / DeltaTime) : MovementVector3.Zero; }
        }

        public float YawVelocity
        {
            get { return DeltaTime > 0f ? YawRadians / DeltaTime : 0f; }
        }

        public bool Equals(MovementRootMotionDelta other)
        {
            return Sequence == other.Sequence
                && Position.Equals(other.Position)
                && YawRadians.Equals(other.YawRadians)
                && DeltaTime.Equals(other.DeltaTime)
                && WorldUp.Equals(other.WorldUp);
        }

        public override bool Equals(object obj)
        {
            return obj is MovementRootMotionDelta other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Sequence;
                hash = (hash * 397) ^ Position.GetHashCode();
                hash = (hash * 397) ^ YawRadians.GetHashCode();
                hash = (hash * 397) ^ DeltaTime.GetHashCode();
                hash = (hash * 397) ^ WorldUp.GetHashCode();
                return hash;
            }
        }
    }

    public interface IMovementLocomotionSink
    {
        void Apply(in MovementAnimationState state);
    }

    public interface IMovementActionSink
    {
        void Play(in MovementAnimationAction action);
    }

    public interface IRootMotionSource
    {
        bool TryConsume(out MovementRootMotionDelta delta);
    }

    public sealed class MovementAnimationFrameBuilder
    {
        private uint _sequence;
        private uint _wrapCount;
        private bool _hasBaseline;
        private bool _previousGrounded;
        private bool _previousClimbing;
        private bool _previousWallSliding;
        private bool _previousSwimming;
        private bool _previousFlying;

        public uint CurrentSequence { get { return _sequence; } }

        public uint WrapCount { get { return _wrapCount; } }

        public bool HasBaseline { get { return _hasBaseline; } }

        // The sequence wraps and becomes observable through WrapCount.
        // Idempotence compares equality, so a wrap never stalls a sink.
        public uint AdvanceSequence()
        {
            if (_sequence < uint.MaxValue)
            {
                _sequence++;
                return _sequence;
            }
            _sequence = 1u;
            if (_wrapCount < uint.MaxValue) _wrapCount++;
            return _sequence;
        }

        public void ResetSequence(uint startValue)
        {
            _sequence = startValue;
            _hasBaseline = false;
        }

        public void ResetBaseline()
        {
            _hasBaseline = false;
        }

        public MovementStepEvent DeriveEdges(
            bool grounded,
            bool climbing,
            bool wallSliding,
            bool swimming,
            bool flying)
        {
            MovementStepEvent events = MovementStepEvent.None;
            if (_hasBaseline)
            {
                if (grounded && !_previousGrounded) events |= MovementStepEvent.Landed;
                if (climbing != _previousClimbing)
                    events |= climbing ? MovementStepEvent.ClimbEntered : MovementStepEvent.ClimbExited;
                if (wallSliding != _previousWallSliding)
                    events |= wallSliding ? MovementStepEvent.WallSlideEntered : MovementStepEvent.WallSlideExited;
                if (swimming != _previousSwimming)
                    events |= swimming ? MovementStepEvent.SwimEntered : MovementStepEvent.SwimExited;
                if (flying != _previousFlying)
                    events |= flying ? MovementStepEvent.FlyEntered : MovementStepEvent.FlyExited;
            }

            _previousGrounded = grounded;
            _previousClimbing = climbing;
            _previousWallSliding = wallSliding;
            _previousSwimming = swimming;
            _previousFlying = flying;
            _hasBaseline = true;
            return events;
        }
    }
}
