using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Interaction.Core;
using CycloneGames.RPGFoundation.Interaction.Networking;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Interaction.Networking.Tests.Editor
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
        public void NetworkProtocol_UsesReservedRPGFoundationMessageIds()
        {
            Assert.That(InteractionNetworkProtocol.REQUEST_MESSAGE_ID, Is.GreaterThanOrEqualTo(InteractionNetworkProtocol.MESSAGE_ID_BASE));
            Assert.That(InteractionNetworkProtocol.CANCEL_REQUEST_MESSAGE_ID, Is.LessThanOrEqualTo(InteractionNetworkProtocol.MESSAGE_ID_MAX));
            Assert.That(InteractionNetworkProtocol.IsRPGFoundationInteractionMessageId(InteractionNetworkProtocol.REQUEST_MESSAGE_ID), Is.True);
            Assert.That(NetworkMessageRanges.Module.Contains(InteractionNetworkProtocol.REQUEST_MESSAGE_ID), Is.True);
            Assert.That(InteractionNetworkProtocol.IsInteractionMessage(InteractionNetworkProtocol.REQUEST_MESSAGE_ID), Is.True);
            Assert.That(InteractionNetworkProtocol.IsInteractionMessage(InteractionNetworkProtocol.RESULT_MESSAGE_ID), Is.True);
            Assert.That(InteractionNetworkProtocol.IsInteractionMessage(InteractionNetworkProtocol.CANCEL_REQUEST_MESSAGE_ID), Is.True);
            Assert.That(InteractionNetworkProtocol.IsInteractionMessage(InteractionNetworkProtocol.DETERMINISTIC_REQUEST_MESSAGE_ID), Is.True);
            Assert.That(InteractionNetworkProtocol.IsInteractionMessage(NetworkConstants.UserMsgIdMin), Is.False);
        }

        [Test]
        public void NetworkProtocol_RegisterMessageCatalog_UsesRPGFoundationRange()
        {
            var catalog = new NetworkMessageCatalog();

            InteractionNetworkProtocol.RegisterMessageCatalog(catalog);

            Assert.That(catalog.TryGet(
                InteractionNetworkProtocol.REQUEST_MESSAGE_ID,
                out NetworkMessageDescriptor descriptor), Is.True);
            Assert.That(InteractionNetworkProtocol.MessageRange.Contains(descriptor.MessageId), Is.True);
            Assert.That(NetworkMessageRanges.Module.Contains(descriptor.MessageId), Is.True);
            Assert.That(catalog.TryGetRegisteredRange(descriptor.MessageId, out NetworkMessageIdRange range), Is.True);
            Assert.That(range.Name, Is.EqualTo(InteractionNetworkProtocol.MessageOwner));
            Assert.That(descriptor.ContractId, Is.EqualTo("InteractionNetworkRequest:v1"));
            Assert.That(descriptor.Owner, Is.EqualTo(InteractionNetworkProtocol.MessageOwner));
            Assert.That(descriptor.DefaultChannel, Is.EqualTo(NetworkChannel.Reliable));
        }

        [Test]
        public void NetworkProtocol_RegisterMessageCatalog_IsIdempotentForSameDescriptor()
        {
            var catalog = new NetworkMessageCatalog();

            InteractionNetworkProtocol.RegisterMessageCatalog(catalog);
            InteractionNetworkProtocol.RegisterMessageCatalog(catalog);

            Assert.That(catalog.MessageCount, Is.EqualTo(4));
            Assert.That(catalog.ManifestCount, Is.EqualTo(1));
        }

        [Test]
        public void ProtocolManifest_UsesFrozenV1SchemasAndFingerprint()
        {
            NetworkProtocolManifest manifest = InteractionNetworkProtocol.CreateProtocolManifest();
            string[] canonicalSchemaLiterals =
            {
                "InteractionNetworkRequest:v1",
                "InteractionNetworkResult:v1",
                "InteractionNetworkCancelRequest:v1",
                "InteractionNetworkRequest:v1"
            };
            ulong[] expectedSchemaHashes =
            {
                0xD76DBACD901D2BB7UL,
                0x50B6ECBD2F378EC9UL,
                0x00E384398B2C4151UL,
                0xD76DBACD901D2BB7UL
            };

            Assert.That(manifest.Fingerprint, Is.EqualTo(0xCC0BA75DE490D6CEUL));
            Assert.That(InteractionNetworkProtocol.ProtocolFingerprint, Is.EqualTo(manifest.Fingerprint));
            Assert.That(canonicalSchemaLiterals.Length, Is.EqualTo(expectedSchemaHashes.Length));
            Assert.That(manifest.Messages.Count, Is.EqualTo(expectedSchemaHashes.Length));
            for (int i = 0; i < expectedSchemaHashes.Length; i++)
            {
                Assert.That(
                    ComputeFnv1a64(canonicalSchemaLiterals[i]),
                    Is.EqualTo(expectedSchemaHashes[i]),
                    canonicalSchemaLiterals[i]);
                Assert.That(manifest.Messages[i].SchemaHash, Is.EqualTo(expectedSchemaHashes[i]));
                Assert.That(manifest.Messages[i].ContractId, Is.EqualTo(canonicalSchemaLiterals[i]));
            }
        }

        private static ulong ComputeFnv1a64(string canonicalLiteral)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;

            for (int i = 0; i < canonicalLiteral.Length; i++)
            {
                char character = canonicalLiteral[i];
                if (character > 0x7F)
                {
                    throw new AssertionException("Canonical schema literals must contain ASCII characters only.");
                }

                hash ^= (byte)character;
                hash = unchecked(hash * prime);
            }

            return hash;
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
