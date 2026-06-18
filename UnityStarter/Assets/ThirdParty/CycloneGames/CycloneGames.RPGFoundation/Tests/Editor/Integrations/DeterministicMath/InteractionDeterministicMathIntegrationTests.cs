using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Runtime.Interaction;
using CycloneGames.RPGFoundation.Runtime.Interaction.Integrations.DeterministicMath;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Tests.Editor.Integrations.DeterministicMath
{
    public sealed class InteractionDeterministicMathIntegrationTests
    {
        private const int WORLD_ID = 11;
        private const ulong INSTIGATOR_ID = 1001UL;
        private const ulong TARGET_ID = 2001UL;

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
                FPInt64.FromString("1.125"),
                FPInt64.FromString("-2.5"),
                FPInt64.FromString("3.75"));

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
