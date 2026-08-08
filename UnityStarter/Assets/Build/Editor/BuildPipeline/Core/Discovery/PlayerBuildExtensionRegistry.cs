using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Build.Pipeline.Editor
{
    public static class PlayerBuildExtensionRegistry
    {
        public static IReadOnlyList<PlayerBuildExtensionDescriptor> GetDescriptors()
        {
            var authoringCandidates = new List<AuthoringCandidate>();
            foreach (Type type in TypeCache.GetTypesWithAttribute<PlayerBuildExtensionAuthoringAttribute>())
            {
                var registration = (PlayerBuildExtensionAuthoringAttribute)
                    Attribute.GetCustomAttribute(
                        type,
                        typeof(PlayerBuildExtensionAuthoringAttribute),
                        inherit: false);
                if (registration != null)
                {
                    authoringCandidates.Add(new AuthoringCandidate(type, registration));
                }
            }

            IReadOnlyList<AdapterCandidate> adapterCandidates =
                DiscoverAdapterCandidates();
            var descriptors = new List<PlayerBuildExtensionDescriptor>(
                authoringCandidates.Count);
            foreach (IGrouping<string, AuthoringCandidate> group in authoringCandidates
                         .GroupBy(candidate => candidate.Registration.ProviderId,
                             StringComparer.OrdinalIgnoreCase)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                AuthoringCandidate authoring = RequireUnique(
                    group.Key,
                    group.ToArray(),
                    "Player extension authoring configuration");
                ValidateAuthoringType(authoring.Type);
                AdapterCandidate[] matchingAdapters = adapterCandidates
                    .Where(candidate => string.Equals(
                        candidate.Registration.ProviderId,
                        group.Key,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                AdapterCandidate adapter = matchingAdapters.Length == 0
                    ? null
                    : RequireUnique(
                        group.Key,
                        matchingAdapters,
                        "Player extension adapter");
                if (adapter != null)
                {
                    ValidateAdapterType(adapter.Type, adapter.Registration);
                    if (adapter.Registration.ConfigurationType != authoring.Type)
                    {
                        throw new InvalidOperationException(
                            $"Player extension provider '{group.Key}' authoring type '{authoring.Type.FullName}' " +
                            $"does not match adapter configuration type '{adapter.Registration.ConfigurationType.FullName}'.");
                    }
                }

                descriptors.Add(new PlayerBuildExtensionDescriptor(
                    authoring.Registration.ProviderId,
                    string.IsNullOrWhiteSpace(authoring.Registration.DisplayName)
                        ? authoring.Registration.ProviderId
                        : authoring.Registration.DisplayName.Trim(),
                    authoring.Registration.Description?.Trim() ?? string.Empty,
                    authoring.Type,
                    adapter?.Type,
                    adapter?.Registration.CompatibilityId ?? string.Empty,
                    string.IsNullOrWhiteSpace(authoring.Registration.RequiredEditorTypeName)
                    || ReflectionCache.GetType(authoring.Registration.RequiredEditorTypeName) != null));
            }

            return descriptors
                .OrderBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal)
                .ThenBy(descriptor => descriptor.ProviderId, StringComparer.Ordinal)
                .ToArray();
        }

        public static IPlayerBuildExtensionAdapter ResolveAdapter(
            PlayerBuildExtensionConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            string requested = configuration.ProviderId?.Trim();
            if (string.IsNullOrWhiteSpace(requested))
            {
                throw new InvalidOperationException(
                    $"Player extension configuration '{configuration.GetType().FullName}' returned an empty provider id.");
            }

            BuildIdentityPolicy.ValidateBuildIdentifier(
                requested,
                "Player extension provider id");

            AdapterCandidate[] matches = DiscoverAdapterCandidates()
                .Where(candidate => string.Equals(
                    candidate.Registration.ProviderId,
                    requested,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length == 0)
            {
                return null;
            }

            AdapterCandidate candidate = RequireUnique(
                requested,
                matches,
                "Player extension adapter");
            ValidateAdapterType(candidate.Type, candidate.Registration);
            if (!candidate.Registration.ConfigurationType.IsInstanceOfType(configuration))
            {
                throw new InvalidOperationException(
                    $"Player extension provider '{requested}' requires configuration type " +
                    $"'{candidate.Registration.ConfigurationType.FullName}', but received " +
                    $"'{configuration.GetType().FullName}'.");
            }

            var adapter = (IPlayerBuildExtensionAdapter)CreateInstance(
                candidate.Type,
                "Player extension adapter");
            if (!string.Equals(
                    adapter.ProviderId?.Trim(),
                    candidate.Registration.ProviderId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Player extension adapter '{candidate.Type.FullName}' registration does not match its runtime ProviderId.");
            }

            string runtimeCompatibilityId = adapter.CompatibilityId;
            try
            {
                BuildIdentityPolicy.ValidateBuildIdentifier(
                    runtimeCompatibilityId,
                    "Player extension adapter runtime compatibility id");
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException(
                    $"Player extension adapter '{candidate.Type.FullName}' returned an invalid runtime CompatibilityId.",
                    exception);
            }

            if (!string.Equals(
                    runtimeCompatibilityId,
                    candidate.Registration.CompatibilityId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Player extension adapter '{candidate.Type.FullName}' registration CompatibilityId " +
                    $"'{candidate.Registration.CompatibilityId}' does not match its runtime CompatibilityId " +
                    $"'{runtimeCompatibilityId}'.");
            }

            return adapter;
        }

        public static IReadOnlyList<IPlayerBuildEnvironmentGuard> ResolveEnvironmentGuards()
        {
            var candidates = new List<GuardCandidate>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IPlayerBuildEnvironmentGuard>())
            {
                var registration = (PlayerBuildEnvironmentGuardRegistrationAttribute)
                    Attribute.GetCustomAttribute(
                        type,
                        typeof(PlayerBuildEnvironmentGuardRegistrationAttribute),
                        inherit: false);
                if (registration != null)
                {
                    candidates.Add(new GuardCandidate(type, registration));
                }
            }

            var guards = new List<IPlayerBuildEnvironmentGuard>();
            foreach (IGrouping<string, GuardCandidate> group in candidates
                         .GroupBy(candidate => candidate.Registration.GuardId,
                             StringComparer.OrdinalIgnoreCase)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                GuardCandidate candidate = RequireUnique(
                    group.Key,
                    group.ToArray(),
                    "Player environment guard");
                var guard = (IPlayerBuildEnvironmentGuard)CreateInstance(
                    candidate.Type,
                    "Player environment guard");
                if (!string.Equals(
                        guard.GuardId?.Trim(),
                        candidate.Registration.GuardId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Player environment guard '{candidate.Type.FullName}' registration does not match its runtime GuardId.");
                }

                guards.Add(guard);
            }

            return guards;
        }

        private static IReadOnlyList<AdapterCandidate> DiscoverAdapterCandidates()
        {
            var candidates = new List<AdapterCandidate>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IPlayerBuildExtensionAdapter>())
            {
                var registration = (PlayerBuildExtensionAdapterRegistrationAttribute)
                    Attribute.GetCustomAttribute(
                        type,
                        typeof(PlayerBuildExtensionAdapterRegistrationAttribute),
                        inherit: false);
                if (registration != null)
                {
                    candidates.Add(new AdapterCandidate(type, registration));
                }
            }

            return candidates;
        }

        private static void ValidateAdapterType(
            Type type,
            PlayerBuildExtensionAdapterRegistrationAttribute registration)
        {
            if (registration.ConfigurationType == null
                || !typeof(PlayerBuildExtensionConfiguration).IsAssignableFrom(
                    registration.ConfigurationType)
                || registration.ConfigurationType.IsAbstract
                || registration.ConfigurationType.ContainsGenericParameters)
            {
                throw new InvalidOperationException(
                    $"Player extension adapter '{type.FullName}' must register a concrete {nameof(PlayerBuildExtensionConfiguration)} type.");
            }

            ValidateConstructibleType(type, "Player extension adapter");
        }

        private static void ValidateAuthoringType(Type type)
        {
            if (type == null
                || !typeof(PlayerBuildExtensionConfiguration).IsAssignableFrom(type)
                || type.IsAbstract
                || type.ContainsGenericParameters)
            {
                throw new InvalidOperationException(
                    $"Player extension authoring type '{type?.FullName ?? "<null>"}' must be a concrete {nameof(PlayerBuildExtensionConfiguration)} type.");
            }
        }

        private static void ValidateConstructibleType(Type type, string role)
        {
            if (type == null
                || type.IsAbstract
                || type.ContainsGenericParameters
                || type.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new InvalidOperationException(
                    $"{role} '{type?.FullName ?? "<null>"}' must be a concrete type with a public parameterless constructor.");
            }

        }

        private static object CreateInstance(Type type, string role)
        {
            ValidateConstructibleType(type, role);
            try
            {
                return Activator.CreateInstance(type);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Failed to create {role} '{type.FullName}'.",
                    exception);
            }
        }

        private static T RequireUnique<T>(
            string id,
            IReadOnlyList<T> candidates,
            string role) where T : IRegisteredType
        {
            if (candidates.Count != 1)
            {
                string types = string.Join(
                    ", ",
                    candidates.Select(candidate => candidate.Type.FullName)
                        .OrderBy(name => name, StringComparer.Ordinal));
                throw new InvalidOperationException(
                    $"Multiple {role} types provide id '{id}': {types}. Provider ids must be globally unique.");
            }

            return candidates[0];
        }

        private interface IRegisteredType
        {
            Type Type { get; }
        }

        private sealed class AdapterCandidate : IRegisteredType
        {
            internal AdapterCandidate(
                Type type,
                PlayerBuildExtensionAdapterRegistrationAttribute registration)
            {
                Type = type;
                Registration = registration;
            }

            public Type Type { get; }
            internal PlayerBuildExtensionAdapterRegistrationAttribute Registration { get; }
        }

        private sealed class AuthoringCandidate : IRegisteredType
        {
            internal AuthoringCandidate(
                Type type,
                PlayerBuildExtensionAuthoringAttribute registration)
            {
                Type = type;
                Registration = registration;
            }

            public Type Type { get; }
            internal PlayerBuildExtensionAuthoringAttribute Registration { get; }
        }

        private sealed class GuardCandidate : IRegisteredType
        {
            internal GuardCandidate(
                Type type,
                PlayerBuildEnvironmentGuardRegistrationAttribute registration)
            {
                Type = type;
                Registration = registration;
            }

            public Type Type { get; }
            internal PlayerBuildEnvironmentGuardRegistrationAttribute Registration { get; }
        }
    }
}
