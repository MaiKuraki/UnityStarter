using System;
using System.Collections.Generic;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Integrations.DataTable;
using NUnit.Framework;

namespace CycloneGames.GameplayTags.DataTable.Tests.Editor
{
    /// <summary>Authorized reload/rollback deep-audit cases; no production code is changed.</summary>
    public sealed class GameplayTagsDataTableDeepAuditTests
    {
        [SetUp]
        public void SetUp()
        {
            GameplayTagManager.ResetForTests();
            GameplayTagRedirector.ClearAll();
            GameplayTagHost.ClearRegisteredProjectTagSources();
            GameplayTagHost.Use(new GameplayTagDataTableTestPlatform());
        }

        [TearDown]
        public void TearDown()
        {
            GameplayTagManager.ResetForTests();
            GameplayTagRedirector.ClearAll();
            GameplayTagHost.ClearRegisteredProjectTagSources();
        }

        [Test]
        public void DataTable_ValidRevisionReload_PublishesAddRemoveAndMetadataChanges()
        {
            var rows = new List<DeepAuditRow>
            {
                new DeepAuditRow("DeepAudit.Table.A", "before"),
                new DeepAuditRow("DeepAudit.Table.B", "remove-me")
            };
            Register(rows);
            GameplayTagManager.InitializeIfNeeded();
            Assert.That(GameplayTagManager.Request("DeepAudit.Table.A").Description, Is.EqualTo("before"));

            rows[0].Description = "after";
            rows.RemoveAt(1);
            rows.Add(new DeepAuditRow("DeepAudit.Table.C", "added"));
            GameplayTagManager.Reload(false);

            Multiple(() =>
            {
                Assert.That(GameplayTagManager.Request("DeepAudit.Table.A").Description, Is.EqualTo("after"));
                Assert.That(GameplayTagManager.TryRequest("DeepAudit.Table.B", out _), Is.False);
                Assert.That(GameplayTagManager.Request("DeepAudit.Table.C").Description, Is.EqualTo("added"));
            });
        }

        [Test]
        public void DataTable_InvalidRevisionReload_RollsBackWithoutPartialPublication()
        {
            var rows = new List<DeepAuditRow>
            {
                new DeepAuditRow("DeepAudit.Table.Stable", "stable")
            };
            Register(rows);
            GameplayTagManager.InitializeIfNeeded();
            GameplayTag stableBefore = GameplayTagManager.Request("DeepAudit.Table.Stable");
            int generation = GameplayTagManager.Generation;
            int epoch = GameplayTagManager.RuntimeIndexEpoch;
            ulong manifest = GameplayTagManager.ManifestHash;

            rows[0].Description = "must-not-publish";
            rows.Add(new DeepAuditRow("DeepAudit.Table.New", "must-not-publish"));
            rows.Add(new DeepAuditRow("", "invalid"));
            Assert.Throws<InvalidOperationException>(() => GameplayTagManager.Reload(false));

            Multiple(() =>
            {
                Assert.That(GameplayTagManager.Generation, Is.EqualTo(generation));
                Assert.That(GameplayTagManager.RuntimeIndexEpoch, Is.EqualTo(epoch));
                Assert.That(GameplayTagManager.ManifestHash, Is.EqualTo(manifest));
                Assert.That(GameplayTagManager.Request("DeepAudit.Table.Stable").RuntimeIndex,
                    Is.EqualTo(stableBefore.RuntimeIndex));
                Assert.That(GameplayTagManager.Request("DeepAudit.Table.Stable").Description, Is.EqualTo("stable"));
                Assert.That(GameplayTagManager.TryRequest("DeepAudit.Table.New", out _), Is.False);
            });
        }

        [Test]
        public void DataTable_InvalidFlagsRevision_RollsBackWithoutPartialPublication()
        {
            var rows = new List<DeepAuditRow>
            {
                new DeepAuditRow("DeepAudit.Table.Flags", "stable")
            };
            Register(rows);
            GameplayTagManager.InitializeIfNeeded();
            int generation = GameplayTagManager.Generation;
            int epoch = GameplayTagManager.RuntimeIndexEpoch;
            ulong manifest = GameplayTagManager.ManifestHash;

            rows[0].Description = "must-not-publish";
            GameplayTagHost.RegisterProjectTagSource(new GameplayTagDataTableSource<DeepAuditRow>(
                "DeepAudit.MutableRows",
                rows,
                static row => row.Name,
                static row => row.Description,
                static _ => (GameplayTagFlags)2));
            Assert.Throws<InvalidOperationException>(() => GameplayTagManager.Reload(false));

            Multiple(() =>
            {
                Assert.That(GameplayTagManager.Generation, Is.EqualTo(generation));
                Assert.That(GameplayTagManager.RuntimeIndexEpoch, Is.EqualTo(epoch));
                Assert.That(GameplayTagManager.ManifestHash, Is.EqualTo(manifest));
                Assert.That(GameplayTagManager.Request("DeepAudit.Table.Flags").Description, Is.EqualTo("stable"));
            });
        }

        private static void Multiple(Action assertions) => assertions();

        private static void Register(List<DeepAuditRow> rows)
        {
            GameplayTagHost.RegisterProjectTagSource(new GameplayTagDataTableSource<DeepAuditRow>(
                "DeepAudit.MutableRows",
                rows,
                static row => row.Name,
                static row => row.Description));
        }

        private sealed class DeepAuditRow
        {
            internal DeepAuditRow(string name, string description)
            {
                Name = name;
                Description = description;
            }

            internal string Name { get; }
            internal string Description { get; set; }
        }
    }
}
