using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Networking.Editor;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Networking.Tests.Runtime.Editor
{
    public sealed class GASNetworkRuntimeContentResolverTests
    {
        [Test]
        public void Resolver_BuildsScriptableValuesAndUsesExplicitBidirectionalMappings()
        {
            TestAbilitySO abilityAsset = CreateAbilityAsset("Runtime.Fireball");
            ConcreteGameplayEffectSO effectAsset = CreateEffectAsset("Runtime.Burning");
            effectAsset.DurationPolicy = EDurationPolicy.Infinite;
            effectAsset.GrantedAbilities = new List<GameplayAbilitySO> { abilityAsset };
            TestTargetSurface targetSurface = ScriptableObject.CreateInstance<TestTargetSurface>();
            try
            {
                var builder = new GASNetworkContentCatalogBuilder();
                GASNetworkContentId abilityId = builder.Add(
                    GASNetworkContentKind.AbilityDefinition,
                    "ability.fireball",
                    Revision("ability.fireball:1"),
                    abilityAsset);
                GASNetworkContentId effectId = builder.Add(
                    GASNetworkContentKind.EffectDefinition,
                    "effect.burning",
                    Revision("effect.burning:1"),
                    effectAsset);
                GASNetworkContentId attributeId = builder.Add(
                    GASNetworkContentKind.Attribute,
                    "attribute.health",
                    Revision("attribute.health:1"),
                    "Health");
                GASNetworkContentId setByCallerId = builder.Add(
                    GASNetworkContentKind.SetByCallerName,
                    "setbycaller.damage",
                    Revision("setbycaller.damage:1"),
                    "Damage");
                GASNetworkContentId targetSurfaceId = builder.Add(
                    GASNetworkContentKind.TargetSurface,
                    "surface.ground",
                    Revision("surface.ground:1"),
                    targetSurface);

                var resolver = new GASNetworkRuntimeContentResolver(builder.Build());

                Assert.That(abilityAsset.CreateCount, Is.EqualTo(1));
                Assert.That(resolver.TryResolveAbility(abilityId, out GameplayAbility ability), Is.True);
                Assert.That(abilityAsset.GetGameplayAbility(), Is.SameAs(ability));
                Assert.That(abilityAsset.CreateCount, Is.EqualTo(1));
                Assert.That(ability, Is.TypeOf<TestGameplayAbility>());
                Assert.That(ability.Name, Is.EqualTo("Runtime.Fireball"));
                Assert.That(resolver.TryGetAbilityId(ability, out GASNetworkContentId encodedAbility), Is.True);
                Assert.That(encodedAbility, Is.EqualTo(abilityId));

                Assert.That(resolver.TryResolveEffect(effectId, out GameplayEffect effect), Is.True);
                Assert.That(effect, Is.SameAs(effectAsset.GetGameplayEffect()));
                Assert.That(effect.GrantedAbilities[0], Is.SameAs(ability));
                Assert.That(resolver.TryGetEffectId(effect, out GASNetworkContentId encodedEffect), Is.True);
                Assert.That(encodedEffect, Is.EqualTo(effectId));

                Assert.That(resolver.TryResolveAttributeName(attributeId, out string attributeName), Is.True);
                Assert.That(attributeName, Is.EqualTo("Health"));
                Assert.That(resolver.TryGetAttributeId("Health", out GASNetworkContentId encodedAttribute), Is.True);
                Assert.That(encodedAttribute, Is.EqualTo(attributeId));
                Assert.That(resolver.TryResolveSetByCallerName(setByCallerId, out string setByCallerName), Is.True);
                Assert.That(setByCallerName, Is.EqualTo("Damage"));
                Assert.That(resolver.TryGetSetByCallerNameId("Damage", out GASNetworkContentId encodedName), Is.True);
                Assert.That(encodedName, Is.EqualTo(setByCallerId));
                Assert.That(resolver.TryResolveTargetSurface(targetSurfaceId, out object resolvedSurface), Is.True);
                Assert.That(resolvedSurface, Is.SameAs(targetSurface));
                Assert.That(resolver.TryGetTargetSurfaceId(targetSurface, out GASNetworkContentId encodedSurface), Is.True);
                Assert.That(encodedSurface, Is.EqualTo(targetSurfaceId));
                Assert.That(resolver.TryGetTargetSurfaceId(new object(), out _), Is.False);
                Assert.That(resolver.TryGetTargetSurfaceId(null, out _), Is.False);
                Assert.That(
                    resolver.TryResolveTargetSurface(new GASNetworkContentId(ulong.MaxValue), out object missingSurface),
                    Is.False);
                Assert.That(missingSurface, Is.Null);

                GameplayAbility sameNameButUnregistered = CreateRuntimeAbility("Runtime.Fireball");
                Assert.That(resolver.TryGetAbilityId(sameNameButUnregistered, out _), Is.False);
                Assert.That(resolver.TryGetAttributeId("health", out _), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(abilityAsset);
                UnityEngine.Object.DestroyImmediate(effectAsset);
                UnityEngine.Object.DestroyImmediate(targetSurface);
            }
        }

        [Test]
        public void Resolver_AcceptsExactRuntimeDefinitionsAndRejectsSameNameAliases()
        {
            GameplayAbility ability = CreateRuntimeAbility("Runtime.Dash");
            var effect = new GameplayEffect("Runtime.Haste", EDurationPolicy.Infinite);
            var builder = new GASNetworkContentCatalogBuilder();
            GASNetworkContentId abilityId = builder.Add(
                GASNetworkContentKind.AbilityDefinition,
                "ability.dash",
                Revision("ability.dash:1"),
                ability);
            GASNetworkContentId effectId = builder.Add(
                GASNetworkContentKind.EffectDefinition,
                "effect.haste",
                Revision("effect.haste:1"),
                effect);

            var resolver = new GASNetworkRuntimeContentResolver(builder.Build());

            Assert.That(resolver.TryResolveAbility(abilityId, out GameplayAbility resolvedAbility), Is.True);
            Assert.That(resolvedAbility, Is.SameAs(ability));
            Assert.That(resolver.TryResolveEffect(effectId, out GameplayEffect resolvedEffect), Is.True);
            Assert.That(resolvedEffect, Is.SameAs(effect));

            GameplayAbility separatelyCreatedAbility = CreateRuntimeAbility("Runtime.Dash");
            var separatelyCreatedEffect = new GameplayEffect("Runtime.Haste", EDurationPolicy.Infinite);
            Assert.That(resolver.TryGetAbilityId(separatelyCreatedAbility, out _), Is.False);
            Assert.That(resolver.TryGetEffectId(separatelyCreatedEffect, out _), Is.False);
        }

        [Test]
        public void Resolver_RejectsRuntimeNameValueAndMissingValueConflicts()
        {
            var duplicateAbilityBuilder = new GASNetworkContentCatalogBuilder();
            duplicateAbilityBuilder.Add(
                GASNetworkContentKind.AbilityDefinition,
                "ability.first",
                Revision("ability.first:1"),
                CreateRuntimeAbility("Runtime.Shared"));
            duplicateAbilityBuilder.Add(
                GASNetworkContentKind.AbilityDefinition,
                "ability.second",
                Revision("ability.second:1"),
                CreateRuntimeAbility("Runtime.Shared"));
            Assert.Throws<InvalidOperationException>(() =>
                new GASNetworkRuntimeContentResolver(duplicateAbilityBuilder.Build()));

            string firstHealth = new string("Health".ToCharArray());
            string secondHealth = new string("Health".ToCharArray());
            var duplicateAttributeBuilder = new GASNetworkContentCatalogBuilder();
            duplicateAttributeBuilder.Add(
                GASNetworkContentKind.Attribute,
                "attribute.first",
                Revision("attribute.first:1"),
                firstHealth);
            duplicateAttributeBuilder.Add(
                GASNetworkContentKind.Attribute,
                "attribute.second",
                Revision("attribute.second:1"),
                secondHealth);
            Assert.Throws<InvalidOperationException>(() =>
                new GASNetworkRuntimeContentResolver(duplicateAttributeBuilder.Build()));

            var missingValueBuilder = new GASNetworkContentCatalogBuilder();
            missingValueBuilder.Add(
                GASNetworkContentKind.EffectDefinition,
                "effect.missing",
                Revision("effect.missing:1"));
            Assert.Throws<InvalidOperationException>(() =>
                new GASNetworkRuntimeContentResolver(missingValueBuilder.Build()));

            var missingTargetSurfaceBuilder = new GASNetworkContentCatalogBuilder();
            missingTargetSurfaceBuilder.Add(
                GASNetworkContentKind.TargetSurface,
                "surface.missing",
                Revision("surface.missing:1"));
            Assert.Throws<InvalidOperationException>(() =>
                new GASNetworkRuntimeContentResolver(missingTargetSurfaceBuilder.Build()));
        }

        [Test]
        public void AuthoringAsset_BuildsEveryExplicitGroupAndResolver()
        {
            GASNetworkContentCatalogAsset asset = ScriptableObject.CreateInstance<GASNetworkContentCatalogAsset>();
            TestAbilitySO abilityAsset = CreateAbilityAsset("Runtime.AuthoredAbility");
            ConcreteGameplayEffectSO effectAsset = CreateEffectAsset("Runtime.AuthoredEffect");
            TestTargetSurface targetSurface = ScriptableObject.CreateInstance<TestTargetSurface>();
            try
            {
                var serialized = new SerializedObject(asset);
                AddObjectRegistration(serialized, "abilities", "ability.authored", "ability-v1", abilityAsset);
                AddObjectRegistration(serialized, "effects", "effect.authored", "effect-v1", effectAsset);
                AddNameRegistration(serialized, "attributes", "attribute.health", "attribute-v1", "Health");
                AddNameRegistration(serialized, "setByCallerNames", "setbycaller.damage", "setbycaller-v1", "Damage");
                AddObjectRegistration(serialized, "targetSurfaces", "surface.ground", "surface-v1", targetSurface);
                Assert.That(serialized.ApplyModifiedProperties(), Is.True);

                GASNetworkContentCatalog catalog = asset.BuildCatalog();
                var resolver = new GASNetworkRuntimeContentResolver(catalog);

                Assert.That(catalog.Count, Is.EqualTo(5));
                Assert.That(abilityAsset.CreateCount, Is.EqualTo(1));
                Assert.That(catalog.TryGetId(
                    GASNetworkContentKind.AbilityDefinition,
                    "ability.authored",
                    out GASNetworkContentId abilityId), Is.True);
                Assert.That(resolver.TryResolveAbility(abilityId, out GameplayAbility ability), Is.True);
                Assert.That(ability.Name, Is.EqualTo("Runtime.AuthoredAbility"));
                Assert.That(resolver.TryGetAttributeId("Health", out _), Is.True);
                Assert.That(resolver.TryGetSetByCallerNameId("Damage", out _), Is.True);
                Assert.That(catalog.TryGetId(
                    GASNetworkContentKind.TargetSurface,
                    "surface.ground",
                    out GASNetworkContentId surfaceId), Is.True);
                Assert.That(catalog.TryResolve(
                    surfaceId,
                    GASNetworkContentKind.TargetSurface,
                    out object surface), Is.True);
                Assert.That(surface, Is.SameAs(targetSurface));
                Assert.That(resolver.TryGetTargetSurfaceId(targetSurface, out GASNetworkContentId encodedSurface), Is.True);
                Assert.That(encodedSurface, Is.EqualTo(surfaceId));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(abilityAsset);
                UnityEngine.Object.DestroyImmediate(effectAsset);
                UnityEngine.Object.DestroyImmediate(targetSurface);
            }
        }

        [Test]
        public void AuthoringAsset_RejectsDuplicateEmptyAndRuntimeNameConflicts()
        {
            GASNetworkContentCatalogAsset duplicateKeys = ScriptableObject.CreateInstance<GASNetworkContentCatalogAsset>();
            GASNetworkContentCatalogAsset missingReference = ScriptableObject.CreateInstance<GASNetworkContentCatalogAsset>();
            GASNetworkContentCatalogAsset duplicateRuntimeNames = ScriptableObject.CreateInstance<GASNetworkContentCatalogAsset>();
            GASNetworkContentCatalogAsset duplicateTargetSurface = ScriptableObject.CreateInstance<GASNetworkContentCatalogAsset>();
            TestAbilitySO first = CreateAbilityAsset("Runtime.Duplicate");
            TestAbilitySO second = CreateAbilityAsset("Runtime.Duplicate");
            TestTargetSurface targetSurface = ScriptableObject.CreateInstance<TestTargetSurface>();
            try
            {
                var duplicateSerialized = new SerializedObject(duplicateKeys);
                AddObjectRegistration(duplicateSerialized, "abilities", "ability.same", "1", first);
                AddObjectRegistration(duplicateSerialized, "abilities", "ability.same", "2", second);
                duplicateSerialized.ApplyModifiedProperties();
                Assert.Throws<InvalidOperationException>(() => duplicateKeys.BuildCatalog());

                var missingSerialized = new SerializedObject(missingReference);
                AddObjectRegistration(missingSerialized, "effects", "effect.missing", "1", null);
                missingSerialized.ApplyModifiedProperties();
                Assert.Throws<InvalidOperationException>(() => missingReference.BuildCatalog());

                var runtimeNameSerialized = new SerializedObject(duplicateRuntimeNames);
                AddObjectRegistration(runtimeNameSerialized, "abilities", "ability.first", "1", first);
                AddObjectRegistration(runtimeNameSerialized, "abilities", "ability.second", "1", second);
                runtimeNameSerialized.ApplyModifiedProperties();
                Assert.Throws<InvalidOperationException>(() => duplicateRuntimeNames.BuildCatalog());

                var targetSurfaceSerialized = new SerializedObject(duplicateTargetSurface);
                AddObjectRegistration(targetSurfaceSerialized, "targetSurfaces", "surface.first", "1", targetSurface);
                AddObjectRegistration(targetSurfaceSerialized, "targetSurfaces", "surface.second", "1", targetSurface);
                targetSurfaceSerialized.ApplyModifiedProperties();
                Assert.Throws<InvalidOperationException>(() => duplicateTargetSurface.BuildCatalog());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(duplicateKeys);
                UnityEngine.Object.DestroyImmediate(missingReference);
                UnityEngine.Object.DestroyImmediate(duplicateRuntimeNames);
                UnityEngine.Object.DestroyImmediate(duplicateTargetSurface);
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(targetSurface);
            }
        }

        [Test]
        public void AuthoringAsset_RequiresEveryEffectGrantedAbilityToBeRegistered()
        {
            GASNetworkContentCatalogAsset asset = ScriptableObject.CreateInstance<GASNetworkContentCatalogAsset>();
            TestAbilitySO ability = CreateAbilityAsset("Runtime.EffectGranted");
            ConcreteGameplayEffectSO effect = CreateEffectAsset("Runtime.GrantsAbility");
            effect.DurationPolicy = EDurationPolicy.Infinite;
            effect.GrantedAbilities = new List<GameplayAbilitySO> { ability };
            try
            {
                var serialized = new SerializedObject(asset);
                AddObjectRegistration(serialized, "effects", "effect.grant", "1", effect);
                Assert.That(serialized.ApplyModifiedProperties(), Is.True);
                Assert.Throws<InvalidOperationException>(() => asset.BuildCatalog());

                serialized.Update();
                AddObjectRegistration(serialized, "abilities", "ability.granted", "1", ability);
                Assert.That(serialized.ApplyModifiedProperties(), Is.True);
                Assert.DoesNotThrow(() => asset.BuildCatalog());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(ability);
                UnityEngine.Object.DestroyImmediate(effect);
            }
        }

        [Test]
        public void Inspector_SupportsMultiObjectSerializedEditing()
        {
            GASNetworkContentCatalogAsset first = ScriptableObject.CreateInstance<GASNetworkContentCatalogAsset>();
            GASNetworkContentCatalogAsset second = ScriptableObject.CreateInstance<GASNetworkContentCatalogAsset>();
            UnityEditor.Editor inspector = null;
            try
            {
                var selected = new UnityEngine.Object[] { first, second };
                inspector = UnityEditor.Editor.CreateEditor(selected);

                Assert.That(inspector, Is.TypeOf<GASNetworkContentCatalogAssetEditor>());
                Assert.That(
                    Attribute.IsDefined(
                        typeof(GASNetworkContentCatalogAssetEditor),
                        typeof(CanEditMultipleObjects),
                        inherit: true),
                    Is.True);

                var serialized = new SerializedObject(selected);
                SerializedProperty attributes = serialized.FindProperty("attributes");
                attributes.arraySize = 1;
                Assert.That(serialized.ApplyModifiedProperties(), Is.True);

                var firstSerialized = new SerializedObject(first);
                var secondSerialized = new SerializedObject(second);
                Assert.That(firstSerialized.FindProperty("attributes").arraySize, Is.EqualTo(1));
                Assert.That(secondSerialized.FindProperty("attributes").arraySize, Is.EqualTo(1));
            }
            finally
            {
                if (inspector != null)
                    UnityEngine.Object.DestroyImmediate(inspector);
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void Resolver_WarmedLookupPathDoesNotAllocate()
        {
            GameplayAbility ability = CreateRuntimeAbility("Runtime.AllocationAbility");
            var effect = new GameplayEffect("Runtime.AllocationEffect", EDurationPolicy.Instant);
            var builder = new GASNetworkContentCatalogBuilder();
            GASNetworkContentId abilityId = builder.Add(
                GASNetworkContentKind.AbilityDefinition,
                "ability.allocation",
                Revision("ability.allocation:1"),
                ability);
            GASNetworkContentId effectId = builder.Add(
                GASNetworkContentKind.EffectDefinition,
                "effect.allocation",
                Revision("effect.allocation:1"),
                effect);
            GASNetworkContentId attributeId = builder.Add(
                GASNetworkContentKind.Attribute,
                "attribute.allocation",
                Revision("attribute.allocation:1"),
                "Health");
            GASNetworkContentId nameId = builder.Add(
                GASNetworkContentKind.SetByCallerName,
                "setbycaller.allocation",
                Revision("setbycaller.allocation:1"),
                "Damage");
            var targetSurface = new object();
            GASNetworkContentId surfaceId = builder.Add(
                GASNetworkContentKind.TargetSurface,
                "surface.allocation",
                Revision("surface.allocation:1"),
                targetSurface);
            var resolver = new GASNetworkRuntimeContentResolver(builder.Build());
            GameplayAbility equivalentAbility = CreateRuntimeAbility("Runtime.AllocationAbility");
            var equivalentEffect = new GameplayEffect("Runtime.AllocationEffect", EDurationPolicy.Instant);

            resolver.TryGetAbilityId(equivalentAbility, out _);
            resolver.TryResolveAbility(abilityId, out _);
            resolver.TryGetEffectId(equivalentEffect, out _);
            resolver.TryResolveEffect(effectId, out _);
            resolver.TryGetAttributeId("Health", out _);
            resolver.TryResolveAttributeName(attributeId, out _);
            resolver.TryGetSetByCallerNameId("Damage", out _);
            resolver.TryResolveSetByCallerName(nameId, out _);
            resolver.TryGetTargetSurfaceId(targetSurface, out _);
            resolver.TryResolveTargetSurface(surfaceId, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++)
            {
                resolver.TryGetAbilityId(equivalentAbility, out _);
                resolver.TryResolveAbility(abilityId, out _);
                resolver.TryGetEffectId(equivalentEffect, out _);
                resolver.TryResolveEffect(effectId, out _);
                resolver.TryGetAttributeId("Health", out _);
                resolver.TryResolveAttributeName(attributeId, out _);
                resolver.TryGetSetByCallerNameId("Damage", out _);
                resolver.TryResolveSetByCallerName(nameId, out _);
                resolver.TryGetTargetSurfaceId(targetSurface, out _);
                resolver.TryResolveTargetSurface(surfaceId, out _);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        private static TestAbilitySO CreateAbilityAsset(string runtimeName)
        {
            TestAbilitySO asset = ScriptableObject.CreateInstance<TestAbilitySO>();
            asset.AbilityName = runtimeName;
            asset.InstancingPolicy = EGameplayAbilityInstancingPolicy.InstancedPerActor;
            asset.ExecutionPolicy = EAbilityExecutionPolicy.AuthorityOnly;
            return asset;
        }

        private static ConcreteGameplayEffectSO CreateEffectAsset(string runtimeName)
        {
            ConcreteGameplayEffectSO asset = ScriptableObject.CreateInstance<ConcreteGameplayEffectSO>();
            asset.EffectName = runtimeName;
            asset.DurationPolicy = EDurationPolicy.Instant;
            return asset;
        }

        private static TestGameplayAbility CreateRuntimeAbility(string runtimeName)
        {
            var ability = new TestGameplayAbility();
            ability.Initialize(
                runtimeName,
                EGameplayAbilityInstancingPolicy.InstancedPerActor,
                EAbilityExecutionPolicy.AuthorityOnly,
                cost: null,
                cooldown: null,
                abilityTags: null,
                activationBlockedTags: null,
                activationRequiredTags: null,
                cancelAbilitiesWithTag: null,
                blockAbilitiesWithTag: null);
            return ability;
        }

        private static void AddObjectRegistration(
            SerializedObject serialized,
            string group,
            string stableKey,
            string revision,
            UnityEngine.Object reference)
        {
            SerializedProperty registration = AppendRegistration(serialized, group, stableKey, revision);
            registration.FindPropertyRelative("reference").objectReferenceValue = reference;
        }

        private static void AddNameRegistration(
            SerializedObject serialized,
            string group,
            string stableKey,
            string revision,
            string value)
        {
            SerializedProperty registration = AppendRegistration(serialized, group, stableKey, revision);
            registration.FindPropertyRelative("value").stringValue = value;
        }

        private static SerializedProperty AppendRegistration(
            SerializedObject serialized,
            string group,
            string stableKey,
            string revision)
        {
            SerializedProperty registrations = serialized.FindProperty(group);
            int index = registrations.arraySize;
            registrations.arraySize = index + 1;
            SerializedProperty registration = registrations.GetArrayElementAtIndex(index);
            registration.FindPropertyRelative("stableKey").stringValue = stableKey;
            registration.FindPropertyRelative("revision").stringValue = revision;
            return registration;
        }

        private static ulong Revision(string value)
        {
            return GASNetworkContentCatalogBuilder.ComputeRevisionHash(value);
        }

        private sealed class TestAbilitySO : GameplayAbilitySO
        {
            public int CreateCount { get; private set; }

            protected override GameplayAbility CreateGameplayAbility()
            {
                CreateCount++;
                var ability = new TestGameplayAbility();
                InitializeAbility(ability);
                return ability;
            }
        }

        private sealed class TestGameplayAbility : GameplayAbility
        {
            public override GameplayAbility CreateRuntimeInstance()
            {
                var instance = new TestGameplayAbility();
                instance.Initialize(
                    Name,
                    InstancingPolicy,
                    ExecutionPolicy,
                    CostEffectDefinition,
                    CooldownEffectDefinition,
                    AbilityTags,
                    ActivationBlockedTags,
                    ActivationRequiredTags,
                    CancelAbilitiesWithTag,
                    BlockAbilitiesWithTag,
                    ActivationOwnedTags,
                    ActivateAbilityOnGranted,
                    SourceRequiredTags,
                    SourceBlockedTags,
                    TargetRequiredTags,
                    TargetBlockedTags);
                return instance;
            }
        }

        private sealed class TestTargetSurface : ScriptableObject
        {
        }
    }
}
