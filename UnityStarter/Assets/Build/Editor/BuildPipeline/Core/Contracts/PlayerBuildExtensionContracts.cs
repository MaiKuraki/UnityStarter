using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public abstract class PlayerBuildExtensionConfiguration : ScriptableObject
    {
        public abstract string ProviderId { get; }
    }

    public sealed class PlayerBuildExtensionRequest
    {
        public PlayerBuildExtensionRequest(
            BuildRequest buildRequest,
            BuildStepInvocation playerInvocation,
            PlayerBuildExtensionConfiguration configuration)
        {
            BuildRequest = buildRequest ?? throw new ArgumentNullException(nameof(buildRequest));
            PlayerInvocation = playerInvocation ?? throw new ArgumentNullException(nameof(playerInvocation));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            string providerId = configuration.ProviderId;
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new InvalidOperationException(
                    $"Player extension configuration '{configuration.GetType().FullName}' returned an empty provider id.");
            }

            ProviderId = providerId.Trim();
            BuildIdentityPolicy.ValidateBuildIdentifier(
                ProviderId,
                "Player extension provider id");
        }

        public BuildRequest BuildRequest { get; }
        public BuildStepInvocation PlayerInvocation { get; }
        public PlayerBuildExtensionConfiguration Configuration { get; }
        public string ProviderId { get; }
    }

    public sealed class PlayerBuildEnvironmentRequest
    {
        public PlayerBuildEnvironmentRequest(
            BuildRequest buildRequest,
            BuildStepInvocation playerInvocation,
            IReadOnlyList<AssetContentBuildRequest> assetContentRequests,
            IReadOnlyList<PlayerBuildExtensionRequest> extensionRequests)
        {
            BuildRequest = buildRequest ?? throw new ArgumentNullException(nameof(buildRequest));
            PlayerInvocation = playerInvocation ?? throw new ArgumentNullException(nameof(playerInvocation));
            AssetContentRequests = Snapshot(
                assetContentRequests,
                nameof(assetContentRequests));
            ExtensionRequests = Snapshot(
                extensionRequests,
                nameof(extensionRequests));
        }

        public BuildRequest BuildRequest { get; }
        public BuildStepInvocation PlayerInvocation { get; }
        public IReadOnlyList<AssetContentBuildRequest> AssetContentRequests { get; }
        public IReadOnlyList<PlayerBuildExtensionRequest> ExtensionRequests { get; }

        public bool HasAssetContentProvider(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return false;
            }

            for (int index = 0; index < AssetContentRequests.Count; index++)
            {
                if (string.Equals(
                        AssetContentRequests[index].ProviderId,
                        providerId.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasPlayerExtension(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return false;
            }

            for (int index = 0; index < ExtensionRequests.Count; index++)
            {
                if (string.Equals(
                        ExtensionRequests[index].ProviderId,
                        providerId.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<T> Snapshot<T>(
            IReadOnlyList<T> values,
            string parameterName) where T : class
        {
            if (values == null || values.Count == 0)
            {
                return Array.Empty<T>();
            }

            var snapshot = new T[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                snapshot[index] = values[index]
                    ?? throw new ArgumentException(
                        $"{parameterName} contains a null entry at index {index}.",
                        parameterName);
            }

            return new ReadOnlyCollection<T>(snapshot);
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PlayerBuildExtensionAuthoringAttribute : Attribute
    {
        public PlayerBuildExtensionAuthoringAttribute(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException(
                    "Player extension provider id is required.",
                    nameof(providerId));
            }

            ProviderId = providerId.Trim();
            BuildIdentityPolicy.ValidateBuildIdentifier(
                ProviderId,
                "Player extension provider id");
        }

        public string ProviderId { get; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string RequiredEditorTypeName { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PlayerBuildExtensionAdapterRegistrationAttribute : Attribute
    {
        public PlayerBuildExtensionAdapterRegistrationAttribute(
            string providerId,
            string compatibilityId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException(
                    "Player extension provider id is required.",
                    nameof(providerId));
            }

            ProviderId = providerId.Trim();
            BuildIdentityPolicy.ValidateBuildIdentifier(
                ProviderId,
                "Player extension provider id");

            if (string.IsNullOrWhiteSpace(compatibilityId))
            {
                throw new ArgumentException(
                    "Player extension adapter compatibility id is required.",
                    nameof(compatibilityId));
            }

            CompatibilityId = compatibilityId;
            BuildIdentityPolicy.ValidateBuildIdentifier(
                CompatibilityId,
                "Player extension adapter compatibility id");
        }

        public string ProviderId { get; }
        public string CompatibilityId { get; }
        public Type ConfigurationType { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PlayerBuildEnvironmentGuardRegistrationAttribute : Attribute
    {
        public PlayerBuildEnvironmentGuardRegistrationAttribute(string guardId)
        {
            if (string.IsNullOrWhiteSpace(guardId))
            {
                throw new ArgumentException(
                    "Player environment guard id is required.",
                    nameof(guardId));
            }

            GuardId = guardId.Trim();
            BuildIdentityPolicy.ValidateBuildIdentifier(
                GuardId,
                "Player environment guard id");
        }

        public string GuardId { get; }
    }

    public sealed class PlayerBuildExtensionDescriptor
    {
        internal PlayerBuildExtensionDescriptor(
            string providerId,
            string displayName,
            string description,
            Type configurationType,
            Type adapterType,
            string adapterCompatibilityId,
            bool dependencyAvailable)
        {
            ProviderId = providerId;
            DisplayName = displayName;
            Description = description;
            ConfigurationType = configurationType;
            AdapterType = adapterType;
            AdapterCompatibilityId = adapterCompatibilityId;
            DependencyAvailable = dependencyAvailable;
        }

        public string ProviderId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public Type ConfigurationType { get; }
        public Type AdapterType { get; }
        public string AdapterCompatibilityId { get; }
        public bool DependencyAvailable { get; }
        public bool IsAvailable => AdapterType != null && DependencyAvailable;
    }

    public interface IPlayerBuildExtensionAdapter
    {
        string ProviderId { get; }
        string CompatibilityId { get; }
        IReadOnlyList<string> Validate(PlayerBuildExtensionRequest request);
        IDisposable BeginPlayerBuild(PlayerBuildExtensionRequest request);
    }

    public interface IPlayerBuildEnvironmentGuard
    {
        string GuardId { get; }
        IReadOnlyList<string> ValidateEnvironment(PlayerBuildEnvironmentRequest request);
        IDisposable BeginEnvironment(PlayerBuildEnvironmentRequest request);
    }
}
