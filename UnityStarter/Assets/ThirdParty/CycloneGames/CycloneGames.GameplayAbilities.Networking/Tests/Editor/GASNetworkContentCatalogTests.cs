using System;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Networking.Tests.Editor
{
    public sealed class GASNetworkContentCatalogTests
    {
        [Test]
        public void Build_IsOrderIndependentAndRevisionSensitive()
        {
            ulong abilityRevision = GASNetworkContentCatalogBuilder.ComputeRevisionHash("fireball:1");
            ulong effectRevision = GASNetworkContentCatalogBuilder.ComputeRevisionHash("burning:1");

            var firstBuilder = new GASNetworkContentCatalogBuilder();
            firstBuilder.Add(GASNetworkContentKind.AbilityDefinition, "Ability.Fireball", abilityRevision);
            firstBuilder.Add(GASNetworkContentKind.EffectDefinition, "Effect.Burning", effectRevision);

            var secondBuilder = new GASNetworkContentCatalogBuilder();
            secondBuilder.Add(GASNetworkContentKind.EffectDefinition, "Effect.Burning", effectRevision);
            secondBuilder.Add(GASNetworkContentKind.AbilityDefinition, "Ability.Fireball", abilityRevision);

            var changedBuilder = new GASNetworkContentCatalogBuilder();
            changedBuilder.Add(
                GASNetworkContentKind.AbilityDefinition,
                "Ability.Fireball",
                GASNetworkContentCatalogBuilder.ComputeRevisionHash("fireball:2"));
            changedBuilder.Add(GASNetworkContentKind.EffectDefinition, "Effect.Burning", effectRevision);

            Assert.That(firstBuilder.Build().ManifestHash, Is.EqualTo(secondBuilder.Build().ManifestHash));
            Assert.That(changedBuilder.Build().ManifestHash, Is.Not.EqualTo(firstBuilder.Build().ManifestHash));
        }

        [Test]
        public void Lookup_RequiresTheExpectedKindAndPreservesReferenceIdentity()
        {
            var ability = new object();
            var builder = new GASNetworkContentCatalogBuilder();
            GASNetworkContentId abilityId = builder.Add(
                GASNetworkContentKind.AbilityDefinition,
                "Ability.Dash",
                GASNetworkContentCatalogBuilder.ComputeRevisionHash("dash:1"),
                ability);
            GASNetworkContentCatalog catalog = builder.Build();

            Assert.That(catalog.TryGetId(
                GASNetworkContentKind.AbilityDefinition,
                "Ability.Dash",
                out GASNetworkContentId keyId), Is.True);
            Assert.That(keyId, Is.EqualTo(abilityId));
            Assert.That(catalog.TryGetId(
                ability,
                GASNetworkContentKind.AbilityDefinition,
                out GASNetworkContentId valueId), Is.True);
            Assert.That(valueId, Is.EqualTo(abilityId));
            Assert.That(catalog.TryResolve(
                abilityId,
                GASNetworkContentKind.AbilityDefinition,
                out object resolved), Is.True);
            Assert.That(resolved, Is.SameAs(ability));

            Assert.That(catalog.TryResolve(
                abilityId,
                GASNetworkContentKind.EffectDefinition,
                out _), Is.False);
        }

        [Test]
        public void Add_RejectsAmbiguousOrUnversionedEntries()
        {
            var value = new object();
            var builder = new GASNetworkContentCatalogBuilder();
            ulong revision = GASNetworkContentCatalogBuilder.ComputeRevisionHash("ability:1");
            builder.Add(GASNetworkContentKind.AbilityDefinition, "Ability.Shared", revision, value);

            Assert.Throws<InvalidOperationException>(() =>
                builder.Add(GASNetworkContentKind.AbilityDefinition, "Ability.Shared", revision));
            Assert.Throws<InvalidOperationException>(() =>
                builder.Add(GASNetworkContentKind.EffectDefinition, "Effect.Shared", revision, value));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                builder.Add(GASNetworkContentKind.Attribute, "Attribute.Health", 0UL));
            Assert.Throws<ArgumentException>(() =>
                GASNetworkContentCatalogBuilder.ComputeContentId(
                    GASNetworkContentKind.Attribute,
                    "Attribute.\u0001Health"));
        }

        [Test]
        public void ContentId_SeparatesSemanticNamespaces()
        {
            GASNetworkContentId ability = GASNetworkContentCatalogBuilder.ComputeContentId(
                GASNetworkContentKind.AbilityDefinition,
                "Shared.Key");
            GASNetworkContentId effect = GASNetworkContentCatalogBuilder.ComputeContentId(
                GASNetworkContentKind.EffectDefinition,
                "Shared.Key");

            Assert.That(ability.IsValid, Is.True);
            Assert.That(effect.IsValid, Is.True);
            Assert.That(ability, Is.Not.EqualTo(effect));
        }

        [Test]
        public void UnknownContentKinds_AreRejectedAtRegistrationAndLookupBoundaries()
        {
            const GASNetworkContentKind unknown = (GASNetworkContentKind)byte.MaxValue;
            ulong revision = GASNetworkContentCatalogBuilder.ComputeRevisionHash("unknown:1");
            var builder = new GASNetworkContentCatalogBuilder();

            Assert.Throws<ArgumentOutOfRangeException>(() => builder.Add(unknown, "Unknown.Entry", revision));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GASNetworkContentCatalogBuilder.ComputeContentId(unknown, "Unknown.Entry"));

            var value = new object();
            GASNetworkContentId id = builder.Add(
                GASNetworkContentKind.AbilityDefinition,
                "Ability.Valid",
                revision,
                value);
            GASNetworkContentCatalog catalog = builder.Build();

            Assert.That(catalog.TryGetId(unknown, "Ability.Valid", out _), Is.False);
            Assert.That(catalog.TryGetId(value, unknown, out _), Is.False);
            Assert.That(catalog.TryResolve(id, unknown, out _), Is.False);
        }
    }
}
