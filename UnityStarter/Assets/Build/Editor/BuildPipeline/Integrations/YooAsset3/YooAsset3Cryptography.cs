using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using YooAsset;

namespace Build.Pipeline.Editor.Integrations.YooAsset3
{
    /// <summary>
    /// Immutable request passed only to the selected cryptography adapter.
    /// Implementations must not include secrets in exceptions, warnings, or identities.
    /// </summary>
    public sealed class YooAsset3CryptographyRequest
    {
        public YooAsset3CryptographyRequest(
            AssetContentBuildRequest buildRequest,
            YooAssetPackageProfile packageProfile,
            YooAssetCryptographyConfiguration configuration)
        {
            BuildRequest = buildRequest
                ?? throw new ArgumentNullException(nameof(buildRequest));
            PackageProfile = packageProfile
                ?? throw new ArgumentNullException(nameof(packageProfile));
            Configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
        }

        public AssetContentBuildRequest BuildRequest { get; }
        public YooAssetPackageProfile PackageProfile { get; }
        public YooAssetCryptographyConfiguration Configuration { get; }
    }

    /// <summary>
    /// Strong YooAsset 3 boundary implemented by project or package integrations.
    /// The registration attribute is the discoverable identity; this interface
    /// creates the official services without serialized implementation type names.
    /// </summary>
    public interface IYooAsset3CryptographyAdapter
    {
        string AdapterId { get; }
        string RuntimeDecryptContractId { get; }
        void Validate(YooAsset3CryptographyRequest request);
        IBundleEncryptor CreateBundleEncryptor(YooAsset3CryptographyRequest request);
        IManifestEncryptor CreateManifestEncryptor(YooAsset3CryptographyRequest request);
        IManifestDecryptor CreateManifestDecryptor(YooAsset3CryptographyRequest request);
    }

    internal sealed class YooAsset3CryptographyBinding
    {
        private YooAsset3CryptographyBinding(
            string adapterId,
            string runtimeDecryptContractId,
            IBundleEncryptor bundleEncryptor,
            IManifestEncryptor manifestEncryptor,
            IManifestDecryptor manifestDecryptor)
        {
            AdapterId = adapterId;
            RuntimeDecryptContractId = runtimeDecryptContractId;
            BundleEncryptor = bundleEncryptor;
            ManifestEncryptor = manifestEncryptor;
            ManifestDecryptor = manifestDecryptor;
        }

        public string AdapterId { get; }
        public string RuntimeDecryptContractId { get; }
        public IBundleEncryptor BundleEncryptor { get; }
        public IManifestEncryptor ManifestEncryptor { get; }
        public IManifestDecryptor ManifestDecryptor { get; }

        public static YooAsset3CryptographyBinding None()
        {
            return new YooAsset3CryptographyBinding(
                YooAssetCryptographyIdentity.NoneAdapterId,
                YooAssetCryptographyIdentity.NoneRuntimeDecryptContractId,
                null,
                null,
                null);
        }

        public static YooAsset3CryptographyBinding Create(
            string adapterId,
            string runtimeDecryptContractId,
            IBundleEncryptor bundleEncryptor,
            IManifestEncryptor manifestEncryptor,
            IManifestDecryptor manifestDecryptor)
        {
            if (bundleEncryptor == null)
            {
                throw new InvalidOperationException(
                    $"YooAsset cryptography adapter '{adapterId}' returned no {nameof(IBundleEncryptor)}.");
            }

            if (manifestEncryptor == null)
            {
                throw new InvalidOperationException(
                    $"YooAsset cryptography adapter '{adapterId}' returned no {nameof(IManifestEncryptor)}.");
            }

            if (manifestDecryptor == null)
            {
                throw new InvalidOperationException(
                    $"YooAsset cryptography adapter '{adapterId}' returned no {nameof(IManifestDecryptor)}.");
            }

            return new YooAsset3CryptographyBinding(
                adapterId,
                runtimeDecryptContractId,
                bundleEncryptor,
                manifestEncryptor,
                manifestDecryptor);
        }
    }

    internal static class YooAsset3CryptographyRegistry
    {
        public static YooAsset3CryptographyBinding Resolve(
            AssetContentBuildRequest buildRequest,
            YooAssetPackageProfile packageProfile)
        {
            if (buildRequest == null)
            {
                throw new ArgumentNullException(nameof(buildRequest));
            }

            if (packageProfile == null)
            {
                throw new ArgumentNullException(nameof(packageProfile));
            }

            YooAssetCryptographyConfiguration configuration =
                packageProfile.cryptography;
            if (configuration == null)
            {
                return YooAsset3CryptographyBinding.None();
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
                    throw new InvalidOperationException();
                }
            }
            catch (Exception)
            {
                throw new InvalidOperationException(
                    "The selected YooAsset cryptography configuration did not provide a valid adapter identity. Details are intentionally suppressed to prevent secret disclosure.");
            }

            Candidate[] matches = DiscoverCandidates()
                .Where(candidate => string.Equals(
                    candidate.Registration.AdapterId,
                    requested,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => candidate.Type.FullName, StringComparer.Ordinal)
                .ToArray();
            if (matches.Length == 0)
            {
                throw new InvalidOperationException(
                    $"No available YooAsset 3 cryptography adapter provides id '{requested}'.");
            }

            if (matches.Length != 1)
            {
                string types = string.Join(
                    ", ",
                    matches.Select(candidate => candidate.Type.FullName));
                throw new InvalidOperationException(
                    $"Multiple YooAsset 3 cryptography adapters provide id '{requested}': {types}. Adapter ids must be globally unique.");
            }

            Candidate match = matches[0];
            ValidateRegistration(match);
            if (configuration.GetType() != match.Registration.ConfigurationType)
            {
                throw new InvalidOperationException(
                    $"YooAsset cryptography adapter '{requested}' requires configuration type " +
                    $"'{match.Registration.ConfigurationType.FullName}', but received " +
                    $"'{configuration.GetType().FullName}'.");
            }

            var request = new YooAsset3CryptographyRequest(
                buildRequest,
                packageProfile,
                configuration);

            try
            {
                IYooAsset3CryptographyAdapter adapter = CreateAdapter(match.Type);
                ValidateRuntimeIdentity(adapter, match);
                adapter.Validate(request);
                return YooAsset3CryptographyBinding.Create(
                    match.Registration.AdapterId,
                    match.Registration.RuntimeDecryptContractId,
                    adapter.CreateBundleEncryptor(request),
                    adapter.CreateManifestEncryptor(request),
                    adapter.CreateManifestDecryptor(request));
            }
            catch (Exception)
            {
                throw new InvalidOperationException(
                    $"YooAsset cryptography adapter '{requested}' failed preflight. " +
                    "Review its configuration and secret source. Adapter exception details are intentionally suppressed to prevent secret disclosure.");
            }
        }

        private static IReadOnlyList<Candidate> DiscoverCandidates()
        {
            var result = new List<Candidate>();
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
                    result.Add(new Candidate(type, registration));
                }
            }

            return result;
        }

        private static void ValidateRegistration(Candidate candidate)
        {
            Type adapterType = candidate.Type;
            Type configurationType = candidate.Registration.ConfigurationType;
            if (!typeof(IYooAsset3CryptographyAdapter).IsAssignableFrom(adapterType)
                || adapterType.IsAbstract
                || adapterType.ContainsGenericParameters
                || adapterType.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new InvalidOperationException(
                    $"Registered YooAsset cryptography adapter '{adapterType.FullName}' must be a concrete " +
                    $"{nameof(IYooAsset3CryptographyAdapter)} with a public parameterless constructor.");
            }

            if (configurationType == null
                || !typeof(YooAssetCryptographyConfiguration).IsAssignableFrom(configurationType)
                || configurationType.IsAbstract
                || configurationType.ContainsGenericParameters)
            {
                throw new InvalidOperationException(
                    $"Registered YooAsset cryptography adapter '{adapterType.FullName}' must bind one concrete " +
                    $"{nameof(YooAssetCryptographyConfiguration)} type.");
            }
        }

        private static IYooAsset3CryptographyAdapter CreateAdapter(Type type)
        {
            try
            {
                return (IYooAsset3CryptographyAdapter)Activator.CreateInstance(type);
            }
            catch (Exception)
            {
                throw new InvalidOperationException(
                    $"Failed to create YooAsset cryptography adapter '{type.FullName}'. Constructor details are intentionally suppressed.");
            }
        }

        private static void ValidateRuntimeIdentity(
            IYooAsset3CryptographyAdapter adapter,
            Candidate candidate)
        {
            string adapterId = adapter.AdapterId?.Trim();
            string runtimeContractId = adapter.RuntimeDecryptContractId?.Trim();
            BuildIdentityPolicy.ValidateBuildIdentifier(
                adapterId,
                "YooAsset cryptography adapter id");
            BuildIdentityPolicy.ValidateBuildIdentifier(
                runtimeContractId,
                "YooAsset runtime decrypt contract id");
            if (!string.Equals(
                    adapterId,
                    candidate.Registration.AdapterId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    runtimeContractId,
                    candidate.Registration.RuntimeDecryptContractId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"YooAsset cryptography adapter '{candidate.Type.FullName}' runtime identities do not match its registration.");
            }
        }

        private sealed class Candidate
        {
            public Candidate(
                Type type,
                YooAssetCryptographyAdapterRegistrationAttribute registration)
            {
                Type = type;
                Registration = registration;
            }

            public Type Type { get; }
            public YooAssetCryptographyAdapterRegistrationAttribute Registration { get; }
        }
    }
}
