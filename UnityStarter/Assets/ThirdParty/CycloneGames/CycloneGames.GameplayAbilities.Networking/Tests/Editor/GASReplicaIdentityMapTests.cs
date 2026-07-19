using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Networking.Tests.Editor
{
    public sealed class GASReplicaIdentityMapTests
    {
        [Test]
        public void BindMaintainsExactBidirectionalIdentity()
        {
            var map = new GASReplicaIdentityMap(new GASNetworkEntityId(7), 3, 2, 2);

            Assert.That(map.BindGrant(new GASNetworkGrantId(100), 4), Is.EqualTo(GASReplicaIdentityBindResult.Bound));
            Assert.That(map.BindGrant(new GASNetworkGrantId(100), 4), Is.EqualTo(GASReplicaIdentityBindResult.Existing));
            Assert.That(map.BindGrant(new GASNetworkGrantId(100), 5), Is.EqualTo(GASReplicaIdentityBindResult.Conflict));
            Assert.That(map.BindGrant(new GASNetworkGrantId(101), 4), Is.EqualTo(GASReplicaIdentityBindResult.Conflict));
            Assert.That(map.TryGetAbilitySpecHandle(new GASNetworkGrantId(100), out int handle), Is.True);
            Assert.That(handle, Is.EqualTo(4));
            Assert.That(map.TryGetGrantId(4, out GASNetworkGrantId grant), Is.True);
            Assert.That(grant, Is.EqualTo(new GASNetworkGrantId(100)));
        }

        [Test]
        public void CapacityRemovalAndEpochResetAreBoundedAndExplicit()
        {
            var map = new GASReplicaIdentityMap(new GASNetworkEntityId(8), 9, 1, 1);

            Assert.That(map.BindEffect(new GASNetworkEffectId(70), 11), Is.EqualTo(GASReplicaIdentityBindResult.Bound));
            Assert.That(map.BindEffect(new GASNetworkEffectId(71), 12), Is.EqualTo(GASReplicaIdentityBindResult.CapacityExhausted));
            Assert.That(map.RemoveEffect(new GASNetworkEffectId(70), out int removed), Is.True);
            Assert.That(removed, Is.EqualTo(11));
            Assert.That(map.BindEffect(new GASNetworkEffectId(71), 12), Is.EqualTo(GASReplicaIdentityBindResult.Bound));

            map.ResetEpoch(10);
            Assert.That(map.StreamEpoch, Is.EqualTo(10));
            Assert.That(map.EffectCount, Is.Zero);
            Assert.That(map.TryGetEffectReconciliationId(new GASNetworkEffectId(71), out _), Is.False);
        }
    }
}
