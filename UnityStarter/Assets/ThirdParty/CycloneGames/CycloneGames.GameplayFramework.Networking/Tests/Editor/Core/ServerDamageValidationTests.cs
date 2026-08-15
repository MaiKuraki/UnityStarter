using System;
using System.Threading;
using CycloneGames.Networking;
using CycloneGames.Networking.Buffers;
using NUnit.Framework;

namespace CycloneGames.GameplayFramework.Networking.Tests.Editor
{
    public sealed class ServerDamageValidationTests
    {
        [SetUp]
        public void SetUp()
        {
            NetworkBufferPool.Clear();
            NetworkBufferPool.Configure(maxPoolSize: 32, clearBuffersOnReturn: false);
        }

        [TearDown]
        public void TearDown()
        {
            NetworkBufferPool.Clear();
            NetworkBufferPool.Configure(maxPoolSize: 32, clearBuffersOnReturn: false);
        }

        private static ServerDamageValidationRequest MakeRequest(
            int instigatorId = 1,
            int targetId = 2,
            int ownerConn = 10,
            int requestConn = 10,
            bool targetCanBeDamaged = true,
            float requestedDamage = 25f,
            float maxDamage = 50f,
            float maxRangeSqr = 100f,
            double currentTime = 10d,
            double lastAcceptedTime = double.NegativeInfinity,
            double cooldown = 0.5d,
            NetworkVector3 instigatorPos = default,
            NetworkVector3 targetPos = default)
        {
            return new ServerDamageValidationRequest(
                instigatorId,
                targetId,
                ownerConn,
                requestConn,
                targetCanBeDamaged,
                instigatorPos,
                targetPos,
                requestedDamage,
                maxDamage,
                maxRangeSqr,
                currentTime,
                lastAcceptedTime,
                cooldown);
        }

        [Test]
        public void DefaultDamageResults_AreUnknownAndFailClosed()
        {
            ServerDamageValidationResult validation = default;
            DamageResultMessage message = default;

            Assert.AreEqual(ServerDamageRejectReason.Unknown, validation.Reason);
            Assert.IsFalse(validation.Accepted);
            Assert.AreEqual(ServerDamageRejectReason.Unknown, message.ResultCode);

            using NetworkBuffer buffer = NetworkBufferPool.Get();
            Assert.Throws<System.InvalidOperationException>(() => buffer.WriteDamageResult(message));
        }

        [Test]
        public void Validate_Accepts_Valid_Request_And_Returns_Damage()
        {
            ServerDamageValidationResult result = DefaultServerDamageValidator.Instance.Validate(MakeRequest());

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(ServerDamageRejectReason.Accepted, result.Reason);
            Assert.AreEqual(25f, result.ApprovedDamage);
        }

        [Test]
        public void Validate_Clamps_Damage_To_Max()
        {
            ServerDamageValidationResult result = DefaultServerDamageValidator.Instance.Validate(
                MakeRequest(requestedDamage: 9999f, maxDamage: 40f));

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(40f, result.ApprovedDamage);
        }

        [Test]
        public void Validate_Rejects_Ownership_Mismatch()
        {
            ServerDamageValidationResult result = DefaultServerDamageValidator.Instance.Validate(
                MakeRequest(ownerConn: 10, requestConn: 11));

            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(ServerDamageRejectReason.OwnershipMismatch, result.Reason);
            Assert.AreEqual(0f, result.ApprovedDamage);
        }

        [Test]
        public void Validate_Rejects_Non_Damageable_Target()
        {
            ServerDamageValidationResult result = DefaultServerDamageValidator.Instance.Validate(
                MakeRequest(targetCanBeDamaged: false));

            Assert.AreEqual(ServerDamageRejectReason.TargetNotDamageable, result.Reason);
        }

        [Test]
        public void Validate_Rejects_While_On_Cooldown()
        {
            ServerDamageValidationResult result = DefaultServerDamageValidator.Instance.Validate(
                MakeRequest(currentTime: 10f, lastAcceptedTime: 9.8f, cooldown: 0.5f));

            Assert.AreEqual(ServerDamageRejectReason.OnCooldown, result.Reason);
        }

        [Test]
        public void Validate_Accepts_After_Cooldown_Elapsed()
        {
            ServerDamageValidationResult result = DefaultServerDamageValidator.Instance.Validate(
                MakeRequest(currentTime: 10f, lastAcceptedTime: 9.0f, cooldown: 0.5f));

            Assert.IsTrue(result.Accepted);
        }

        [Test]
        public void Validate_Rejects_Out_Of_Range()
        {
            ServerDamageValidationResult result = DefaultServerDamageValidator.Instance.Validate(
                MakeRequest(
                    maxRangeSqr: 25f,
                    instigatorPos: new NetworkVector3(0f, 0f, 0f),
                    targetPos: new NetworkVector3(10f, 0f, 0f)));

            Assert.AreEqual(ServerDamageRejectReason.OutOfRange, result.Reason);
        }

        [Test]
        public void Validate_Accepts_Within_Range()
        {
            ServerDamageValidationResult result = DefaultServerDamageValidator.Instance.Validate(
                MakeRequest(
                    maxRangeSqr: 25f,
                    instigatorPos: new NetworkVector3(0f, 0f, 0f),
                    targetPos: new NetworkVector3(3f, 0f, 0f)));

            Assert.IsTrue(result.Accepted);
        }

        [Test]
        public void Validate_Rejects_Invalid_Payload()
        {
            Assert.AreEqual(
                ServerDamageRejectReason.InvalidPayload,
                DefaultServerDamageValidator.Instance.Validate(MakeRequest(instigatorId: 5, targetId: 5)).Reason);

            Assert.AreEqual(
                ServerDamageRejectReason.InvalidPayload,
                DefaultServerDamageValidator.Instance.Validate(MakeRequest(instigatorId: 0)).Reason);

            Assert.AreEqual(
                ServerDamageRejectReason.InvalidPayload,
                DefaultServerDamageValidator.Instance.Validate(MakeRequest(requestedDamage: float.NaN)).Reason);

            Assert.AreEqual(
                ServerDamageRejectReason.InvalidPayload,
                DefaultServerDamageValidator.Instance.Validate(MakeRequest(requestedDamage: -1f)).Reason);
        }

        [Test]
        public void Validate_Rejects_Invalid_Actor_And_Connection_Identifiers()
        {
            AssertInvalid(MakeRequest(instigatorId: -1));
            AssertInvalid(MakeRequest(targetId: -1));
            AssertInvalid(MakeRequest(ownerConn: 0));
            AssertInvalid(MakeRequest(ownerConn: -1));
            AssertInvalid(MakeRequest(requestConn: 0));
            AssertInvalid(MakeRequest(requestConn: -1));
        }

        [Test]
        public void Validate_Rejects_NonFinite_Positions()
        {
            AssertInvalid(MakeRequest(
                instigatorPos: new NetworkVector3(float.NaN, 0f, 0f)));
            AssertInvalid(MakeRequest(
                targetPos: new NetworkVector3(0f, float.PositiveInfinity, 0f)));
        }

        [Test]
        public void Validate_Rejects_Invalid_Clock_Range_And_Cooldown_Values()
        {
            float[] invalidFloatValues =
            {
                -1f,
                float.NaN,
                float.PositiveInfinity,
                float.NegativeInfinity
            };

            for (int i = 0; i < invalidFloatValues.Length; i++)
            {
                float value = invalidFloatValues[i];
                AssertInvalid(MakeRequest(maxRangeSqr: value));
            }

            double[] invalidDoubleValues =
            {
                -1d,
                double.NaN,
                double.PositiveInfinity,
                double.NegativeInfinity
            };
            for (int i = 0; i < invalidDoubleValues.Length; i++)
            {
                double value = invalidDoubleValues[i];
                AssertInvalid(MakeRequest(currentTime: value));
                AssertInvalid(MakeRequest(cooldown: value));
            }

            AssertInvalid(MakeRequest(lastAcceptedTime: -1d));
            AssertInvalid(MakeRequest(lastAcceptedTime: double.NaN));
            AssertInvalid(MakeRequest(lastAcceptedTime: double.PositiveInfinity));
            AssertInvalid(MakeRequest(currentTime: 10d, lastAcceptedTime: 11d));
        }

        [Test]
        public void Validate_Allows_Unknown_Cooldown_Sentinel()
        {
            ServerDamageValidationResult result = DefaultServerDamageValidator.Instance.Validate(
                MakeRequest(lastAcceptedTime: double.NegativeInfinity));

            Assert.IsTrue(result.Accepted);
        }

        [Test]
        public void ValidationResultFactoriesRejectInvalidStates()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                ServerDamageValidationResult.Accept(-1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                ServerDamageValidationResult.Accept(float.NaN));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                ServerDamageValidationResult.Accept(float.PositiveInfinity));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                ServerDamageValidationResult.Reject(ServerDamageRejectReason.Unknown));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                ServerDamageValidationResult.Reject(ServerDamageRejectReason.Accepted));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                ServerDamageValidationResult.Reject((ServerDamageRejectReason)byte.MaxValue));
        }

        [Test]
        public void CooldownTracker_Returns_Sentinel_When_Unknown()
        {
            var tracker = new DamageCooldownTracker();

            Assert.AreEqual(double.NegativeInfinity, tracker.GetLastAcceptedTime(1));
        }

        [Test]
        public void CooldownTracker_Records_And_Removes()
        {
            var tracker = new DamageCooldownTracker();

            Assert.IsTrue(tracker.TryMarkAccepted(1, 5d));
            Assert.AreEqual(5d, tracker.GetLastAcceptedTime(1));
            Assert.AreEqual(1, tracker.TrackedCount);

            tracker.Remove(1);
            Assert.AreEqual(double.NegativeInfinity, tracker.GetLastAcceptedTime(1));
            Assert.AreEqual(0, tracker.TrackedCount);
        }

        [Test]
        public void CooldownTracker_Drives_Validator_Across_Two_Shots()
        {
            var tracker = new DamageCooldownTracker();
            int instigatorId = 1;

            ServerDamageValidationResult first = DefaultServerDamageValidator.Instance.Validate(
                MakeRequest(currentTime: 10.0f, lastAcceptedTime: tracker.GetLastAcceptedTime(instigatorId), cooldown: 0.5f));
            Assert.IsTrue(first.Accepted);
            Assert.IsTrue(tracker.TryMarkAccepted(instigatorId, 10.0d));

            ServerDamageValidationResult tooSoon = DefaultServerDamageValidator.Instance.Validate(
                MakeRequest(currentTime: 10.2f, lastAcceptedTime: tracker.GetLastAcceptedTime(instigatorId), cooldown: 0.5f));
            Assert.AreEqual(ServerDamageRejectReason.OnCooldown, tooSoon.Reason);

            ServerDamageValidationResult later = DefaultServerDamageValidator.Instance.Validate(
                MakeRequest(currentTime: 10.6f, lastAcceptedTime: tracker.GetLastAcceptedTime(instigatorId), cooldown: 0.5f));
            Assert.IsTrue(later.Accepted);
        }

        [Test]
        public void DamageRequest_RoundTrips()
        {
            var message = new DamageRequestMessage
            {
                Sequence = 42u,
                InstigatorActorId = 7,
                TargetActorId = 9,
                WeaponOrAbilityId = 3,
                DamageEventType = 1,
                RequestedDamage = 33.5f,
                ShotOrigin = new NetworkVector3(1f, 2f, 3f),
                HitLocation = new NetworkVector3(4f, 5f, 6f),
                ClientTimeSeconds = 12.5f
            };

            using NetworkBuffer buffer = NetworkBufferPool.Get();
            buffer.WriteDamageRequest(message);
            buffer.FlipForRead();
            DamageRequestMessage roundTripped = buffer.ReadDamageRequest();

            Assert.AreEqual(message.Sequence, roundTripped.Sequence);
            Assert.AreEqual(message.InstigatorActorId, roundTripped.InstigatorActorId);
            Assert.AreEqual(message.TargetActorId, roundTripped.TargetActorId);
            Assert.AreEqual(message.WeaponOrAbilityId, roundTripped.WeaponOrAbilityId);
            Assert.AreEqual(message.DamageEventType, roundTripped.DamageEventType);
            Assert.AreEqual(message.RequestedDamage, roundTripped.RequestedDamage);
            Assert.AreEqual(message.ShotOrigin, roundTripped.ShotOrigin);
            Assert.AreEqual(message.HitLocation, roundTripped.HitLocation);
            Assert.AreEqual(message.ClientTimeSeconds, roundTripped.ClientTimeSeconds);
        }

        [Test]
        public void DamageResult_RoundTrips()
        {
            var message = new DamageResultMessage
            {
                RequestSequence = 42u,
                InstigatorActorId = 7,
                TargetActorId = 9,
                AppliedDamage = 30f,
                ResultCode = ServerDamageRejectReason.Accepted,
                DamageEventType = 1,
                HitLocation = new NetworkVector3(4f, 5f, 6f)
            };

            using NetworkBuffer buffer = NetworkBufferPool.Get();
            buffer.WriteDamageResult(message);
            buffer.FlipForRead();
            DamageResultMessage roundTripped = buffer.ReadDamageResult();

            Assert.AreEqual(message.RequestSequence, roundTripped.RequestSequence);
            Assert.AreEqual(message.AppliedDamage, roundTripped.AppliedDamage);
            Assert.AreEqual(message.ResultCode, roundTripped.ResultCode);
            Assert.AreEqual(message.HitLocation, roundTripped.HitLocation);
        }

        [Test]
        public void DamageResult_WriteRejectsInvalidDamageAndHitLocation()
        {
            using NetworkBuffer buffer = NetworkBufferPool.Get();

            Assert.Throws<System.InvalidOperationException>(() => buffer.WriteDamageResult(
                CreateDamageResult(appliedDamage: -1f)));
            Assert.Throws<System.InvalidOperationException>(() => buffer.WriteDamageResult(
                CreateDamageResult(appliedDamage: float.NaN)));
            Assert.Throws<System.InvalidOperationException>(() => buffer.WriteDamageResult(
                CreateDamageResult(appliedDamage: float.PositiveInfinity)));
            Assert.Throws<System.InvalidOperationException>(() => buffer.WriteDamageResult(
                CreateDamageResult(hitLocation: new NetworkVector3(float.NaN, 0f, 0f))));
            Assert.Throws<System.InvalidOperationException>(() => buffer.WriteDamageResult(
                CreateDamageResult(
                    appliedDamage: 1f,
                    resultCode: ServerDamageRejectReason.Custom)));
        }

        [Test]
        public void DamageResult_WriteRejectsAcceptedInvalidActorIdentifiers()
        {
            using NetworkBuffer buffer = NetworkBufferPool.Get();

            Assert.Throws<System.InvalidOperationException>(() => buffer.WriteDamageResult(
                CreateDamageResult(instigatorActorId: 0)));
            Assert.Throws<System.InvalidOperationException>(() => buffer.WriteDamageResult(
                CreateDamageResult(targetActorId: -1)));
            Assert.Throws<System.InvalidOperationException>(() => buffer.WriteDamageResult(
                CreateDamageResult(instigatorActorId: 5, targetActorId: 5)));
        }

        [Test]
        public void DamageResult_ReadRejectsAcceptedInvalidActorIdentifiers()
        {
            using NetworkBuffer buffer = NetworkBufferPool.Get();
            buffer.WriteUInt(1u);
            buffer.WriteInt(0);
            buffer.WriteInt(2);
            buffer.WriteFloat(1f);
            buffer.WriteByte((byte)ServerDamageRejectReason.Accepted);
            buffer.WriteByte(0);
            buffer.WriteFloat(0f);
            buffer.WriteFloat(0f);
            buffer.WriteFloat(0f);
            buffer.FlipForRead();

            Assert.Throws<System.InvalidOperationException>(() => buffer.ReadDamageResult());
        }

        [Test]
        public void DamageResult_MatchesFrozenLittleEndianBytes()
        {
            var message = new DamageResultMessage
            {
                RequestSequence = 0x01020304u,
                InstigatorActorId = 0x05060708,
                TargetActorId = 0x11121314,
                AppliedDamage = 1f,
                ResultCode = ServerDamageRejectReason.Accepted,
                DamageEventType = 2,
                HitLocation = new NetworkVector3(1f, -2f, 0.5f)
            };
            byte[] expected =
            {
                0x04, 0x03, 0x02, 0x01,
                0x08, 0x07, 0x06, 0x05,
                0x14, 0x13, 0x12, 0x11,
                0x00, 0x00, 0x80, 0x3F,
                0x01,
                0x02,
                0x00, 0x00, 0x80, 0x3F,
                0x00, 0x00, 0x00, 0xC0,
                0x00, 0x00, 0x00, 0x3F
            };

            using NetworkBuffer buffer = NetworkBufferPool.Get();
            buffer.WriteDamageResult(message);
            System.ArraySegment<byte> segment = buffer.ToArraySegment();
            var actual = new byte[segment.Count];
            System.Buffer.BlockCopy(segment.Array, segment.Offset, actual, 0, segment.Count);

            Assert.AreEqual(GameplayFrameworkNetworkProtocol.DamageResultPayloadBytes, segment.Count);
            CollectionAssert.AreEqual(expected, actual);
        }

        [TestCase(0)]
        [TestCase(10)]
        public void DamageResult_ReadRejectsUnknownOrOutOfRangeResultCode(byte resultCode)
        {
            using NetworkBuffer buffer = NetworkBufferPool.Get();
            buffer.WriteUInt(1u);
            buffer.WriteInt(1);
            buffer.WriteInt(2);
            buffer.WriteFloat(0f);
            buffer.WriteByte(resultCode);
            buffer.WriteByte(0);
            buffer.WriteFloat(0f);
            buffer.WriteFloat(0f);
            buffer.WriteFloat(0f);
            buffer.FlipForRead();

            Assert.Throws<System.InvalidOperationException>(() => buffer.ReadDamageResult());
        }

        [Test]
        public void DamageRequest_WriteRejectsMalformedValuesAndIdentifiers()
        {
            var message = new DamageRequestMessage
            {
                Sequence = 1u,
                InstigatorActorId = 1,
                TargetActorId = 2,
                WeaponOrAbilityId = 0,
                DamageEventType = 0,
                RequestedDamage = float.NaN,
                ShotOrigin = NetworkVector3.Zero,
                HitLocation = NetworkVector3.Zero,
                ClientTimeSeconds = 0f
            };

            using NetworkBuffer buffer = NetworkBufferPool.Get();
            Assert.Throws<System.InvalidOperationException>(() => buffer.WriteDamageRequest(message));

            message.RequestedDamage = 1f;
            message.InstigatorActorId = 0;
            Assert.Throws<System.InvalidOperationException>(() => buffer.WriteDamageRequest(message));
            message.InstigatorActorId = 1;
            message.WeaponOrAbilityId = -1;
            Assert.Throws<System.InvalidOperationException>(() => buffer.WriteDamageRequest(message));
        }

        [Test]
        public void DamageReadersRejectTruncatedAndTrailingPayloads()
        {
            var request = new DamageRequestMessage
            {
                Sequence = 1u,
                InstigatorActorId = 1,
                TargetActorId = 2,
                WeaponOrAbilityId = 0,
                RequestedDamage = 1f,
                ShotOrigin = NetworkVector3.Zero,
                HitLocation = NetworkVector3.Zero,
                ClientTimeSeconds = 0f
            };
            byte[] requestBytes;
            using (NetworkBuffer writer = NetworkBufferPool.Get())
            {
                writer.WriteDamageRequest(request);
                requestBytes = Copy(writer.ToArraySegment());
            }

            AssertMalformedRequestPayload(
                new ReadOnlySpan<byte>(requestBytes, 0, requestBytes.Length - 1));
            byte[] requestTrailing = new byte[requestBytes.Length + 1];
            Buffer.BlockCopy(requestBytes, 0, requestTrailing, 0, requestBytes.Length);
            AssertMalformedRequestPayload(requestTrailing);

            DamageResultMessage result = CreateDamageResult();
            byte[] resultBytes;
            using (NetworkBuffer writer = NetworkBufferPool.Get())
            {
                writer.WriteDamageResult(result);
                resultBytes = Copy(writer.ToArraySegment());
            }

            AssertMalformedResultPayload(
                new ReadOnlySpan<byte>(resultBytes, 0, resultBytes.Length - 1));
            byte[] resultTrailing = new byte[resultBytes.Length + 1];
            Buffer.BlockCopy(resultBytes, 0, resultTrailing, 0, resultBytes.Length);
            AssertMalformedResultPayload(resultTrailing);
        }

        [Test]
        public void Protocol_Registers_Damage_Messages()
        {
            var catalog = new NetworkMessageCatalog();

            GameplayFrameworkNetworkProtocol.RegisterMessageCatalog(catalog);

            Assert.IsTrue(catalog.TryGet(GameplayFrameworkNetworkProtocol.MsgDamageRequest, out NetworkMessageDescriptor request));
            Assert.AreEqual(NetworkChannel.Reliable, request.DefaultChannel);
            Assert.IsTrue(catalog.TryGet(GameplayFrameworkNetworkProtocol.MsgDamageResult, out NetworkMessageDescriptor resultDescriptor));
            Assert.AreEqual(NetworkChannel.Reliable, resultDescriptor.DefaultChannel);
            Assert.IsTrue(GameplayFrameworkNetworkProtocol.MessageRange.Contains(GameplayFrameworkNetworkProtocol.MsgDamageRequest));
            Assert.IsTrue(GameplayFrameworkNetworkProtocol.MessageRange.Contains(GameplayFrameworkNetworkProtocol.MsgDamageResult));
        }

        [Test]
        public void Processor_Accept_Updates_Cooldown_And_Builds_Result()
        {
            var processor = new ServerAuthoritativeDamageProcessor();

            ServerDamageValidationResult result = processor.Process(
                MakeRequest(currentTime: 10f, lastAcceptedTime: processor.CooldownTracker.GetLastAcceptedTime(1)),
                out DamageResultMessage resultMessage,
                requestSequence: 99u,
                damageEventType: 1,
                hitLocation: new NetworkVector3(1f, 0f, 0f));

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(10d, processor.CooldownTracker.GetLastAcceptedTime(1));
            Assert.AreEqual(99u, resultMessage.RequestSequence);
            Assert.AreEqual(ServerDamageRejectReason.Accepted, resultMessage.ResultCode);
            Assert.AreEqual(result.ApprovedDamage, resultMessage.AppliedDamage);
        }

        [Test]
        public void Processor_Reject_Does_Not_Update_Cooldown()
        {
            var processor = new ServerAuthoritativeDamageProcessor();

            processor.Process(
                MakeRequest(ownerConn: 10, requestConn: 11),
                out DamageResultMessage resultMessage);

            Assert.AreEqual(double.NegativeInfinity, processor.CooldownTracker.GetLastAcceptedTime(1));
            Assert.AreEqual(ServerDamageRejectReason.OwnershipMismatch, resultMessage.ResultCode);
            Assert.AreEqual(0f, resultMessage.AppliedDamage);
        }

        [Test]
        public void Processor_FailsClosed_When_CustomValidatorReturnsMalformedResult()
        {
            var processor = new ServerAuthoritativeDamageProcessor(new DefaultResultValidator());

            ServerDamageValidationResult result = processor.Process(
                MakeRequest(),
                out DamageResultMessage resultMessage);

            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(ServerDamageRejectReason.Custom, result.Reason);
            Assert.AreEqual(0f, result.ApprovedDamage);
            Assert.AreEqual(ServerDamageRejectReason.Custom, resultMessage.ResultCode);
            Assert.AreEqual(0f, resultMessage.AppliedDamage);
            Assert.AreEqual(double.NegativeInfinity, processor.CooldownTracker.GetLastAcceptedTime(1));
        }

        [Test]
        public void ProcessorRunsBaselineBeforeCustomValidator()
        {
            AssertBaselineRejectsBeforeCustom(MakeRequest(instigatorId: 0));
            AssertBaselineRejectsBeforeCustom(MakeRequest(ownerConn: 0));
            AssertBaselineRejectsBeforeCustom(MakeRequest(currentTime: float.NaN));
            AssertBaselineRejectsBeforeCustom(MakeRequest(
                instigatorPos: new NetworkVector3(float.NaN, 0f, 0f)));
        }

        [TestCase(-1, 2, 0, 2)]
        [TestCase(1, -2, 1, 0)]
        [TestCase(int.MinValue, -1, 0, 0)]
        public void ProcessorProducesSerializableFailClosedResultForNegativeActorIds(
            int instigatorActorId,
            int targetActorId,
            int expectedInstigatorActorId,
            int expectedTargetActorId)
        {
            var processor = new ServerAuthoritativeDamageProcessor();

            ServerDamageValidationResult result = processor.Process(
                MakeRequest(instigatorId: instigatorActorId, targetId: targetActorId),
                out DamageResultMessage resultMessage);

            Assert.AreEqual(ServerDamageRejectReason.InvalidPayload, result.Reason);
            Assert.AreEqual(expectedInstigatorActorId, resultMessage.InstigatorActorId);
            Assert.AreEqual(expectedTargetActorId, resultMessage.TargetActorId);
            using NetworkBuffer buffer = NetworkBufferPool.Get();
            Assert.DoesNotThrow(() => buffer.WriteDamageResult(resultMessage));
            buffer.FlipForRead();
            DamageResultMessage roundTripped = buffer.ReadDamageResult();
            Assert.AreEqual(resultMessage.InstigatorActorId, roundTripped.InstigatorActorId);
            Assert.AreEqual(resultMessage.TargetActorId, roundTripped.TargetActorId);
            Assert.AreEqual(ServerDamageRejectReason.InvalidPayload, roundTripped.ResultCode);
        }

        [Test]
        public void ProcessorRejectsInvalidHitLocationBeforeCustomOrCooldownMutation()
        {
            var validator = new CountingAcceptValidator(25f);
            var processor = new ServerAuthoritativeDamageProcessor(validator);

            ServerDamageValidationResult result = processor.Process(
                MakeRequest(currentTime: 10f),
                out DamageResultMessage resultMessage,
                hitLocation: new NetworkVector3(float.NaN, 0f, 0f));

            Assert.AreEqual(ServerDamageRejectReason.InvalidPayload, result.Reason);
            Assert.AreEqual(0, validator.CallCount);
            Assert.AreEqual(double.NegativeInfinity, processor.CooldownTracker.GetLastAcceptedTime(1));
            Assert.IsTrue(resultMessage.HitLocation.IsFinite());
            using NetworkBuffer buffer = NetworkBufferPool.Get();
            Assert.DoesNotThrow(() => buffer.WriteDamageResult(resultMessage));
        }

        [Test]
        public void ProcessorRejectsCustomDamageAboveBaselineCap()
        {
            var validator = new CountingAcceptValidator(100f);
            var processor = new ServerAuthoritativeDamageProcessor(validator);

            ServerDamageValidationResult result = processor.Process(
                MakeRequest(requestedDamage: 100f, maxDamage: 40f),
                out DamageResultMessage resultMessage);

            Assert.AreEqual(1, validator.CallCount);
            Assert.AreEqual(ServerDamageRejectReason.Custom, result.Reason);
            Assert.AreEqual(0f, resultMessage.AppliedDamage);
            Assert.AreEqual(double.NegativeInfinity, processor.CooldownTracker.GetLastAcceptedTime(1));
        }

        [Test]
        public void ProcessorUsesOwnCooldownTrackerInsteadOfCallerTimestamp()
        {
            var processor = new ServerAuthoritativeDamageProcessor();
            ServerDamageValidationResult first = processor.Process(
                MakeRequest(currentTime: 10d, lastAcceptedTime: double.NegativeInfinity, cooldown: 0.5d),
                out _);

            ServerDamageValidationResult bypassAttempt = processor.Process(
                MakeRequest(currentTime: 10.2d, lastAcceptedTime: double.NegativeInfinity, cooldown: 0.5d),
                out _);

            Assert.IsTrue(first.Accepted);
            Assert.AreEqual(ServerDamageRejectReason.OnCooldown, bypassAttempt.Reason);
            Assert.AreEqual(10d, processor.CooldownTracker.GetLastAcceptedTime(1));
        }

        [Test]
        public void CooldownTrackerRejectsTimeRegressionWithoutMutation()
        {
            var tracker = new DamageCooldownTracker();
            Assert.IsTrue(tracker.TryMarkAccepted(1, 10d));

            Assert.Throws<System.InvalidOperationException>(() => tracker.TryMarkAccepted(1, 9d));
            Assert.AreEqual(10d, tracker.GetLastAcceptedTime(1));
        }

        [Test]
        public void Processor_Enforces_Cooldown_Across_Two_Shots()
        {
            var processor = new ServerAuthoritativeDamageProcessor();

            ServerDamageValidationResult first = processor.Process(
                MakeRequest(currentTime: 10.0f, lastAcceptedTime: processor.CooldownTracker.GetLastAcceptedTime(1), cooldown: 0.5f),
                out _);
            Assert.IsTrue(first.Accepted);

            ServerDamageValidationResult tooSoon = processor.Process(
                MakeRequest(currentTime: 10.2f, lastAcceptedTime: processor.CooldownTracker.GetLastAcceptedTime(1), cooldown: 0.5f),
                out _);
            Assert.AreEqual(ServerDamageRejectReason.OnCooldown, tooSoon.Reason);

            ServerDamageValidationResult later = processor.Process(
                MakeRequest(currentTime: 10.6f, lastAcceptedTime: processor.CooldownTracker.GetLastAcceptedTime(1), cooldown: 0.5f),
                out _);
            Assert.IsTrue(later.Accepted);
        }

        [Test]
        public void ProcessorRetainsSubSecondCooldownPrecisionAfterLongSessionEpoch()
        {
            const double LongSessionEpoch = 16_777_216d;
            var processor = new ServerAuthoritativeDamageProcessor();

            ServerDamageValidationResult first = processor.Process(
                MakeRequest(currentTime: LongSessionEpoch, cooldown: 0.5d),
                out _);
            ServerDamageValidationResult tooSoon = processor.Process(
                MakeRequest(currentTime: LongSessionEpoch + 0.25d, cooldown: 0.5d),
                out _);
            ServerDamageValidationResult elapsed = processor.Process(
                MakeRequest(currentTime: LongSessionEpoch + 0.5d, cooldown: 0.5d),
                out _);

            Assert.IsTrue(first.Accepted);
            Assert.AreEqual(ServerDamageRejectReason.OnCooldown, tooSoon.Reason);
            Assert.IsTrue(elapsed.Accepted);
            Assert.AreEqual(LongSessionEpoch + 0.5d, processor.CooldownTracker.GetLastAcceptedTime(1));
        }

        [Test]
        public void ProcessorFailsClosedWithDedicatedReasonWhenCooldownCapacityIsFull()
        {
            var tracker = new DamageCooldownTracker(initialCapacity: 1, maximumTrackedInstigators: 1);
            var processor = new ServerAuthoritativeDamageProcessor(cooldownTracker: tracker);

            Assert.IsTrue(processor.Process(MakeRequest(instigatorId: 1, targetId: 3), out _).Accepted);
            ServerDamageValidationResult rejected = processor.Process(
                MakeRequest(instigatorId: 2, targetId: 3),
                out DamageResultMessage rejectedMessage);
            DamageCooldownTrackerSnapshot snapshot = tracker.GetAdmissionSnapshot();

            Assert.AreEqual(ServerDamageRejectReason.CooldownCapacityReached, rejected.Reason);
            Assert.AreEqual(ServerDamageRejectReason.CooldownCapacityReached, rejectedMessage.ResultCode);
            Assert.AreEqual(1, snapshot.TrackedCount);
            Assert.AreEqual(1, snapshot.MaximumTrackedInstigators);
            Assert.AreEqual(1L, snapshot.RejectedAdmissionCount);

            Assert.IsTrue(tracker.Remove(1));
            Assert.IsTrue(processor.Process(MakeRequest(instigatorId: 2, targetId: 3), out _).Accepted);
        }

        [Test]
        public void CooldownTrackerRejectsWorkerThreadAccess()
        {
            var tracker = new DamageCooldownTracker();
            Exception captured = null;
            using var completed = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                try
                {
                    tracker.GetLastAcceptedTime(1);
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
                finally
                {
                    completed.Set();
                }
            });

            thread.Start();
            Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
            thread.Join();
            Assert.IsInstanceOf<InvalidOperationException>(captured);
        }


        private static void AssertInvalid(in ServerDamageValidationRequest request)
        {
            Assert.AreEqual(
                ServerDamageRejectReason.InvalidPayload,
                DefaultServerDamageValidator.Instance.Validate(in request).Reason);
        }

        private static DamageResultMessage CreateDamageResult(
            float appliedDamage = 1f,
            ServerDamageRejectReason resultCode = ServerDamageRejectReason.Accepted,
            NetworkVector3 hitLocation = default,
            int instigatorActorId = 1,
            int targetActorId = 2)
        {
            return new DamageResultMessage
            {
                RequestSequence = 1u,
                InstigatorActorId = instigatorActorId,
                TargetActorId = targetActorId,
                AppliedDamage = appliedDamage,
                ResultCode = resultCode,
                DamageEventType = 0,
                HitLocation = hitLocation
            };
        }

        private static byte[] Copy(ArraySegment<byte> segment)
        {
            var result = new byte[segment.Count];
            Buffer.BlockCopy(segment.Array, segment.Offset, result, 0, segment.Count);
            return result;
        }

        private static void AssertMalformedRequestPayload(ReadOnlySpan<byte> payload)
        {
            using NetworkBuffer reader = NetworkBufferPool.GetWithData(payload);
            Assert.Throws<InvalidOperationException>(() => reader.ReadDamageRequest());
        }

        private static void AssertMalformedResultPayload(ReadOnlySpan<byte> payload)
        {
            using NetworkBuffer reader = NetworkBufferPool.GetWithData(payload);
            Assert.Throws<InvalidOperationException>(() => reader.ReadDamageResult());
        }

        private static void AssertBaselineRejectsBeforeCustom(
            in ServerDamageValidationRequest request)
        {
            var validator = new CountingAcceptValidator(25f);
            var processor = new ServerAuthoritativeDamageProcessor(validator);

            ServerDamageValidationResult result = processor.Process(in request, out _);

            Assert.AreEqual(ServerDamageRejectReason.InvalidPayload, result.Reason);
            Assert.AreEqual(0, validator.CallCount);
            Assert.AreEqual(0, processor.CooldownTracker.TrackedCount);
        }

        private sealed class CountingAcceptValidator : IServerDamageValidator
        {
            private readonly float approvedDamage;

            public CountingAcceptValidator(float approvedDamage)
            {
                this.approvedDamage = approvedDamage;
            }

            public int CallCount { get; private set; }

            public ServerDamageValidationResult Validate(in ServerDamageValidationRequest request)
            {
                CallCount++;
                return ServerDamageValidationResult.Accept(approvedDamage);
            }
        }

        private sealed class DefaultResultValidator : IServerDamageValidator
        {
            public ServerDamageValidationResult Validate(in ServerDamageValidationRequest request)
            {
                return default;
            }
        }
    }
}
