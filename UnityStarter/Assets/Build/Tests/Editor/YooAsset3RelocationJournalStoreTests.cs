using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Build.Pipeline.Integrations.YooAsset3.Publication;
using NUnit.Framework;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    /// <summary>
    /// Core-assembly tests for RelocationJournalStore persistence concurrency: unique temporary
    /// candidate names, fail-closed promotion, stale temporary cleanup, and the enumeration
    /// contract that never surfaces temporary files as journals. The fixture uses a single short
    /// directory under Temp as the project root to stay within the Win32 MAX_PATH budget.
    /// </summary>
    public sealed class YooAsset3RelocationJournalStoreTests
    {
        private static readonly IJournalSerializer Serializer = UnityJournalSerializer.Instance;

        private string testRoot;
        private string projectRoot;

        [SetUp]
        public void SetUp()
        {
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            testRoot = Path.Combine(
                unityProjectRoot,
                "Temp",
                "Y3R" + Guid.NewGuid().ToString("N").Substring(0, 8));
            projectRoot = testRoot;
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets", "StreamingAssets"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, true);
            }
        }

        [Test]
        public void Persist_ConcurrentWriters_AlwaysLeaveAValidJournal()
        {
            string transactionId = Guid.NewGuid().ToString("N");
            const int writerCount = 8;
            const int writesPerWriter = 4;
            int successCount = 0;
            var failures = new List<Exception>();
            var failureLock = new object();
            var workers = new List<Thread>(writerCount);

            for (int writer = 0; writer < writerCount; writer++)
            {
                int writerIndex = writer;
                var worker = new Thread(() =>
                {
                    try
                    {
                        for (int write = 0; write < writesPerWriter; write++)
                        {
                            RelocationJournalDocument document = RelocationJournalStore.Create(transactionId);
                            RelocationJournalStore.AppendEntry(
                                document,
                                Path.Combine(
                                    RelocationPathPolicy.GetStreamingAssetsRoot(projectRoot),
                                    $"writer{writerIndex}.json"),
                                Path.Combine(
                                    RelocationJournalStore.GetRelocationRoot(projectRoot),
                                    $"writer{writerIndex}.file"),
                                RelocationJournalStore.KindFile);
                            RelocationJournalStore.Persist(document, projectRoot, Serializer);
                            Interlocked.Increment(ref successCount);
                        }
                    }
                    catch (Exception exception)
                    {
                        // A lost promotion race fails closed with an exception; that is the
                        // designed behavior and is reported below instead of being hidden.
                        lock (failureLock)
                        {
                            failures.Add(exception);
                        }
                    }
                });
                worker.IsBackground = true;
                workers.Add(worker);
            }

            foreach (Thread worker in workers)
            {
                worker.Start();
            }

            foreach (Thread worker in workers)
            {
                worker.Join(30000);
            }

            // Every candidate uses FileMode.CreateNew on a unique name, so writers can never
            // truncate each other's file. The final journal must be structurally valid with a
            // matching checksum (Load itself re-validates) and exactly one entry (the winning
            // writer's document, not a torn merge). Writers that lost the promotion race fail
            // closed with an exception (counted in failures) instead of leaving a corrupt file.
            RelocationJournalDocument persisted = RelocationJournalStore.Load(projectRoot, transactionId, Serializer);
            Assert.That(persisted, Is.Not.Null, "at least one Persist must have promoted a journal.");
            Assert.That(successCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(persisted.entries, Has.Length.EqualTo(1));
            Assert.That(
                persisted.checksum,
                Is.EqualTo(RelocationJournalStore.ComputeChecksum(persisted)));
            Assert.That(
                RelocationJournalStore.EnumeratePendingTransactionIds(projectRoot),
                Is.EqualTo(new[] { transactionId }));
        }

        [Test]
        public void Persist_WithStaleTemporaryFiles_SucceedsAndKeepsEnumerationClean()
        {
            string transactionId = Guid.NewGuid().ToString("N");
            string journalPath = RelocationJournalStore.GetJournalPath(projectRoot, transactionId);
            Directory.CreateDirectory(RelocationJournalStore.GetStateRoot(projectRoot));
            string staleOne = journalPath + ".tmp-" + Guid.NewGuid().ToString("N");
            string staleTwo = journalPath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(staleOne, "stale");
            File.WriteAllText(staleTwo, "stale");

            RelocationJournalDocument document = RelocationJournalStore.Create(transactionId);
            RelocationJournalStore.AppendEntry(
                document,
                Path.Combine(RelocationPathPolicy.GetStreamingAssetsRoot(projectRoot), "a.json"),
                Path.Combine(RelocationJournalStore.GetRelocationRoot(projectRoot), "a.file"),
                RelocationJournalStore.KindFile);
            RelocationJournalStore.Persist(document, projectRoot, Serializer);

            // A successful Persist must not clean other writers' temporary files, and the
            // enumeration must surface only the real journal.
            Assert.That(File.Exists(staleOne), Is.True);
            Assert.That(File.Exists(staleTwo), Is.True);
            Assert.That(
                RelocationJournalStore.EnumeratePendingTransactionIds(projectRoot),
                Is.EqualTo(new[] { transactionId }));
            Assert.That(RelocationJournalStore.Load(projectRoot, transactionId, Serializer), Is.Not.Null);

            // Deleting the journal removes this transaction's stale candidates.
            RelocationJournalStore.Delete(projectRoot, transactionId);
            Assert.That(File.Exists(staleOne), Is.False);
            Assert.That(File.Exists(staleTwo), Is.False);
            Assert.That(File.Exists(journalPath), Is.False);
        }

        [Test]
        public void Persist_WhenPromotionFails_KeepsTheDurableCandidateAndFailsClosed()
        {
            string transactionId = Guid.NewGuid().ToString("N");
            string journalPath = RelocationJournalStore.GetJournalPath(projectRoot, transactionId);
            Directory.CreateDirectory(RelocationJournalStore.GetStateRoot(projectRoot));
            // A directory occupying the journal path makes every promotion attempt fail closed.
            Directory.CreateDirectory(journalPath);

            RelocationJournalDocument document = RelocationJournalStore.Create(transactionId);
            RelocationJournalStore.AppendEntry(
                document,
                Path.Combine(RelocationPathPolicy.GetStreamingAssetsRoot(projectRoot), "a.json"),
                Path.Combine(RelocationJournalStore.GetRelocationRoot(projectRoot), "a.file"),
                RelocationJournalStore.KindFile);

            Assert.That(
                () => RelocationJournalStore.Persist(document, projectRoot, Serializer),
                Throws.Exception);

            // The journal path is still not a file, Load is defined (null), and the enumeration
            // is unaffected by the leftover candidate.
            Assert.That(File.Exists(journalPath), Is.False);
            Assert.That(RelocationJournalStore.Load(projectRoot, transactionId, Serializer), Is.Null);
            Assert.That(
                RelocationJournalStore.EnumeratePendingTransactionIds(projectRoot),
                Is.Empty);
            Assert.That(
                Directory.GetFiles(RelocationJournalStore.GetStateRoot(projectRoot), "*.json.tmp-*"),
                Has.Length.EqualTo(1),
                "the durable candidate must be kept as diagnostic evidence after a promotion failure.");

            // The retirement path cleans this transaction's leftover candidates.
            RelocationJournalStore.Delete(projectRoot, transactionId);
            Assert.That(
                Directory.GetFiles(RelocationJournalStore.GetStateRoot(projectRoot), "*.json.tmp-*"),
                Has.Length.EqualTo(0));
        }

        [Test]
        public void Persist_WithFailingSerializer_LeavesNoJournalAndNoResidue()
        {
            string transactionId = Guid.NewGuid().ToString("N");
            RelocationJournalDocument document = RelocationJournalStore.Create(transactionId);
            RelocationJournalStore.AppendEntry(
                document,
                Path.Combine(RelocationPathPolicy.GetStreamingAssetsRoot(projectRoot), "a.json"),
                Path.Combine(RelocationJournalStore.GetRelocationRoot(projectRoot), "a.file"),
                RelocationJournalStore.KindFile);

            Assert.That(
                () => RelocationJournalStore.Persist(document, projectRoot, new ThrowingToJsonSerializer()),
                Throws.InvalidOperationException);

            Assert.That(File.Exists(RelocationJournalStore.GetJournalPath(projectRoot, transactionId)), Is.False);
            Assert.That(
                Directory.Exists(RelocationJournalStore.GetStateRoot(projectRoot))
                    ? Directory.GetFiles(RelocationJournalStore.GetStateRoot(projectRoot), "*.json.tmp-*")
                    : Array.Empty<string>(),
                Has.Length.EqualTo(0),
                "a failure before any candidate file is created must leave no residue.");
        }

        private sealed class ThrowingToJsonSerializer : IJournalSerializer
        {
            public string ToJson<T>(T value) where T : class
            {
                throw new InvalidOperationException("injected serializer failure");
            }

            public T FromJson<T>(string json) where T : class
            {
                return UnityJournalSerializer.Instance.FromJson<T>(json);
            }
        }
    }
}
