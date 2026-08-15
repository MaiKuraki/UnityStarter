using System;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class YooAssetCryptographyAuthoringTests
    {
        [Test]
        public void Inspect_NullConfiguration_ReportsExplicitUnencryptedMode()
        {
            YooAssetCryptographyAvailability availability =
                YooAssetCryptographyAuthoringCatalog.Inspect(null);

            Assert.That(
                availability.Status,
                Is.EqualTo(YooAssetCryptographyAvailabilityStatus.None));
            Assert.That(
                availability.AdapterId,
                Is.EqualTo(YooAssetCryptographyIdentity.NoneAdapterId));
            Assert.That(
                availability.RuntimeDecryptContractId,
                Is.EqualTo(YooAssetCryptographyIdentity.NoneRuntimeDecryptContractId));
        }

        [Test]
        public void Inspect_UniqueMatchingRegistration_ReportsAvailable()
        {
            AuthoringValidConfiguration configuration =
                ScriptableObject.CreateInstance<AuthoringValidConfiguration>();
            try
            {
                YooAssetCryptographyAvailability availability =
                    YooAssetCryptographyAuthoringCatalog.Inspect(configuration);

                Assert.That(
                    availability.Status,
                    Is.EqualTo(YooAssetCryptographyAvailabilityStatus.Available));
                Assert.That(
                    availability.RuntimeDecryptContractId,
                    Is.EqualTo("authoring-valid-runtime"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void Inspect_MissingRegistration_ReportsUnavailable()
        {
            AuthoringMissingConfiguration configuration =
                ScriptableObject.CreateInstance<AuthoringMissingConfiguration>();
            try
            {
                YooAssetCryptographyAvailability availability =
                    YooAssetCryptographyAuthoringCatalog.Inspect(configuration);

                Assert.That(
                    availability.Status,
                    Is.EqualTo(YooAssetCryptographyAvailabilityStatus.MissingAdapter));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void Inspect_DuplicateRegistration_ReportsUnavailable()
        {
            AuthoringDuplicateConfiguration configuration =
                ScriptableObject.CreateInstance<AuthoringDuplicateConfiguration>();
            try
            {
                YooAssetCryptographyAvailability availability =
                    YooAssetCryptographyAuthoringCatalog.Inspect(configuration);

                Assert.That(
                    availability.Status,
                    Is.EqualTo(YooAssetCryptographyAvailabilityStatus.DuplicateAdapter));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void Inspect_RegistrationTypeMismatch_ReportsUnavailable()
        {
            AuthoringMismatchedConfiguration configuration =
                ScriptableObject.CreateInstance<AuthoringMismatchedConfiguration>();
            try
            {
                YooAssetCryptographyAvailability availability =
                    YooAssetCryptographyAuthoringCatalog.Inspect(configuration);

                Assert.That(
                    availability.Status,
                    Is.EqualTo(YooAssetCryptographyAvailabilityStatus.TypeMismatch));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void Registration_ReservedNoneIdentity_IsRejected()
        {
            Assert.Throws<ArgumentException>(() =>
                new YooAssetCryptographyAdapterRegistrationAttribute(
                    YooAssetCryptographyIdentity.NoneAdapterId,
                    typeof(AuthoringValidConfiguration),
                    "authoring-runtime"));
        }
    }

    public sealed class AuthoringValidConfiguration : YooAssetCryptographyConfiguration
    {
        public override string AdapterId => "authoring-valid-crypto";
    }

    [YooAssetCryptographyAdapterRegistration(
        "authoring-valid-crypto",
        typeof(AuthoringValidConfiguration),
        "authoring-valid-runtime")]
    public sealed class AuthoringValidAdapterRegistration { }

    public sealed class AuthoringMissingConfiguration : YooAssetCryptographyConfiguration
    {
        public override string AdapterId => "authoring-missing-crypto";
    }

    public sealed class AuthoringDuplicateConfiguration : YooAssetCryptographyConfiguration
    {
        public override string AdapterId => "authoring-duplicate-crypto";
    }

    [YooAssetCryptographyAdapterRegistration(
        "authoring-duplicate-crypto",
        typeof(AuthoringDuplicateConfiguration),
        "authoring-duplicate-runtime")]
    public sealed class AuthoringDuplicateAdapterRegistrationA { }

    [YooAssetCryptographyAdapterRegistration(
        "authoring-duplicate-crypto",
        typeof(AuthoringDuplicateConfiguration),
        "authoring-duplicate-runtime")]
    public sealed class AuthoringDuplicateAdapterRegistrationB { }

    public sealed class AuthoringMismatchedConfiguration : YooAssetCryptographyConfiguration
    {
        public override string AdapterId => "authoring-mismatched-crypto";
    }

    public sealed class AuthoringRegisteredConfiguration : YooAssetCryptographyConfiguration
    {
        public override string AdapterId => "authoring-mismatched-crypto";
    }

    [YooAssetCryptographyAdapterRegistration(
        "authoring-mismatched-crypto",
        typeof(AuthoringRegisteredConfiguration),
        "authoring-mismatched-runtime")]
    public sealed class AuthoringMismatchedAdapterRegistration { }
}
