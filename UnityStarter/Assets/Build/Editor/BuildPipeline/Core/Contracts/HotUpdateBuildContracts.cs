using System;
using System.Collections.Generic;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Dependency-free authoring contract for one hot-update provider.
    /// The provider identity comes from the concrete asset, so recipes never
    /// maintain a second handwritten provider field.
    /// </summary>
    public abstract class HotUpdateBuildConfiguration : ScriptableObject
    {
        public abstract string ProviderId { get; }
    }

    /// <summary>
    /// Provider-neutral input for a single hot-update invocation.
    /// </summary>
    public sealed class HotUpdateBuildRequest
    {
        public HotUpdateBuildRequest(
            BuildExecutionContext context,
            BuildStepInvocation invocation,
            HotUpdateBuildConfiguration configuration)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Invocation = invocation ?? throw new ArgumentNullException(nameof(invocation));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public BuildExecutionContext Context { get; }
        public BuildStepInvocation Invocation { get; }
        public HotUpdateBuildConfiguration Configuration { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class HotUpdateAdapterRegistrationAttribute : Attribute
    {
        public HotUpdateAdapterRegistrationAttribute(
            string providerId,
            Type configurationType)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException(
                    "Hot-update adapter provider id is required.",
                    nameof(providerId));
            }

            BuildIdentityPolicy.ValidateBuildIdentifier(
                providerId,
                "Hot-update adapter provider id");

            if (configurationType == null)
            {
                throw new ArgumentNullException(nameof(configurationType));
            }

            if (!typeof(HotUpdateBuildConfiguration).IsAssignableFrom(configurationType)
                || configurationType.IsAbstract
                || configurationType.ContainsGenericParameters)
            {
                throw new ArgumentException(
                    "Hot-update adapter configuration type must be a concrete HotUpdateBuildConfiguration.",
                    nameof(configurationType));
            }

            ProviderId = providerId.Trim();
            ConfigurationType = configurationType;
        }

        public string ProviderId { get; }
        public Type ConfigurationType { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class HotUpdateProviderAuthoringAttribute : Attribute
    {
        public HotUpdateProviderAuthoringAttribute(string providerId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException(
                    "Hot-update provider authoring id is required.",
                    nameof(providerId));
            }

            BuildIdentityPolicy.ValidateBuildIdentifier(
                providerId,
                "Hot-update provider authoring id");

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException(
                    "Hot-update provider display name is required.",
                    nameof(displayName));
            }

            ProviderId = providerId.Trim();
            DisplayName = displayName.Trim();
        }

        public string ProviderId { get; }
        public string DisplayName { get; }
        public string Description { get; set; }
        public string[] RequiredEditorTypeNames { get; set; } = Array.Empty<string>();
        public int Order { get; set; }
    }

    public sealed class HotUpdateProviderDescriptor
    {
        internal HotUpdateProviderDescriptor(
            string providerId,
            string displayName,
            string description,
            int order,
            Type configurationType,
            Type adapterType,
            bool dependencyAvailable)
        {
            ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Description = description ?? string.Empty;
            Order = order;
            ConfigurationType = configurationType
                ?? throw new ArgumentNullException(nameof(configurationType));
            AdapterType = adapterType;
            DependencyAvailable = dependencyAvailable;
        }

        public string ProviderId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public int Order { get; }
        public Type ConfigurationType { get; }
        public Type AdapterType { get; }
        public bool AdapterAvailable => AdapterType != null;
        public bool DependencyAvailable { get; }
        public bool IsAvailable => AdapterAvailable && DependencyAvailable;
    }

    /// <summary>
    /// Provider-owned execution strategy for the generic hot-update step.
    /// Implementations may retain invocation-local state; one instance is
    /// created and cached for each invocation and build run.
    /// </summary>
    public interface IHotUpdateBuildAdapter
    {
        string ProviderId { get; }
        Type ConfigurationType { get; }
        BuildStepRequirements GetRequirements(HotUpdateBuildRequest request);
        IReadOnlyList<string> Validate(HotUpdateBuildRequest request);
        void Execute(HotUpdateBuildRequest request);
    }

    /// <summary>
    /// Optional provider-owned compatibility check performed by a dependent
    /// Player invocation. This keeps provider-specific compilation rules out
    /// of the generic Player step.
    /// </summary>
    public interface IHotUpdatePlayerBuildValidator
    {
        IReadOnlyList<string> ValidatePlayerBuild(HotUpdateBuildRequest request);
    }
}
