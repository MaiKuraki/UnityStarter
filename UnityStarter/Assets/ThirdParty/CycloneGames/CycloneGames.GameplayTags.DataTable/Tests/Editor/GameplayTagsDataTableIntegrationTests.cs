using System;
using System.Collections.Generic;
using System.Text;

using CycloneGames.DataTable;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Integrations.DataTable;
using CycloneGames.Logging;
using NUnit.Framework;

namespace CycloneGames.GameplayTags.DataTable.Tests.Editor
{
    public sealed class GameplayTagsDataTableIntegrationTests
    {
        private ScopedSilentLogWriter _logScope;

        [SetUp]
        public void SetUp()
        {
            GameplayTagManager.ResetForTests();
            GameplayTagRedirector.ClearAll();
            // A host that accepts the project tag sources under test; no build data and not playing.
            // The null platform cannot hold project sources, so RegisterProjectTagSource would throw.
            GameplayTagHost.Use(new GameplayTagDataTableTestPlatform());
            _logScope = new ScopedSilentLogWriter();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                GameplayTagManager.ResetForTests();
                GameplayTagRedirector.ClearAll();
                GameplayTagHost.ClearRegisteredProjectTagSources();
            }
            finally
            {
                _logScope?.Dispose();
                _logScope = null;
            }
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

            GameplayTagHost.RegisterProjectTagSource(new GameplayTagDataTableSource<TagCatalogRow>(
                "Design.GameplayTags",
                table,
                static row => row.Name,
                static row => row.Comment,
                static row => row.Flags,
                static row => row.Enabled));

            GameplayTagManager.InitializeIfNeeded();

            Assert.That(GameplayTagManager.Request("DataTableTest.Ability.Fireball").Description, Is.EqualTo("Fireball ability."));
            Assert.That(GameplayTagManager.Request("DataTableTest.Effect.Burn").IsValid, Is.True);
            Assert.That(GameplayTagManager.TryRequest("DataTableTest.Hidden.EditorOnly", out _), Is.False);
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

            GameplayTagHost.RegisterProjectTagSource(
                new GameplayTagDataTableSource<GeneratedTagCatalogRow>(
                    "Design.GeneratedGameplayTags",
                    table,
                    static row => row.Name,
                    static row => row.Comment,
                    isEnabled: static row => row.Enabled));

            GameplayTagManager.InitializeIfNeeded();

            Assert.That(
                GameplayTagManager.Request("DataTableTest.Generated.LubanCompatible").Description,
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

            GameplayTagHost.RegisterProjectTagSource(
                new GameplayTagDataTableSource<GeneratedTagStructRow>(
                    "Design.GeneratedValueTypeGameplayTags",
                    table,
                    static row => row.Name,
                    static row => row.Comment));

            GameplayTagManager.InitializeIfNeeded();

            Assert.That(
                GameplayTagManager.Request("DataTableTest.Generated.FlatBufferStyle").Description,
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

            GameplayTagHost.RegisterProjectTagSource(new GameplayTagDataTableReferenceSource<AbilityConfigRow>(
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

            Assert.That(abilityTags.HasTagExact(GameplayTagManager.Request("DataTableTest.Ability.Fireball")), Is.True);
            Assert.That(GameplayTagManager.Request("DataTableTest.Ability.Damage.Fire").IsValid, Is.True);
            Assert.That(GameplayTagManager.Request("DataTableTest.State.CrowdControl.Stunned").IsValid, Is.True);
            Assert.That(activationRequirements.RequiredTags.HasTagExact(GameplayTagManager.Request("DataTableTest.State.Combat.Ready")), Is.True);
            Assert.That(activationRequirements.ForbiddenTags.HasTagExact(GameplayTagManager.Request("DataTableTest.State.CrowdControl.Stunned")), Is.True);
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
            GameplayTagHost.RegisterProjectTagSource(source);
            GameplayTagManager.InitializeIfNeeded();

            Assert.That(GameplayTagManager.Request("DataTableTest.OriginalAccessor").IsValid, Is.True);
            Assert.That(GameplayTagManager.TryRequest("DataTableTest.MutatedAccessor", out _), Is.False);
        }

        [Test]
        public void DataTableReferenceSource_RejectsEmptyTagEntriesAtomically()
        {
            GameplayTagManager.RegisterDynamicTag("DataTableTest.Baseline");
            GameplayTagManager.InitializeIfNeeded();
            int generation = GameplayTagManager.Generation;
            int runtimeIndexEpoch = GameplayTagManager.RuntimeIndexEpoch;
            DataTable<AbilityConfigRow> table = new(new[]
            {
                new AbilityConfigRow(1002, new[] { "DataTableTest.Valid", "" }, null, null, null)
            });
            GameplayTagHost.RegisterProjectTagSource(new GameplayTagDataTableReferenceSource<AbilityConfigRow>(
                "Design.InvalidAbilities",
                table,
                static row => row.AbilityTags));

            Assert.Throws<InvalidOperationException>(GameplayTagManager.Reload);
            Assert.That(GameplayTagManager.Generation, Is.EqualTo(generation));
            Assert.That(GameplayTagManager.RuntimeIndexEpoch, Is.EqualTo(runtimeIndexEpoch));
            Assert.That(GameplayTagManager.Request("DataTableTest.Baseline").IsValid, Is.True);
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

            GameplayTagHost.RegisterProjectTagSource(new GameplayTagDataTableReferenceSource<AbilityConfigRow>(
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
            Assert.That(GameplayTagManager.TryRequest("DataTableTest.Disabled", out _), Is.False);
            Assert.That(GameplayTagManager.Request("DataTableTest.Enabled").IsValid, Is.True);
        }

        [Test]
        public void DataTableReferenceSource_PropagatesAccessorFailureWithoutPublishingPartialRegistry()
        {
            GameplayTagManager.RegisterDynamicTag("DataTableTest.Baseline");
            GameplayTagManager.InitializeIfNeeded();
            int generation = GameplayTagManager.Generation;
            int runtimeIndexEpoch = GameplayTagManager.RuntimeIndexEpoch;
            IReadOnlyList<AbilityConfigRow> rows = new[]
            {
                new AbilityConfigRow(1006, new[] { "DataTableTest.BeforeFailure" }, null, null, null),
                new AbilityConfigRow(1007, new[] { "DataTableTest.Throws" }, null, null, null)
            };
            GameplayTagHost.RegisterProjectTagSource(new GameplayTagDataTableReferenceSource<AbilityConfigRow>(
                "Design.ThrowingAbilities",
                rows,
                row => row.Id == 1007
                    ? throw new InvalidOperationException("Injected accessor failure.")
                    : row.AbilityTags));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(GameplayTagManager.Reload);

            Assert.That(error.Message, Is.EqualTo("Injected accessor failure."));
            Assert.That(GameplayTagManager.Generation, Is.EqualTo(generation));
            Assert.That(GameplayTagManager.RuntimeIndexEpoch, Is.EqualTo(runtimeIndexEpoch));
            Assert.That(GameplayTagManager.Request("DataTableTest.Baseline").IsValid, Is.True);
            Assert.That(GameplayTagManager.TryRequest("DataTableTest.BeforeFailure", out _), Is.False);
        }

        [Test]
        public void DataTableReferenceSource_DeduplicatesRepeatedReferences()
        {
            IReadOnlyList<AbilityConfigRow> rows = new[]
            {
                new AbilityConfigRow(1008, new[] { "DataTableTest.Shared", "DataTableTest.Shared" }, null, null, null),
                new AbilityConfigRow(1009, new[] { "DataTableTest.Shared" }, null, null, null)
            };
            GameplayTagHost.RegisterProjectTagSource(new GameplayTagDataTableReferenceSource<AbilityConfigRow>(
                "Design.DuplicateReferences",
                rows,
                static row => row.AbilityTags));

            GameplayTagManager.InitializeIfNeeded();

            GameplayTag shared = GameplayTagManager.Request("DataTableTest.Shared");
            Assert.That(shared.IsValid, Is.True);

            int sharedTagCount = 0;
            GameplayTag[] allTags = GameplayTagManager.Current.CreateAllTagsArray();
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
            int generation = GameplayTagManager.Generation;
            int runtimeIndexEpoch = GameplayTagManager.RuntimeIndexEpoch;
            IReadOnlyList<AbilityConfigRow> rows = new[]
            {
                new AbilityConfigRow(1010, null, null, null, null)
            };
            GameplayTagHost.RegisterProjectTagSource(new GameplayTagDataTableReferenceSource<AbilityConfigRow>(
                "Design.BudgetOverflow",
                rows,
                static _ => EnumerateBudgetOverflowTags()));

            Assert.Throws<InvalidOperationException>(GameplayTagManager.Reload);
            Assert.That(GameplayTagManager.Generation, Is.EqualTo(generation));
            Assert.That(GameplayTagManager.RuntimeIndexEpoch, Is.EqualTo(runtimeIndexEpoch));
            Assert.That(GameplayTagManager.Request("DataTableTest.Baseline").IsValid, Is.True);
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

        private sealed class ScopedSilentLogWriter : ILogWriter, IDisposable
        {
            private ILogWriter _previousWriter;
            private bool _isDisposed;

            public ScopedSilentLogWriter()
            {
                _previousWriter = LogRuntime.Writer;
                if (!LogRuntime.TryReplaceWriter(_previousWriter, this))
                {
                    throw new InvalidOperationException("The process log writer changed while the test scope was being installed.");
                }
            }

            public bool IsEnabled(LogSeverity severity, string category) => false;

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
            }

            public void Write(
                LogSeverity severity,
                string category,
                Action<StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
            }

            public void Write<TState>(
                LogSeverity severity,
                string category,
                TState state,
                Action<TState, StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
            }

            public void WriteException(
                LogSeverity severity,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
            }

            public void Dispose()
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                ILogWriter previousWriter = _previousWriter;
                _previousWriter = null;
                LogRuntime.TryReplaceWriter(this, previousWriter);
            }
        }
    }
}
