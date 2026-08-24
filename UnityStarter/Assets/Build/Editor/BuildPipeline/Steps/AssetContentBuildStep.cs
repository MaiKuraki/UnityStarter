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
    [BuildStepRegistration(
        BuildStepTypeIds.AssetContent,
        DisplayName = "Asset Content",
        Description = "Build the selected asset-content provider.",
        Category = "Content",
        ConfigurationType = typeof(AssetContentBuildConfiguration),
        ConfigurationRequired = true,
        Multiplicity = BuildStepMultiplicity.Multiple)]
    public sealed class AssetContentBuildStep : IBuildStep
    {
        public string StepTypeId => BuildStepTypeIds.AssetContent;

        public bool IsApplicable(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            return true;
        }

        public IReadOnlyList<string> Validate(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            var errors = new List<string>();
            AssetContentBuildConfiguration configuration =
                invocation.GetConfiguration<AssetContentBuildConfiguration>();
            if (configuration == null)
            {
                errors.Add(
                    "Asset Content requires an AssetContentBuildConfiguration asset.");
                return errors;
            }

            IAssetContentBuildAdapter adapter;
            try
            {
                adapter = context.ResolveAssetContentAdapter(invocation);
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
                return errors;
            }

            if (adapter == null)
            {
                errors.Add(
                    $"No compatible '{configuration.ProviderId}' content adapter is available. " +
                    "Install a supported version-gated integration or select another provider.");
                return errors;
            }

            if (context.Version == null)
            {
                errors.Add("Version context is unavailable.");
                return errors;
            }

            AssetContentBuildRequest adapterRequest = CreateAdapterRequest(
                context,
                invocation,
                configuration);
            AssetContentBuildResult validation = adapter.Validate(adapterRequest);
            if (validation == null || !validation.Succeeded)
            {
                errors.Add(validation?.ErrorInfo ?? "The content adapter returned no validation result.");
                return errors;
            }

            if (adapter is IAssetContentBuildOutputClaimProvider claimProvider)
            {
                try
                {
                    context.RegisterExclusiveOutputPaths(
                        invocation.InvocationId,
                        claimProvider.GetExclusiveOutputPaths(adapterRequest));
                }
                catch (Exception exception)
                {
                    errors.Add(
                        "Exclusive content output claim validation failed: " +
                        exception.Message);
                }
            }

            return errors;
        }

        public void Execute(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            AssetContentBuildConfiguration configuration =
                invocation.GetRequiredConfiguration<AssetContentBuildConfiguration>();
            IAssetContentBuildAdapter adapter = context.ResolveAssetContentAdapter(
                invocation);
            if (adapter == null)
            {
                throw new BuildFailedException(
                    $"No compatible '{configuration.ProviderId}' content adapter is available.");
            }

            AssetContentBuildOperation operation = adapter.Build(
                CreateAdapterRequest(context, invocation, configuration));
            if (operation == null)
            {
                throw new BuildFailedException(
                    $"{adapter.ProviderId} returned a null content build operation.");
            }

            IReadOnlyList<AssetContentBuildResult> results = operation.Results;
            if (results == null || results.Count == 0)
            {
                operation.Publication?.Dispose();
                throw new BuildFailedException($"{adapter.ProviderId} did not return any package build results.");
            }

            try
            {
                foreach (AssetContentBuildResult result in results)
                {
                    if (result == null)
                    {
                        throw new BuildFailedException(
                            $"{adapter.ProviderId} returned a null package result.");
                    }

                    context.AddContentResult(invocation.InvocationId, result);
                    if (!result.Succeeded)
                    {
                        throw new BuildFailedException(
                            $"{adapter.ProviderId} failed in '{result.FailedTask}': " +
                            $"{result.ErrorInfo}\n{result.ErrorStack}");
                    }
                }

                if (operation.Publication != null)
                {
                    context.RegisterDeferredPublication(operation.Publication);
                }
            }
            catch
            {
                operation.Publication?.Dispose();
                throw;
            }
        }

        private static AssetContentBuildRequest CreateAdapterRequest(
            BuildExecutionContext context,
            BuildStepInvocation invocation,
            AssetContentBuildConfiguration configuration)
        {
            return new AssetContentBuildRequest(
                invocation.InvocationId,
                context.Request.Target,
                context.Version.PackageVersion,
                context.Request.ProjectRoot,
                configuration,
                invocation.Incrementality,
                context.Request.BatchMode,
                context.Request.Purpose,
                context.Request.ReplaceExactVersion);
        }
    }
}

