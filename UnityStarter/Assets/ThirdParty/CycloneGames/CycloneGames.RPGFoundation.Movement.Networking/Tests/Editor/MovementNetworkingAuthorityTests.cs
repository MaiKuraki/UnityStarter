using CycloneGames.Networking;
using CycloneGames.Networking.Simulation;
using CycloneGames.RPGFoundation.Movement.Core;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Movement.Networking.Tests.Editor
{
    public sealed class MovementNetworkingAuthorityTests
    {
        private const ulong ENTITY_ID = 1001UL;

        [Test]
        public void InputValidator_Accepts_Valid_Predicted_Input()
        {
            MovementInputCommandMessage command = CreateInput(clientTick: 20, sequence: 3, predictionKey: 77);
            var context = new MovementNetworkInputValidationContext(
                sender: null,
                serverTick: new NetworkTickId(22),
                lastAcceptedClientTick: new NetworkTickId(19),
                maxAcceptedTickDrift: 8,
                allowedButtonMask: uint.MaxValue,
                allowedCustomFlags: 0xFFU);

            NetworkActionResult result = DefaultMovementNetworkInputValidator.Instance.Validate(command, context);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.PredictionKey, Is.EqualTo(77));
            Assert.That(result.Sequence, Is.EqualTo(3));
        }

        [Test]
        public void InputValidator_Rejects_OutOfRange_MoveAxes()
        {
            MovementInputCommandMessage command = CreateInput(clientTick: 20, sequence: 3);
            command.MoveAxes = new NetworkVector3(5f, 0f, 0f);
            var context = new MovementNetworkInputValidationContext(
                sender: null,
                serverTick: new NetworkTickId(20),
                lastAcceptedClientTick: NetworkTickId.Invalid,
                maxMoveAxesMagnitude: 1.25f);

            NetworkActionResult result = DefaultMovementNetworkInputValidator.Instance.Validate(command, context);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Code, Is.EqualTo(NetworkActionResultCode.InvalidPayload));
        }

        [Test]
        public void InputValidator_Rejects_Duplicate_Sequence_From_Context()
        {
            MovementInputCommandMessage command = CreateInput(clientTick: 20, sequence: 3);
            var context = new MovementNetworkInputValidationContext(
                sender: null,
                serverTick: new NetworkTickId(20),
                lastAcceptedClientTick: new NetworkTickId(20),
                lastAcceptedInputSequence: 3);

            NetworkActionResult result = DefaultMovementNetworkInputValidator.Instance.Validate(command, context);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Code, Is.EqualTo(NetworkActionResultCode.Duplicate));
        }

        [Test]
        public void InputAuthority_Records_Accepted_Input_And_Rejects_Replay()
        {
            var authority = new MovementNetworkInputAuthority(new MovementNetworkInputHistory(capacity: 4));
            MovementInputCommandMessage command = CreateInput(clientTick: 20, sequence: 3);
            var context = new MovementNetworkInputValidationContext(
                sender: null,
                serverTick: new NetworkTickId(20),
                lastAcceptedClientTick: NetworkTickId.Invalid);

            Assert.That(authority.TryAccept(command, context, out NetworkActionResult accepted), Is.True);
            Assert.That(accepted.IsAccepted, Is.True);
            Assert.That(authority.TryAccept(command, context, out NetworkActionResult duplicate), Is.False);
            Assert.That(duplicate.Code, Is.EqualTo(NetworkActionResultCode.Duplicate));
            Assert.That(authority.History.TryGetLatest(ENTITY_ID, out MovementInputCommandMessage latest, out int tick, out ushort sequence), Is.True);
            Assert.That(latest.InputSequence, Is.EqualTo(3));
            Assert.That(tick, Is.EqualTo(20));
            Assert.That(sequence, Is.EqualTo(3));
        }

        [Test]
        public void SnapshotHistory_Returns_Latest_Server_Snapshot()
        {
            var history = new MovementNetworkSnapshotHistory(capacity: 2);
            MovementNetworkSnapshotMessage first = CreateSnapshot(serverTick: 10, sequence: 1, x: 1f);
            MovementNetworkSnapshotMessage second = CreateSnapshot(serverTick: 11, sequence: 2, x: 2f);

            history.Record(first);
            history.Record(second);

            Assert.That(history.TryGetLatest(ENTITY_ID, out MovementNetworkSnapshotMessage latest), Is.True);
            Assert.That(latest.ServerTick, Is.EqualTo(11));
            Assert.That(latest.Position.X, Is.EqualTo(2f));
        }

        [Test]
        public void Reconciliation_Creates_Correction_When_Position_Drifts()
        {
            MovementNetworkSnapshotMessage predicted = CreateSnapshot(serverTick: 10, sequence: 4, x: 0f);
            MovementNetworkSnapshotMessage authoritative = CreateSnapshot(serverTick: 11, sequence: 5, x: 4f);
            var policy = new MovementNetworkCorrectionPolicy(
                positionCorrectionThreshold: 0.05f,
                positionHardSnapThreshold: 2f,
                velocityCorrectionThreshold: 0.5f);

            bool created = MovementNetworkReconciliation.TryCreateCorrection(
                predicted,
                authoritative,
                policy,
                out MovementCorrectionMessage correction);

            Assert.That(created, Is.True);
            Assert.That(correction.IsValid, Is.True);
            Assert.That(correction.CorrectedClientTick, Is.EqualTo(predicted.Tick));
            Assert.That(correction.InputSequence, Is.EqualTo(predicted.Sequence));
            Assert.That(correction.Snapshot.IsTeleport, Is.True);
        }

        private static MovementInputCommandMessage CreateInput(
            int clientTick,
            ushort sequence,
            int predictionKey = 0)
        {
            return new MovementInputCommandMessage(
                ENTITY_ID,
                clientTick,
                lastReceivedServerTick: clientTick - 1,
                inputSequence: sequence,
                buttonMask: 1U,
                customFlags: 0U,
                deltaTime: 0.016f,
                moveAxes: new NetworkVector3(1f, 0f, 0f),
                aimDirection: NetworkVector3.Forward,
                predictionKey: predictionKey);
        }

        private static MovementNetworkSnapshotMessage CreateSnapshot(int serverTick, ushort sequence, float x)
        {
            var snapshot = new MovementSnapshot
            {
                Position = new Unity.Mathematics.float3(x, 0f, 0f),
                Velocity = new Unity.Mathematics.float3(0f, 0f, 0f),
                WorldUp = new Unity.Mathematics.float3(0f, 1f, 0f),
                StateType = MovementStateType.Walk,
                VerticalVelocity = 0f,
                IsGrounded = true,
                JumpCount = 0,
                Tick = serverTick - 1,
                Timestamp = serverTick * 0.016f
            };

            return MovementNetworkSnapshotMessage.FromMovementSnapshot(
                ENTITY_ID,
                snapshot,
                serverTick,
                sequence);
        }
    }
}
