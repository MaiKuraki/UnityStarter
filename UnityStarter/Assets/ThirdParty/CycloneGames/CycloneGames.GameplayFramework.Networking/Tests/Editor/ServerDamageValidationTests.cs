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
            NetworkBufferPool.ResetConfiguration();
        }

        [TearDown]
        public void TearDown()
        {
            NetworkBufferPool.Clear();
            NetworkBufferPool.ResetConfiguration();
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
            float currentTime = 10f,
            float lastAcceptedTime = float.NegativeInfinity,
            float cooldown = 0.5f,
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
        public void CooldownTracker_Returns_Sentinel_When_Unknown()
        {
            var tracker = new DamageCooldownTracker();

            Assert.AreEqual(float.NegativeInfinity, tracker.GetLastAcceptedTime(1));
        }

        [Test]
        public void CooldownTracker_Records_And_Removes()
        {
            var tracker = new DamageCooldownTracker();

            tracker.MarkAccepted(1, 5f);
            Assert.AreEqual(5f, tracker.GetLastAcceptedTime(1));
            Assert.AreEqual(1, tracker.TrackedCount);

            tracker.Remove(1);
            Assert.AreEqual(float.NegativeInfinity, tracker.GetLastAcceptedTime(1));
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
            tracker.MarkAccepted(instigatorId, 10.0f);

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
                ResultCode = (byte)ServerDamageRejectReason.Accepted,
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
        public void DamageRequest_Read_Rejects_NonFinite()
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
            buffer.WriteDamageRequest(message);
            buffer.FlipForRead();

            Assert.Throws<System.InvalidOperationException>(() => buffer.ReadDamageRequest());
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
            Assert.AreEqual(10f, processor.CooldownTracker.GetLastAcceptedTime(1));
            Assert.AreEqual(99u, resultMessage.RequestSequence);
            Assert.AreEqual((byte)ServerDamageRejectReason.Accepted, resultMessage.ResultCode);
            Assert.AreEqual(result.ApprovedDamage, resultMessage.AppliedDamage);
        }

        [Test]
        public void Processor_Reject_Does_Not_Update_Cooldown()
        {
            var processor = new ServerAuthoritativeDamageProcessor();

            processor.Process(
                MakeRequest(ownerConn: 10, requestConn: 11),
                out DamageResultMessage resultMessage);

            Assert.AreEqual(float.NegativeInfinity, processor.CooldownTracker.GetLastAcceptedTime(1));
            Assert.AreEqual((byte)ServerDamageRejectReason.OwnershipMismatch, resultMessage.ResultCode);
            Assert.AreEqual(0f, resultMessage.AppliedDamage);
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
    }
}
