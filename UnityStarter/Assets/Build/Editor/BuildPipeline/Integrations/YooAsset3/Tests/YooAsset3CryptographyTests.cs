using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace Build.Pipeline.Editor.Integrations.YooAsset3.Tests
{
    public sealed class YooAsset3CryptographyTests
    {
        [Test]
        public void Resolve_NoConfiguration_UsesExplicitUnencryptedIdentity()
        {
            YooAssetPackageProfile profile = CreateProfile(null);

            YooAsset3CryptographyBinding binding =
                YooAsset3CryptographyRegistry.Resolve(CreateRequest(), profile);

            Assert.That(binding.AdapterId, Is.EqualTo(YooAssetCryptographyIdentity.NoneAdapterId));
            Assert.That(
                binding.RuntimeDecryptContractId,
                Is.EqualTo(YooAssetCryptographyIdentity.NoneRuntimeDecryptContractId));
            Assert.That(binding.BundleEncryptor, Is.Null);
            Assert.That(binding.ManifestEncryptor, Is.Null);
            Assert.That(binding.ManifestDecryptor, Is.Null);
        }

        [Test]
        public void Resolve_MatchingRegistration_CreatesAllOfficialServices()
        {
            ValidCryptographyConfiguration configuration =
                ScriptableObject.CreateInstance<ValidCryptographyConfiguration>();
            try
            {
                YooAsset3CryptographyBinding binding =
                    YooAsset3CryptographyRegistry.Resolve(
                        CreateRequest(),
                        CreateProfile(configuration));

                Assert.That(binding.AdapterId, Is.EqualTo(ValidCryptographyAdapter.Id));
                Assert.That(
                    binding.RuntimeDecryptContractId,
                    Is.EqualTo(ValidCryptographyAdapter.RuntimeContract));
                Assert.That(binding.BundleEncryptor, Is.TypeOf<TestBundleEncryptor>());
                Assert.That(binding.ManifestEncryptor, Is.TypeOf<TestManifestCryptography>());
                Assert.That(binding.ManifestDecryptor, Is.TypeOf<TestManifestCryptography>());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void Resolve_MissingRegistration_FailsClosed()
        {
            MissingCryptographyConfiguration configuration =
                ScriptableObject.CreateInstance<MissingCryptographyConfiguration>();
            try
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => YooAsset3CryptographyRegistry.Resolve(
                        CreateRequest(),
                        CreateProfile(configuration)));
                StringAssert.Contains("No available", exception.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void Resolve_DuplicateRegistration_FailsClosed()
        {
            DuplicateCryptographyConfiguration configuration =
                ScriptableObject.CreateInstance<DuplicateCryptographyConfiguration>();
            try
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => YooAsset3CryptographyRegistry.Resolve(
                        CreateRequest(),
                        CreateProfile(configuration)));
                StringAssert.Contains("Multiple", exception.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void Resolve_RegisteredConfigurationTypeMismatch_FailsClosed()
        {
            MismatchedCryptographyConfiguration configuration =
                ScriptableObject.CreateInstance<MismatchedCryptographyConfiguration>();
            try
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => YooAsset3CryptographyRegistry.Resolve(
                        CreateRequest(),
                        CreateProfile(configuration)));
                StringAssert.Contains("requires configuration type", exception.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void Resolve_EmptyRuntimeContract_FailsClosed()
        {
            EmptyRuntimeContractConfiguration configuration =
                ScriptableObject.CreateInstance<EmptyRuntimeContractConfiguration>();
            try
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => YooAsset3CryptographyRegistry.Resolve(
                        CreateRequest(),
                        CreateProfile(configuration)));
                StringAssert.Contains("intentionally suppressed", exception.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void ApplyCryptographyServices_AssignsAllServices_ToEveryPipelineKind()
        {
            var bundle = new TestBundleEncryptor();
            var manifest = new TestManifestCryptography();
            YooAsset3CryptographyBinding binding = YooAsset3CryptographyBinding.Create(
                "test-apply",
                "test-runtime-v1",
                bundle,
                manifest,
                manifest);
            BuildParameters[] parameters =
            {
                new ScriptableBuildParameters(),
                new RawFileBuildParameters(),
                new ArchiveFileBuildParameters()
            };

            foreach (BuildParameters value in parameters)
            {
                YooAsset3BuildParameterFactory.ApplyCryptographyServices(value, binding);
                Assert.That(value.BundleEncryptor, Is.SameAs(bundle));
                Assert.That(value.ManifestEncryptor, Is.SameAs(manifest));
                Assert.That(value.ManifestDecryptor, Is.SameAs(manifest));
            }
        }

        private static AssetContentBuildRequest CreateRequest()
        {
            return new AssetContentBuildRequest(
                "crypto-test",
                BuildTarget.StandaloneWindows64,
                "1.0.0",
                Environment.CurrentDirectory,
                null,
                BuildIncrementality.Clean,
                false);
        }

        private static YooAssetPackageProfile CreateProfile(
            YooAssetCryptographyConfiguration configuration)
        {
            return new YooAssetPackageProfile
            {
                packageName = "DefaultPackage",
                cryptography = configuration
            };
        }
    }

    public sealed class ValidCryptographyConfiguration : YooAssetCryptographyConfiguration
    {
        public override string AdapterId => ValidCryptographyAdapter.Id;
    }

    [YooAssetCryptographyAdapterRegistration(
        Id,
        typeof(ValidCryptographyConfiguration),
        RuntimeContract)]
    public sealed class ValidCryptographyAdapter : IYooAsset3CryptographyAdapter
    {
        public const string Id = "test-valid-crypto";
        public const string RuntimeContract = "test-valid-runtime-v1";

        public string AdapterId => Id;
        public string RuntimeDecryptContractId => RuntimeContract;
        public void Validate(YooAsset3CryptographyRequest request) { }
        public IBundleEncryptor CreateBundleEncryptor(YooAsset3CryptographyRequest request) => new TestBundleEncryptor();
        public IManifestEncryptor CreateManifestEncryptor(YooAsset3CryptographyRequest request) => new TestManifestCryptography();
        public IManifestDecryptor CreateManifestDecryptor(YooAsset3CryptographyRequest request) => new TestManifestCryptography();
    }

    public sealed class MissingCryptographyConfiguration : YooAssetCryptographyConfiguration
    {
        public override string AdapterId => "test-missing-crypto";
    }

    public sealed class DuplicateCryptographyConfiguration : YooAssetCryptographyConfiguration
    {
        public override string AdapterId => "test-duplicate-crypto";
    }

    [YooAssetCryptographyAdapterRegistration(
        "test-duplicate-crypto",
        typeof(DuplicateCryptographyConfiguration),
        "test-duplicate-runtime-v1")]
    public sealed class DuplicateCryptographyAdapterA : TestCryptographyAdapterBase { }

    [YooAssetCryptographyAdapterRegistration(
        "test-duplicate-crypto",
        typeof(DuplicateCryptographyConfiguration),
        "test-duplicate-runtime-v1")]
    public sealed class DuplicateCryptographyAdapterB : TestCryptographyAdapterBase { }

    public sealed class MismatchedCryptographyConfiguration : YooAssetCryptographyConfiguration
    {
        public override string AdapterId => "test-type-mismatch";
    }

    public sealed class RegisteredMismatchedCryptographyConfiguration : YooAssetCryptographyConfiguration
    {
        public override string AdapterId => "test-type-mismatch";
    }

    [YooAssetCryptographyAdapterRegistration(
        "test-type-mismatch",
        typeof(RegisteredMismatchedCryptographyConfiguration),
        "test-type-mismatch-runtime-v1")]
    public sealed class MismatchedCryptographyAdapter : TestCryptographyAdapterBase
    {
        public override string AdapterId => "test-type-mismatch";
        public override string RuntimeDecryptContractId => "test-type-mismatch-runtime-v1";
    }

    public sealed class EmptyRuntimeContractConfiguration : YooAssetCryptographyConfiguration
    {
        public override string AdapterId => "test-empty-runtime";
    }

    [YooAssetCryptographyAdapterRegistration(
        "test-empty-runtime",
        typeof(EmptyRuntimeContractConfiguration),
        "test-runtime-registration-v1")]
    public sealed class EmptyRuntimeContractAdapter : TestCryptographyAdapterBase
    {
        public override string AdapterId => "test-empty-runtime";
        public override string RuntimeDecryptContractId => string.Empty;
    }

    public abstract class TestCryptographyAdapterBase : IYooAsset3CryptographyAdapter
    {
        public virtual string AdapterId => "test-duplicate-crypto";
        public virtual string RuntimeDecryptContractId => "test-duplicate-runtime-v1";
        public void Validate(YooAsset3CryptographyRequest request) { }
        public IBundleEncryptor CreateBundleEncryptor(YooAsset3CryptographyRequest request) => new TestBundleEncryptor();
        public IManifestEncryptor CreateManifestEncryptor(YooAsset3CryptographyRequest request) => new TestManifestCryptography();
        public IManifestDecryptor CreateManifestDecryptor(YooAsset3CryptographyRequest request) => new TestManifestCryptography();
    }

    public sealed class TestBundleEncryptor : IBundleEncryptor
    {
        public BundleEncryptResult Encrypt(BundleEncryptArgs args)
        {
            return new BundleEncryptResult(false, null);
        }
    }

    public sealed class TestManifestCryptography : IManifestEncryptor, IManifestDecryptor
    {
        public byte[] Encrypt(byte[] fileData) => fileData;
        public byte[] Decrypt(byte[] fileData) => fileData;
    }
}
