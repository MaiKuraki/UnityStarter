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
        private static void ValidateHotUpdatePlayerBuildHooks(
            BuildExecutionContext context,
            BuildStepInvocation playerInvocation,
            ICollection<string> errors)
        {
            IReadOnlyList<BuildStepInvocation> hotUpdateInvocations =
                context.GetDependencyInvocations(
                    playerInvocation,
                    BuildStepTypeIds.HotUpdate);
            for (int index = 0; index < hotUpdateInvocations.Count; index++)
            {
                BuildStepInvocation invocation = hotUpdateInvocations[index];
                HotUpdateBuildConfiguration configuration =
                    invocation.GetConfiguration<HotUpdateBuildConfiguration>();
                if (configuration == null)
                {
                    continue;
                }

                IHotUpdateBuildAdapter adapter;
                try
                {
                    adapter = context.ResolveHotUpdateAdapter(invocation);
                }
                catch (Exception exception)
                {
                    errors.Add(
                        $"Hot-update Player compatibility resolution failed for '{invocation.InvocationId}': " +
                        exception.Message);
                    continue;
                }

                if (adapter == null)
                {
                    errors.Add(
                        $"No compatible '{configuration.ProviderId}' hot-update adapter is available for Player compatibility validation.");
                    continue;
                }

                if (!(adapter is IHotUpdatePlayerBuildValidator validator))
                {
                    continue;
                }

                try
                {
                    IReadOnlyList<string> providerErrors =
                        validator.ValidatePlayerBuild(
                            HotUpdateBuildStep.CreateRequest(
                                context,
                                invocation))
                        ?? Array.Empty<string>();
                    for (int errorIndex = 0;
                         errorIndex < providerErrors.Count;
                         errorIndex++)
                    {
                        if (!string.IsNullOrWhiteSpace(providerErrors[errorIndex]))
                        {
                            errors.Add(
                                $"Hot-update Player compatibility [{invocation.InvocationId}]: " +
                                providerErrors[errorIndex]);
                        }
                    }
                }
                catch (Exception exception)
                {
                    errors.Add(
                        $"Hot-update Player compatibility validation failed for '{invocation.InvocationId}': " +
                        exception.Message);
                }
            }
        }

        private static void ValidateAssetContentPlayerBuildHook(
            BuildExecutionContext context,
            BuildStepInvocation playerInvocation,
            ICollection<string> errors)
        {
            IReadOnlyList<BuildStepInvocation> contentInvocations =
                context.GetDependencyInvocations(
                    playerInvocation,
                    BuildStepTypeIds.AssetContent);
            var claims = new List<AssetContentPlayerSessionClaim>();
            for (int index = 0; index < contentInvocations.Count; index++)
            {
                BuildStepInvocation invocation = contentInvocations[index];
                AssetContentBuildConfiguration configuration =
                    invocation.GetConfiguration<AssetContentBuildConfiguration>();
                if (configuration == null)
                {
                    continue;
                }

                IAssetContentBuildAdapter adapter;
                try
                {
                    adapter = context.ResolveAssetContentAdapter(invocation);
                }
                catch (Exception exception)
                {
                    errors.Add(
                        $"Asset-content Player hook resolution failed for '{invocation.InvocationId}': " +
                        exception.Message);
                    continue;
                }

                if (adapter == null)
                {
                    errors.Add(
                        $"No compatible '{configuration.ProviderId}' content adapter is available " +
                        $"for Player dependency '{invocation.InvocationId}'.");
                    continue;
                }

                if (!(adapter is IAssetContentPlayerBuildSessionFactory sessionFactory))
                {
                    continue;
                }

                claims.Add(new AssetContentPlayerSessionClaim(
                    invocation.InvocationId,
                    sessionFactory));
                if (context.Version == null)
                {
                    continue;
                }

                try
                {
                    IReadOnlyList<string> hookErrors = sessionFactory.ValidatePlayerBuild(
                        CreateAssetContentRequest(context, invocation)) ?? Array.Empty<string>();
                    foreach (string error in hookErrors)
                    {
                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            errors.Add(
                                $"Asset-content Player hook [{invocation.InvocationId}]: {error}");
                        }
                    }
                }
                catch (Exception exception)
                {
                    errors.Add(
                        $"Asset-content Player hook validation failed for '{invocation.InvocationId}': " +
                        exception.Message);
                }
            }

            AddValidationErrors(
                errors,
                "Asset-content Player session exclusivity",
                AssetContentPlayerSessionPolicy.ValidateExclusiveClaims(
                    playerInvocation.InvocationId,
                    claims));
        }

        private static AssetContentBuildRequest CreateAssetContentRequest(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            AssetContentBuildConfiguration configuration =
                invocation.GetRequiredConfiguration<AssetContentBuildConfiguration>();
            return new AssetContentBuildRequest(
                invocation.InvocationId,
                context.Request.Target,
                context.Version?.PackageVersion ?? string.Empty,
                context.Request.ProjectRoot,
                configuration,
                invocation.Incrementality,
                context.Request.BatchMode,
                context.Request.Purpose,
                context.Request.ReplaceExactVersion);
        }

        private static IReadOnlyList<AssetContentBuildRequest>
            CreateAssetContentRequests(
                BuildExecutionContext context,
                IReadOnlyList<BuildStepInvocation> contentInvocations)
        {
            if (contentInvocations == null)
            {
                throw new ArgumentNullException(nameof(contentInvocations));
            }

            var requests = new AssetContentBuildRequest[contentInvocations.Count];
            for (int index = 0; index < contentInvocations.Count; index++)
            {
                requests[index] = CreateAssetContentRequest(
                    context,
                    contentInvocations[index]);
            }

            return requests;
        }

        private static IReadOnlyList<AssetContentPlayerSessionBinding>
            ResolveAssetContentPlayerSessionBindings(
                BuildExecutionContext context,
                IReadOnlyList<BuildStepInvocation> contentInvocations,
                IReadOnlyList<AssetContentBuildRequest> requests)
        {
            if (contentInvocations == null)
            {
                throw new ArgumentNullException(nameof(contentInvocations));
            }

            if (requests == null)
            {
                throw new ArgumentNullException(nameof(requests));
            }

            if (contentInvocations.Count != requests.Count)
            {
                throw new ArgumentException(
                    "Asset-content Player requests must match dependency invocations.",
                    nameof(requests));
            }

            var bindings = new List<AssetContentPlayerSessionBinding>();
            for (int index = 0; index < contentInvocations.Count; index++)
            {
                BuildStepInvocation invocation = contentInvocations[index];
                AssetContentBuildConfiguration configuration =
                    invocation.GetRequiredConfiguration<AssetContentBuildConfiguration>();
                IAssetContentBuildAdapter adapter =
                    context.ResolveAssetContentAdapter(invocation);
                if (adapter == null)
                {
                    throw new BuildFailedException(
                        $"No compatible '{configuration.ProviderId}' content adapter is available " +
                        $"for content invocation '{invocation.InvocationId}'.");
                }

                if (adapter is IAssetContentPlayerBuildSessionFactory factory)
                {
                    bindings.Add(new AssetContentPlayerSessionBinding(
                        invocation.InvocationId,
                        factory,
                        requests[index]));
                }
            }

            return bindings;
        }
    }
}

