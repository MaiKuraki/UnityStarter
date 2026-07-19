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
        public void DataTableSource_AcceptsGeneratedRowsWithoutFrameworkRowInterface()
        {
            DataTable<string, GeneratedTagCatalogRow> table = new(
                new[]
                {
                    new GeneratedTagCatalogRow(
                        "DataTableTest.Generated.LubanCompatible",
                        "Generated rows can use an explicit key selector.",
                        true)
                },
                static row => row.Name,
                StringComparer.Ordinal);

            GameplayTagRuntimePlatform.RegisterProjectTagSource(
                new GameplayTagDataTableSource<GeneratedTagCatalogRow>(
                    "Design.GeneratedGameplayTags",
                    table,
                    static row => row.Name,
                    static row => row.Comment,
                    isEnabled: static row => row.Enabled));

            GameplayTagManager.InitializeIfNeeded();

            Assert.That(
                GameplayTagManager.RequestTag("DataTableTest.Generated.LubanCompatible").Description,
                Is.EqualTo("Generated rows can use an explicit key selector."));
        }

        [Test]
        public void DataTableSource_AcceptsGeneratedValueTypeRows()
        {
            DataTable<int, GeneratedTagStructRow> table = new(
                new[]
                {
                    new GeneratedTagStructRow(
                        42,
                        "DataTableTest.Generated.FlatBufferStyle",
                        "Value-type generated views are supported.")
                },
                static row => row.Id);

            GameplayTagRuntimePlatform.RegisterProjectTagSource(
                new GameplayTagDataTableSource<GeneratedTagStructRow>(
                    "Design.GeneratedValueTypeGameplayTags",
                    table,
                    static row => row.Name,
                    static row => row.Comment));

            GameplayTagManager.InitializeIfNeeded();

            Assert.That(
                GameplayTagManager.RequestTag("DataTableTest.Generated.FlatBufferStyle").Description,
                Is.EqualTo("Value-type generated views are supported."));
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

        [Test]
        public void DataTableReferenceSource_DefensivelyCopiesAccessorArray()
        {
            IReadOnlyList<AbilityConfigRow> rows = new[]
            {
                new AbilityConfigRow(
                    1012,
                    new[] { "DataTableTest.OriginalAccessor" },
                    null,
                    null,
                    null)
            };
            var accessors = new Func<AbilityConfigRow, IEnumerable<string>>[]
            {
                static row => row.AbilityTags
            };
            var source = new GameplayTagDataTableReferenceSource<AbilityConfigRow>(
                "Design.AccessorOwnership",
                rows,
                accessors);

            accessors[0] = static _ => new[] { "DataTableTest.MutatedAccessor" };
            GameplayTagRuntimePlatform.RegisterProjectTagSource(source);
            GameplayTagManager.InitializeIfNeeded();

            Assert.That(GameplayTagManager.RequestTag("DataTableTest.OriginalAccessor").IsValid, Is.True);
            Assert.That(GameplayTagManager.TryRequestTag("DataTableTest.MutatedAccessor", out _), Is.False);
        }

        [Test]
        public void DataTableReferenceSource_RejectsEmptyTagEntriesAtomically()
        {
            GameplayTagManager.RegisterDynamicTag("DataTableTest.Baseline");
            GameplayTagManager.InitializeIfNeeded();
            int generation = GameplayTagManager.CurrentGeneration;
            int runtimeIndexEpoch = GameplayTagManager.CurrentRuntimeIndexEpoch;
            DataTable<AbilityConfigRow> table = new(new[]
            {
                new AbilityConfigRow(1002, new[] { "DataTableTest.Valid", "" }, null, null, null)
            });
            GameplayTagRuntimePlatform.RegisterProjectTagSource(new GameplayTagDataTableReferenceSource<AbilityConfigRow>(
                "Design.InvalidAbilities",
                table,
                static row => row.AbilityTags));

            Assert.Throws<InvalidOperationException>(GameplayTagManager.ReloadTags);
            Assert.That(GameplayTagManager.CurrentGeneration, Is.EqualTo(generation));
            Assert.That(GameplayTagManager.CurrentRuntimeIndexEpoch, Is.EqualTo(runtimeIndexEpoch));
            Assert.That(GameplayTagManager.RequestTag("DataTableTest.Baseline").IsValid, Is.True);
        }

        [Test]
        public void DataTableReferenceSource_SkipsNullDisabledRowsAndNullCollections()
        {
            int disabledAccessorCalls = 0;
            IReadOnlyList<AbilityConfigRow> rows = new AbilityConfigRow[]
            {
                null,
                new AbilityConfigRow(1003, new[] { "DataTableTest.Disabled" }, null, null, null),
                new AbilityConfigRow(1004, null, null, null, null),
                new AbilityConfigRow(1005, new[] { "DataTableTest.Enabled" }, null, null, null)
            };

            GameplayTagRuntimePlatform.RegisterProjectTagSource(new GameplayTagDataTableReferenceSource<AbilityConfigRow>(
                "Design.NullableAbilities",
                rows,
                getDescription: null,
                isEnabled: row =>
                {
                    bool enabled = row.Id != 1003;
                    if (!enabled) disabledAccessorCalls++;
                    return enabled;
                },
                getTagNameCollections: new Func<AbilityConfigRow, IEnumerable<string>>[]
                {
                    static row => row.AbilityTags,
                    null
                }));

            GameplayTagManager.InitializeIfNeeded();

            Assert.That(disabledAccessorCalls, Is.EqualTo(1));
            Assert.That(GameplayTagManager.TryRequestTag("DataTableTest.Disabled", out _), Is.False);
            Assert.That(GameplayTagManager.RequestTag("DataTableTest.Enabled").IsValid, Is.True);
        }

        [Test]
        public void DataTableReferenceSource_PropagatesAccessorFailureWithoutPublishingPartialRegistry()
        {
            GameplayTagManager.RegisterDynamicTag("DataTableTest.Baseline");
            GameplayTagManager.InitializeIfNeeded();
            int generation = GameplayTagManager.CurrentGeneration;
            int runtimeIndexEpoch = GameplayTagManager.CurrentRuntimeIndexEpoch;
            IReadOnlyList<AbilityConfigRow> rows = new[]
            {
                new AbilityConfigRow(1006, new[] { "DataTableTest.BeforeFailure" }, null, null, null),
                new AbilityConfigRow(1007, new[] { "DataTableTest.Throws" }, null, null, null)
            };
            GameplayTagRuntimePlatform.RegisterProjectTagSource(new GameplayTagDataTableReferenceSource<AbilityConfigRow>(
                "Design.ThrowingAbilities",
                rows,
                row => row.Id == 1007
                    ? throw new InvalidOperationException("Injected accessor failure.")
                    : row.AbilityTags));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(GameplayTagManager.ReloadTags);

            Assert.That(error.Message, Is.EqualTo("Injected accessor failure."));
            Assert.That(GameplayTagManager.CurrentGeneration, Is.EqualTo(generation));
            Assert.That(GameplayTagManager.CurrentRuntimeIndexEpoch, Is.EqualTo(runtimeIndexEpoch));
            Assert.That(GameplayTagManager.RequestTag("DataTableTest.Baseline").IsValid, Is.True);
            Assert.That(GameplayTagManager.TryRequestTag("DataTableTest.BeforeFailure", out _), Is.False);
        }

        [Test]
        public void DataTableReferenceSource_DeduplicatesRepeatedReferences()
        {
            IReadOnlyList<AbilityConfigRow> rows = new[]
            {
                new AbilityConfigRow(1008, new[] { "DataTableTest.Shared", "DataTableTest.Shared" }, null, null, null),
                new AbilityConfigRow(1009, new[] { "DataTableTest.Shared" }, null, null, null)
            };
            GameplayTagRuntimePlatform.RegisterProjectTagSource(new GameplayTagDataTableReferenceSource<AbilityConfigRow>(
                "Design.DuplicateReferences",
                rows,
                static row => row.AbilityTags));

            GameplayTagManager.InitializeIfNeeded();

            GameplayTag shared = GameplayTagManager.RequestTag("DataTableTest.Shared");
            Assert.That(shared.IsValid, Is.True);

            int sharedTagCount = 0;
            ReadOnlySpan<GameplayTag> allTags = GameplayTagManager.GetAllTags();
            for (int i = 0; i < allTags.Length; i++)
            {
                if (allTags[i].Name == "DataTableTest.Shared")
                    sharedTagCount++;
            }

            Assert.That(sharedTagCount, Is.EqualTo(1));
        }

        [Test]
        public void DataTableReferenceSource_RejectsRegistryBudgetOverflowAtomically()
        {
            GameplayTagManager.RegisterDynamicTag("DataTableTest.Baseline");
            GameplayTagManager.InitializeIfNeeded();
            int generation = GameplayTagManager.CurrentGeneration;
            int runtimeIndexEpoch = GameplayTagManager.CurrentRuntimeIndexEpoch;
            IReadOnlyList<AbilityConfigRow> rows = new[]
            {
                new AbilityConfigRow(1010, null, null, null, null)
            };
            GameplayTagRuntimePlatform.RegisterProjectTagSource(new GameplayTagDataTableReferenceSource<AbilityConfigRow>(
                "Design.BudgetOverflow",
                rows,
                static _ => EnumerateBudgetOverflowTags()));

            Assert.Throws<InvalidOperationException>(GameplayTagManager.ReloadTags);
            Assert.That(GameplayTagManager.CurrentGeneration, Is.EqualTo(generation));
            Assert.That(GameplayTagManager.CurrentRuntimeIndexEpoch, Is.EqualTo(runtimeIndexEpoch));
            Assert.That(GameplayTagManager.RequestTag("DataTableTest.Baseline").IsValid, Is.True);
        }

        [Test]
        public void DataTableReferenceSource_StopsUnboundedEnumerableAtTerminalAttemptBudget()
        {
            int yieldedCount = 0;
            int laterAccessorCalls = 0;
            IEnumerable<string> EnumerateRepeatedTag()
            {
                while (true)
                {
                    yieldedCount++;
                    yield return "DataTableTest.Repeated";
                }
            }

            GameplayTagRegistrationContext context = new(
                maxRegisteredTagCount: 4,
                maxRegistrationAttemptCount: 4,
                maxRetainedDiagnosticCount: 2);
            IReadOnlyList<AbilityConfigRow> rows = new[]
            {
                new AbilityConfigRow(1011, null, null, null, null)
            };
            GameplayTagDataTableReferenceSource<AbilityConfigRow> source = new(
                "Design.UnboundedReferences",
                rows,
                static _ => string.Empty,
                static _ => true,
                _ => EnumerateRepeatedTag(),
                _ =>
                {
                    laterAccessorCalls++;
                    return Array.Empty<string>();
                });

            source.RegisterTags(context);

            Assert.That(context.IsRegistrationTerminated, Is.True);
            Assert.That(context.RegistrationAttemptCount, Is.EqualTo(4));
            Assert.That(context.RegisteredTagCount, Is.EqualTo(1));
            Assert.That(yieldedCount, Is.EqualTo(5));
            Assert.That(laterAccessorCalls, Is.Zero,
                "No later accessor may run after the candidate reaches a terminal budget error.");
        }

        private static IEnumerable<string> EnumerateBudgetOverflowTags()
        {
            for (int i = 0; i <= GameplayTagUtility.MaxRegisteredTagCount; i++)
            {
                yield return "DataTableBudget.Tag" + i;
            }
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

        private sealed class GeneratedTagCatalogRow
        {
            public GeneratedTagCatalogRow(string name, string comment, bool enabled)
            {
                Name = name;
                Comment = comment;
                Enabled = enabled;
            }

            public string Name { get; }
            public string Comment { get; }
            public bool Enabled { get; }
        }

        private readonly struct GeneratedTagStructRow
        {
            public GeneratedTagStructRow(int id, string name, string comment)
            {
                Id = id;
                Name = name;
                Comment = comment;
            }

            public int Id { get; }
            public string Name { get; }
            public string Comment { get; }
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
