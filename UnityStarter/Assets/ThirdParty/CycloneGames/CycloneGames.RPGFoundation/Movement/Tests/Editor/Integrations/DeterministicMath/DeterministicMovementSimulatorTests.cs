using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Movement.Integrations.DeterministicMath;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Movement.Tests.Editor
{
    public sealed class DeterministicMovementSimulatorTests
    {
        private static DeterministicMovementConfig MakeConfig()
        {
            return DeterministicMovementConfig.FromFloats(
                maxHorizontalSpeed: 10f,
                groundAcceleration: 100f,
                airAcceleration: 20f,
                groundDeceleration: 100f,
                gravity: -20f,
                jumpSpeed: 10f,
                maxFallSpeed: -40f,
                groundHeight: 0f);
        }

        private static FPInt64 Dt => FPInt64.FromInt(1) / FPInt64.FromInt(60);

        [Test]
        public void Step_Is_Bit_Deterministic_Across_Independent_Runs()
        {
            DeterministicMovementConfig config = MakeConfig();

            long[] runA = SimulateRawTrace(config);
            long[] runB = SimulateRawTrace(config);

            Assert.AreEqual(runA.Length, runB.Length);
            for (int i = 0; i < runA.Length; i++)
            {
                Assert.AreEqual(runA[i], runB[i], "Divergence at sample " + i);
            }
        }

        [Test]
        public void Gravity_Decreases_Vertical_Velocity_While_Airborne()
        {
            DeterministicMovementConfig config = MakeConfig();
            var state = DeterministicMovementState.Create(new FPVector3(FPInt64.Zero, FPInt64.FromInt(10), FPInt64.Zero), isGrounded: false);
            var input = new DeterministicMovementInput(FPVector3.Zero, Dt, jumpRequested: false);

            DeterministicMovementState next = DeterministicMovementSimulator.Step(state, input, config);

            Assert.Less(next.VerticalVelocity.RawValue, state.VerticalVelocity.RawValue);
        }

        [Test]
        public void Vertical_Velocity_Clamps_To_Max_Fall_Speed()
        {
            DeterministicMovementConfig config = MakeConfig();
            var state = DeterministicMovementState.Create(new FPVector3(FPInt64.Zero, FPInt64.FromInt(1000), FPInt64.Zero), isGrounded: false);
            var input = new DeterministicMovementInput(FPVector3.Zero, Dt, jumpRequested: false);

            for (int i = 0; i < 600; i++)
            {
                state = DeterministicMovementSimulator.Step(state, input, config);
            }

            Assert.AreEqual(config.MaxFallSpeed.RawValue, state.VerticalVelocity.RawValue);
        }

        [Test]
        public void Falling_Entity_Lands_On_Ground_Plane()
        {
            DeterministicMovementConfig config = MakeConfig();
            var state = DeterministicMovementState.Create(new FPVector3(FPInt64.Zero, FPInt64.FromInt(5), FPInt64.Zero), isGrounded: false);
            var input = new DeterministicMovementInput(FPVector3.Zero, Dt, jumpRequested: false);

            for (int i = 0; i < 240; i++)
            {
                state = DeterministicMovementSimulator.Step(state, input, config);
            }

            Assert.IsTrue(state.IsGrounded);
            Assert.AreEqual(config.GroundHeight.RawValue, state.Position.Y.RawValue);
            Assert.AreEqual(0L, state.VerticalVelocity.RawValue);
        }

        [Test]
        public void Jump_Sets_Vertical_Velocity_And_Leaves_Ground()
        {
            DeterministicMovementConfig config = MakeConfig();
            var state = DeterministicMovementState.Create(FPVector3.Zero, isGrounded: true);
            var input = new DeterministicMovementInput(FPVector3.Zero, Dt, jumpRequested: true);

            DeterministicMovementState afterJump = DeterministicMovementSimulator.Step(state, input, config);

            Assert.AreEqual(config.JumpSpeed.RawValue, afterJump.VerticalVelocity.RawValue);
            Assert.IsFalse(afterJump.IsGrounded);
        }

        [Test]
        public void Horizontal_Velocity_Accelerates_Toward_Max_Speed()
        {
            DeterministicMovementConfig config = MakeConfig();
            var state = DeterministicMovementState.Create(FPVector3.Zero, isGrounded: true);
            var input = DeterministicMovementInput.FromFloats(moveX: 1f, moveZ: 0f, deltaTime: 1f / 60f, jumpRequested: false);

            for (int i = 0; i < 120; i++)
            {
                state = DeterministicMovementSimulator.Step(state, input, config);
            }

            FPInt64 maxSqr = config.MaxHorizontalSpeed * config.MaxHorizontalSpeed;
            Assert.LessOrEqual(state.HorizontalVelocity.SqrMagnitude.RawValue, maxSqr.RawValue);
            Assert.AreEqual(config.MaxHorizontalSpeed.RawValue, state.HorizontalVelocity.X.RawValue);
        }

        [Test]
        public void Over_Long_Input_Is_Clamped_To_Max_Speed()
        {
            DeterministicMovementConfig config = MakeConfig();
            var state = DeterministicMovementState.Create(FPVector3.Zero, isGrounded: true);
            // Move direction with magnitude 5 (far above the unit expectation).
            var input = new DeterministicMovementInput(
                new FPVector3(FPInt64.FromInt(5), FPInt64.Zero, FPInt64.Zero),
                Dt,
                jumpRequested: false);

            for (int i = 0; i < 120; i++)
            {
                state = DeterministicMovementSimulator.Step(state, input, config);
            }

            // Fixed-point sqrt + division when renormalising an over-long vector introduces a few raw units
            // of rounding, so compare the clamped speed within tolerance rather than by exact RawValue.
            Assert.AreEqual(config.MaxHorizontalSpeed.ToFloat(), state.HorizontalVelocity.X.ToFloat(), 0.01f);
        }

        [Test]
        public void Grounded_No_Input_Decelerates_To_Zero()
        {
            DeterministicMovementConfig config = MakeConfig();
            var state = new DeterministicMovementState(
                FPVector3.Zero,
                new FPVector3(config.MaxHorizontalSpeed, FPInt64.Zero, FPInt64.Zero),
                FPInt64.Zero,
                isGrounded: true,
                tick: 0);
            var input = new DeterministicMovementInput(FPVector3.Zero, Dt, jumpRequested: false);

            for (int i = 0; i < 120; i++)
            {
                state = DeterministicMovementSimulator.Step(state, input, config);
            }

            Assert.AreEqual(0L, state.HorizontalVelocity.X.RawValue);
            Assert.AreEqual(0L, state.HorizontalVelocity.Z.RawValue);
        }

        [Test]
        public void Tick_Advances_Each_Step()
        {
            DeterministicMovementConfig config = MakeConfig();
            var state = DeterministicMovementState.Create(FPVector3.Zero, isGrounded: true, tick: 100);
            var input = new DeterministicMovementInput(FPVector3.Zero, Dt, jumpRequested: false);

            state = DeterministicMovementSimulator.Step(state, input, config);

            Assert.AreEqual(101, state.Tick);
        }

        [Test]
        public void Conversion_State_To_Snapshot_RoundTrips_Approximately()
        {
            var state = new DeterministicMovementState(
                new FPVector3(FPInt64.FromFloat(3.5f), FPInt64.FromFloat(1.25f), FPInt64.FromFloat(-2f)),
                new FPVector3(FPInt64.FromFloat(4f), FPInt64.Zero, FPInt64.FromFloat(1f)),
                FPInt64.FromFloat(-3f),
                isGrounded: true,
                tick: 7);

            MovementSnapshot snapshot = DeterministicMovementConversions.ToSnapshot(in state, MovementStateType.Run, timestamp: 1.5f);
            DeterministicMovementState restored = DeterministicMovementConversions.ToDeterministicState(in snapshot);

            Assert.AreEqual(3.5f, restored.Position.X.ToFloat(), 1e-3f);
            Assert.AreEqual(1.25f, restored.Position.Y.ToFloat(), 1e-3f);
            Assert.AreEqual(-2f, restored.Position.Z.ToFloat(), 1e-3f);
            Assert.AreEqual(4f, restored.HorizontalVelocity.X.ToFloat(), 1e-3f);
            Assert.AreEqual(-3f, restored.VerticalVelocity.ToFloat(), 1e-3f);
            Assert.AreEqual(7, restored.Tick);
            Assert.IsTrue(restored.IsGrounded);
        }

        private static long[] SimulateRawTrace(DeterministicMovementConfig config)
        {
            var state = DeterministicMovementState.Create(new FPVector3(FPInt64.Zero, FPInt64.FromInt(3), FPInt64.Zero), isGrounded: false);
            const int ticks = 180;
            long[] trace = new long[ticks * 4];

            for (int i = 0; i < ticks; i++)
            {
                // Deterministic but varying input pattern to exercise all branches.
                float moveX = (i % 3) - 1; // -1, 0, 1 cycle
                bool jump = (i % 50) == 0;
                var input = DeterministicMovementInput.FromFloats(moveX, 0f, 1f / 60f, jump);

                state = DeterministicMovementSimulator.Step(state, input, config);

                int baseIndex = i * 4;
                trace[baseIndex] = state.Position.X.RawValue;
                trace[baseIndex + 1] = state.Position.Y.RawValue;
                trace[baseIndex + 2] = state.HorizontalVelocity.X.RawValue;
                trace[baseIndex + 3] = state.VerticalVelocity.RawValue;
            }

            return trace;
        }
    }
}
