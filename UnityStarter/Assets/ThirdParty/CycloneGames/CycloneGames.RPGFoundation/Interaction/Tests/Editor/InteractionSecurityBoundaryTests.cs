using CycloneGames.RPGFoundation.Interaction.Networking;
using CycloneGames.RPGFoundation.Interaction.Core;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Interaction.Tests.Editor
{
    /// <summary>
    /// Regression coverage for the authority-boundary defects found in the 2026-11-15 audit.
    /// Each case fails on the pre-fix behaviour and passes on the current implementation.
    /// </summary>
    public sealed class InteractionSecurityBoundaryTests
    {
        [Test]
        public void RateLimiter_AnonymousCallersShareOneBoundedWindow()
        {
            var limiter = new InteractionRateLimiter(64);
            const int maxRequests = 4;
            const int windowTicks = 100;

            for (int i = 0; i < maxRequests; i++)
            {
                Assert.That(limiter.TryConsume(InteractionStableId.None, 10, maxRequests, windowTicks), Is.True,
                    "anonymous request " + i + " should be admitted inside the window budget");
            }

            Assert.That(limiter.TryConsume(InteractionStableId.None, 10, maxRequests, windowTicks), Is.False,
                "anonymous traffic must hit the same budget as an identified caller");
        }

        [Test]
        public void RateLimiter_ResumesAfterTickDomainRollback()
        {
            var limiter = new InteractionRateLimiter(64);
            const ulong key = 77UL;
            const int maxRequests = 2;
            const int windowTicks = 50;

            Assert.That(limiter.TryConsume(key, 1000, maxRequests, windowTicks), Is.True);
            Assert.That(limiter.TryConsume(key, 1001, maxRequests, windowTicks), Is.True);
            Assert.That(limiter.TryConsume(key, 1002, maxRequests, windowTicks), Is.False,
                "window must be saturated before the rollback");

            // Server restart: the tick domain jumps backwards, then advances again.
            Assert.That(limiter.TryConsume(key, 10, maxRequests, windowTicks), Is.True,
                "a backwards jump must not leave the window permanently frozen");

            for (int tick = 11; tick < 10 + windowTicks; tick++)
            {
                limiter.TryConsume(key, tick, maxRequests, windowTicks);
            }

            Assert.That(limiter.TryConsume(key, 10 + windowTicks + 5, maxRequests, windowTicks), Is.True,
                "the window must expire on the new timeline");
        }

        [Test]
        public void Queue_CountsAnonymousInstigatorsAgainstTheirOwnBudget()
        {
            var queue = new InteractionQueue(capacity: 8);
            var request = new InteractionRequest(1, 0, 0, InteractionStableId.None, 500UL, "open", 10);

            Assert.That(queue.TryEnqueue(request), Is.True);
            Assert.That(queue.CountQueuedForInstigator(InteractionStableId.None), Is.EqualTo(1),
                "anonymous queue accounting previously returned zero and bypassed the per-instigator cap");

            Assert.That(queue.TryDequeue(out _), Is.True);
            Assert.That(queue.CountQueuedForInstigator(InteractionStableId.None), Is.Zero);
        }

        [Test]
        public void Authority_RejectedRequestsDoNotConsumeReplayHistory()
        {
            var options = new InteractionAuthorityOptions(worldId: 1, requireStableIds: true);
            var service = new InteractionAuthorityService(options);

            // Every request targets an unknown id, so all of them are rejected before recording.
            for (int i = 1; i <= 200; i++)
            {
                var request = new InteractionRequest(i, 900UL + (ulong)i, 8000UL + (ulong)i, "open", 100, 1);
                InteractionValidationResult result = service.ValidateRequest(request, InteractionVector3.Zero, 100);
                Assert.That(result.Failure, Is.EqualTo(InteractionValidationFailure.UnknownTarget));
            }

            Assert.That(service.GetMemorySnapshot().RequestHistoryCount, Is.Zero,
                "rejects must not consume the globally shared history budget");

            // A legitimate request still fits and is accepted exactly once.
            service.TryRegisterTarget(new InteractionTargetSnapshot(
                worldId: 1,
                targetStableId: 8000UL,
                position: InteractionVector3.Zero,
                interactionRange: 5f,
                isAvailable: true,
                allowDefaultAction: true,
                enabledActionIds: new[] { "open" }));

            var accepted = new InteractionRequest(9001, 900UL, 8000UL, "open", 100, 1);
            Assert.That(service.ValidateRequest(accepted, InteractionVector3.Zero, 100).IsAccepted, Is.True);
            Assert.That(service.ValidateRequest(accepted, InteractionVector3.Zero, 100).Failure,
                Is.EqualTo(InteractionValidationFailure.DuplicateRequest));
        }

        [Test]
        public void ReplayWindow_FailsClosedInsteadOfEvictingLiveInstigators()
        {
            var window = new InteractionRequestReplayWindow();

            for (int i = 0; i < InteractionRequestReplayWindow.MaxTrackedInstigators; i++)
            {
                Assert.That(window.TryAccept((ulong)(i + 1), 1), Is.True, "admission " + i);
            }

            Assert.That(window.TryAccept(999_999UL, 1), Is.False,
                "a full table must reject new identities instead of evicting a live one");
            Assert.That(window.RejectedAdmissionCount, Is.EqualTo(1));
            Assert.That(window.TrackedInstigatorCount, Is.EqualTo(InteractionRequestReplayWindow.MaxTrackedInstigators));

            // The oldest identity must still be protected: replaying its request must fail.
            Assert.That(window.TryAccept(1UL, 1), Is.False,
                "the first instigator's window must not have been re-opened by eviction");

            window.Remove(1UL);
            Assert.That(window.TryAccept(999_999UL, 1), Is.True, "an explicit release must free capacity");
        }
    }
}
