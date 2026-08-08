using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Build.Pipeline.Editor
{
    public static class BuildPipelineRegistry
    {
        public static IReadOnlyList<BuildStepDescriptor> GetBuildStepDescriptors()
        {
            var diagnostics = new List<string>();
            IReadOnlyList<BuildStepDescriptor> descriptors =
                GetBuildStepDescriptors(diagnostics);
            if (diagnostics.Count > 0)
            {
                throw new InvalidOperationException(
                    "Build step authoring catalog is invalid:\n" +
                    string.Join("\n", diagnostics));
            }

            return descriptors;
        }

        internal static IReadOnlyList<BuildStepDescriptor> GetBuildStepDescriptors(
            ICollection<string> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            var candidates = new Dictionary<string, List<StepRegistrationCandidate>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IBuildStep>())
            {
                BuildStepRegistrationAttribute registration;
                try
                {
                    registration = (BuildStepRegistrationAttribute)Attribute.GetCustomAttribute(
                        type,
                        typeof(BuildStepRegistrationAttribute),
                        inherit: false);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(
                        $"Build step '{type.FullName}' has invalid registration metadata: {exception.Message}");
                    continue;
                }

                if (registration == null || registration.HiddenFromAuthoring)
                {
                    continue;
                }

                if (!candidates.TryGetValue(
                    registration.StepTypeId,
                    out List<StepRegistrationCandidate> registeredTypes))
                {
                    registeredTypes = new List<StepRegistrationCandidate>();
                    candidates.Add(registration.StepTypeId, registeredTypes);
                }

                registeredTypes.Add(new StepRegistrationCandidate(type, registration));
            }

            var descriptors = new List<BuildStepDescriptor>(candidates.Count);
            foreach (KeyValuePair<string, List<StepRegistrationCandidate>> entry in
                     candidates.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                try
                {
                    StepRegistrationCandidate winner = SelectUniqueStepCandidate(entry.Key, entry.Value);
                    ValidateConstructibleType(winner.Type, "build step");
                    ValidateStepConfigurationContract(
                        winner.Type,
                        winner.Registration);
                    descriptors.Add(new BuildStepDescriptor(
                        winner.Registration.StepTypeId,
                        winner.Registration.DisplayName,
                        winner.Registration.Description,
                        winner.Registration.Category,
                        winner.Type,
                        winner.Registration.ConfigurationType,
                        winner.Registration.ConfigurationRequired,
                        winner.Registration.Multiplicity));
                }
                catch (Exception exception)
                {
                    diagnostics.Add(
                        $"Build step type id '{entry.Key}' is unavailable: {exception.Message}");
                }
            }

            return descriptors
                .OrderBy(descriptor => descriptor.Category, StringComparer.Ordinal)
                .ThenBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal)
                .ThenBy(descriptor => descriptor.StepTypeId, StringComparer.Ordinal)
                .ToArray();
        }

        public static IReadOnlyList<AssetContentProviderDescriptor> GetAssetContentProviderDescriptors()
        {
            var diagnostics = new List<string>();
            IReadOnlyList<AssetContentProviderDescriptor> descriptors =
                GetAssetContentProviderDescriptors(diagnostics);
            if (diagnostics.Count > 0)
            {
                throw new InvalidOperationException(
                    "Asset provider authoring catalog is invalid:\n" +
                    string.Join("\n", diagnostics));
            }

            return descriptors;
        }

        internal static IReadOnlyList<AssetContentProviderDescriptor> GetAssetContentProviderDescriptors(
            ICollection<string> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            var authoringTypes = new Dictionary<string, List<ProviderAuthoringCandidate>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (Type type in TypeCache.GetTypesWithAttribute<AssetContentProviderAuthoringAttribute>())
            {
                AssetContentProviderAuthoringAttribute registration;
                try
                {
                    registration = (AssetContentProviderAuthoringAttribute)Attribute.GetCustomAttribute(
                        type,
                        typeof(AssetContentProviderAuthoringAttribute),
                        inherit: false);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(
                        $"Content provider configuration '{type.FullName}' has invalid registration metadata: {exception.Message}");
                    continue;
                }

                if (registration == null)
                {
                    continue;
                }

                if (!typeof(AssetContentBuildConfiguration).IsAssignableFrom(type)
                    || type.IsAbstract
                    || type.ContainsGenericParameters)
                {
                    diagnostics.Add(
                        $"Content provider configuration '{type.FullName}' must be a concrete AssetContentBuildConfiguration type.");
                    continue;
                }

                if (!authoringTypes.TryGetValue(
                    registration.ProviderId,
                    out List<ProviderAuthoringCandidate> registeredTypes))
                {
                    registeredTypes = new List<ProviderAuthoringCandidate>();
                    authoringTypes.Add(registration.ProviderId, registeredTypes);
                }

                registeredTypes.Add(new ProviderAuthoringCandidate(type, registration));
            }

            Dictionary<string, Type> adapterTypes = ResolveAdapterTypes(
                authoringTypes.Keys,
                diagnostics);
            var descriptors = new List<AssetContentProviderDescriptor>(authoringTypes.Count);
            foreach (KeyValuePair<string, List<ProviderAuthoringCandidate>> entry in
                     authoringTypes.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (entry.Value.Count != 1)
                {
                    diagnostics.Add(
                        $"Content provider id '{entry.Key}' is declared by multiple configuration types: " +
                        string.Join(", ", entry.Value.Select(candidate => candidate.Type.FullName)) + ".");
                    continue;
                }

                ProviderAuthoringCandidate candidate = entry.Value[0];
                adapterTypes.TryGetValue(candidate.Registration.ProviderId, out Type adapterType);
                try
                {
                    descriptors.Add(new AssetContentProviderDescriptor(
                        candidate.Registration.ProviderId,
                        candidate.Registration.DisplayName,
                        candidate.Registration.Description?.Trim() ?? string.Empty,
                        candidate.Registration.Order,
                        candidate.Type,
                        adapterType,
                        string.IsNullOrWhiteSpace(candidate.Registration.RequiredEditorTypeName)
                        || ReflectionCache.GetType(candidate.Registration.RequiredEditorTypeName) != null));
                }
                catch (Exception exception)
                {
                    diagnostics.Add(
                        $"Content provider id '{entry.Key}' is unavailable: {exception.Message}");
                }
            }

            return descriptors
                .OrderBy(descriptor => descriptor.Order)
                .ThenBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal)
                .ThenBy(descriptor => descriptor.ProviderId, StringComparer.Ordinal)
                .ToArray();
        }

        public static IReadOnlyList<IBuildStep> ResolveSteps(IReadOnlyList<string> requestedIds)
        {
            if (requestedIds == null)
            {
                throw new ArgumentNullException(nameof(requestedIds));
            }

            var requested = new HashSet<string>(requestedIds, StringComparer.OrdinalIgnoreCase);
            var candidates = new Dictionary<string, List<StepRegistrationCandidate>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IBuildStep>())
            {
                BuildStepRegistrationAttribute registration;
                try
                {
                    registration = (BuildStepRegistrationAttribute)Attribute.GetCustomAttribute(
                        type,
                        typeof(BuildStepRegistrationAttribute),
                        inherit: false);
                }
                catch
                {
                    // Malformed metadata on an unrelated optional extension must not
                    // prevent a requested, independently registered step from resolving.
                    continue;
                }

                if (registration == null || !requested.Contains(registration.StepTypeId))
                {
                    continue;
                }

                if (!candidates.TryGetValue(
                    registration.StepTypeId,
                    out List<StepRegistrationCandidate> registeredTypes))
                {
                    registeredTypes = new List<StepRegistrationCandidate>();
                    candidates.Add(registration.StepTypeId, registeredTypes);
                }

                registeredTypes.Add(new StepRegistrationCandidate(type, registration));
            }

            var steps = new List<IBuildStep>();
            var resolvedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string requestedId in requestedIds)
            {
                if (string.IsNullOrWhiteSpace(requestedId)
                    || !resolvedIds.Add(requestedId)
                    || !candidates.TryGetValue(
                        requestedId,
                        out List<StepRegistrationCandidate> registeredTypes))
                {
                    continue;
                }

                StepRegistrationCandidate winner = SelectUniqueStepCandidate(requestedId, registeredTypes);
                Type type = winner.Type;
                BuildStepRegistrationAttribute registration = winner.Registration;
                ValidateConstructibleType(type, "build step");
                try
                {
                    var step = (IBuildStep)Activator.CreateInstance(type);
                    ValidateStepRegistration(type, registration, step);
                    steps.Add(step);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException($"Failed to create build step '{type.FullName}'.", exception);
                }
            }

            return steps;
        }

        public static IAssetContentBuildAdapter ResolveContentAdapter(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException("Content provider identifier is required.", nameof(providerId));
            }

            string requestedProviderId = providerId.Trim();

            var candidates = new List<AdapterRegistrationCandidate>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IAssetContentBuildAdapter>())
            {
                AssetContentAdapterRegistrationAttribute registration;
                try
                {
                    registration = (AssetContentAdapterRegistrationAttribute)Attribute.GetCustomAttribute(
                        type,
                        typeof(AssetContentAdapterRegistrationAttribute),
                        inherit: false);
                }
                catch
                {
                    // Resolution is provider-scoped. Invalid metadata belonging to an
                    // unrelated optional adapter is surfaced by the authoring catalog.
                    continue;
                }

                if (registration == null ||
                    !string.Equals(registration.ProviderId, requestedProviderId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                candidates.Add(new AdapterRegistrationCandidate(type, registration));
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            if (candidates.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Multiple content adapter types provide provider id '{requestedProviderId}': " +
                    $"{FormatTypeNames(candidates.Select(candidate => candidate.Type))}. " +
                    "Provider ids must be globally unique.");
            }

            Type winnerType = candidates[0].Type;
            AssetContentAdapterRegistrationAttribute winnerRegistration = candidates[0].Registration;
            ValidateConstructibleType(winnerType, "content adapter");
            IAssetContentBuildAdapter adapter;
            try
            {
                adapter = (IAssetContentBuildAdapter)Activator.CreateInstance(winnerType);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Failed to create content adapter '{winnerType.FullName}'.", exception);
            }

            string candidateProviderId = adapter.ProviderId?.Trim();
            if (string.IsNullOrEmpty(candidateProviderId))
            {
                throw new InvalidOperationException(
                    $"Content adapter '{winnerType.FullName}' returned an empty provider identifier.");
            }

            if (!string.Equals(candidateProviderId, winnerRegistration.ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Content adapter '{winnerType.FullName}' registration metadata does not match its runtime ProviderId contract.");
            }

            return adapter;
        }

        public static IReadOnlyList<IBuildRecoveryParticipant> ResolveRecoveryParticipants()
        {
            var candidates = new List<RecoveryRegistrationCandidate>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IBuildRecoveryParticipant>())
            {
                var registration = (BuildRecoveryRegistrationAttribute)Attribute.GetCustomAttribute(
                    type,
                    typeof(BuildRecoveryRegistrationAttribute),
                    inherit: false);
                if (registration == null)
                {
                    continue;
                }

                candidates.Add(new RecoveryRegistrationCandidate(type, registration));
            }

            return ResolveRecoveryParticipants(candidates);
        }

        internal static IReadOnlyList<IBuildRecoveryParticipant> ResolveRecoveryParticipants(
            IEnumerable<RecoveryRegistrationCandidate> registrations)
        {
            if (registrations == null)
            {
                throw new ArgumentNullException(nameof(registrations));
            }

            var candidates = new Dictionary<string, List<RecoveryRegistrationCandidate>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (RecoveryRegistrationCandidate candidate in registrations)
            {
                if (candidate == null)
                {
                    throw new ArgumentException(
                        "Build recovery registration candidates may not contain null entries.",
                        nameof(registrations));
                }

                if (!candidates.TryGetValue(
                        candidate.Registration.Id,
                        out List<RecoveryRegistrationCandidate> registeredTypes))
                {
                    registeredTypes = new List<RecoveryRegistrationCandidate>();
                    candidates.Add(candidate.Registration.Id, registeredTypes);
                }

                registeredTypes.Add(candidate);
            }

            var participants = new List<IBuildRecoveryParticipant>(candidates.Count);
            foreach (KeyValuePair<string, List<RecoveryRegistrationCandidate>> entry in
                     candidates.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (entry.Value.Count != 1)
                {
                    string types = string.Join(
                        ", ",
                        entry.Value
                            .Select(candidate => candidate.Type.FullName)
                            .OrderBy(typeName => typeName, StringComparer.Ordinal));
                    throw new InvalidOperationException(
                        $"Multiple build recovery participants provide id '{entry.Key}': {types}. " +
                        "Recovery ownership identifiers must be globally unique; priority only orders participants with different identifiers.");
                }

                Type winnerType = entry.Value[0].Type;
                BuildRecoveryRegistrationAttribute registration = entry.Value[0].Registration;
                ValidateConstructibleType(winnerType, "build recovery participant");
                IBuildRecoveryParticipant participant;
                try
                {
                    participant = (IBuildRecoveryParticipant)Activator.CreateInstance(winnerType);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Failed to create build recovery participant '{winnerType.FullName}'.",
                        exception);
                }

                if (!string.Equals(participant.Id?.Trim(), registration.Id, StringComparison.OrdinalIgnoreCase)
                    || participant.Priority != registration.Priority)
                {
                    throw new InvalidOperationException(
                        $"Build recovery participant '{winnerType.FullName}' registration metadata does not match its runtime Id/Priority contract.");
                }

                participants.Add(participant);
            }

            return participants;
        }

        private static void ValidateConstructibleType(Type type, string registrationKind)
        {
            if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters
                || type.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new InvalidOperationException(
                    $"Registered {registrationKind} '{type.FullName}' must be a concrete type with a public parameterless constructor.");
            }
        }

        private static StepRegistrationCandidate SelectUniqueStepCandidate(
            string id,
            IReadOnlyList<StepRegistrationCandidate> candidates)
        {
            if (candidates.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Multiple build step types provide id '{id}': " +
                    $"{FormatTypeNames(candidates.Select(candidate => candidate.Type))}. " +
                    "Build step type ids must be globally unique.");
            }

            return candidates[0];
        }

        private static Dictionary<string, Type> ResolveAdapterTypes(
            IEnumerable<string> providerIds,
            ICollection<string> diagnostics)
        {
            var requestedProviderIds = new HashSet<string>(
                providerIds ?? throw new ArgumentNullException(nameof(providerIds)),
                StringComparer.OrdinalIgnoreCase);
            var candidates = new Dictionary<string, List<AdapterRegistrationCandidate>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IAssetContentBuildAdapter>())
            {
                AssetContentAdapterRegistrationAttribute registration;
                try
                {
                    registration = (AssetContentAdapterRegistrationAttribute)Attribute.GetCustomAttribute(
                        type,
                        typeof(AssetContentAdapterRegistrationAttribute),
                        inherit: false);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(
                        $"Content adapter '{type.FullName}' has invalid registration metadata: {exception.Message}");
                    continue;
                }

                if (registration == null || !requestedProviderIds.Contains(registration.ProviderId))
                {
                    continue;
                }

                if (!candidates.TryGetValue(
                    registration.ProviderId,
                    out List<AdapterRegistrationCandidate> registeredTypes))
                {
                    registeredTypes = new List<AdapterRegistrationCandidate>();
                    candidates.Add(registration.ProviderId, registeredTypes);
                }

                registeredTypes.Add(new AdapterRegistrationCandidate(type, registration));
            }

            var result = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<AdapterRegistrationCandidate>> entry in
                     candidates.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                try
                {
                    if (entry.Value.Count != 1)
                    {
                        throw new InvalidOperationException(
                            $"Multiple content adapter types provide provider id '{entry.Key}': " +
                            $"{FormatTypeNames(entry.Value.Select(candidate => candidate.Type))}. " +
                            "Provider ids must be globally unique.");
                    }

                    ValidateConstructibleType(entry.Value[0].Type, "content adapter");
                    result.Add(entry.Key, entry.Value[0].Type);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(
                        $"Content adapter id '{entry.Key}' is unavailable: {exception.Message}");
                }
            }

            return result;
        }

        internal static string FormatTypeNames(IEnumerable<Type> types)
        {
            return string.Join(
                ", ",
                types
                    .Select(type => type.FullName ?? type.Name)
                    .OrderBy(typeName => typeName, StringComparer.Ordinal));
        }

        private sealed class StepRegistrationCandidate
        {
            public StepRegistrationCandidate(Type type, BuildStepRegistrationAttribute registration)
            {
                Type = type;
                Registration = registration;
            }

            public Type Type { get; }
            public BuildStepRegistrationAttribute Registration { get; }
        }

        private sealed class AdapterRegistrationCandidate
        {
            public AdapterRegistrationCandidate(
                Type type,
                AssetContentAdapterRegistrationAttribute registration)
            {
                Type = type;
                Registration = registration;
            }

            public Type Type { get; }
            public AssetContentAdapterRegistrationAttribute Registration { get; }
        }

        private sealed class ProviderAuthoringCandidate
        {
            public ProviderAuthoringCandidate(
                Type type,
                AssetContentProviderAuthoringAttribute registration)
            {
                Type = type;
                Registration = registration;
            }

            public Type Type { get; }
            public AssetContentProviderAuthoringAttribute Registration { get; }
        }

        internal sealed class RecoveryRegistrationCandidate
        {
            internal RecoveryRegistrationCandidate(
                Type type,
                BuildRecoveryRegistrationAttribute registration)
            {
                Type = type ?? throw new ArgumentNullException(nameof(type));
                Registration = registration ?? throw new ArgumentNullException(nameof(registration));
            }

            public Type Type { get; }
            public BuildRecoveryRegistrationAttribute Registration { get; }
        }

        private static void ValidateStepRegistration(
            Type type,
            BuildStepRegistrationAttribute registration,
            IBuildStep step)
        {
            ValidateStepConfigurationContract(type, registration);

            if (step == null || string.IsNullOrWhiteSpace(step.StepTypeId))
            {
                throw new InvalidOperationException(
                    $"Build step '{type.FullName}' returned an empty identifier.");
            }

            try
            {
                BuildIdentityPolicy.ValidatePlainText(
                    step.StepTypeId,
                    "Build step runtime type id",
                    BuildStepRegistrationAttribute.MaximumIdCharacters);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException(
                    $"Build step '{type.FullName}' returned an invalid identifier. {exception.Message}",
                    exception);
            }

            if (!string.Equals(step.StepTypeId, registration.StepTypeId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Build step '{type.FullName}' registration metadata does not match its runtime StepTypeId contract.");
            }
        }

        internal static void ValidateStepConfiguration(
            IBuildStep step,
            BuildStepInvocation invocation)
        {
            if (step == null)
            {
                throw new ArgumentNullException(nameof(step));
            }

            if (invocation == null)
            {
                throw new ArgumentNullException(nameof(invocation));
            }

            var registration = (BuildStepRegistrationAttribute)Attribute.GetCustomAttribute(
                step.GetType(),
                typeof(BuildStepRegistrationAttribute),
                inherit: false);
            if (registration == null)
            {
                throw new InvalidOperationException(
                    $"Build step '{step.GetType().FullName}' has no registration metadata.");
            }

            ValidateStepConfigurationContract(step.GetType(), registration);
            if (registration.ConfigurationRequired && invocation.Configuration == null)
            {
                throw new InvalidOperationException(
                    $"Build invocation '{invocation.InvocationId}' ({invocation.StepTypeId}) requires a " +
                    $"{registration.ConfigurationType.Name} configuration asset.");
            }

            if (invocation.Configuration != null
                && registration.ConfigurationType == null)
            {
                throw new InvalidOperationException(
                    $"Build invocation '{invocation.InvocationId}' ({invocation.StepTypeId}) does not accept a configuration asset, but " +
                    $"'{invocation.Configuration.name}' ({invocation.Configuration.GetType().Name}) is assigned.");
            }

            if (invocation.Configuration != null
                && !registration.ConfigurationType.IsInstanceOfType(invocation.Configuration))
            {
                throw new InvalidOperationException(
                    $"Build invocation '{invocation.InvocationId}' ({invocation.StepTypeId}) requires {registration.ConfigurationType.Name}, but " +
                    $"'{invocation.Configuration.name}' is {invocation.Configuration.GetType().Name}.");
            }
        }

        private static void ValidateStepConfigurationContract(
            Type type,
            BuildStepRegistrationAttribute registration)
        {
            if (registration.Multiplicity != BuildStepMultiplicity.Single
                && registration.Multiplicity != BuildStepMultiplicity.Multiple)
            {
                throw new InvalidOperationException(
                    $"Build step '{type.FullName}' declares an unsupported multiplicity value '{registration.Multiplicity}'.");
            }

            if (registration.ConfigurationRequired
                && registration.ConfigurationType == null)
            {
                throw new InvalidOperationException(
                    $"Build step '{type.FullName}' marks configuration as required without declaring ConfigurationType.");
            }

            if (registration.ConfigurationType != null
                && !typeof(UnityEngine.ScriptableObject).IsAssignableFrom(
                    registration.ConfigurationType))
            {
                throw new InvalidOperationException(
                    $"Build step '{type.FullName}' configuration type " +
                    $"'{registration.ConfigurationType.FullName}' must derive from ScriptableObject.");
            }
        }
    }

    public static class BuildPlanCompiler
    {
        public static IReadOnlyList<CompiledBuildStep> Compile(BuildExecutionContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            IReadOnlyList<IBuildStep> discovered = BuildPipelineRegistry.ResolveSteps(
                context.Request.StepTypeIds);
            var selected = new Dictionary<string, IBuildStep>(StringComparer.OrdinalIgnoreCase);
            var invocations = new Dictionary<string, BuildStepInvocation>(
                StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < context.Request.Steps.Count; index++)
            {
                BuildStepInvocation invocation = context.Request.Steps[index];
                string invocationId = invocation.InvocationId?.Trim();
                string stepTypeId = invocation.StepTypeId?.Trim();
                if (string.IsNullOrEmpty(invocationId))
                {
                    throw new InvalidOperationException(
                        $"Configured build invocation at index {index} has an empty identity.");
                }

                if (string.IsNullOrEmpty(stepTypeId))
                {
                    throw new InvalidOperationException(
                        $"Build invocation '{invocationId}' has an empty step type identity.");
                }

                BuildIdentityPolicy.ValidateBuildIdentifier(
                    invocationId,
                    "Build invocation id");
                if (!invocations.TryAdd(invocationId, invocation))
                {
                    throw new InvalidOperationException(
                        $"Build invocation id '{invocationId}' is configured more than once.");
                }

                IBuildStep registeredStep = ResolveStep(discovered, stepTypeId);
                if (registeredStep == null)
                {
                    throw new InvalidOperationException(
                        $"No build step implementation is available for type id '{stepTypeId}' " +
                        $"used by invocation '{invocationId}'.");
                }

                IBuildStep step = CreateInvocationStep(
                    registeredStep,
                    invocationId,
                    stepTypeId);

                BuildPipelineRegistry.ValidateStepConfiguration(
                    step,
                    invocation);

                selected.Add(invocationId, step);
            }

            ValidateMultiplicity(context.Request.Steps, selected);

            if (selected.Count == 0)
            {
                throw new InvalidOperationException("The build plan does not contain any steps.");
            }

            var applicability = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < context.Request.Steps.Count; index++)
            {
                BuildStepInvocation invocation = context.Request.Steps[index];
                applicability[invocation.InvocationId] = selected[invocation.InvocationId]
                    .IsApplicable(context, invocation);
            }

            var outgoing = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var incomingCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in selected.Keys)
            {
                outgoing[id] = new List<string>();
                incomingCount[id] = 0;
            }

            foreach (KeyValuePair<string, IBuildStep> entry in selected)
            {
                IBuildStep step = entry.Value;
                if (!applicability[entry.Key])
                {
                    continue;
                }

                BuildStepInvocation invocation = invocations[entry.Key];
                IReadOnlyList<BuildInvocationDependency> dependencies =
                    invocation.Dependencies;
                var uniqueDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (BuildInvocationDependency dependencyDeclaration in dependencies)
                {
                    string dependencyId = dependencyDeclaration?.InvocationId?.Trim();
                    if (string.IsNullOrWhiteSpace(dependencyId)
                        || !uniqueDependencies.Add(dependencyId))
                    {
                        throw new InvalidOperationException(
                            $"Build invocation '{entry.Key}' ({step.StepTypeId}) declares an invalid or duplicate invocation dependency.");
                    }

                    if (!invocations.TryGetValue(
                            dependencyId,
                            out BuildStepInvocation dependency))
                    {
                        if (dependencyDeclaration.Mode == BuildDependencyMode.IfSelected)
                        {
                            continue;
                        }

                        throw new InvalidOperationException(
                            $"Build invocation '{entry.Key}' ({step.StepTypeId}) requires missing invocation '{dependencyId}'.");
                    }

                    if (!applicability[dependency.InvocationId])
                    {
                        throw new InvalidOperationException(
                            $"Build invocation '{entry.Key}' ({step.StepTypeId}) requires non-applicable " +
                            $"invocation '{dependency.InvocationId}' ({dependency.StepTypeId}).");
                    }

                    outgoing[dependency.InvocationId].Add(entry.Key);
                    incomingCount[entry.Key]++;
                }
            }

            var ready = new List<string>();
            foreach (KeyValuePair<string, int> entry in incomingCount)
            {
                if (entry.Value == 0)
                {
                    ready.Add(entry.Key);
                }
            }

            ready.Sort(StringComparer.OrdinalIgnoreCase);
            var orderedIds = new List<string>(selected.Count);
            while (ready.Count > 0)
            {
                string current = ready[0];
                ready.RemoveAt(0);
                orderedIds.Add(current);

                foreach (string dependent in outgoing[current])
                {
                    incomingCount[dependent]--;
                    if (incomingCount[dependent] == 0)
                    {
                        ready.Add(dependent);
                        ready.Sort(StringComparer.OrdinalIgnoreCase);
                    }
                }
            }

            if (orderedIds.Count != selected.Count)
            {
                string cycleIds = string.Join(", ", incomingCount.Where(entry => entry.Value > 0).Select(entry => entry.Key));
                throw new InvalidOperationException($"Build step dependency cycle detected: {cycleIds}.");
            }

            CompiledBuildStep[] compiledPlan = orderedIds
                .Select(invocationId => new CompiledBuildStep(
                    invocations[invocationId],
                    selected[invocationId],
                    applicability[invocationId]))
                .ToArray();
            context.SetPlan(compiledPlan);

            var validationErrors = new List<string>();
            int applicableCount = 0;
            foreach (string invocationId in orderedIds)
            {
                IBuildStep step = selected[invocationId];
                BuildStepInvocation invocation = invocations[invocationId];
                if (!applicability[invocationId])
                {
                    continue;
                }

                applicableCount++;
                IReadOnlyList<string> errors = step.Validate(context, invocation)
                    ?? Array.Empty<string>();
                foreach (string error in errors)
                {
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        validationErrors.Add(
                            $"[{invocation.InvocationId}:{invocation.StepTypeId}] {error}");
                    }
                }
            }

            if (applicableCount == 0)
            {
                validationErrors.Add("The build plan does not contain any applicable steps for this request.");
            }

            if (validationErrors.Count > 0)
            {
                throw new InvalidOperationException("Build preflight failed:\n" + string.Join("\n", validationErrors));
            }

            return compiledPlan;
        }

        private static IBuildStep ResolveStep(IReadOnlyList<IBuildStep> discovered, string requestedId)
        {
            IBuildStep[] matches = discovered
                .Where(step => string.Equals(
                    step.StepTypeId,
                    requestedId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Build step type id '{requestedId}' is provided by multiple runtime types: " +
                    $"{BuildPipelineRegistry.FormatTypeNames(matches.Select(step => step.GetType()))}. " +
                    "Build step type ids must be globally unique.");
            }

            return matches.Length == 0 ? null : matches[0];
        }

        private static IBuildStep CreateInvocationStep(
            IBuildStep registeredStep,
            string invocationId,
            string stepTypeId)
        {
            try
            {
                return (IBuildStep)Activator.CreateInstance(
                    registeredStep.GetType());
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Failed to create build step type '{stepTypeId}' for invocation '{invocationId}'.",
                    exception);
            }
        }

        private static void ValidateMultiplicity(
            IReadOnlyList<BuildStepInvocation> invocations,
            IReadOnlyDictionary<string, IBuildStep> selected)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < invocations.Count; index++)
            {
                string stepTypeId = invocations[index].StepTypeId;
                counts.TryGetValue(stepTypeId, out int count);
                counts[stepTypeId] = count + 1;
            }

            foreach (KeyValuePair<string, int> entry in counts)
            {
                if (entry.Value <= 1)
                {
                    continue;
                }

                IBuildStep registeredStep = selected.Values.First(step =>
                    string.Equals(
                        step.StepTypeId,
                        entry.Key,
                        StringComparison.OrdinalIgnoreCase));
                var registration = (BuildStepRegistrationAttribute)Attribute.GetCustomAttribute(
                    registeredStep.GetType(),
                    typeof(BuildStepRegistrationAttribute),
                    inherit: false);
                if (registration != null
                    && registration.Multiplicity == BuildStepMultiplicity.Multiple)
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Build step type '{entry.Key}' allows one invocation per recipe, but {entry.Value} were selected.");
            }
        }
    }
}
