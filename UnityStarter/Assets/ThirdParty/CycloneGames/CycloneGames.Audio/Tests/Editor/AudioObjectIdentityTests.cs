// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.Audio.Tests
{
    /// <summary>
    /// Verifies session-scoped identity and the source-level ban on Unity instance IDs.
    /// </summary>
    public sealed class AudioObjectIdentityTests
    {
        private readonly List<Object> created = new List<Object>();
        private readonly List<GameObject> createdGameObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < created.Count; i++)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();

            for (int i = 0; i < createdGameObjects.Count; i++)
            {
                if (createdGameObjects[i] != null) Object.DestroyImmediate(createdGameObjects[i]);
            }

            createdGameObjects.Clear();
        }

        [Test]
        public void Of_NullReference_ReturnsZero()
        {
            Assert.AreEqual(0, AudioObjectId.Of(null));
        }

        [Test]
        public void Of_SameAsset_ReturnsStableIdentity()
        {
            AudioBank bank = Track(ScriptableObject.CreateInstance<AudioBank>());

            int first = AudioObjectId.Of(bank);
            int second = AudioObjectId.Of(bank);
            int third = bank.RuntimeId;

            Assert.AreNotEqual(0, first);
            Assert.AreEqual(first, second);
            Assert.AreEqual(first, third);
        }

        [Test]
        public void Of_ForeignObject_ReturnsStableIdentity()
        {
            GameObject gameObject = Track(new GameObject("AudioIdentityProbe"));
            Transform transform = gameObject.transform;

            int objectId = AudioObjectId.Of(gameObject);
            int transformId = AudioObjectId.Of(transform);

            Assert.AreNotEqual(0, objectId);
            Assert.AreNotEqual(0, transformId);
            Assert.AreNotEqual(objectId, transformId, "A GameObject and its Transform must not share an identity.");
            Assert.AreEqual(objectId, AudioObjectId.Of(gameObject));
            Assert.AreEqual(transformId, AudioObjectId.Of(transform));
        }

        [Test]
        public void Of_EveryAssetSubtype_IsUnique()
        {
            const int sampleCount = 256;
            var ids = new HashSet<int>();

            for (int i = 0; i < sampleCount; i++)
            {
                Assert.IsTrue(ids.Add(AudioObjectId.Of(Track(ScriptableObject.CreateInstance<AudioBank>()))));
                Assert.IsTrue(ids.Add(AudioObjectId.Of(Track(ScriptableObject.CreateInstance<AudioEvent>()))));
                Assert.IsTrue(ids.Add(AudioObjectId.Of(Track(ScriptableObject.CreateInstance<AudioParameter>()))));
                Assert.IsTrue(ids.Add(AudioObjectId.Of(Track(ScriptableObject.CreateInstance<AudioSwitch>()))));
                Assert.IsTrue(ids.Add(AudioObjectId.Of(Track(ScriptableObject.CreateInstance<AudioStateGroup>()))));
                Assert.IsTrue(ids.Add(AudioObjectId.Of(Track(ScriptableObject.CreateInstance<AudioStateMixProfile>()))));
                Assert.IsTrue(ids.Add(AudioObjectId.Of(Track(ScriptableObject.CreateInstance<AudioClipReference>()))));
            }

            Assert.AreEqual(sampleCount * 7, ids.Count);
            Assert.IsFalse(ids.Contains(0));
        }

        [Test]
        public void Of_DestroyedAssets_AreNeverReissued()
        {
            const int batchSize = 200;
            var retired = new HashSet<int>();

            for (int i = 0; i < batchSize; i++)
            {
                AudioBank bank = ScriptableObject.CreateInstance<AudioBank>();
                retired.Add(AudioObjectId.Of(bank));
                Object.DestroyImmediate(bank);
            }

            var live = new HashSet<int>();
            for (int i = 0; i < batchSize; i++)
            {
                AudioBank bank = Track(ScriptableObject.CreateInstance<AudioBank>());
                int id = AudioObjectId.Of(bank);
                Assert.IsTrue(live.Add(id), "Identity was reissued while a previous owner was still live.");
                Assert.IsFalse(retired.Contains(id), "Identity was reissued after its previous owner was destroyed.");
            }
        }

        [Test]
        public void Of_DestroyedGameObject_KeepsIdentityAndSuccessorGetsNewOne()
        {
            GameObject first = new GameObject("AudioIdentityRetired");
            int retiredId = AudioObjectId.Of(first);
            Object.DestroyImmediate(first);

            GameObject second = Track(new GameObject("AudioIdentitySuccessor"));
            int successorId = AudioObjectId.Of(second);

            Assert.AreNotEqual(0, retiredId);
            Assert.AreNotEqual(retiredId, successorId);
        }

        [Test]
        public void Next_IsUniqueAcrossThreads()
        {
            const int threadCount = 4;
            const int idsPerThread = 4096;
            var results = new int[threadCount][];

            var threads = new System.Threading.Thread[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int index = t;
                threads[t] = new System.Threading.Thread(() =>
                {
                    int[] ids = new int[idsPerThread];
                    for (int i = 0; i < idsPerThread; i++)
                    {
                        ids[i] = AudioObjectId.Next();
                    }

                    results[index] = ids;
                });
            }

            for (int t = 0; t < threadCount; t++) threads[t].Start();
            for (int t = 0; t < threadCount; t++) threads[t].Join();

            var seen = new HashSet<int>();
            for (int t = 0; t < threadCount; t++)
            {
                Assert.IsNotNull(results[t]);
                for (int i = 0; i < idsPerThread; i++)
                {
                    Assert.IsTrue(seen.Add(results[t][i]), "Identity allocation produced a duplicate value.");
                }
            }

            Assert.AreEqual(threadCount * idsPerThread, seen.Count);
        }

        [Test]
        public void RuntimeId_ReadPath_AllocatesNothing()
        {
            AudioBank bank = Track(ScriptableObject.CreateInstance<AudioBank>());
            GameObject gameObject = Track(new GameObject("AudioIdentityAllocationProbe"));

            _ = AudioObjectId.Of(bank);
            _ = AudioObjectId.Of(gameObject);

            const int iterations = 200000;
            long assetBytes = MeasureAllocatedBytes(() => _ = AudioObjectId.Of(bank), iterations);
            long foreignBytes = MeasureAllocatedBytes(() => _ = AudioObjectId.Of(gameObject), iterations);

            Assert.AreEqual(0, assetBytes, "Reading an asset identity must not allocate.");
            Assert.AreEqual(0, foreignBytes, "Re-reading a foreign object identity must not allocate.");
        }

        [Test]
        public void GetInstanceId_IsNoLongerUsedByTheModule()
        {
            // Built at runtime so this guard does not match its own source text.
            string retiredApi = "GetInstance" + "ID(";
            string replacementApi = "GetEntity" + "Id(";

            string packageRoot = Path.Combine(Application.dataPath, "ThirdParty/CycloneGames/CycloneGames.Audio");
            Assert.IsTrue(Directory.Exists(packageRoot), $"Audio package root not found: {packageRoot}");

            // Tests may deliberately call the retired API to measure it; shipping code may not.
            string testsRoot = NormalizeDirectory(Path.Combine(packageRoot, "Tests"));

            var offenders = new StringBuilder();
            int scanned = 0;

            foreach (string file in Directory.EnumerateFiles(packageRoot, "*.cs", SearchOption.AllDirectories))
            {
                string normalizedFile = NormalizeDirectory(file);
                if (normalizedFile.StartsWith(testsRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                scanned++;
                string text = File.ReadAllText(file);
                if (text.Contains(retiredApi) || text.Contains(replacementApi))
                {
                    offenders.Append(normalizedFile).Append('\n');
                }
            }

            Assert.Greater(scanned, 40, "The guard did not scan the expected number of source files.");
            Assert.AreEqual(
                string.Empty,
                offenders.ToString(),
                "Audio sources must resolve identity through AudioObjectId; Unity instance IDs are retired in newer editors.");
        }

        private static string NormalizeDirectory(string path)
        {
            return Path.GetFullPath(path).Replace('\\', '/') + "/";
        }

        private T Track<T>(T value) where T : Object
        {
            created.Add(value);
            return value;
        }

        private GameObject Track(GameObject value)
        {
            createdGameObjects.Add(value);
            return value;
        }

        internal static long MeasureAllocatedBytes(Action action, int iterations)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetTotalMemory(true);
            for (int i = 0; i < iterations; i++)
            {
                action();
            }

            return GC.GetTotalMemory(false) - before;
        }
    }
}
