using System.Reflection;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Audio.Tests.Editor
{
    public sealed class AudioClipHandleLifetimeTests
    {
        [Test]
        public void EmbeddedHandle_RetainAndReleaseAreIdempotentOnEditModeMainThread()
        {
            AudioClip clip = AudioClip.Create("EmbeddedHandleTest", 1, 1, 44100, false);
            IAudioClipHandle handle = AudioClipResolver.CreateEmbedded(clip);

            try
            {
                Assert.NotNull(handle);
                Assert.AreEqual(1, handle.RefCount);

                handle.Retain();
                Assert.AreEqual(2, handle.RefCount);

                handle.Release();
                Assert.AreEqual(1, handle.RefCount);
                Assert.AreSame(clip, handle.Clip);

                handle.Release();
                Assert.AreEqual(0, handle.RefCount);
                Assert.IsNull(handle.Clip);

                handle.Release();
                handle.Dispose();
                handle.Retain();

                Assert.AreEqual(0, handle.RefCount);
                Assert.IsNull(handle.Clip);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void ManagedHandle_ReleaseActionRunsOnceAfterFinalReleaseOnEditModeMainThread()
        {
            AudioClip clip = AudioClip.Create("ManagedHandleTest", 1, 1, 44100, false);
            int releaseCount = 0;
            IAudioClipHandle handle = AudioClipResolver.CreateManaged(clip, () => releaseCount++);

            try
            {
                Assert.NotNull(handle);
                Assert.AreEqual(1, handle.RefCount);

                handle.Retain();
                Assert.AreEqual(2, handle.RefCount);

                handle.Release();
                Assert.AreEqual(1, handle.RefCount);
                Assert.AreEqual(0, releaseCount);

                handle.Release();
                Assert.AreEqual(0, handle.RefCount);
                Assert.AreEqual(1, releaseCount);
                Assert.IsNull(handle.Clip);

                handle.Release();
                handle.Dispose();
                handle.Retain();

                Assert.AreEqual(0, handle.RefCount);
                Assert.AreEqual(1, releaseCount);
                Assert.IsNull(handle.Clip);
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void AudioHandle_PooledEventReuse_DoesNotReviveStaleGeneration()
        {
            MethodInfo acquireMethod = typeof(AudioManager).GetMethod(
                "GetActiveEventFromPool",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo recycleMethod = typeof(AudioManager).GetMethod(
                "RecycleInactiveEvent",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(acquireMethod);
            Assert.NotNull(recycleMethod);

            AudioEvent audioEvent = ScriptableObject.CreateInstance<AudioEvent>();
            ActiveEvent first = null;
            ActiveEvent second = null;

            try
            {
                first = (ActiveEvent)acquireMethod.Invoke(null, new object[] { audioEvent, null });
                AudioHandle staleHandle = first.Handle;
                Assert.IsTrue(staleHandle.IsValid);

                recycleMethod.Invoke(null, new object[] { first });
                Assert.IsFalse(staleHandle.IsValid);

                second = (ActiveEvent)acquireMethod.Invoke(null, new object[] { audioEvent, null });
                Assert.AreSame(first, second, "The retained LIFO pool should reuse the event for this generation test.");
                Assert.IsTrue(second.Handle.IsValid);
                Assert.IsFalse(staleHandle.IsValid);
            }
            finally
            {
                if (second != null && second.Handle.IsValid)
                {
                    recycleMethod.Invoke(null, new object[] { second });
                }
                else if (first != null && first.Handle.IsValid)
                {
                    recycleMethod.Invoke(null, new object[] { first });
                }

                Object.DestroyImmediate(audioEvent);
            }
        }

        [Test]
        public void BankClipLease_DisposeFromStaleLifecycle_DoesNotDebitCurrentBudget()
        {
            FieldInfo generationField = typeof(AudioManager).GetField(
                "managerLifecycleGeneration",
                BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo memoryField = typeof(AudioManager).GetField(
                "activeBankClipLeaseMemoryBytes",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(generationField);
            Assert.NotNull(memoryField);

            int originalGeneration = (int)generationField.GetValue(null);
            long originalMemoryBytes = (long)memoryField.GetValue(null);
            const int staleGeneration = 101;
            const int currentGeneration = 102;

            try
            {
                generationField.SetValue(null, currentGeneration);
                memoryField.SetValue(null, 128L);

                var staleLease = new AudioBankClipLease(
                    null,
                    0,
                    new IAudioClipHandle[0],
                    0,
                    64L,
                    staleGeneration);
                staleLease.Dispose();
                Assert.AreEqual(128L, (long)memoryField.GetValue(null));

                var currentLease = new AudioBankClipLease(
                    null,
                    0,
                    new IAudioClipHandle[0],
                    0,
                    64L,
                    currentGeneration);
                currentLease.Dispose();
                Assert.AreEqual(64L, (long)memoryField.GetValue(null));
            }
            finally
            {
                generationField.SetValue(null, originalGeneration);
                memoryField.SetValue(null, originalMemoryBytes);
            }
        }
    }
}
