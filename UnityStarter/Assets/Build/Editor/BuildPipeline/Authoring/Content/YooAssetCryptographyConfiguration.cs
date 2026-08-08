using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Dependency-free authoring boundary for one YooAsset package cryptography policy.
    /// Concrete configurations live beside their version-gated adapters and expose no
    /// implementation type names through serialized fields.
    /// </summary>
    public abstract class YooAssetCryptographyConfiguration : ScriptableObject
    {
        public abstract string AdapterId { get; }
    }

    public static class YooAssetCryptographyIdentity
    {
        public const string NoneAdapterId = "none";
        public const string NoneRuntimeDecryptContractId = "none";
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class YooAssetCryptographyAdapterRegistrationAttribute : Attribute
    {
        public YooAssetCryptographyAdapterRegistrationAttribute(
            string adapterId,
            Type configurationType,
            string runtimeDecryptContractId)
        {
            AdapterId = ValidateIdentifier(adapterId, "YooAsset cryptography adapter id");
            RuntimeDecryptContractId = ValidateIdentifier(
                runtimeDecryptContractId,
                "YooAsset runtime decrypt contract id");
            ConfigurationType = configurationType
                ?? throw new ArgumentNullException(nameof(configurationType));
        }

        public string AdapterId { get; }
        public Type ConfigurationType { get; }
        public string RuntimeDecryptContractId { get; }

        private static string ValidateIdentifier(string value, string displayName)
        {
            string normalized = value?.Trim();
            BuildIdentityPolicy.ValidateBuildIdentifier(normalized, displayName);
            if (string.Equals(
                    normalized,
                    YooAssetCryptographyIdentity.NoneAdapterId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"{displayName} '{YooAssetCryptographyIdentity.NoneAdapterId}' is reserved for packages without cryptography.",
                    nameof(value));
            }

            return normalized;
        }
    }

    internal enum YooAssetCryptographyAvailabilityStatus
    {
        None,
        Available,
        MissingAdapter,
        DuplicateAdapter,
        InvalidConfiguration,
        TypeMismatch,
        InvalidRegistration
    }

    internal readonly struct YooAssetCryptographyAvailability
    {
        public YooAssetCryptographyAvailability(
            YooAssetCryptographyAvailabilityStatus status,
            string diagnostic,
            string adapterId = null,
            string runtimeDecryptContractId = null)
        {
            Status = status;
            Diagnostic = diagnostic ?? string.Empty;
            AdapterId = adapterId ?? string.Empty;
            RuntimeDecryptContractId = runtimeDecryptContractId ?? string.Empty;
        }

        public YooAssetCryptographyAvailabilityStatus Status { get; }
        public string Diagnostic { get; }
        public string AdapterId { get; }
        public string RuntimeDecryptContractId { get; }
        public bool IsAvailable => Status == YooAssetCryptographyAvailabilityStatus.Available;
    }

    internal static class YooAssetCryptographyAuthoringCatalog
    {
        public static YooAssetCryptographyAvailability Inspect(
            YooAssetCryptographyConfiguration configuration)
        {
            if (configuration == null)
            {
                return new YooAssetCryptographyAvailability(
                    YooAssetCryptographyAvailabilityStatus.None,
                    "No cryptography configuration is selected. Bundles and manifests remain unencrypted.",
                    YooAssetCryptographyIdentity.NoneAdapterId,
                    YooAssetCryptographyIdentity.NoneRuntimeDecryptContractId);
            }

            string requested;
            try
            {
                requested = configuration.AdapterId?.Trim();
                BuildIdentityPolicy.ValidateBuildIdentifier(
                    requested,
                    "YooAsset cryptography adapter id");
                if (string.Equals(
                        requested,
                        YooAssetCryptographyIdentity.NoneAdapterId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Adapter id '{YooAssetCryptographyIdentity.NoneAdapterId}' is reserved for packages without cryptography.");
                }
            }
            catch (Exception)
            {
                return new YooAssetCryptographyAvailability(
                    YooAssetCryptographyAvailabilityStatus.InvalidConfiguration,
                    $"Cryptography configuration '{configuration.GetType().FullName}' did not provide a valid adapter identity. Details are suppressed to prevent secret disclosure.");
            }

            RegistrationCandidate[] matches;
            try
            {
                matches = DiscoverCandidates()
                    .Where(candidate => string.Equals(
                        candidate.Registration.AdapterId,
                        requested,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderBy(candidate => candidate.AdapterType.FullName, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception exception)
            {
                return new YooAssetCryptographyAvailability(
                    YooAssetCryptographyAvailabilityStatus.InvalidRegistration,
                    "Cryptography adapter registration discovery failed: " + exception.Message,
                    requested);
            }

            if (matches.Length == 0)
            {
                return new YooAssetCryptographyAvailability(
                    YooAssetCryptographyAvailabilityStatus.MissingAdapter,
                    $"No available YooAsset 3 cryptography adapter provides id '{requested}'. Install its dependencies or clear the configuration reference.",
                    requested);
            }

            if (matches.Length != 1)
            {
                string types = string.Join(
                    ", ",
                    matches.Select(candidate => candidate.AdapterType.FullName));
                return new YooAssetCryptographyAvailability(
                    YooAssetCryptographyAvailabilityStatus.DuplicateAdapter,
                    $"Multiple YooAsset 3 cryptography adapters provide id '{requested}': {types}. Adapter ids must be globally unique.",
                    requested);
            }

            RegistrationCandidate match = matches[0];
            YooAssetCryptographyAdapterRegistrationAttribute registration =
                match.Registration;
            if (!IsConcreteConfigurationType(registration.ConfigurationType))
            {
                return new YooAssetCryptographyAvailability(
                    YooAssetCryptographyAvailabilityStatus.InvalidRegistration,
                    $"Cryptography adapter '{match.AdapterType.FullName}' registered an invalid configuration type.",
                    requested);
            }

            if (configuration.GetType() != registration.ConfigurationType)
            {
                return new YooAssetCryptographyAvailability(
                    YooAssetCryptographyAvailabilityStatus.TypeMismatch,
                    $"Cryptography adapter '{requested}' requires configuration type '{registration.ConfigurationType.FullName}', but received '{configuration.GetType().FullName}'.",
                    requested,
                    registration.RuntimeDecryptContractId);
            }

            if (match.AdapterType.IsAbstract || match.AdapterType.ContainsGenericParameters)
            {
                return new YooAssetCryptographyAvailability(
                    YooAssetCryptographyAvailabilityStatus.InvalidRegistration,
                    $"Cryptography adapter '{match.AdapterType.FullName}' must be a concrete non-generic type.",
                    requested,
                    registration.RuntimeDecryptContractId);
            }

            return new YooAssetCryptographyAvailability(
                YooAssetCryptographyAvailabilityStatus.Available,
                $"Adapter '{requested}' is available. Runtime decrypt contract: '{registration.RuntimeDecryptContractId}'.",
                requested,
                registration.RuntimeDecryptContractId);
        }

        private static IReadOnlyList<RegistrationCandidate> DiscoverCandidates()
        {
            var result = new List<RegistrationCandidate>();
            foreach (Type type in TypeCache.GetTypesWithAttribute<
                         YooAssetCryptographyAdapterRegistrationAttribute>())
            {
                var registration =
                    (YooAssetCryptographyAdapterRegistrationAttribute)
                    Attribute.GetCustomAttribute(
                        type,
                        typeof(YooAssetCryptographyAdapterRegistrationAttribute),
                        inherit: false);
                if (registration != null)
                {
                    result.Add(new RegistrationCandidate(type, registration));
                }
            }

            return result;
        }

        private static bool IsConcreteConfigurationType(Type type)
        {
            return type != null
                && typeof(YooAssetCryptographyConfiguration).IsAssignableFrom(type)
                && !type.IsAbstract
                && !type.ContainsGenericParameters;
        }

        private sealed class RegistrationCandidate
        {
            public RegistrationCandidate(
                Type adapterType,
                YooAssetCryptographyAdapterRegistrationAttribute registration)
            {
                AdapterType = adapterType;
                Registration = registration;
            }

            public Type AdapterType { get; }
            public YooAssetCryptographyAdapterRegistrationAttribute Registration { get; }
        }
    }
}
