using CycloneGames.RPGFoundation.Interaction.Runtime;
using NUnit.Framework;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Tests.Editor
{
    public sealed class InteractionAuthorityServiceTests
    {
        private const int WORLD_ID = 7;
        private const ulong INSTIGATOR_ID = 1001UL;
        private const ulong TARGET_ID = 2001UL;

        [Test]
        public void ValidateRequest_AcceptsStableRequestWithinRange()
        {
            var service = CreateService();
            Assert.That(service.TryRegisterTarget(CreateTarget()), Is.True);

            var request = new InteractionRequest(1, INSTIGATOR_ID, TARGET_ID, "open", tick: 100, worldId: WORLD_ID);
            InteractionValidationResult result = service.ValidateRequest(request, new InteractionVector3(3f, 0f, 4f), serverTick: 100);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Target.TargetStableId, Is.EqualTo(TARGET_ID));

            InteractionMetricsSnapshot metrics = service.Metrics.GetSnapshot();
            Assert.That(metrics.TotalRequests, Is.EqualTo(1));
            Assert.That(metrics.AcceptedRequests, Is.EqualTo(1));
            Assert.That(metrics.RejectedRequests, Is.EqualTo(0));
        }

        [Test]
        public void ValidateRequest_RejectsWrongWorldBeforeTargetLookup()
        {
            var service = CreateService();
            service.TryRegisterTarget(CreateTarget());

            var request = new InteractionRequest(1, INSTIGATOR_ID, TARGET_ID, "open", tick: 100, worldId: WORLD_ID + 1);
            InteractionValidationResult result = service.ValidateRequest(request, InteractionVector3.Zero, serverTick: 100);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Failure, Is.EqualTo(InteractionValidationFailure.WrongWorld));
            Assert.That(service.Metrics.GetRejectedCount(InteractionValidationFailure.WrongWorld), Is.EqualTo(1));
        }

        [Test]
        public void ValidateRequest_RejectsDuplicateRequestIds()
        {
            var service = CreateService();
            service.TryRegisterTarget(CreateTarget());

            var request = new InteractionRequest(42, INSTIGATOR_ID, TARGET_ID, "open", tick: 100, worldId: WORLD_ID);

            Assert.That(service.ValidateRequest(request, InteractionVector3.Zero, serverTick: 100).IsAccepted, Is.True);

            InteractionValidationResult duplicate = service.ValidateRequest(request, InteractionVector3.Zero, serverTick: 101);

            Assert.That(duplicate.IsAccepted, Is.False);
            Assert.That(duplicate.Failure, Is.EqualTo(InteractionValidationFailure.DuplicateRequest));
        }

        [Test]
        public void ValidateRequest_DoesNotTreatSameRequestIdFromDifferentInstigatorsAsDuplicate()
        {
            var service = CreateService();
            service.TryRegisterTarget(CreateTarget());

            var first = new InteractionRequest(42, INSTIGATOR_ID, TARGET_ID, "open", tick: 100, worldId: WORLD_ID);
            var second = new InteractionRequest(42, INSTIGATOR_ID + 1UL, TARGET_ID, "open", tick: 101, worldId: WORLD_ID);

            Assert.That(service.ValidateRequest(first, InteractionVector3.Zero, serverTick: 100).IsAccepted, Is.True);
            Assert.That(service.ValidateRequest(second, InteractionVector3.Zero, serverTick: 101).IsAccepted, Is.True);
        }

        [Test]
        public void ValidateRequest_DoesNotTreatSameRequestIdFromDifferentLocalInstigatorsAsDuplicate()
        {
            var service = new InteractionAuthorityService(new InteractionAuthorityOptions(
                worldId: WORLD_ID,
                requireStableIds: false));
            service.TryRegisterTarget(CreateTarget());

            var first = new InteractionRequest(42, 101, 0, InteractionStableId.None, TARGET_ID, "open", 100, WORLD_ID);
            var second = new InteractionRequest(42, 102, 0, InteractionStableId.None, TARGET_ID, "open", 101, WORLD_ID);

            Assert.That(service.ValidateRequest(first, InteractionVector3.Zero, serverTick: 100).IsAccepted, Is.True);
            Assert.That(service.ValidateRequest(second, InteractionVector3.Zero, serverTick: 101).IsAccepted, Is.True);
        }

        [Test]
        public void ValidateRequest_UsesPluggablePositionProvider()
        {
            var service = CreateService();
            service.TryRegisterTarget(CreateTarget());

            var request = new InteractionRequest(7, INSTIGATOR_ID, TARGET_ID, "open", tick: 100, worldId: WORLD_ID);
            var provider = new TestInteractionPositionProvider(new InteractionVector3(3f, 0f, 4f), hasPosition: true);

            InteractionValidationResult result = service.ValidateRequest(request, provider, serverTick: 100);

            Assert.That(result.IsAccepted, Is.True);
        }

        [Test]
        public void ValidateRequest_RejectsMissingPositionProvider()
        {
            var service = CreateService();
            service.TryRegisterTarget(CreateTarget());

            var request = new InteractionRequest(7, INSTIGATOR_ID, TARGET_ID, "open", tick: 100, worldId: WORLD_ID);
            var provider = new TestInteractionPositionProvider(InteractionVector3.Zero, hasPosition: false);

            InteractionValidationResult result = service.ValidateRequest(request, provider, serverTick: 100);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Failure, Is.EqualTo(InteractionValidationFailure.InvalidRequest));
            Assert.That(service.Metrics.GetRejectedCount(InteractionValidationFailure.InvalidRequest), Is.EqualTo(1));
        }

        [Test]
        public void ValidateRequest_RejectsOutOfRangeAndUnknownAction()
        {
            var service = CreateService();
            service.TryRegisterTarget(CreateTarget());

            var farRequest = new InteractionRequest(1, INSTIGATOR_ID, TARGET_ID, "open", tick: 100, worldId: WORLD_ID);
            InteractionValidationResult far = service.ValidateRequest(farRequest, new InteractionVector3(6f, 0f, 0f), serverTick: 100);

            var actionRequest = new InteractionRequest(2, INSTIGATOR_ID, TARGET_ID, "hack", tick: 101, worldId: WORLD_ID);
            InteractionValidationResult action = service.ValidateRequest(actionRequest, InteractionVector3.Zero, serverTick: 101);

            Assert.That(far.Failure, Is.EqualTo(InteractionValidationFailure.OutOfRange));
            Assert.That(action.Failure, Is.EqualTo(InteractionValidationFailure.ActionNotAllowed));
        }

        [Test]
        public void ValidateRequest_RateLimitsPerInstigator()
        {
            var service = new InteractionAuthorityService(new InteractionAuthorityOptions(
                worldId: WORLD_ID,
                rateLimitWindowTicks: 10,
                maxRequestsPerRateLimitWindow: 2));
            service.TryRegisterTarget(CreateTarget());

            Assert.That(service.ValidateRequest(new InteractionRequest(1, INSTIGATOR_ID, TARGET_ID, "open", 100, WORLD_ID), InteractionVector3.Zero, 100).IsAccepted, Is.True);
            Assert.That(service.ValidateRequest(new InteractionRequest(2, INSTIGATOR_ID, TARGET_ID, "open", 101, WORLD_ID), InteractionVector3.Zero, 101).IsAccepted, Is.True);

            InteractionValidationResult limited = service.ValidateRequest(new InteractionRequest(3, INSTIGATOR_ID, TARGET_ID, "open", 102, WORLD_ID), InteractionVector3.Zero, 102);

            Assert.That(limited.IsAccepted, Is.False);
            Assert.That(limited.Failure, Is.EqualTo(InteractionValidationFailure.RateLimited));
        }

        [Test]
        public void ValidateRequest_RejectsWhenRequestHistoryCapacityIsReached()
        {
            var service = new InteractionAuthorityService(new InteractionAuthorityOptions(
                worldId: WORLD_ID,
                maxRequestsPerRateLimitWindow: 100,
                requestHistoryWindowTicks: 600,
                requestHistoryCapacity: 2));
            service.TryRegisterTarget(CreateTarget());

            Assert.That(service.ValidateRequest(new InteractionRequest(1, INSTIGATOR_ID, TARGET_ID, "open", 100, WORLD_ID), InteractionVector3.Zero, 100).IsAccepted, Is.True);
            Assert.That(service.ValidateRequest(new InteractionRequest(2, INSTIGATOR_ID, TARGET_ID, "open", 101, WORLD_ID), InteractionVector3.Zero, 101).IsAccepted, Is.True);

            InteractionValidationResult full = service.ValidateRequest(new InteractionRequest(3, INSTIGATOR_ID, TARGET_ID, "open", 102, WORLD_ID), InteractionVector3.Zero, 102);

            Assert.That(full.IsAccepted, Is.False);
            Assert.That(full.Failure, Is.EqualTo(InteractionValidationFailure.RequestHistoryFull));
        }

        [Test]
        public void ValidateRequest_PurgesRequestHistoryByServerTick()
        {
            var service = new InteractionAuthorityService(new InteractionAuthorityOptions(
                worldId: WORLD_ID,
                maxRequestsPerRateLimitWindow: 100,
                requestHistoryWindowTicks: 1,
                requestHistoryCapacity: 1));
            service.TryRegisterTarget(CreateTarget());

            Assert.That(service.ValidateRequest(new InteractionRequest(1, INSTIGATOR_ID, TARGET_ID, "open", 100, WORLD_ID), InteractionVector3.Zero, 100).IsAccepted, Is.True);

            InteractionValidationResult afterWindow = service.ValidateRequest(
                new InteractionRequest(2, INSTIGATOR_ID, TARGET_ID, "open", 102, WORLD_ID),
                InteractionVector3.Zero,
                serverTick: 102);

            Assert.That(afterWindow.IsAccepted, Is.True);
        }

        [Test]
        public void TryQueueRequest_RejectsPerInstigatorQueueOverflow()
        {
            var service = new InteractionAuthorityService(new InteractionAuthorityOptions(
                worldId: WORLD_ID,
                queueCapacityPerTarget: 4,
                maxQueuedRequestsPerInstigator: 1));
            service.TryRegisterTarget(CreateTarget());

            InteractionValidationResult first = service.TryQueueRequest(
                new InteractionRequest(1, INSTIGATOR_ID, TARGET_ID, "open", 100, WORLD_ID),
                InteractionVector3.Zero,
                serverTick: 100);

            InteractionValidationResult second = service.TryQueueRequest(
                new InteractionRequest(2, INSTIGATOR_ID, TARGET_ID, "open", 101, WORLD_ID),
                InteractionVector3.Zero,
                serverTick: 101);

            Assert.That(first.IsAccepted, Is.True);
            Assert.That(first.QueuePosition, Is.EqualTo(1));
            Assert.That(second.IsAccepted, Is.False);
            Assert.That(second.Failure, Is.EqualTo(InteractionValidationFailure.TooManyQueuedForInstigator));
        }

        [Test]
        public void TryQueueRequest_AllowsSameRequestIdFromDifferentInstigators()
        {
            var service = new InteractionAuthorityService(new InteractionAuthorityOptions(
                worldId: WORLD_ID,
                queueCapacityPerTarget: 4,
                maxQueuedRequestsPerInstigator: 2));
            service.TryRegisterTarget(CreateTarget());

            InteractionValidationResult first = service.TryQueueRequest(
                new InteractionRequest(42, INSTIGATOR_ID, TARGET_ID, "open", 100, WORLD_ID),
                InteractionVector3.Zero,
                serverTick: 100);

            InteractionValidationResult second = service.TryQueueRequest(
                new InteractionRequest(42, INSTIGATOR_ID + 1UL, TARGET_ID, "open", 101, WORLD_ID),
                InteractionVector3.Zero,
                serverTick: 101);

            Assert.That(first.IsAccepted, Is.True);
            Assert.That(second.IsAccepted, Is.True);
            Assert.That(second.QueuePosition, Is.EqualTo(2));
        }

        private static InteractionAuthorityService CreateService()
        {
            return new InteractionAuthorityService(new InteractionAuthorityOptions(worldId: WORLD_ID));
        }

        private static InteractionTargetSnapshot CreateTarget()
        {
            return new InteractionTargetSnapshot(
                WORLD_ID,
                TARGET_ID,
                InteractionVector3.Zero,
                interactionRange: 5f,
                isAvailable: true,
                allowDefaultAction: true,
                enabledActionIds: new[] { "open" });
        }

        private sealed class TestInteractionPositionProvider : IInteractionPositionProvider
        {
            private readonly InteractionVector3 _position;
            private readonly bool _hasPosition;

            public TestInteractionPositionProvider(InteractionVector3 position, bool hasPosition)
            {
                _position = position;
                _hasPosition = hasPosition;
            }

            public bool TryGetInteractionPosition(out InteractionVector3 position)
            {
                position = _position;
                return _hasPosition;
            }
        }
    }
}
