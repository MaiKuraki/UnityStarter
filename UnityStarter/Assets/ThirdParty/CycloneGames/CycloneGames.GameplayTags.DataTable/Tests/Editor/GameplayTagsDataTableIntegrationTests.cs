using System;
using System.Collections.Generic;

using CycloneGames.DataTable;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Integrations.DataTable;
using NUnit.Framework;

namespace CycloneGames.GameplayTags.DataTable.Tests.Editor
{
    public sealed class GameplayTagsDataTableIntegrationTests
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
            GameplayTagRuntimePlatform.EnumerateProjectTagSources = static () => Array.Empty<IGameplayTagSource>();
            GameplayTagRuntimePlatform.ClearRegisteredProjectTagSources();
        }

        [TearDown]
        public void TearDown()
        {
            GameplayTagManager.ResetForTests();
            GameplayTagRedirector.ClearAll();
            GameplayTagRuntimePlatform.ClearRegisteredProjectTagSources();
        }

        [Test]
        public void DataTableSource_RegistersLubanStyleTagCatalogRows()
        {
            DataTable<TagCatalogRow> table = new(new[]
            {
                new TagCatalogRow(1, "DataTableTest.Ability.Fireball", "Fireball ability.", GameplayTagFlags.None, true),
                new TagCatalogRow(2, "DataTableTest.Effect.Burn", "Burning damage over time.", GameplayTagFlags.None, true),
                new TagCatalogRow(3, "DataTableTest.Hidden.EditorOnly", "Hidden in editor.", GameplayTagFlags.HideInEditor, false)
            });

            GameplayTagRuntimePlatform.RegisterProjectTagSource(new GameplayTagDataTableSource<TagCatalogRow>(
                "Design.GameplayTags",
                table,
                static row => row.Name,
                static row => row.Comment,
                static row => row.Flags,
                static row => row.Enabled));

            GameplayTagManager.InitializeIfNeeded();

            Assert.That(GameplayTagManager.RequestTag("DataTableTest.Ability.Fireball").Description, Is.EqualTo("Fireball ability."));
            Assert.That(GameplayTagManager.RequestTag("DataTableTest.Effect.Burn").IsValid, Is.True);
            Assert.That(GameplayTagManager.TryRequestTag("DataTableTest.Hidden.EditorOnly", out _), Is.False);
        }

        [Test]
        public void DataTableReferenceSource_RegistersTagsReferencedByGeneratedAbilityRows()
        {
            DataTable<AbilityConfigRow> table = new(new[]
            {
                new AbilityConfigRow(
                    1001,
                    new[] { "DataTableTest.Ability.Fireball", "DataTableTest.Ability.Damage.Fire" },
                    new[] { "DataTableTest.State.Combat.Ready" },
                    new[] { "DataTableTest.State.CrowdControl.Stunned" },
                    new[] { "DataTableTest.State.Casting.Fireball" })
            });

            GameplayTagRuntimePlatform.RegisterProjectTagSource(new GameplayTagDataTableReferenceSource<AbilityConfigRow>(
                "Design.Abilities",
                table,
                static row => row.AbilityTags,
                static row => row.ActivationRequiredTags,
                static row => row.ActivationBlockedTags,
                static row => row.ActivationOwnedTags));

            GameplayTagManager.InitializeIfNeeded();

            AbilityConfigRow row = table.Get(1001);
            GameplayTagContainer abilityTags = GameplayTagContainerNameExtensions.FromTagNames(row.AbilityTags);
            GameplayTagRequirements activationRequirements = GameplayTagContainerNameExtensions.CreateRequirementsFromTagNames(
                row.ActivationBlockedTags,
                row.ActivationRequiredTags);

            Assert.That(abilityTags.HasTagExact(GameplayTagManager.RequestTag("DataTableTest.Ability.Fireball")), Is.True);
            Assert.That(GameplayTagManager.RequestTag("DataTableTest.Ability.Damage.Fire").IsValid, Is.True);
            Assert.That(GameplayTagManager.RequestTag("DataTableTest.State.CrowdControl.Stunned").IsValid, Is.True);
            Assert.That(activationRequirements.RequiredTags.HasTagExact(GameplayTagManager.RequestTag("DataTableTest.State.Combat.Ready")), Is.True);
            Assert.That(activationRequirements.ForbiddenTags.HasTagExact(GameplayTagManager.RequestTag("DataTableTest.State.CrowdControl.Stunned")), Is.True);
        }

        private sealed class TagCatalogRow : IDataRow
        {
            public int Id { get; }
            public string Name { get; }
            public string Comment { get; }
            public GameplayTagFlags Flags { get; }
            public bool Enabled { get; }

            public TagCatalogRow(int id, string name, string comment, GameplayTagFlags flags, bool enabled)
            {
                Id = id;
                Name = name;
                Comment = comment;
                Flags = flags;
                Enabled = enabled;
            }
        }

        private sealed class AbilityConfigRow : IDataRow
        {
            public int Id { get; }
            public IReadOnlyList<string> AbilityTags { get; }
            public IReadOnlyList<string> ActivationRequiredTags { get; }
            public IReadOnlyList<string> ActivationBlockedTags { get; }
            public IReadOnlyList<string> ActivationOwnedTags { get; }

            public AbilityConfigRow(
                int id,
                IReadOnlyList<string> abilityTags,
                IReadOnlyList<string> activationRequiredTags,
                IReadOnlyList<string> activationBlockedTags,
                IReadOnlyList<string> activationOwnedTags)
            {
                Id = id;
                AbilityTags = abilityTags;
                ActivationRequiredTags = activationRequiredTags;
                ActivationBlockedTags = activationBlockedTags;
                ActivationOwnedTags = activationOwnedTags;
            }
        }
    }
}
