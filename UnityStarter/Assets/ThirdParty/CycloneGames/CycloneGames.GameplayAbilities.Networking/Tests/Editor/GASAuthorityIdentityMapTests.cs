using System;
using System.Threading;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Networking.Tests.Editor
{
    public sealed class GASAuthorityIdentityMapTests
    {
        [Test]
        public void GetOrCreate_IsStableAndSupportsBothLookupDirections()
        {
            var map = CreateMap(grantCapacity: 2, effectCapacity: 2);

            Assert.That(
                map.GetOrCreateGrantId(101, out GASNetworkGrantId firstGrant),
                Is.EqualTo(GASAuthorityIdentityMapResult.Created));
            Assert.That(
                map.GetOrCreateGrantId(101, out GASNetworkGrantId repeatedGrant),
                Is.EqualTo(GASAuthorityIdentityMapResult.Existing));
            Assert.That(repeatedGrant, Is.EqualTo(firstGrant));
            Assert.That(map.TryGetGrantId(101, out GASNetworkGrantId forwardGrant), Is.True);
            Assert.That(forwardGrant, Is.EqualTo(firstGrant));
            Assert.That(map.TryGetAbilitySpecHandle(firstGrant, out int specHandle), Is.True);
            Assert.That(specHandle, Is.EqualTo(101));

            Assert.That(
                map.GetOrCreateEffectId(501, out GASNetworkEffectId firstEffect),
                Is.EqualTo(GASAuthorityIdentityMapResult.Created));
            Assert.That(
                map.GetOrCreateEffectId(501, out GASNetworkEffectId repeatedEffect),
                Is.EqualTo(GASAuthorityIdentityMapResult.Existing));
            Assert.That(repeatedEffect, Is.EqualTo(firstEffect));
            Assert.That(map.TryGetEffectId(501, out GASNetworkEffectId forwardEffect), Is.True);
            Assert.That(forwardEffect, Is.EqualTo(firstEffect));
            Assert.That(map.TryGetEffectReconciliationId(firstEffect, out int reconciliationId), Is.True);
            Assert.That(reconciliationId, Is.EqualTo(501));
        }

        [Test]
        public void DistinctSpecHandlesReceiveDistinctGrantIdsWithoutDefinitionIdentity()
        {
            var map = CreateMap(grantCapacity: 2, effectCapacity: 0);

            Assert.That(map.GetOrCreateGrantId(11, out GASNetworkGrantId first),
                Is.EqualTo(GASAuthorityIdentityMapResult.Created));
            Assert.That(map.GetOrCreateGrantId(12, out GASNetworkGrantId second),
                Is.EqualTo(GASAuthorityIdentityMapResult.Created));

            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(map.TryGetAbilitySpecHandle(first, out int firstHandle), Is.True);
            Assert.That(map.TryGetAbilitySpecHandle(second, out int secondHandle), Is.True);
            Assert.That(firstHandle, Is.EqualTo(11));
            Assert.That(secondHandle, Is.EqualTo(12));
        }

        [Test]
        public void RemoveIsExactAndNeverReusesAWireIdWithinTheEpoch()
        {
            var map = CreateMap(grantCapacity: 2, effectCapacity: 2);
            map.GetOrCreateGrantId(21, out GASNetworkGrantId firstGrant);
            map.GetOrCreateGrantId(22, out GASNetworkGrantId retainedGrant);
            map.GetOrCreateEffectId(31, out GASNetworkEffectId firstEffect);

            Assert.That(map.RemoveGrantBySpecHandle(21, out GASNetworkGrantId removedGrant), Is.True);
            Assert.That(removedGrant, Is.EqualTo(firstGrant));
            Assert.That(map.TryGetAbilitySpecHandle(firstGrant, out _), Is.False);
            Assert.That(map.TryGetAbilitySpecHandle(retainedGrant, out int retainedHandle), Is.True);
            Assert.That(retainedHandle, Is.EqualTo(22));
            Assert.That(map.GetOrCreateGrantId(21, out GASNetworkGrantId replacementGrant),
                Is.EqualTo(GASAuthorityIdentityMapResult.Created));
            Assert.That(replacementGrant, Is.Not.EqualTo(firstGrant));

            Assert.That(map.RemoveEffectByReconciliationId(31, out GASNetworkEffectId removedEffect), Is.True);
            Assert.That(removedEffect, Is.EqualTo(firstEffect));
            Assert.That(map.GetOrCreateEffectId(31, out GASNetworkEffectId replacementEffect),
                Is.EqualTo(GASAuthorityIdentityMapResult.Created));
            Assert.That(replacementEffect, Is.Not.EqualTo(firstEffect));
        }

        [Test]
        public void CapacityExhaustionFailsClosedWithoutChangingExistingMappings()
        {
            var map = CreateMap(grantCapacity: 1, effectCapacity: 1);
            map.GetOrCreateGrantId(1, out GASNetworkGrantId grant);
            map.GetOrCreateEffectId(1, out GASNetworkEffectId effect);

            Assert.That(map.GetOrCreateGrantId(2, out GASNetworkGrantId rejectedGrant),
                Is.EqualTo(GASAuthorityIdentityMapResult.CapacityExhausted));
            Assert.That(rejectedGrant.IsValid, Is.False);
            Assert.That(map.GetOrCreateEffectId(2, out GASNetworkEffectId rejectedEffect),
                Is.EqualTo(GASAuthorityIdentityMapResult.CapacityExhausted));
            Assert.That(rejectedEffect.IsValid, Is.False);
            Assert.That(map.TryGetGrantId(1, out GASNetworkGrantId stableGrant), Is.True);
            Assert.That(stableGrant, Is.EqualTo(grant));
            Assert.That(map.TryGetEffectId(1, out GASNetworkEffectId stableEffect), Is.True);
            Assert.That(stableEffect, Is.EqualTo(effect));
        }

        [Test]
        public void IdentitySequenceStopsAtUlongMaxValueAndDoesNotWrapToZero()
        {
            var map = new GASAuthorityIdentityMap(
                new GASNetworkEntityId(9UL),
                streamEpoch: 3u,
                grantCapacity: 2,
                effectCapacity: 2,
                firstGrantId: ulong.MaxValue,
                firstEffectId: ulong.MaxValue);

            Assert.That(map.GetOrCreateGrantId(1, out GASNetworkGrantId lastGrant),
                Is.EqualTo(GASAuthorityIdentityMapResult.Created));
            Assert.That(lastGrant.Value, Is.EqualTo(ulong.MaxValue));
            Assert.That(map.GetOrCreateGrantId(2, out GASNetworkGrantId exhaustedGrant),
                Is.EqualTo(GASAuthorityIdentityMapResult.IdentityExhausted));
            Assert.That(exhaustedGrant.IsValid, Is.False);

            Assert.That(map.GetOrCreateEffectId(1, out GASNetworkEffectId lastEffect),
                Is.EqualTo(GASAuthorityIdentityMapResult.Created));
            Assert.That(lastEffect.Value, Is.EqualTo(ulong.MaxValue));
            Assert.That(map.GetOrCreateEffectId(2, out GASNetworkEffectId exhaustedEffect),
                Is.EqualTo(GASAuthorityIdentityMapResult.IdentityExhausted));
            Assert.That(exhaustedEffect.IsValid, Is.False);
        }

        [Test]
        public void ResetEpochClearsMappingsAndInvalidatesTheRetiredEpoch()
        {
            var map = CreateMap(grantCapacity: 2, effectCapacity: 2, streamEpoch: 7u);
            map.GetOrCreateGrantId(4, out GASNetworkGrantId retiredGrant);
            map.GetOrCreateEffectId(5, out GASNetworkEffectId retiredEffect);

            Assert.Throws<ArgumentOutOfRangeException>(() => map.ResetEpoch(0u));
            Assert.Throws<ArgumentOutOfRangeException>(() => map.ResetEpoch(7u));
            map.ResetEpoch(8u);

            Assert.That(map.StreamEpoch, Is.EqualTo(8u));
            Assert.That(map.GrantCount, Is.Zero);
            Assert.That(map.EffectCount, Is.Zero);
            Assert.That(map.TryGetAbilitySpecHandle(retiredGrant, out _), Is.False);
            Assert.That(map.TryGetEffectReconciliationId(retiredEffect, out _), Is.False);
            Assert.That(map.GetOrCreateGrantId(4, out GASNetworkGrantId newEpochGrant),
                Is.EqualTo(GASAuthorityIdentityMapResult.Created));
            Assert.That(newEpochGrant.Value, Is.EqualTo(1UL));
        }

        [Test]
        public void WrongThreadAccessFailsInsteadOfIntroducingImplicitSynchronization()
        {
            var map = CreateMap(grantCapacity: 1, effectCapacity: 1);
            Exception observed = null;
            var thread = new Thread(() =>
            {
                try
                {
                    map.GetOrCreateGrantId(1, out _);
                }
                catch (Exception exception)
                {
                    observed = exception;
                }
            });

            thread.Start();
            thread.Join();

            Assert.That(observed, Is.TypeOf<InvalidOperationException>());
            Assert.That(map.GrantCount, Is.Zero);
        }

        [Test]
        public void WarmedLookupPathDoesNotAllocateOnTheCurrentThread()
        {
            var map = CreateMap(grantCapacity: 1, effectCapacity: 1);
            map.GetOrCreateGrantId(1, out GASNetworkGrantId grant);
            map.GetOrCreateEffectId(1, out GASNetworkEffectId effect);
            map.TryGetGrantId(1, out _);
            map.TryGetAbilitySpecHandle(grant, out _);
            map.TryGetEffectId(1, out _);
            map.TryGetEffectReconciliationId(effect, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++)
            {
                map.TryGetGrantId(1, out _);
                map.TryGetAbilitySpecHandle(grant, out _);
                map.TryGetEffectId(1, out _);
                map.TryGetEffectReconciliationId(effect, out _);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void ConstructorAndLocalIdentityValidationFailClosed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GASAuthorityIdentityMap(default, 1u, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GASAuthorityIdentityMap(new GASNetworkEntityId(1UL), 0u, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GASAuthorityIdentityMap(new GASNetworkEntityId(1UL), 1u, -1, 1));

            var map = CreateMap(grantCapacity: 1, effectCapacity: 1);
            Assert.That(map.GetOrCreateGrantId(0, out GASNetworkGrantId invalidGrant),
                Is.EqualTo(GASAuthorityIdentityMapResult.InvalidLocalIdentity));
            Assert.That(invalidGrant.IsValid, Is.False);
            Assert.That(map.GetOrCreateEffectId(-1, out GASNetworkEffectId invalidEffect),
                Is.EqualTo(GASAuthorityIdentityMapResult.InvalidLocalIdentity));
            Assert.That(invalidEffect.IsValid, Is.False);
        }

        private static GASAuthorityIdentityMap CreateMap(
            int grantCapacity,
            int effectCapacity,
            uint streamEpoch = 1u)
        {
            return new GASAuthorityIdentityMap(
                new GASNetworkEntityId(100UL),
                streamEpoch,
                grantCapacity,
                effectCapacity);
        }
    }
}
