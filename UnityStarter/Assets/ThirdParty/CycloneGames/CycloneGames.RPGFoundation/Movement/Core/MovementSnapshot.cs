using System;

namespace CycloneGames.RPGFoundation.Movement.Core
{
    public static class MovementStateDomain
    {
        public const int MaximumId = 10;

        public static bool IsDefined(int value)
        {
            return value >= 0 && value <= MaximumId;
        }
    }

    // Compact high-frequency delta. Use MovementFullState to rebuild a simulator.
    public struct MovementSnapshot
    {
        public MovementVector3 Position;
        public MovementVector3 Velocity;
        public MovementVector3 WorldUp;
        public ushort StateId;
        public byte Flags;
        public byte JumpCount;
        public float VerticalVelocity;
        public bool IsGrounded;
        public long NetworkTick;
        public double NetworkTimeSeconds;

        public bool IsValid
        {
            get
            {
                return MovementStateDomain.IsDefined(StateId)
                       && JumpCount <= 32
                       && NetworkTick >= 0L
                       && IsFiniteNonNegative(NetworkTimeSeconds)
                       && float.IsFinite(VerticalVelocity)
                       && Position.IsFinite
                       && Velocity.IsFinite
                       && WorldUp.IsFinite
                       && WorldUp.IsUnit(0.05f);
            }
        }

        public MovementStateType StateType
        {
            get { return (MovementStateType)StateId; }
        }

        public static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.0;
        }
    }

    // Every value that influences the next simulation tick lives here.
    // Presentation data must not be stored in these slots.
    public struct MovementFullState
    {
        public long NetworkTick;
        public double NetworkTimeSeconds;

        public MovementVector3 Position;
        public MovementVector3 Velocity;
        public MovementVector3 WorldUp;
        public MovementVector3 GroundNormal;
        public MovementVector3 InputDirection;
        public MovementVector3 FacingDirection;
        public MovementVector3 RollDirection;
        public MovementVector3 WallJumpDirection;
        public MovementVector3 LastWallNormal;
        public MovementVector3 WallClimbNormal;
        public MovementVector3 PendingImpulse;
        public MovementVector3 PendingForce;
        public MovementVector3 PlatformLocalPosition;
        public MovementVector3 PlatformVelocity;

        public float PlatformLocalRotationX;
        public float PlatformLocalRotationY;
        public float PlatformLocalRotationZ;
        public float PlatformLocalRotationW;
        public float VerticalVelocity;
        public float SlopeAngle;
        public float RollTimer;
        public float WallClingTimer;
        public float LastWallJumpTime;
        public float StateElapsed;
        public float UnconstrainedTimer;
        public float CurrentSpeed;

        public int JumpCount;
        public int StateTick;

        public float Extension0;
        public float Extension1;
        public float Extension2;
        public float Extension3;
        public int ExtensionInt0;
        public int ExtensionInt1;

        public ushort StateId;
        public byte ClimbingMode;

        public bool IsGrounded;
        public bool WasGrounded;
        public bool IsOnNonWalkableSlope;
        public bool IsWallJumping;
        public bool IsWallSliding;
        public bool IsOnPlatform;
        public bool UseRootMotion;
        public bool JumpPressed;
        public bool SprintHeld;
        public bool CrouchHeld;
        public bool RollPressed;

        public bool IsValid
        {
            get
            {
                if (!MovementStateDomain.IsDefined(StateId)
                    || ClimbingMode > 2
                    || JumpCount < 0 || JumpCount > 32
                    || NetworkTick < 0L
                    || StateTick < 0
                    || !MovementSnapshot.IsFiniteNonNegative(NetworkTimeSeconds))
                    return false;

                if (!FiniteNonNegative(RollTimer) || !FiniteNonNegative(WallClingTimer)
                    || !FiniteNonNegative(StateElapsed) || !FiniteNonNegative(UnconstrainedTimer)
                    || !FiniteNonNegative(LastWallJumpTime) || !float.IsFinite(CurrentSpeed)
                    || !float.IsFinite(SlopeAngle) || !float.IsFinite(VerticalVelocity))
                    return false;

                if (!float.IsFinite(PlatformLocalRotationX) || !float.IsFinite(PlatformLocalRotationY)
                    || !float.IsFinite(PlatformLocalRotationZ) || !float.IsFinite(PlatformLocalRotationW))
                    return false;

                if (!float.IsFinite(Extension0) || !float.IsFinite(Extension1)
                    || !float.IsFinite(Extension2) || !float.IsFinite(Extension3))
                    return false;

                if (!Position.IsFinite || !Velocity.IsFinite || !GroundNormal.IsFinite
                    || !InputDirection.IsFinite || !FacingDirection.IsFinite
                    || !RollDirection.IsFinite || !WallJumpDirection.IsFinite
                    || !LastWallNormal.IsFinite || !WallClimbNormal.IsFinite
                    || !PendingImpulse.IsFinite || !PendingForce.IsFinite
                    || !PlatformLocalPosition.IsFinite || !PlatformVelocity.IsFinite)
                    return false;

                return WorldUp.IsFinite && WorldUp.IsUnit(0.05f);
            }
        }

        public static MovementFullState FromDelta(in MovementSnapshot snapshot)
        {
            MovementFullState state = default;
            state.NetworkTick = snapshot.NetworkTick;
            state.NetworkTimeSeconds = snapshot.NetworkTimeSeconds;
            state.Position = snapshot.Position;
            state.Velocity = snapshot.Velocity;
            state.WorldUp = snapshot.WorldUp;
            state.StateId = snapshot.StateId;
            state.JumpCount = snapshot.JumpCount;
            state.VerticalVelocity = snapshot.VerticalVelocity;
            state.IsGrounded = snapshot.IsGrounded;
            state.WasGrounded = snapshot.IsGrounded;
            state.GroundNormal = snapshot.WorldUp;
            return state;
        }

        public MovementSnapshot ToDelta(byte flags)
        {
            MovementSnapshot snapshot = default;
            snapshot.NetworkTick = NetworkTick;
            snapshot.NetworkTimeSeconds = NetworkTimeSeconds;
            snapshot.Position = Position;
            snapshot.Velocity = Velocity;
            snapshot.WorldUp = WorldUp;
            snapshot.StateId = StateId;
            snapshot.Flags = flags;
            snapshot.JumpCount = (byte)JumpCount;
            snapshot.VerticalVelocity = VerticalVelocity;
            snapshot.IsGrounded = IsGrounded;
            return snapshot;
        }

        private static bool FiniteNonNegative(float value)
        {
            return float.IsFinite(value) && value >= 0f;
        }
    }

    public interface IMovementSnapshotProvider
    {
        MovementSnapshot GetSnapshot();
        MovementFullState CaptureFullState();
        void ApplySnapshot(in MovementSnapshot snapshot);
        void ApplyFullState(in MovementFullState state);
        void ResetFromFullState(in MovementFullState state);
    }

    public interface IMovementValidator
    {
        bool ValidatePosition(MovementVector3 from, MovementVector3 to, float deltaTime);
        bool ValidateStateTransition(MovementStateType from, MovementStateType to);
    }

    #region Configuration value-domain rules

    public enum MovementConfigField : byte
    {
        WalkSpeed = 0,
        RunSpeed = 1,
        SprintSpeed = 2,
        CrouchSpeed = 3,
        JumpForce = 4,
        MaxJumpCount = 5,

        RollDistance3D = 6,
        RollDuration3D = 7,
        SwimSpeed = 8,
        FlySpeed = 9,
        Gravity3D = 10,
        AirControl3D = 11,
        GroundedCheckDistance = 12,
        SlopeLimit = 13,
        StepHeight = 14,
        MinAirborneTimeForFall = 15,
        CeilingCheckDistance = 16,
        MinSpeedForGapBridge3D = 17,
        MaxGapDistance3D = 18,
        MaxGapHeightDiff = 19,
        LadderClimbSpeed3D = 20,
        WallClimbSpeed3D = 21,
        WallCheckDistance3D = 22,
        WallClingDuration3D = 23,
        WallSlideSpeed3D = 24,
        WallJumpForceHorizontal = 25,
        WallJumpForceVertical = 26,
        WallJumpCooldown3D = 27,
        DifferentWallAngle3D = 28,
        RotationSpeed = 29,
        CoyoteTime3D = 30,
        JumpBufferTime3D = 31,

        AirControl2D = 32,
        CoyoteTime2D = 33,
        JumpBufferTime2D = 34,
        Gravity2D = 35,
        MaxFallSpeed = 36,
        GroundCheckDistance2D = 37,
        GroundCheckSizeX = 38,
        GroundCheckSizeY = 39,
        SlideSpeed = 40,
        RollDistance2D = 41,
        RollDuration2D = 42,
        MinSpeedForGapBridge2D = 43,
        MaxGapDistance2D = 44,
        LadderClimbSpeed2D = 45,
        WallClimbSpeed2D = 46,
        WallCheckDistance2D = 47,
        WallClingDuration2D = 48,
        WallSlideSpeed2D = 49,
        WallJumpForceX = 50,
        WallJumpForceY = 51,
        WallJumpCooldown2D = 52,
        DifferentWallAngle2D = 53
    }

    public enum MovementConfigIssueCode : byte
    {
        None = 0,
        NonFinite = 1,
        BelowMin = 2,
        AboveMax = 3,
        InvalidInteger = 4,
        SpeedOrdering = 5,
        StepHeightExceedsRadius = 6,
        EmptyParameterName = 7
    }

    public readonly struct MovementConfigBounds
    {
        public readonly MovementConfigField Field;
        public readonly float Min;
        public readonly float Max;
        public readonly float Default;
        public readonly string Unit;
        public readonly string Name;

        public MovementConfigBounds(MovementConfigField field, float min, float max,
            float defaultValue, string unit, string name)
        {
            Field = field;
            Min = min;
            Max = max;
            Default = defaultValue;
            Unit = unit;
            Name = name;
        }

        public bool Contains(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value)
                   && value >= Min && value <= Max;
        }
    }

    public readonly struct MovementConfigIssue
    {
        public readonly MovementConfigField Field;
        public readonly MovementConfigIssueCode Code;
        public readonly float Observed;
        public readonly float Corrected;

        public MovementConfigIssue(MovementConfigField field, MovementConfigIssueCode code,
            float observed, float corrected)
        {
            Field = field;
            Code = code;
            Observed = observed;
            Corrected = corrected;
        }

        public string Describe(MovementConfigRules rules)
        {
            MovementConfigBounds bounds = rules.GetBounds(Field);
            switch (Code)
            {
                case MovementConfigIssueCode.NonFinite:
                    return bounds.Name + " was NaN/Infinity; reset to " + bounds.Default.ToString("R") + ". ";
                case MovementConfigIssueCode.BelowMin:
                    return bounds.Name + " " + Observed.ToString("R") + " is below " +
                           bounds.Min.ToString("R") + " " + bounds.Unit + "; clamped. ";
                case MovementConfigIssueCode.AboveMax:
                    return bounds.Name + " " + Observed.ToString("R") + " exceeds " +
                           bounds.Max.ToString("R") + " " + bounds.Unit + "; clamped. ";
                case MovementConfigIssueCode.SpeedOrdering:
                    return "Speed ordering should be Crouch <= Walk <= Run <= Sprint. ";
                case MovementConfigIssueCode.StepHeightExceedsRadius:
                    return "Step Height exceeds the capsule radius; character may clip through geometry. ";
                case MovementConfigIssueCode.EmptyParameterName:
                    return "An animation parameter name is empty. ";
                default:
                    return string.Empty;
            }
        }
    }

    public sealed class MovementConfigRules
    {
        public const int FieldCount = 54;
        public const int MaxJumpCountMaximum = 10;

        private readonly MovementConfigBounds[] _bounds;

        public MovementConfigRules()
        {
            _bounds = new MovementConfigBounds[FieldCount];
            Set(MovementConfigField.WalkSpeed, 0f, 50f, 1.5f, "m/s");
            Set(MovementConfigField.RunSpeed, 0f, 50f, 3f, "m/s");
            Set(MovementConfigField.SprintSpeed, 0f, 50f, 6f, "m/s");
            Set(MovementConfigField.CrouchSpeed, 0f, 50f, 1f, "m/s");
            Set(MovementConfigField.JumpForce, 0f, 100f, 6f, "m/s");
            Set(MovementConfigField.MaxJumpCount, 0f, MaxJumpCountMaximum, 1f, "count");

            Set(MovementConfigField.RollDistance3D, 0f, 50f, 5f, "m");
            Set(MovementConfigField.RollDuration3D, 0.01f, 10f, 0.5f, "s");
            Set(MovementConfigField.SwimSpeed, 0f, 50f, 3f, "m/s");
            Set(MovementConfigField.FlySpeed, 0f, 50f, 6f, "m/s");
            Set(MovementConfigField.Gravity3D, -200f, 0f, -20f, "m/s^2");
            Set(MovementConfigField.AirControl3D, 0f, 2f, 0.5f, "x");
            Set(MovementConfigField.GroundedCheckDistance, 0.001f, 2f, 0.025f, "m");
            Set(MovementConfigField.SlopeLimit, 0f, 89f, 45f, "deg");
            Set(MovementConfigField.StepHeight, 0f, 2f, 0.3f, "m");
            Set(MovementConfigField.MinAirborneTimeForFall, 0.01f, 1f, 0.1f, "s");
            Set(MovementConfigField.CeilingCheckDistance, 0.001f, 2f, 0.1f, "m");
            Set(MovementConfigField.MinSpeedForGapBridge3D, 0f, 50f, 3f, "m/s");
            Set(MovementConfigField.MaxGapDistance3D, 0f, 20f, 0.5f, "m");
            Set(MovementConfigField.MaxGapHeightDiff, 0f, 20f, 0.3f, "m");
            Set(MovementConfigField.LadderClimbSpeed3D, 0f, 50f, 3f, "m/s");
            Set(MovementConfigField.WallClimbSpeed3D, 0f, 50f, 2f, "m/s");
            Set(MovementConfigField.WallCheckDistance3D, 0.001f, 5f, 0.5f, "m");
            Set(MovementConfigField.WallClingDuration3D, 0.01f, 10f, 0.5f, "s");
            Set(MovementConfigField.WallSlideSpeed3D, 0f, 50f, 2f, "m/s");
            Set(MovementConfigField.WallJumpForceHorizontal, 0f, 100f, 8f, "m/s");
            Set(MovementConfigField.WallJumpForceVertical, 0f, 100f, 10f, "m/s");
            Set(MovementConfigField.WallJumpCooldown3D, 0f, 5f, 0.1f, "s");
            Set(MovementConfigField.DifferentWallAngle3D, 0f, 180f, 60f, "deg");
            Set(MovementConfigField.RotationSpeed, 0f, 1000f, 10f, "deg/s");
            Set(MovementConfigField.CoyoteTime3D, 0f, 2f, 0.1f, "s");
            Set(MovementConfigField.JumpBufferTime3D, 0f, 2f, 0.1f, "s");

            Set(MovementConfigField.AirControl2D, 0f, 2f, 0.5f, "x");
            Set(MovementConfigField.CoyoteTime2D, 0f, 2f, 0.1f, "s");
            Set(MovementConfigField.JumpBufferTime2D, 0f, 2f, 0.1f, "s");
            Set(MovementConfigField.Gravity2D, 0f, 200f, 25f, "m/s^2");
            Set(MovementConfigField.MaxFallSpeed, 0f, 200f, 20f, "m/s");
            Set(MovementConfigField.GroundCheckDistance2D, 0.001f, 2f, 0.1f, "m");
            Set(MovementConfigField.GroundCheckSizeX, 0.001f, 10f, 0.8f, "m");
            Set(MovementConfigField.GroundCheckSizeY, 0.001f, 10f, 0.1f, "m");
            Set(MovementConfigField.SlideSpeed, 0f, 50f, 7f, "m/s");
            Set(MovementConfigField.RollDistance2D, 0f, 50f, 5f, "m");
            Set(MovementConfigField.RollDuration2D, 0.01f, 10f, 0.5f, "s");
            Set(MovementConfigField.MinSpeedForGapBridge2D, 0f, 50f, 4f, "m/s");
            Set(MovementConfigField.MaxGapDistance2D, 0f, 20f, 1f, "m");
            Set(MovementConfigField.LadderClimbSpeed2D, 0f, 50f, 3f, "m/s");
            Set(MovementConfigField.WallClimbSpeed2D, 0f, 50f, 2f, "m/s");
            Set(MovementConfigField.WallCheckDistance2D, 0.001f, 5f, 0.3f, "m");
            Set(MovementConfigField.WallClingDuration2D, 0.01f, 10f, 0.5f, "s");
            Set(MovementConfigField.WallSlideSpeed2D, 0f, 50f, 2f, "m/s");
            Set(MovementConfigField.WallJumpForceX, 0f, 100f, 8f, "m/s");
            Set(MovementConfigField.WallJumpForceY, 0f, 100f, 10f, "m/s");
            Set(MovementConfigField.WallJumpCooldown2D, 0f, 5f, 0.1f, "s");
            Set(MovementConfigField.DifferentWallAngle2D, 0f, 180f, 60f, "deg");
        }

        private void Set(MovementConfigField field, float min, float max, float defaultValue, string unit)
        {
            _bounds[(int)field] = new MovementConfigBounds(
                field, min, max, defaultValue, unit, field.ToString());
        }

        public MovementConfigBounds GetBounds(MovementConfigField field)
        {
            int index = (int)field;
            if (index < 0 || index >= _bounds.Length) return default;
            return _bounds[index];
        }

        public bool IsValid(MovementConfigField field, float value)
        {
            return GetBounds(field).Contains(value);
        }

        public float Sanitize(MovementConfigField field, float value, out MovementConfigIssueCode code)
        {
            MovementConfigBounds bounds = GetBounds(field);
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                code = MovementConfigIssueCode.NonFinite;
                return bounds.Default;
            }
            if (value < bounds.Min)
            {
                code = MovementConfigIssueCode.BelowMin;
                return bounds.Min;
            }
            if (value > bounds.Max)
            {
                code = MovementConfigIssueCode.AboveMax;
                return bounds.Max;
            }
            code = MovementConfigIssueCode.None;
            return value;
        }

        public int SanitizeJumpCount(int value, out MovementConfigIssueCode code)
        {
            if (value < 0)
            {
                code = MovementConfigIssueCode.BelowMin;
                return 0;
            }
            if (value > MaxJumpCountMaximum)
            {
                code = MovementConfigIssueCode.AboveMax;
                return MaxJumpCountMaximum;
            }
            code = MovementConfigIssueCode.None;
            return value;
        }

        public int Validate(ReadOnlySpan<float> values, Span<MovementConfigIssue> issues)
        {
            int count = 0;
            int limit = values.Length < FieldCount ? values.Length : FieldCount;
            for (int i = 0; i < limit; i++)
            {
                MovementConfigField field = (MovementConfigField)(byte)i;
                MovementConfigIssueCode code;
                float corrected = Sanitize(field, values[i], out code);
                if (code == MovementConfigIssueCode.None) continue;
                if (count >= issues.Length) break;
                issues[count++] = new MovementConfigIssue(field, code, values[i], corrected);
            }

            if (count < issues.Length && limit >= (int)MovementConfigField.SprintSpeed + 1)
            {
                float crouch = values[(int)MovementConfigField.CrouchSpeed];
                float walk = values[(int)MovementConfigField.WalkSpeed];
                float run = values[(int)MovementConfigField.RunSpeed];
                float sprint = values[(int)MovementConfigField.SprintSpeed];
                if (crouch > walk || walk > run || run > sprint)
                    issues[count++] = new MovementConfigIssue(
                        MovementConfigField.SprintSpeed,
                        MovementConfigIssueCode.SpeedOrdering, sprint, sprint);
            }
            return count;
        }

        public bool CheckStepHeight(float stepHeight, float capsuleRadius, out MovementConfigIssueCode code)
        {
            if (stepHeight > capsuleRadius)
            {
                code = MovementConfigIssueCode.StepHeightExceedsRadius;
                return false;
            }
            code = MovementConfigIssueCode.None;
            return true;
        }

        public bool CheckParameterName(string value, out MovementConfigIssueCode code)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrWhiteSpace(value))
            {
                code = MovementConfigIssueCode.EmptyParameterName;
                return false;
            }
            code = MovementConfigIssueCode.None;
            return true;
        }

        public void WriteDefaults(Span<float> destination)
        {
            int limit = destination.Length < FieldCount ? destination.Length : FieldCount;
            for (int i = 0; i < limit; i++)
                destination[i] = _bounds[i].Default;
        }
    }

    public enum MovementConfigPresetKind : byte
    {
        Humanoid3D = 0,
        Agile3D = 1,
        Heavy3D = 2,
        Platformer2D = 3,
        TopDown2D = 4
    }

    public readonly struct MovementConfigPresetEntry
    {
        public readonly MovementConfigField Field;
        public readonly float Value;
        public MovementConfigPresetEntry(MovementConfigField field, float value)
        {
            Field = field;
            Value = value;
        }
    }

    public readonly struct MovementConfigRawOverride
    {
        public readonly string SerializedField;
        public readonly float Value;
        public MovementConfigRawOverride(string serializedField, float value)
        {
            SerializedField = serializedField;
            Value = value;
        }
    }

    public sealed class MovementConfigPreset
    {
        public readonly string Name;
        public readonly string Description;
        public readonly bool Is3D;
        public readonly MovementConfigPresetEntry[] Entries;
        public readonly MovementConfigRawOverride[] RawOverrides;
        public MovementConfigPreset(string name, string description, bool is3D,
            MovementConfigPresetEntry[] entries, MovementConfigRawOverride[] rawOverrides = null)
        {
            Name = name;
            Description = description;
            Is3D = is3D;
            Entries = entries;
            RawOverrides = rawOverrides ?? new MovementConfigRawOverride[0];
        }
        public int Count { get { return Entries.Length; } }
        public int RawCount { get { return RawOverrides.Length; } }
    }

    public sealed class MovementConfigPresetLibrary
    {
        public const int PresetCount = 5;
        private MovementConfigPreset[] _presets;
        private int _presetCount;
        private readonly string[] _serializedNames;

        public MovementConfigPresetLibrary()
        {
            _presets = new MovementConfigPreset[PresetCount];
            _presets[0] = new MovementConfigPreset("Standard", "Balanced baseline. Walk 1.5 / Run 3 / Sprint 6 m/s, 1 jump, 0.5 s roll, -20 m/s^2 gravity. Overrides numeric fields only; every other setting keeps its current value.", true, new[]
            {
                E(MovementConfigField.WalkSpeed, 1.5f), E(MovementConfigField.RunSpeed, 3f),
                E(MovementConfigField.SprintSpeed, 6f), E(MovementConfigField.CrouchSpeed, 1f),
                E(MovementConfigField.JumpForce, 6f), E(MovementConfigField.MaxJumpCount, 1f),
                E(MovementConfigField.RollDistance3D, 5f), E(MovementConfigField.RollDuration3D, 0.5f),
                E(MovementConfigField.Gravity3D, -20f), E(MovementConfigField.StepHeight, 0.3f),
                E(MovementConfigField.SlopeLimit, 45f), E(MovementConfigField.RotationSpeed, 10f)
            });
            _presets[1] = new MovementConfigPreset("Swift", "High-mobility archetype (scout / rogue): ~1.7x ground speed, 2 jumps, 0.35 s roll and lighter gravity. Overrides numeric fields only.", true, new[]
            {
                E(MovementConfigField.WalkSpeed, 2.5f), E(MovementConfigField.RunSpeed, 5f),
                E(MovementConfigField.SprintSpeed, 9f), E(MovementConfigField.CrouchSpeed, 1.5f),
                E(MovementConfigField.JumpForce, 8f), E(MovementConfigField.MaxJumpCount, 2f),
                E(MovementConfigField.RollDistance3D, 7f), E(MovementConfigField.RollDuration3D, 0.35f),
                E(MovementConfigField.Gravity3D, -18f), E(MovementConfigField.StepHeight, 0.35f),
                E(MovementConfigField.RotationSpeed, 14f)
            });
            _presets[2] = new MovementConfigPreset("Bulky", "Grounded archetype (tank / bruiser): ~0.5x ground speed, single jump, 0.8 s roll and stronger gravity. Overrides numeric fields only.", true, new[]
            {
                E(MovementConfigField.WalkSpeed, 1f), E(MovementConfigField.RunSpeed, 2f),
                E(MovementConfigField.SprintSpeed, 3f), E(MovementConfigField.CrouchSpeed, 0.6f),
                E(MovementConfigField.JumpForce, 4f), E(MovementConfigField.MaxJumpCount, 1f),
                E(MovementConfigField.RollDistance3D, 3f), E(MovementConfigField.RollDuration3D, 0.8f),
                E(MovementConfigField.Gravity3D, -28f), E(MovementConfigField.StepHeight, 0.25f),
                E(MovementConfigField.RotationSpeed, 6f)
            });
            _presets[3] = new MovementConfigPreset("Platformer", "Side-view platformer: +25 m/s^2 gravity, 20 m/s max fall, 0.1 s coyote and jump buffer. Also enables jump and locks the Z axis.", false, new[]
            {
                E(MovementConfigField.WalkSpeed, 1.5f), E(MovementConfigField.RunSpeed, 3f),
                E(MovementConfigField.SprintSpeed, 6f), E(MovementConfigField.CrouchSpeed, 1f),
                E(MovementConfigField.JumpForce, 6f), E(MovementConfigField.MaxJumpCount, 1f),
                E(MovementConfigField.Gravity2D, 25f), E(MovementConfigField.MaxFallSpeed, 20f),
                E(MovementConfigField.AirControl2D, 0.5f), E(MovementConfigField.CoyoteTime2D, 0.1f),
                E(MovementConfigField.JumpBufferTime2D, 0.1f), E(MovementConfigField.RollDistance2D, 5f),
                E(MovementConfigField.RollDuration2D, 0.5f)
            }, new[]
            {
                R("enableJump", 1f), R("lockZAxis", 1f), R("movementType", 0f)
            });
            _presets[4] = new MovementConfigPreset("TopDown", "Top-down / 4-8 way: no gravity, no jump, full air control. Also sets Movement Type to TopDown and disables jump.", false, new[]
            {
                E(MovementConfigField.WalkSpeed, 2f), E(MovementConfigField.RunSpeed, 4f),
                E(MovementConfigField.SprintSpeed, 6f), E(MovementConfigField.CrouchSpeed, 1f),
                E(MovementConfigField.JumpForce, 0f), E(MovementConfigField.MaxJumpCount, 0f),
                E(MovementConfigField.Gravity2D, 0f), E(MovementConfigField.MaxFallSpeed, 0f),
                E(MovementConfigField.AirControl2D, 1f), E(MovementConfigField.CoyoteTime2D, 0f),
                E(MovementConfigField.JumpBufferTime2D, 0f),
                E(MovementConfigField.RollDistance2D, 4f), E(MovementConfigField.RollDuration2D, 0.4f)
            }, new[]
            {
                R("enableJump", 0f), R("lockZAxis", 1f), R("movementType", 2f)
            });

            _serializedNames = new string[MovementConfigRules.FieldCount];
            FillNames(_serializedNames);
            _presetCount = PresetCount;
        }

        private static MovementConfigPresetEntry E(MovementConfigField f, float v)
        {
            return new MovementConfigPresetEntry(f, v);
        }

        private static MovementConfigRawOverride R(string name, float v)
        {
            return new MovementConfigRawOverride(name, v);
        }

        public int Count { get { return _presetCount; } }
        public MovementConfigPreset Get(int index)
        {
            return index >= 0 && index < _presetCount ? _presets[index] : null;
        }

        public int Register(MovementConfigPreset preset)
        {
            if (preset == null || preset.Entries == null || string.IsNullOrEmpty(preset.Name)) return -1;
            if (_presetCount >= _presets.Length)
                Array.Resize(ref _presets, _presets.Length * 2);
            _presets[_presetCount] = preset;
            return _presetCount++;
        }

        public void ClearExternalPresets()
        {
            for (int i = PresetCount; i < _presetCount; i++) _presets[i] = null;
            _presetCount = PresetCount;
        }
        public string GetSerializedFieldName(MovementConfigField field)
        {
            int i = (int)field;
            return i >= 0 && i < _serializedNames.Length ? _serializedNames[i] : null;
        }
        public int CountFor(bool is3D)
        {
            int n = 0;
            for (int i = 0; i < _presetCount; i++) if (_presets[i].Is3D == is3D) n++;
            return n;
        }
        public int FirstIndexFor(bool is3D)
        {
            for (int i = 0; i < _presetCount; i++) if (_presets[i].Is3D == is3D) return i;
            return -1;
        }

        public int ValidatePreset(MovementConfigRules rules, int index, Span<MovementConfigIssue> issues)
        {
            MovementConfigPreset preset = Get(index);
            if (preset == null) return -1;
            int count = 0;
            for (int i = 0; i < preset.Count; i++)
            {
                MovementConfigPresetEntry entry = preset.Entries[i];
                MovementConfigIssueCode code;
                float corrected = rules.Sanitize(entry.Field, entry.Value, out code);
                if (code == MovementConfigIssueCode.None) continue;
                if (count >= issues.Length) break;
                issues[count++] = new MovementConfigIssue(entry.Field, code, entry.Value, corrected);
            }
            return count;
        }

        private static void FillNames(string[] n)
        {
            n[0] = "walkSpeed"; n[1] = "runSpeed"; n[2] = "sprintSpeed"; n[3] = "crouchSpeed";
            n[4] = "jumpForce"; n[5] = "maxJumpCount";
            n[6] = "rollDistance"; n[7] = "rollDuration"; n[8] = "swimSpeed"; n[9] = "flySpeed";
            n[10] = "gravity"; n[11] = "airControlMultiplier"; n[12] = "groundedCheckDistance";
            n[13] = "slopeLimit"; n[14] = "stepHeight"; n[15] = "minAirborneTimeForFall";
            n[16] = "ceilingCheckDistance"; n[17] = "minSpeedForGapBridge"; n[18] = "maxGapDistance";
            n[19] = "maxGapHeightDiff"; n[20] = "ladderClimbSpeed"; n[21] = "wallClimbSpeed";
            n[22] = "wallCheckDistance"; n[23] = "wallClingDuration"; n[24] = "wallSlideSpeed";
            n[25] = "wallJumpForceHorizontal"; n[26] = "wallJumpForceVertical"; n[27] = "wallJumpCooldown";
            n[28] = "differentWallAngle"; n[29] = "rotationSpeed"; n[30] = "coyoteTime";
            n[31] = "jumpBufferTime";
            n[32] = "airControlMultiplier"; n[33] = "coyoteTime"; n[34] = "jumpBufferTime";
            n[35] = "gravity"; n[36] = "maxFallSpeed"; n[37] = "groundCheckDistance";
            n[38] = "groundCheckSize.x"; n[39] = "groundCheckSize.y"; n[40] = "slideSpeed";
            n[41] = "rollDistance"; n[42] = "rollDuration"; n[43] = "minSpeedForGapBridge";
            n[44] = "maxGapDistance"; n[45] = "ladderClimbSpeed"; n[46] = "wallClimbSpeed";
            n[47] = "wallCheckDistance"; n[48] = "wallClingDuration"; n[49] = "wallSlideSpeed";
            n[50] = "wallJumpForceX"; n[51] = "wallJumpForceY"; n[52] = "wallJumpCooldown";
            n[53] = "differentWallAngle";
        }
    }
    #endregion
}
