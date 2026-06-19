using System;
using System.Collections.Generic;
using System.IO;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayTags.Tests.Editor
{
    public sealed class GameplayTagsCoreTests
    {
        [SetUp]
        public void SetUp()
        {
            GameplayTagManager.ResetForTests();
            GameplayTagRedirector.ClearAll();
            GameplayTagRuntimePlatform.LogWarning = static _ => { };
            GameplayTagRuntimePlatform.LogError = static _ => { };
            GameplayTagRuntimePlatform.IsRuntimePlaying = static () => false;
            GameplayTagRuntimePlatform.LoadBuildTagData = static () => null;
            GameplayTagRuntimePlatform.EnumerateProjectTagSources = static () => System.Array.Empty<IGameplayTagSource>();
        }

        [TearDown]
        public void TearDown()
        {
            GameplayTagManager.ResetForTests();
            GameplayTagRedirector.ClearAll();
        }

        [Test]
        public void DynamicRegistration_BuildsStableHierarchy()
        {
            RegisterTestTags();

            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag damage = GameplayTagManager.RequestTag("Test.Ability.Damage");
            GameplayTag ability = GameplayTagManager.RequestTag("Test.Ability");

            Assert.That(damageFire.IsValid, Is.True);
            Assert.That(damageFire.Name, Is.EqualTo("Test.Ability.Damage.Fire"));
            Assert.That(damageFire.Label, Is.EqualTo("Fire"));
            Assert.That(damageFire.HierarchyLevel, Is.EqualTo(4));
            Assert.That(damageFire.ParentTag, Is.EqualTo(damage));
            Assert.That(damageFire.IsChildOf(damage), Is.True);
            Assert.That(ability.IsParentOf(damageFire), Is.True);
            Assert.That(damageFire.MatchesTagDepth(GameplayTagManager.RequestTag("Test.Ability.Damage.Ice")), Is.EqualTo(3));
        }

        [Test]
        public void Container_AddTagIncludesImplicitParentsAndExactLeaf()
        {
            RegisterTestTags();
            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag damage = GameplayTagManager.RequestTag("Test.Ability.Damage");
            GameplayTag ability = GameplayTagManager.RequestTag("Test.Ability");

            GameplayTagContainer container = new();
            container.AddTag(damageFire);

            Assert.That(container.ExplicitTagCount, Is.EqualTo(1));
            Assert.That(container.HasTagExact(damageFire), Is.True);
            Assert.That(container.HasTagExact(damage), Is.False);
            Assert.That(container.HasTag(damage), Is.True);
            Assert.That(container.HasTag(ability), Is.True);
        }

        [Test]
        public void Container_UnionAndIntersectionPreserveSortedTagSets()
        {
            RegisterTestTags();
            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag damageIce = GameplayTagManager.RequestTag("Test.Ability.Damage.Ice");
            GameplayTag statusStun = GameplayTagManager.RequestTag("Test.Status.Stun");

            GameplayTagContainer lhs = new();
            lhs.AddTag(damageFire);
            lhs.AddTag(statusStun);

            GameplayTagContainer rhs = new();
            rhs.AddTag(damageFire);
            rhs.AddTag(damageIce);

            GameplayTagContainer union = GameplayTagContainer.Union(lhs, rhs);
            GameplayTagContainer intersection = GameplayTagContainer.Intersection(lhs, rhs);

            Assert.That(union.HasTagExact(damageFire), Is.True);
            Assert.That(union.HasTagExact(damageIce), Is.True);
            Assert.That(union.HasTagExact(statusStun), Is.True);
            Assert.That(union.ExplicitTagCount, Is.EqualTo(3));
            Assert.That(intersection.HasTagExact(damageFire), Is.True);
            Assert.That(intersection.ExplicitTagCount, Is.EqualTo(1));
        }

        [Test]
        public void CountContainer_TracksExplicitAndImplicitCounts()
        {
            RegisterTestTags();
            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag damage = GameplayTagManager.RequestTag("Test.Ability.Damage");
            int anyChangeCount = 0;
            int newOrRemovedCount = 0;

            GameplayTagCountContainer container = new();
            container.RegisterTagEventCallback(damage, GameplayTagEventType.AnyCountChange, (_, _) => anyChangeCount++);
            container.RegisterTagEventCallback(damage, GameplayTagEventType.NewOrRemoved, (_, _) => newOrRemovedCount++);

            container.AddTag(damageFire);
            container.AddTag(damageFire);
            container.RemoveTag(damageFire);
            container.RemoveTag(damageFire);

            Assert.That(container.GetExplicitTagCount(damageFire), Is.EqualTo(0));
            Assert.That(container.GetTagCount(damage), Is.EqualTo(0));
            Assert.That(anyChangeCount, Is.EqualTo(4));
            Assert.That(newOrRemovedCount, Is.EqualTo(2));
        }

        [Test]
        public void Query_MatchesNestedExpression()
        {
            RegisterTestTags();
            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag statusStun = GameplayTagManager.RequestTag("Test.Status.Stun");
            GameplayTag statusRoot = GameplayTagManager.RequestTag("Test.Status");

            GameplayTagContainer subject = new();
            subject.AddTag(damageFire);

            GameplayTagContainer requiredDamage = new();
            requiredDamage.AddTag(damageFire);

            GameplayTagContainer forbiddenStatus = new();
            forbiddenStatus.AddTag(statusStun);

            GameplayTagQuery query = new()
            {
                RootExpression = new GameplayTagQueryExpression
                {
                    Operator = EGameplayTagQueryExprOperator.All,
                    Expressions = new List<GameplayTagQueryExpression>
                    {
                        new()
                        {
                            Operator = EGameplayTagQueryExprOperator.All,
                            Tags = requiredDamage
                        },
                        new()
                        {
                            Operator = EGameplayTagQueryExprOperator.None,
                            Tags = forbiddenStatus
                        }
                    }
                }
            };

            Assert.That(query.Matches(subject), Is.True);
            Assert.That(subject.HasTag(statusRoot), Is.False);

            subject.AddTag(statusStun);

            Assert.That(query.Matches(subject), Is.False);
        }

        [Test]
        public void Query_InvalidateCompiledCache_RecompilesMutatedExpression()
        {
            RegisterTestTags();
            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag statusStun = GameplayTagManager.RequestTag("Test.Status.Stun");

            GameplayTagContainer subject = new();
            subject.AddTag(damageFire);

            GameplayTagContainer required = new();
            required.AddTag(damageFire);

            GameplayTagQuery query = new()
            {
                RootExpression = new GameplayTagQueryExpression
                {
                    Operator = EGameplayTagQueryExprOperator.All,
                    Tags = required
                }
            };

            Assert.That(query.Matches(subject), Is.True);

            required.AddTag(statusStun);
            Assert.That(query.Matches(subject), Is.True);

            query.InvalidateCompiledCache();
            Assert.That(query.Matches(subject), Is.False);
        }

        [Test]
        public void Requirements_MatchAcrossStaticAndDynamicContainers()
        {
            RegisterTestTags();
            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag statusStun = GameplayTagManager.RequestTag("Test.Status.Stun");

            GameplayTagContainer staticTags = new();
            GameplayTagContainer dynamicTags = new();
            dynamicTags.AddTag(damageFire);

            GameplayTagContainer required = new();
            required.AddTag(damageFire);

            GameplayTagContainer forbidden = new();
            forbidden.AddTag(statusStun);

            GameplayTagRequirements requirements = new(forbidden, required);

            Assert.That(requirements.Matches(staticTags, dynamicTags), Is.True);

            dynamicTags.AddTag(statusStun);

            Assert.That(requirements.Matches(staticTags, dynamicTags), Is.False);
        }

        [Test]
        public void Mask_UsesRuntimeIndexBitsForSetOperations()
        {
            RegisterTestTags();
            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag damageIce = GameplayTagManager.RequestTag("Test.Ability.Damage.Ice");

            GameplayTagMask a = GameplayTagMask.FromTag(damageFire);
            GameplayTagMask b = GameplayTagMask.FromTag(damageIce);
            GameplayTagMask union = GameplayTagMask.Union(a, b);
            GameplayTagMask intersection = GameplayTagMask.Intersection(a, b);

            Assert.That(a.HasTag(damageFire), Is.True);
            Assert.That(a.HasTag(damageIce), Is.False);
            Assert.That(union.HasTag(damageFire), Is.True);
            Assert.That(union.HasTag(damageIce), Is.True);
            Assert.That(intersection.IsEmpty, Is.True);
        }

        [Test]
        public void Containers_RejectNoneAndInvalidTags()
        {
            GameplayTagContainer container = new();
            GameplayTagCountContainer countContainer = new();

            Assert.Throws<ArgumentException>(() => container.AddTag(GameplayTag.None));
            Assert.Throws<ArgumentException>(() => countContainer.AddTag(GameplayTag.None));
        }

        [Test]
        public void Mask_IgnoresOutOfRangeBitAccess()
        {
            GameplayTagMask mask = default;

            mask.SetBit(-1);
            mask.SetBit(GameplayTagMask.MaxTags);
            mask.ClearBit(-1);
            mask.ClearBit(GameplayTagMask.MaxTags);

            Assert.That(mask.IsEmpty, Is.True);
            Assert.That(mask.GetWord(-1), Is.EqualTo(0UL));
            Assert.That(mask.GetWord(4), Is.EqualTo(0UL));
        }

        [Test]
        public void NetSerializer_RoundTripsFullAndDeltaPackets()
        {
            RegisterTestTags();
            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag damageIce = GameplayTagManager.RequestTag("Test.Ability.Damage.Ice");
            GameplayTag statusStun = GameplayTagManager.RequestTag("Test.Status.Stun");

            GameplayTagContainer source = new();
            source.AddTag(damageFire);
            source.AddTag(statusStun);

            byte[] fullPacket = GameplayTagNetSerializer.SerializeFull(source);
            GameplayTagContainer fullTarget = new();
            GameplayTagNetSerializer.DeserializeFull(fullTarget, fullPacket);

            Assert.That(fullTarget.HasTagExact(damageFire), Is.True);
            Assert.That(fullTarget.HasTagExact(statusStun), Is.True);

            GameplayTagContainer next = source.Clone();
            next.RemoveTag(statusStun);
            next.AddTag(damageIce);

            byte[] deltaPacket = GameplayTagNetSerializer.SerializeDelta(next, source);
            GameplayTagNetSerializer.ApplyDelta(fullTarget, deltaPacket);

            Assert.That(fullTarget.HasTagExact(damageFire), Is.True);
            Assert.That(fullTarget.HasTagExact(damageIce), Is.True);
            Assert.That(fullTarget.HasTagExact(statusStun), Is.False);
        }

        [Test]
        public void NetSerializer_RejectsTruncatedPackets()
        {
            RegisterTestTags();

            GameplayTagContainer target = new();

            Assert.Throws<ArgumentException>(() => GameplayTagNetSerializer.DeserializeFull(target, new byte[] { GameplayTagNetSerializer.CurrentProtocolVersion, 0xFE }));
        }

        [Test]
        public void NetSerializer_RejectsUnsupportedProtocolVersion()
        {
            RegisterTestTags();

            GameplayTagContainer source = new();
            byte[] packet = GameplayTagNetSerializer.SerializeFull(source);
            packet[0] = unchecked((byte)(GameplayTagNetSerializer.CurrentProtocolVersion + 1));

            GameplayTagContainer target = new();
            Assert.That(GameplayTagNetSerializer.IsSupportedProtocolVersion(GameplayTagNetSerializer.CurrentProtocolVersion), Is.True);
            Assert.That(GameplayTagNetSerializer.IsSupportedProtocolVersion(packet[0]), Is.False);
            Assert.Throws<NotSupportedException>(() => GameplayTagNetSerializer.DeserializeFull(target, packet));
        }

        [Test]
        public void NetSerializer_RejectsInvalidTagsBeforeSerialization()
        {
            List<GameplayTag> added = new() { GameplayTag.None };

            Assert.Throws<ArgumentException>(() => GameplayTagNetSerializer.SerializeDelta(added, null));
        }

        [Test]
        public void NetSerializer_RejectsManifestMismatch()
        {
            RegisterTestTags();
            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");

            GameplayTagContainer source = new();
            source.AddTag(damageFire);
            byte[] packet = GameplayTagNetSerializer.SerializeFull(source);

            GameplayTagManager.RegisterDynamicTag("Test.Network.NewTag", "Added after packet serialization.");

            GameplayTagContainer target = new();
            Assert.Throws<InvalidOperationException>(() => GameplayTagNetSerializer.DeserializeFull(target, packet));
        }

        [Test]
        public void DynamicRegistration_BatchesSnapshotRebuildsAfterInitialization()
        {
            RegisterTestTags();

            int treeChangedCount = 0;
            GameplayTagManager.OnGameplayTagTreeChanged += () => treeChangedCount++;

            GameplayTagManager.RegisterDynamicTags(new[]
            {
                "HotUpdate.Ability.Fire",
                "HotUpdate.Ability.Ice"
            });

            Assert.That(treeChangedCount, Is.EqualTo(1));
            Assert.That(GameplayTagManager.RequestTag("HotUpdate.Ability.Fire").IsValid, Is.True);
            Assert.That(GameplayTagManager.RequestTag("HotUpdate.Ability.Ice").IsValid, Is.True);
            Assert.That(GameplayTagManager.RequestTag("HotUpdate.Ability").IsValid, Is.True);
        }

        [Test]
        public void BuildGameplayTagSource_RegistersValidBinaryData()
        {
            byte[] data = CreateBuildTagData("Build.Ability.Fire", "Build.Status.Stun");
            GameplayTagRuntimePlatform.LoadBuildTagData = () => data;

            GameplayTagRegistrationContext context = new();
            new BuildGameplayTagSource().RegisterTags(context);
            List<GameplayTagDefinition> definitions = context.GenerateDefinitions(true);

            Assert.That(definitions.Exists(static definition => definition.TagName == "Build.Ability.Fire"), Is.True);
            Assert.That(definitions.Exists(static definition => definition.TagName == "Build.Status.Stun"), Is.True);
        }

        [Test]
        public void BuildGameplayTagSource_RejectsCorruptedBinaryData()
        {
            byte[] data = CreateBuildTagData("Build.Ability.Fire");
            data[^1] ^= 0xFF;
            GameplayTagRuntimePlatform.LoadBuildTagData = () => data;

            GameplayTagRegistrationContext context = new();
            new BuildGameplayTagSource().RegisterTags(context);
            List<GameplayTagDefinition> definitions = context.GenerateDefinitions(true);

            Assert.That(definitions.Exists(static definition => definition.TagName == "Build.Ability.Fire"), Is.False);
        }

        private static void RegisterTestTags()
        {
            GameplayTagManager.RegisterDynamicTag("Test.Ability.Damage.Fire", "Fire damage");
            GameplayTagManager.RegisterDynamicTag("Test.Ability.Damage.Ice", "Ice damage");
            GameplayTagManager.RegisterDynamicTag("Test.Status.Stun", "Stun status");
            GameplayTagManager.InitializeIfNeeded();
        }

        private static byte[] CreateBuildTagData(params string[] tagNames)
        {
            using MemoryStream memoryStream = new();
            using BinaryWriter writer = new(memoryStream);

            writer.Write(BuildTagBinaryFormat.CurrentFormatVersion);
            writer.Write(tagNames.Length);

            long dataStart = memoryStream.Position;
            for (int i = 0; i < tagNames.Length; i++)
            {
                writer.Write(tagNames[i]);
            }

            long dataEnd = memoryStream.Position;
            writer.Flush();

            byte[] dataWithoutHash = memoryStream.ToArray();
            ulong payloadHash = BuildTagBinaryFormat.ComputePayloadHash64(dataWithoutHash, (int)dataStart, (int)(dataEnd - dataStart));
            writer.Write(payloadHash);

            return memoryStream.ToArray();
        }
    }
}
