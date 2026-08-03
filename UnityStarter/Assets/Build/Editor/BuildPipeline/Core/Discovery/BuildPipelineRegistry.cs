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
                    registration.Id,
                    out List<StepRegistrationCandidate> registeredTypes))
                {
                    registeredTypes = new List<StepRegistrationCandidate>();
                    candidates.Add(registration.Id, registeredTypes);
                }

                registeredTypes.Add(new StepRegistrationCandidate(type, registration));
            }

            var descriptors = new List<BuildStepDescriptor>(candidates.Count);
            foreach (KeyValuePair<string, List<StepRegistrationCandidate>> entry in
                     candidates.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                try
                {
                    StepRegistrationCandidate winner = SelectStepWinner(entry.Key, entry.Value);
                    ValidateConstructibleType(winner.Type, "build step");
                    descriptors.Add(new BuildStepDescriptor(
                        winner.Registration.Id,
                        winner.Registration.DisplayName,
                        winner.Registration.Description,
                        winner.Registration.Category,
                        winner.Registration.Priority,
                        winner.Type));
                }
                catch (Exception exception)
                {
                    diagnostics.Add(
                        $"Build step id '{entry.Key}' is unavailable: {exception.Message}");
                }
            }

            return descriptors
                .OrderBy(descriptor => descriptor.Category, StringComparer.Ordinal)
                .ThenBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal)
                .ThenBy(descriptor => descriptor.Id, StringComparer.Ordinal)
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

                if (!typeof(UnityEngine.ScriptableObject).IsAssignableFrom(type)
                    || type.IsAbstract
                    || type.ContainsGenericParameters)
                {
                    diagnostics.Add(
                        $"Content provider configuration '{type.FullName}' must be a concrete ScriptableObject type.");
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

            Dictionary<string, Type> adapterTypes = ResolveAdapterTypes(diagnostics);
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

                if (registration == null || !requested.Contains(registration.Id))
                {
                    continue;
                }

                if (!candidates.TryGetValue(
                    registration.Id,
                    out List<StepRegistrationCandidate> registeredTypes))
                {
                    registeredTypes = new List<StepRegistrationCandidate>();
                    candidates.Add(registration.Id, registeredTypes);
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

                StepRegistrationCandidate winner = SelectStepWinner(requestedId, registeredTypes);
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

            int highestPriority = candidates.Max(candidate => candidate.Registration.Priority);
            AdapterRegistrationCandidate[] winners = candidates
                .Where(candidate => candidate.Registration.Priority == highestPriority)
                .ToArray();
            if (winners.Length != 1)
            {
                string types = string.Join(", ", winners.Select(candidate => candidate.Type.FullName));
                throw new InvalidOperationException($"Multiple content adapters with provider id '{requestedProviderId}' have priority {highestPriority}: {types}.");
            }

            Type winnerType = winners[0].Type;
            AssetContentAdapterRegistrationAttribute winnerRegistration = winners[0].Registration;
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

            if (!string.Equals(candidateProviderId, winnerRegistration.ProviderId, StringComparison.OrdinalIgnoreCase)
                || adapter.Priority != winnerRegistration.Priority)
            {
                throw new InvalidOperationException(
                    $"Content adapter '{winnerType.FullName}' registration metadata does not match its runtime ProviderId/Priority contract.");
            }

            return adapter;
        }

        public static IReadOnlyList<IBuildRecoveryParticipant> ResolveRecoveryParticipants()
        {
            var candidates = new Dictionary<string, List<RecoveryRegistrationCandidate>>(
                StringComparer.OrdinalIgnoreCase);
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

                if (!candidates.TryGetValue(
                    registration.Id,
                    out List<RecoveryRegistrationCandidate> registeredTypes))
                {
                    registeredTypes = new List<RecoveryRegistrationCandidate>();
                    candidates.Add(registration.Id, registeredTypes);
                }

                registeredTypes.Add(new RecoveryRegistrationCandidate(type, registration));
            }

            var participants = new List<IBuildRecoveryParticipant>(candidates.Count);
            foreach (KeyValuePair<string, List<RecoveryRegistrationCandidate>> entry in
                     candidates.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                int highestPriority = entry.Value.Max(candidate => candidate.Registration.Priority);
                RecoveryRegistrationCandidate[] winners = entry.Value
                    .Where(candidate => candidate.Registration.Priority == highestPriority)
                    .ToArray();
                if (winners.Length != 1)
                {
                    string types = string.Join(", ", winners.Select(candidate => candidate.Type.FullName));
                    throw new InvalidOperationException(
                        $"Multiple build recovery participants provide id '{entry.Key}' at priority {highestPriority}: {types}.");
                }

                Type winnerType = winners[0].Type;
                BuildRecoveryRegistrationAttribute registration = winners[0].Registration;
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

        private static StepRegistrationCandidate SelectStepWinner(
            string id,
            IReadOnlyList<StepRegistrationCandidate> candidates)
        {
            int highestPriority = candidates.Max(candidate => candidate.Registration.Priority);
            StepRegistrationCandidate[] winners = candidates
                .Where(candidate => candidate.Registration.Priority == highestPriority)
                .ToArray();
            if (winners.Length != 1)
            {
                string types = string.Join(", ", winners.Select(candidate => candidate.Type.FullName));
                throw new InvalidOperationException(
                    $"Multiple build step types provide id '{id}' at priority {highestPriority}: {types}.");
            }

            return winners[0];
        }

        private static Dictionary<string, Type> ResolveAdapterTypes(
            ICollection<string> diagnostics)
        {
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

                if (registration == null)
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
                    int highestPriority = entry.Value.Max(candidate => candidate.Registration.Priority);
                    AdapterRegistrationCandidate[] winners = entry.Value
                        .Where(candidate => candidate.Registration.Priority == highestPriority)
                        .ToArray();
                    if (winners.Length != 1)
                    {
                        string types = string.Join(", ", winners.Select(candidate => candidate.Type.FullName));
                        throw new InvalidOperationException(
                            $"Multiple content adapters with provider id '{entry.Key}' have priority {highestPriority}: {types}.");
                    }

                    ValidateConstructibleType(winners[0].Type, "content adapter");
                    result.Add(entry.Key, winners[0].Type);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(
                        $"Content adapter id '{entry.Key}' is unavailable: {exception.Message}");
                }
            }

            return result;
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

        private sealed class RecoveryRegistrationCandidate
        {
            public RecoveryRegistrationCandidate(
                Type type,
                BuildRecoveryRegistrationAttribute registration)
            {
                Type = type;
                Registration = registration;
            }

            public Type Type { get; }
            public BuildRecoveryRegistrationAttribute Registration { get; }
        }

        private static void ValidateStepRegistration(
            Type type,
            BuildStepRegistrationAttribute registration,
            IBuildStep step)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.Id))
            {
                throw new InvalidOperationException(
                    $"Build step '{type.FullName}' returned an empty identifier.");
            }

            try
            {
                BuildIdentityPolicy.ValidatePlainText(
                    step.Id,
                    "Build step runtime id",
                    BuildStepRegistrationAttribute.MaximumIdCharacters);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException(
                    $"Build step '{type.FullName}' returned an invalid identifier. {exception.Message}",
                    exception);
            }

            if (!string.Equals(step.Id, registration.Id, StringComparison.Ordinal) ||
                step.Priority != registration.Priority)
            {
                throw new InvalidOperationException(
                    $"Build step '{type.FullName}' registration metadata does not match its runtime Id/Priority contract.");
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

            IReadOnlyList<IBuildStep> discovered = BuildPipelineRegistry.ResolveSteps(context.Request.StepIds);
            var selected = new Dictionary<string, IBuildStep>(StringComparer.OrdinalIgnoreCase);
            var sourceOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < context.Request.StepIds.Count; index++)
            {
                string requestedId = context.Request.StepIds[index]?.Trim();
                if (string.IsNullOrEmpty(requestedId))
                {
                    throw new InvalidOperationException($"Configured build step at index {index} has an empty identifier.");
                }

                if (selected.ContainsKey(requestedId))
                {
                    throw new InvalidOperationException($"Build step '{requestedId}' is configured more than once.");
                }

                IBuildStep step = ResolveStep(discovered, requestedId);
                if (step == null)
                {
                    throw new InvalidOperationException($"No build step implementation is available for id '{requestedId}'.");
                }

                selected.Add(requestedId, step);
                sourceOrder.Add(requestedId, index);
            }

            if (selected.Count == 0)
            {
                throw new InvalidOperationException("The build plan does not contain any steps.");
            }

            var applicability = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < context.Request.StepIds.Count; index++)
            {
                string stepId = context.Request.StepIds[index].Trim();
                applicability[stepId] = selected[stepId].IsApplicable(context);
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

                IReadOnlyList<string> dependencies = step.GetRequiredStepIds(context) ?? Array.Empty<string>();
                var uniqueDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string dependencyId in dependencies)
                {
                    if (string.IsNullOrWhiteSpace(dependencyId) || !uniqueDependencies.Add(dependencyId))
                    {
                        throw new InvalidOperationException($"Build step '{step.Id}' declares an invalid or duplicate dependency.");
                    }

                    if (!selected.TryGetValue(dependencyId, out IBuildStep dependency))
                    {
                        throw new InvalidOperationException($"Build step '{step.Id}' requires missing step '{dependencyId}'.");
                    }

                    if (!applicability[dependencyId])
                    {
                        throw new InvalidOperationException($"Build step '{step.Id}' requires non-applicable step '{dependencyId}'.");
                    }

                    outgoing[dependencyId].Add(entry.Key);
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

            ready.Sort((left, right) => sourceOrder[left].CompareTo(sourceOrder[right]));
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
                        ready.Sort((left, right) => sourceOrder[left].CompareTo(sourceOrder[right]));
                    }
                }
            }

            if (orderedIds.Count != selected.Count)
            {
                string cycleIds = string.Join(", ", incomingCount.Where(entry => entry.Value > 0).Select(entry => entry.Key));
                throw new InvalidOperationException($"Build step dependency cycle detected: {cycleIds}.");
            }

            var validationErrors = new List<string>();
            int applicableCount = 0;
            foreach (string stepId in orderedIds)
            {
                IBuildStep step = selected[stepId];
                if (!applicability[stepId])
                {
                    continue;
                }

                applicableCount++;
                IReadOnlyList<string> errors = step.Validate(context) ?? Array.Empty<string>();
                foreach (string error in errors)
                {
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        validationErrors.Add($"[{step.Id}] {error}");
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

            return orderedIds
                .Select(stepId => new CompiledBuildStep(selected[stepId], applicability[stepId]))
                .ToArray();
        }

        private static IBuildStep ResolveStep(IReadOnlyList<IBuildStep> discovered, string requestedId)
        {
            IBuildStep winner = null;
            foreach (IBuildStep step in discovered)
            {
                if (!string.Equals(step.Id, requestedId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (winner == null || step.Priority > winner.Priority)
                {
                    winner = step;
                }
                else if (step.Priority == winner.Priority)
                {
                    throw new InvalidOperationException(
                        $"Build step id '{requestedId}' is provided by both '{winner.GetType().FullName}' and '{step.GetType().FullName}' at priority {step.Priority}.");
                }
            }

            return winner;
        }
    }
}
