using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Audio.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace CycloneGames.Audio.Tests.Editor
{
    public sealed class AudioClipResolverOwnershipTests
    {
        [Test]
        public void RegisterReferenceLoader_DisposingStaleLeaseDoesNotRemoveReplacement()
        {
            AudioClipReference reference = AudioClipReference.CreateRuntime(
                AudioLocationKind.AssetAddress,
                "audio/lease-test");
            IDisposable firstLease = null;
            IDisposable replacementLease = null;
            int firstLoaderCalls = 0;
            int replacementLoaderCalls = 0;

            AudioClipReferenceLoader firstLoader = (clipReference, cancellationToken) =>
            {
                firstLoaderCalls++;
                return UniTask.FromResult<IAudioClipHandle>(null);
            };
            AudioClipReferenceLoader replacementLoader = (clipReference, cancellationToken) =>
            {
                replacementLoaderCalls++;
                return UniTask.FromResult<IAudioClipHandle>(null);
            };

            try
            {
                firstLease = AudioClipResolver.RegisterReferenceLoaderScoped(reference, firstLoader);
                replacementLease = AudioClipResolver.RegisterReferenceLoaderScoped(reference, replacementLoader);

                firstLease.Dispose();
                firstLease.Dispose();

                IAudioClipHandle result = AudioClipResolver
                    .LoadExternalAsync(reference, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                Assert.IsNull(result);
                Assert.AreEqual(0, firstLoaderCalls);
                Assert.AreEqual(1, replacementLoaderCalls);
            }
            finally
            {
                replacementLease?.Dispose();
                firstLease?.Dispose();
                UnityEngine.Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void RegisterReferenceLoader_ReusingDelegateStillHonorsNewestLease()
        {
            AudioClipReference reference = AudioClipReference.CreateRuntime(
                AudioLocationKind.AssetAddress,
                "audio/reused-loader-test");
            IDisposable firstLease = null;
            IDisposable replacementLease = null;
            int loaderCalls = 0;

            AudioClipReferenceLoader loader = (clipReference, cancellationToken) =>
            {
                loaderCalls++;
                return UniTask.FromResult<IAudioClipHandle>(null);
            };

            try
            {
                firstLease = AudioClipResolver.RegisterReferenceLoaderScoped(reference, loader);
                replacementLease = AudioClipResolver.RegisterReferenceLoaderScoped(reference, loader);
                firstLease.Dispose();

                IAudioClipHandle result = AudioClipResolver
                    .LoadExternalAsync(reference, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                Assert.IsNull(result);
                Assert.AreEqual(1, loaderCalls);
            }
            finally
            {
                replacementLease?.Dispose();
                firstLease?.Dispose();
                UnityEngine.Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void RegisterReferenceLoader_DisposingInnerLeaseRestoresOuterLoader()
        {
            AudioClipReference reference = AudioClipReference.CreateRuntime(
                AudioLocationKind.AssetAddress,
                "audio/nested-loader-test");
            IDisposable outerLease = null;
            IDisposable innerLease = null;
            int outerLoaderCalls = 0;
            int innerLoaderCalls = 0;

            try
            {
                outerLease = AudioClipResolver.RegisterReferenceLoaderScoped(reference, (clipReference, cancellationToken) =>
                {
                    outerLoaderCalls++;
                    return UniTask.FromResult<IAudioClipHandle>(null);
                });
                innerLease = AudioClipResolver.RegisterReferenceLoaderScoped(reference, (clipReference, cancellationToken) =>
                {
                    innerLoaderCalls++;
                    return UniTask.FromResult<IAudioClipHandle>(null);
                });

                AudioClipResolver.LoadExternalAsync(reference, CancellationToken.None).GetAwaiter().GetResult();
                Assert.AreEqual(0, outerLoaderCalls);
                Assert.AreEqual(1, innerLoaderCalls);

                innerLease.Dispose();
                innerLease.Dispose();
                AudioClipResolver.LoadExternalAsync(reference, CancellationToken.None).GetAwaiter().GetResult();

                Assert.AreEqual(1, outerLoaderCalls);
                Assert.AreEqual(1, innerLoaderCalls);
            }
            finally
            {
                innerLease?.Dispose();
                outerLease?.Dispose();
                UnityEngine.Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void RegisterLocationKindLoader_OutOfOrderDisposeKeepsNewestLoader()
        {
            AudioClipReference reference = AudioClipReference.CreateRuntime(
                AudioLocationKind.AssetAddress,
                "audio/location-loader-test");
            IDisposable outerLease = null;
            IDisposable innerLease = null;
            int outerLoaderCalls = 0;
            int innerLoaderCalls = 0;

            try
            {
                outerLease = AudioClipResolver.RegisterLocationKindLoaderScoped(
                    AudioLocationKind.AssetAddress,
                    (clipReference, cancellationToken) =>
                    {
                        outerLoaderCalls++;
                        return UniTask.FromResult<IAudioClipHandle>(null);
                    });
                innerLease = AudioClipResolver.RegisterLocationKindLoaderScoped(
                    AudioLocationKind.AssetAddress,
                    (clipReference, cancellationToken) =>
                    {
                        innerLoaderCalls++;
                        return UniTask.FromResult<IAudioClipHandle>(null);
                    });

                outerLease.Dispose();
                outerLease.Dispose();
                AudioClipResolver.LoadExternalAsync(reference, CancellationToken.None).GetAwaiter().GetResult();

                Assert.AreEqual(0, outerLoaderCalls);
                Assert.AreEqual(1, innerLoaderCalls);
            }
            finally
            {
                innerLease?.Dispose();
                outerLease?.Dispose();
                UnityEngine.Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void RegisterProvider_DoubleRegistrationIsReferenceCounted()
        {
            var provider = new TestProvider();
            IDisposable firstLease = null;
            IDisposable secondLease = null;
            var providers = new List<IAudioClipProvider>();

            try
            {
                firstLease = AudioClipResolver.RegisterProviderScoped(provider);
                secondLease = AudioClipResolver.RegisterProviderScoped(provider);

                firstLease.Dispose();
                firstLease.Dispose();
                AudioClipResolver.GetProviders(providers);
                CollectionAssert.Contains(providers, provider);

                secondLease.Dispose();
                AudioClipResolver.GetProviders(providers);
                CollectionAssert.DoesNotContain(providers, provider);
            }
            finally
            {
                secondLease?.Dispose();
                firstLease?.Dispose();
                AudioClipResolver.UnregisterProvider(provider);
            }
        }

        [Test]
        public void PreloadBankClips_SameBankAtSingleLeaseBudget_ReplacesResidency()
        {
            AudioClipReference reference = AudioClipReference.CreateRuntime(
                AudioLocationKind.AssetAddress,
                "audio/repeated-preload-budget-test");
            UnityEngine.AudioClip clip = UnityEngine.AudioClip.Create(
                "RepeatedPreloadBudgetTest",
                16,
                1,
                44100,
                false);
            AudioFile audioFile = UnityEngine.ScriptableObject.CreateInstance<AudioFile>();
            AudioEvent audioEvent = UnityEngine.ScriptableObject.CreateInstance<AudioEvent>();
            AudioBank bank = UnityEngine.ScriptableObject.CreateInstance<AudioBank>();
            audioFile.ExternalReference = reference;
            audioEvent.Nodes.Add(audioFile);
            bank.AudioEvents.Add(audioEvent);

            IDisposable registration = null;
            long originalPerLeaseBudget = AudioManager.BankClipLeaseMaxDecodedBytes;
            long originalAggregateBudget = AudioManager.ActiveBankClipLeaseMemoryBudgetBytes;
            long decodedBytes = (long)clip.samples * clip.channels * 4L;
            int releaseCount = 0;

            try
            {
                Assert.AreEqual(0L, AudioManager.ActiveBankClipLeaseMemoryBytes);
                AudioManager.BankClipLeaseMaxDecodedBytes = decodedBytes;
                AudioManager.ActiveBankClipLeaseMemoryBudgetBytes = decodedBytes;
                registration = AudioClipResolver.RegisterReferenceLoaderScoped(
                    reference,
                    (clipReference, cancellationToken) => UniTask.FromResult(
                        AudioClipResolver.CreateManaged(clip, () => releaseCount++)));

                int firstLoadedCount = AudioManager
                    .PreloadBankClipsAsync(bank)
                    .GetAwaiter()
                    .GetResult();
                int replacementLoadedCount = AudioManager
                    .PreloadBankClipsAsync(bank)
                    .GetAwaiter()
                    .GetResult();

                Assert.AreEqual(1, firstLoadedCount);
                Assert.AreEqual(1, replacementLoadedCount);
                Assert.AreEqual(1, releaseCount);
                Assert.AreEqual(decodedBytes, AudioManager.ActiveBankClipLeaseMemoryBytes);

                Assert.IsTrue(AudioManager.ReleasePreloadedBankClips(bank));
                Assert.AreEqual(2, releaseCount);
                Assert.AreEqual(0L, AudioManager.ActiveBankClipLeaseMemoryBytes);
            }
            finally
            {
                AudioManager.ReleasePreloadedBankClips(bank);
                registration?.Dispose();
                AudioManager.BankClipLeaseMaxDecodedBytes = originalPerLeaseBudget;
                AudioManager.ActiveBankClipLeaseMemoryBudgetBytes = originalAggregateBudget;
                UnityEngine.Object.DestroyImmediate(bank);
                UnityEngine.Object.DestroyImmediate(audioEvent);
                UnityEngine.Object.DestroyImmediate(audioFile);
                UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(reference);
            }
        }

        [TestCase(
            "https://user:password@cdn.example.com/private/audio/clip.ogg?token=signed-secret#fragment",
            "https://cdn.example.com/<redacted>")]
        [TestCase("C:\\Users\\PrivateUser\\Audio\\clip.wav", "<redacted>/clip.wav")]
        [TestCase("private/address/clip.wav", "<redacted>/clip.wav")]
        public void GetDiagnosticLocation_RedactsCredentialsTokensAndParentPath(
            string location,
            string expected)
        {
            string diagnosticLocation = AudioClipResolver.GetDiagnosticLocation(location);

            Assert.AreEqual(expected, diagnosticLocation);
            StringAssert.DoesNotContain("password", diagnosticLocation);
            StringAssert.DoesNotContain("signed-secret", diagnosticLocation);
            StringAssert.DoesNotContain("PrivateUser", diagnosticLocation);
        }

        private sealed class TestProvider : IAudioClipProvider
        {
            public string Name => "Resolver Ownership Test Provider";
            public int Priority => 500;
            public bool CanLoad(AudioClipReference reference) => false;

            public UniTask<IAudioClipHandle> LoadAsync(
                AudioClipReference reference,
                CancellationToken cancellationToken)
            {
                return UniTask.FromResult<IAudioClipHandle>(null);
            }
        }
    }
}
