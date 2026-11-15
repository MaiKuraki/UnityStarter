using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Interaction.Core;
using CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Interaction.Tests.Editor.Integrations.DeterministicMath
{
    public sealed class InteractionDeterministicMathIntegrationTests
    {
        private const int WORLD_ID = 11;
        private const ulong INSTIGATOR_ID = 1001UL;
        private const ulong TARGET_ID = 2001UL;

        [Test]
        public void Authority_RejectsTargetRegistrationPastCeiling()
        {
            var authority = CreateAuthority();
            const int Ceiling = 65_536;
            const int Attempts = Ceiling + 512;

            int admitted = 0;
            for (int i = 0; i < Attempts; i++)
            {
                var target = new InteractionDeterministicTargetSnapshot(
                    WORLD_ID,
                    (ulong)(10_000 + i),
                    FPVector3.Zero,
                    FPInt64.FromInt(5),
                    isAvailable: true);
                if (authority.TryRegisterTarget(target))
                {
                    admitted++;
                }
            }

            Assert.That(admitted, Is.EqualTo(Ceiling),
                "registration must stop at the implementation ceiling instead of growing without bound");
            Assert.That(authority.RegisteredTargetCount, Is.EqualTo(Ceiling));
        }

        [Test]
        public void Authority_RejectsQueueOwnerPastCeiling()
        {
            var authority = CreateAuthority();
            const int Ceiling = 65_536;
            const int Attempts = Ceiling + 512;

            int admitted = 0;
            for (int i = 0; i < Attempts; i++)
            {
                if (authority.TryGetOrCreateQueue((ulong)(20_000 + i), out _))
                {
                    admitted++;
                }
            }

            Assert.That(admitted, Is.EqualTo(Ceiling));
            Assert.That(authority.QueueOwnerCount, Is.EqualTo(Ceiling));
            Assert.That(authority.RejectedQueueOwnerAdmissionCount, Is.EqualTo(512),
                "refused admissions must be observable so operators can see saturation");

            // Existing owners keep working at capacity.
            Assert.That(authority.TryGetOrCreateQueue(20_000UL, out _), Is.True);
        }

        [Test]
        public void Authority_RejectedRequestsDoNotConsumeReplayHistory()
        {
            var authority = new InteractionDeterministicAuthorityService(
                new InteractionAuthorityOptions(
                    worldId: WORLD_ID,
                    requireStableIds: true,
                    rateLimitWindowTicks: 0,
                    maxRequestsPerRateLimitWindow: 0,
                    requestHistoryWindowTicks: 10_000,
                    requestHistoryCapacity: 256));

            const int Attempts = 2_000;
            for (int i = 1; i <= Attempts; i++)
            {
                var request = new InteractionRequest(i, INSTIGATOR_ID, (ulong)(900_000 + i), "open", tick: 100, worldId: WORLD_ID);
                InteractionValidationResult result = authority.ValidateRequest(request, FPVector3.Zero, serverTick: 100);
                Assert.That(result.Failure, Is.EqualTo(InteractionValidationFailure.UnknownTarget));
            }

            Assert.That(authority.Metrics.GetRejectedCount(InteractionValidationFailure.RequestHistoryFull), Is.Zero,
                "requests rejected before target lookup must not consume the replay-history budget");
        }

        [Test]
        public void DeterministicAuthority_AcceptsRequestWithinFixedRange()
        {
            var authority = CreateAuthority();
            authority.TryRegisterTarget(CreateTarget());
            var request = new InteractionRequest(1, INSTIGATOR_ID, TARGET_ID, "open", tick: 100, worldId: WORLD_ID);

            InteractionValidationResult result = authority.ValidateRequest(
                request,
                new FPVector3(FPInt64.FromInt(3), FPInt64.Zero, FPInt64.FromInt(4)),
                serverTick: 100);

            Assert.That(result.IsAccepted, Is.True);
        }

        [Test]
        public void DeterministicAuthority_RejectsRequestOutsideFixedRange()
        {
            var authority = CreateAuthority();
            authority.TryRegisterTarget(CreateTarget());
            var request = new InteractionRequest(1, INSTIGATOR_ID, TARGET_ID, "open", tick: 100, worldId: WORLD_ID);

            InteractionValidationResult result = authority.ValidateRequest(
                request,
                new FPVector3(FPInt64.FromInt(6), FPInt64.Zero, FPInt64.Zero),
                serverTick: 100);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Failure, Is.EqualTo(InteractionValidationFailure.OutOfRange));
        }

        [Test]
        public void DeterministicAuthority_UsesSharedRequestHistoryCapacity()
        {
            var authority = new InteractionDeterministicAuthorityService(new InteractionAuthorityOptions(
                worldId: WORLD_ID,
                maxRequestsPerRateLimitWindow: 100,
                requestHistoryWindowTicks: 600,
                requestHistoryCapacity: 1));
            authority.TryRegisterTarget(CreateTarget());

            Assert.That(authority.ValidateRequest(
                new InteractionRequest(1, INSTIGATOR_ID, TARGET_ID, "open", 100, WORLD_ID),
                FPVector3.Zero,
                serverTick: 100).IsAccepted, Is.True);

            InteractionValidationResult full = authority.ValidateRequest(
                new InteractionRequest(2, INSTIGATOR_ID, TARGET_ID, "open", 101, WORLD_ID),
                FPVector3.Zero,
                serverTick: 101);

            Assert.That(full.IsAccepted, Is.False);
            Assert.That(full.Failure, Is.EqualTo(InteractionValidationFailure.RequestHistoryFull));
        }

        [Test]
        public void DeterministicRequest_ActsAsPositionProvider()
        {
            var authority = CreateAuthority();
            authority.TryRegisterTarget(CreateTarget());
            var request = new InteractionDeterministicRequest(
                2,
                INSTIGATOR_ID,
                TARGET_ID,
                "open",
                100,
                WORLD_ID,
                new FPVector3(FPInt64.FromInt(3), FPInt64.Zero, FPInt64.FromInt(4)));

            InteractionValidationResult result = authority.ValidateRequest(
                request.ToInteractionRequest(),
                request,
                serverTick: 100);

            Assert.That(result.IsAccepted, Is.True);
        }

        [Test]
        public void DeterministicVectorPayload_RoundTripsRawFixedPointValues()
        {
            var deterministic = new FPVector3(
                FPInt64.Parse("1.125"),
                FPInt64.Parse("-2.5"),
                FPInt64.Parse("3.75"));

            InteractionDeterministicVector3Payload payload = deterministic.ToDeterministicPayload();
            FPVector3 roundTrip = payload.ToFPVector3();

            Assert.That(roundTrip, Is.EqualTo(deterministic));
        }

        [Test]
        public void DeterministicRequestPayload_ActsAsRawFixedPositionProvider()
        {
            var authority = CreateAuthority();
            authority.TryRegisterTarget(CreateTarget());
            var payload = new InteractionDeterministicRequestPayload(
                3,
                INSTIGATOR_ID,
                TARGET_ID,
                "open",
                100,
                WORLD_ID,
                new FPVector3(FPInt64.FromInt(3), FPInt64.Zero, FPInt64.FromInt(4)));

            InteractionValidationResult result = authority.ValidateRequest(
                payload.ToInteractionRequest(),
                payload,
                serverTick: 100);

            Assert.That(payload.IsValid, Is.True);
            Assert.That(result.IsAccepted, Is.True);
            Assert.That(payload.ToDeterministicRequest().InstigatorPosition, Is.EqualTo(payload.InstigatorPosition.ToFPVector3()));
        }

        private static InteractionDeterministicAuthorityService CreateAuthority()
        {
            return new InteractionDeterministicAuthorityService(new InteractionAuthorityOptions(worldId: WORLD_ID));
        }

        private static InteractionDeterministicTargetSnapshot CreateTarget()
        {
            return new InteractionDeterministicTargetSnapshot(
                WORLD_ID,
                TARGET_ID,
                FPVector3.Zero,
                FPInt64.FromInt(5),
                isAvailable: true,
                allowDefaultAction: true,
                enabledActionIds: new[] { "open" });
        }
    }
}
