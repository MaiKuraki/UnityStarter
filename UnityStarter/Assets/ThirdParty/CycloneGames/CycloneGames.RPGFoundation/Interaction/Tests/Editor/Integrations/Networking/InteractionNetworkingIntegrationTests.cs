using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Interaction.Runtime;
using CycloneGames.RPGFoundation.Interaction.Integrations.Networking;
using NUnit.Framework;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Tests.Editor.Integrations.Networking
{
    public sealed class InteractionNetworkingIntegrationTests
    {
        private const int WORLD_ID = 9;
        private const ulong INSTIGATOR_ID = 1001UL;
        private const ulong TARGET_ID = 2001UL;

        [Test]
        public void NetworkVectorConversion_RoundTripsInteractionVector()
        {
            var interactionVector = new InteractionVector3(1.5f, -2f, 3.25f);

            NetworkVector3 networkVector = interactionVector.ToNetworkVector3();
            InteractionVector3 roundTrip = networkVector.ToInteractionVector3();

            Assert.That(roundTrip, Is.EqualTo(interactionVector));
        }

        [Test]
        public void NetworkAuthorityBridge_RejectsNonFiniteInstigatorPosition()
        {
            var authority = CreateAuthority();
            authority.TryRegisterTarget(CreateTarget());
            var bridge = new InteractionNetworkAuthorityBridge(authority);
            var request = new InteractionNetworkRequest(
                1,
                INSTIGATOR_ID,
                TARGET_ID,
                "open",
                100,
                WORLD_ID,
                new NetworkVector3(float.NaN, 0f, 0f));

            InteractionValidationResult result = bridge.Validate(request, serverTick: 100);

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.Failure, Is.EqualTo(InteractionValidationFailure.InvalidRequest));
        }

        [Test]
        public void NetworkRequest_PreservesStableIdsAndWorld()
        {
            var networkRequest = new InteractionNetworkRequest(
                12,
                INSTIGATOR_ID,
                TARGET_ID,
                "open",
                200,
                WORLD_ID,
                NetworkVector3.Zero);

            InteractionRequest request = networkRequest.ToInteractionRequest();

            Assert.That(request.RequestId, Is.EqualTo(12));
            Assert.That(request.InstigatorStableId, Is.EqualTo(INSTIGATOR_ID));
            Assert.That(request.TargetStableId, Is.EqualTo(TARGET_ID));
            Assert.That(request.ActionId, Is.EqualTo("open"));
            Assert.That(request.Tick, Is.EqualTo(200));
            Assert.That(request.WorldId, Is.EqualTo(WORLD_ID));
        }

        [Test]
        public void NetworkProtocol_UsesReservedInteractionUserMessageIds()
        {
            Assert.That(InteractionNetworkProtocol.REQUEST_MESSAGE_ID, Is.GreaterThanOrEqualTo(NetworkConstants.UserMsgIdMin));
            Assert.That(InteractionNetworkProtocol.CANCEL_REQUEST_MESSAGE_ID, Is.LessThanOrEqualTo(NetworkConstants.MaxMessageId));
            Assert.That(InteractionNetworkProtocol.IsUserMessageId(InteractionNetworkProtocol.REQUEST_MESSAGE_ID), Is.True);
            Assert.That(InteractionNetworkProtocol.IsInteractionMessage(InteractionNetworkProtocol.REQUEST_MESSAGE_ID), Is.True);
            Assert.That(InteractionNetworkProtocol.IsInteractionMessage(InteractionNetworkProtocol.RESULT_MESSAGE_ID), Is.True);
            Assert.That(InteractionNetworkProtocol.IsInteractionMessage(InteractionNetworkProtocol.CANCEL_REQUEST_MESSAGE_ID), Is.True);
            Assert.That(InteractionNetworkProtocol.IsInteractionMessage(InteractionNetworkProtocol.DETERMINISTIC_REQUEST_MESSAGE_ID), Is.True);
            Assert.That(InteractionNetworkProtocol.IsInteractionMessage(NetworkConstants.UserMsgIdMin), Is.False);
        }

        [Test]
        public void NetworkCancelRequest_PreservesIdentityReasonAndWorld()
        {
            var networkCancel = new InteractionNetworkCancelRequest(
                15,
                INSTIGATOR_ID,
                TARGET_ID,
                InteractionCancelReason.Interrupted,
                240,
                WORLD_ID);

            InteractionRequest request = networkCancel.ToInteractionRequest("open");

            Assert.That(networkCancel.IsValid, Is.True);
            Assert.That(request.RequestId, Is.EqualTo(15));
            Assert.That(request.InstigatorStableId, Is.EqualTo(INSTIGATOR_ID));
            Assert.That(request.TargetStableId, Is.EqualTo(TARGET_ID));
            Assert.That(request.ActionId, Is.EqualTo("open"));
            Assert.That(request.Tick, Is.EqualTo(240));
            Assert.That(request.WorldId, Is.EqualTo(WORLD_ID));
            Assert.That(networkCancel.CancelReason, Is.EqualTo(InteractionCancelReason.Interrupted));
        }

        [Test]
        public void NetworkCancelRequest_RequiresTargetStableId()
        {
            var networkCancel = new InteractionNetworkCancelRequest(
                15,
                INSTIGATOR_ID,
                InteractionStableId.None,
                InteractionCancelReason.Interrupted,
                240,
                WORLD_ID);

            Assert.That(networkCancel.IsValid, Is.False);
        }

        private static InteractionAuthorityService CreateAuthority()
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
    }
}
