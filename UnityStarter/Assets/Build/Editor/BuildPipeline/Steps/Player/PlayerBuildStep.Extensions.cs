using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public sealed partial class PlayerBuildStep
    {
        private static void ValidatePlayerBuildExtensions(
            BuildExecutionContext context,
            BuildStepInvocation playerInvocation,
            ICollection<string> errors)
        {
            IReadOnlyList<PlayerExtensionBinding> bindings;
            IReadOnlyList<IPlayerBuildEnvironmentGuard> guards;
            PlayerBuildEnvironmentRequest environmentRequest;
            try
            {
                bindings = ResolvePlayerExtensionBindings(context, playerInvocation);
                guards = PlayerBuildExtensionRegistry.ResolveEnvironmentGuards();
                IReadOnlyList<BuildStepInvocation> contentInvocations =
                    context.GetDependencyInvocations(
                        playerInvocation,
                        BuildStepTypeIds.AssetContent);
                environmentRequest = CreatePlayerEnvironmentRequest(
                    context,
                    playerInvocation,
                    CreateAssetContentRequests(context, contentInvocations),
                    bindings);
                string fingerprint = PlayerBuildExtensionFingerprint.Compute(
                    playerInvocation.GetConfiguration<PlayerBuildConfiguration>());
                context.SetPlayerExtensionFingerprint(fingerprint);
            }
            catch (Exception exception)
            {
                errors.Add("Player extension resolution failed: " + exception.Message);
                return;
            }

            for (int index = 0; index < bindings.Count; index++)
            {
                PlayerExtensionBinding binding = bindings[index];
                try
                {
                    IReadOnlyList<string> validationErrors =
                        binding.Adapter.Validate(binding.Request)
                        ?? Array.Empty<string>();
                    AddValidationErrors(
                        errors,
                        $"Player extension [{binding.Request.ProviderId}]",
                        validationErrors);
                }
                catch (Exception exception)
                {
                    errors.Add(
                        $"Player extension [{binding.Request.ProviderId}] validation failed: " +
                        exception.Message);
                }
            }

            for (int index = 0; index < guards.Count; index++)
            {
                IPlayerBuildEnvironmentGuard guard = guards[index];
                try
                {
                    IReadOnlyList<string> validationErrors =
                        guard.ValidateEnvironment(environmentRequest)
                        ?? Array.Empty<string>();
                    AddValidationErrors(
                        errors,
                        $"Player environment [{guard.GuardId}]",
                        validationErrors);
                }
                catch (Exception exception)
                {
                    errors.Add(
                        $"Player environment [{guard.GuardId}] validation failed: " +
                        exception.Message);
                }
            }
        }

        private static IReadOnlyList<PlayerExtensionBinding> ResolvePlayerExtensionBindings(
            BuildExecutionContext context,
            BuildStepInvocation playerInvocation)
        {
            PlayerBuildConfiguration configuration =
                playerInvocation.GetConfiguration<PlayerBuildConfiguration>();
            if (configuration == null)
            {
                return Array.Empty<PlayerExtensionBinding>();
            }

            IReadOnlyList<PlayerBuildExtensionConfiguration> extensions =
                configuration.Extensions;
            if (extensions.Count > PlayerBuildExtensionFingerprint.MaximumExtensionCount)
            {
                throw new InvalidOperationException(
                    $"A Player build may select at most {PlayerBuildExtensionFingerprint.MaximumExtensionCount} extensions.");
            }

            var bindings = new List<PlayerExtensionBinding>(extensions.Count);
            var providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < extensions.Count; index++)
            {
                PlayerBuildExtensionConfiguration extension = extensions[index]
                    ?? throw new InvalidOperationException(
                        $"Player extension entry {index} is empty.");
                string providerId = extension.ProviderId?.Trim();
                if (string.IsNullOrWhiteSpace(providerId))
                {
                    throw new InvalidOperationException(
                        $"Player extension entry {index} returned an empty provider id.");
                }

                if (!providerIds.Add(providerId))
                {
                    throw new InvalidOperationException(
                        $"Player extension provider '{providerId}' is configured more than once.");
                }

                IPlayerBuildExtensionAdapter adapter =
                    PlayerBuildExtensionRegistry.ResolveAdapter(extension);
                if (adapter == null)
                {
                    throw new InvalidOperationException(
                        $"No Player extension adapter is registered for provider '{providerId}'.");
                }

                bindings.Add(new PlayerExtensionBinding(
                    adapter,
                    new PlayerBuildExtensionRequest(
                        context.Request,
                        playerInvocation,
                        extension)));
            }

            return bindings;
        }

        private static PlayerBuildEnvironmentRequest CreatePlayerEnvironmentRequest(
            BuildExecutionContext context,
            BuildStepInvocation playerInvocation,
            IReadOnlyList<AssetContentBuildRequest> assetContentRequests,
            IReadOnlyList<PlayerExtensionBinding> bindings)
        {
            var extensionRequests = new PlayerBuildExtensionRequest[bindings.Count];
            for (int index = 0; index < bindings.Count; index++)
            {
                extensionRequests[index] = bindings[index].Request;
            }

            return new PlayerBuildEnvironmentRequest(
                context.Request,
                playerInvocation,
                assetContentRequests,
                extensionRequests);
        }

        private static void AddValidationErrors(
            ICollection<string> destination,
            string prefix,
            IReadOnlyList<string> errors)
        {
            for (int index = 0; index < errors.Count; index++)
            {
                if (!string.IsNullOrWhiteSpace(errors[index]))
                {
                    destination.Add(prefix + ": " + errors[index]);
                }
            }
        }

        private sealed class PlayerExtensionBinding
        {
            internal PlayerExtensionBinding(
                IPlayerBuildExtensionAdapter adapter,
                PlayerBuildExtensionRequest request)
            {
                Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
                Request = request ?? throw new ArgumentNullException(nameof(request));
            }

            internal IPlayerBuildExtensionAdapter Adapter { get; }
            internal PlayerBuildExtensionRequest Request { get; }
        }

        private sealed class AssetContentPlayerSessionBinding
        {
            internal AssetContentPlayerSessionBinding(
                string invocationId,
                IAssetContentPlayerBuildSessionFactory factory,
                AssetContentBuildRequest request)
            {
                Factory = factory ?? throw new ArgumentNullException(nameof(factory));
                Request = request ?? throw new ArgumentNullException(nameof(request));
                Claim = new AssetContentPlayerSessionClaim(invocationId, factory);
            }

            internal IAssetContentPlayerBuildSessionFactory Factory { get; }
            internal AssetContentBuildRequest Request { get; }
            internal AssetContentPlayerSessionClaim Claim { get; }
        }
    }
}
