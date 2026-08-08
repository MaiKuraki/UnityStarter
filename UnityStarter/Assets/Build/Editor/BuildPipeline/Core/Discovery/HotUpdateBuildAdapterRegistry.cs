using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Build.Pipeline.Editor
{
    public static class HotUpdateBuildAdapterRegistry
    {
        public static IReadOnlyList<HotUpdateProviderDescriptor> GetProviderDescriptors()
        {
            var diagnostics = new List<string>();
            IReadOnlyList<HotUpdateProviderDescriptor> descriptors =
                GetProviderDescriptors(diagnostics);
            if (diagnostics.Count > 0)
            {
                throw new InvalidOperationException(
                    "Hot-update provider authoring catalog is invalid:\n" +
                    string.Join("\n", diagnostics));
            }

            return descriptors;
        }

        internal static IReadOnlyList<HotUpdateProviderDescriptor> GetProviderDescriptors(
            ICollection<string> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            var configurations = new Dictionary<string, List<ConfigurationCandidate>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (Type type in TypeCache.GetTypesWithAttribute<HotUpdateProviderAuthoringAttribute>())
            {
                HotUpdateProviderAuthoringAttribute registration;
                try
                {
                    registration = (HotUpdateProviderAuthoringAttribute)Attribute.GetCustomAttribute(
                        type,
                        typeof(HotUpdateProviderAuthoringAttribute),
                        inherit: false);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(
                        $"Hot-update configuration '{type.FullName}' has invalid registration metadata: {exception.Message}");
                    continue;
                }

                if (registration == null)
                {
                    continue;
                }

                if (!typeof(HotUpdateBuildConfiguration).IsAssignableFrom(type)
                    || type.IsAbstract
                    || type.ContainsGenericParameters)
                {
                    diagnostics.Add(
                        $"Hot-update configuration '{type.FullName}' must be a concrete HotUpdateBuildConfiguration.");
                    continue;
                }

                if (!configurations.TryGetValue(
                        registration.ProviderId,
                        out List<ConfigurationCandidate> candidates))
                {
                    candidates = new List<ConfigurationCandidate>();
                    configurations.Add(registration.ProviderId, candidates);
                }

                candidates.Add(new ConfigurationCandidate(type, registration));
            }

            var descriptors = new List<HotUpdateProviderDescriptor>(configurations.Count);
            foreach (KeyValuePair<string, List<ConfigurationCandidate>> entry in
                     configurations.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (entry.Value.Count != 1)
                {
                    diagnostics.Add(
                        $"Hot-update provider id '{entry.Key}' is declared by multiple configuration types: " +
                        FormatTypeNames(entry.Value.Select(candidate => candidate.Type)) + ".");
                    continue;
                }

                ConfigurationCandidate configuration = entry.Value[0];
                string[] requiredEditorTypeNames =
                    configuration.Registration.RequiredEditorTypeNames
                    ?? Array.Empty<string>();
                if (requiredEditorTypeNames.Any(string.IsNullOrWhiteSpace))
                {
                    diagnostics.Add(
                        $"Hot-update provider '{entry.Key}' declares an empty required editor type name.");
                    continue;
                }

                string[] duplicateRequiredTypeNames = requiredEditorTypeNames
                    .GroupBy(typeName => typeName, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .OrderBy(typeName => typeName, StringComparer.Ordinal)
                    .ToArray();
                if (duplicateRequiredTypeNames.Length > 0)
                {
                    diagnostics.Add(
                        $"Hot-update provider '{entry.Key}' declares duplicate required editor types: " +
                        string.Join(", ", duplicateRequiredTypeNames) + ".");
                    continue;
                }

                Type adapterType = ResolveAdapterType(
                    configuration.Registration.ProviderId,
                    diagnostics);
                if (adapterType != null)
                {
                    HotUpdateAdapterRegistrationAttribute adapterRegistration =
                        GetRegistration(adapterType);
                    if (adapterRegistration.ConfigurationType != configuration.Type)
                    {
                        diagnostics.Add(
                            $"Hot-update provider '{entry.Key}' authoring type '{configuration.Type.FullName}' " +
                            $"does not match adapter type '{adapterRegistration.ConfigurationType.FullName}'.");
                        continue;
                    }
                }

                descriptors.Add(new HotUpdateProviderDescriptor(
                    configuration.Registration.ProviderId,
                    configuration.Registration.DisplayName,
                    configuration.Registration.Description?.Trim() ?? string.Empty,
                    configuration.Registration.Order,
                    configuration.Type,
                    adapterType,
                    requiredEditorTypeNames.All(
                        typeName => ReflectionCache.GetType(typeName) != null)));
            }

            return descriptors
                .OrderBy(descriptor => descriptor.Order)
                .ThenBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal)
                .ThenBy(descriptor => descriptor.ProviderId, StringComparer.Ordinal)
                .ToArray();
        }

        public static IHotUpdateBuildAdapter ResolveAdapter(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException(
                    "Hot-update provider identifier is required.",
                    nameof(providerId));
            }

            string requestedProviderId = providerId.Trim();
            Type adapterType = ResolveAdapterType(requestedProviderId, diagnostics: null);
            if (adapterType == null)
            {
                return null;
            }

            HotUpdateAdapterRegistrationAttribute registration =
                GetRegistration(adapterType);
            ValidateConstructible(adapterType);

            IHotUpdateBuildAdapter adapter;
            try
            {
                adapter = (IHotUpdateBuildAdapter)Activator.CreateInstance(adapterType);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Failed to create hot-update adapter '{adapterType.FullName}'.",
                    exception);
            }

            string runtimeProviderId = adapter.ProviderId?.Trim();
            if (!string.Equals(
                    runtimeProviderId,
                    registration.ProviderId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Hot-update adapter '{adapterType.FullName}' registration provider id " +
                    "does not match its runtime ProviderId contract.");
            }

            if (adapter.ConfigurationType != registration.ConfigurationType)
            {
                throw new InvalidOperationException(
                    $"Hot-update adapter '{adapterType.FullName}' registration configuration type " +
                    "does not match its runtime ConfigurationType contract.");
            }

            return adapter;
        }

        private static Type ResolveAdapterType(
            string providerId,
            ICollection<string> diagnostics)
        {
            var candidates = new List<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IHotUpdateBuildAdapter>())
            {
                HotUpdateAdapterRegistrationAttribute registration;
                try
                {
                    registration = GetRegistration(type);
                }
                catch (Exception exception)
                {
                    if (diagnostics != null)
                    {
                        diagnostics.Add(
                            $"Hot-update adapter '{type.FullName}' has invalid registration metadata: {exception.Message}");
                    }

                    continue;
                }

                if (registration != null
                    && string.Equals(
                        registration.ProviderId,
                        providerId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(type);
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            if (candidates.Count != 1)
            {
                string message =
                    $"Multiple hot-update adapter types provide provider id '{providerId}': " +
                    FormatTypeNames(candidates) + ". Provider ids must be globally unique.";
                if (diagnostics == null)
                {
                    throw new InvalidOperationException(message);
                }

                diagnostics.Add(message);
                return null;
            }

            try
            {
                ValidateConstructible(candidates[0]);
            }
            catch (Exception exception)
            {
                if (diagnostics == null)
                {
                    throw;
                }

                diagnostics.Add(
                    $"Hot-update adapter id '{providerId}' is unavailable: {exception.Message}");
                return null;
            }

            return candidates[0];
        }

        private static HotUpdateAdapterRegistrationAttribute GetRegistration(Type type)
        {
            return (HotUpdateAdapterRegistrationAttribute)Attribute.GetCustomAttribute(
                type,
                typeof(HotUpdateAdapterRegistrationAttribute),
                inherit: false);
        }

        private static void ValidateConstructible(Type type)
        {
            if (type == null
                || type.IsAbstract
                || type.IsInterface
                || type.ContainsGenericParameters
                || type.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new InvalidOperationException(
                    $"Hot-update adapter '{type?.FullName ?? "<null>"}' must be a concrete type with a public parameterless constructor.");
            }

            if (GetRegistration(type) == null)
            {
                throw new InvalidOperationException(
                    $"Hot-update adapter '{type.FullName}' has no registration metadata.");
            }
        }

        private static string FormatTypeNames(IEnumerable<Type> types)
        {
            return string.Join(
                ", ",
                types
                    .Select(type => type.FullName ?? type.Name)
                    .OrderBy(typeName => typeName, StringComparer.Ordinal));
        }

        private sealed class ConfigurationCandidate
        {
            public ConfigurationCandidate(
                Type type,
                HotUpdateProviderAuthoringAttribute registration)
            {
                Type = type;
                Registration = registration;
            }

            public Type Type { get; }
            public HotUpdateProviderAuthoringAttribute Registration { get; }
        }
    }
}
