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
        BuildStepTypeIds.HotUpdate,
        DisplayName = "Hot Update",
        Description = "Build hot-update code through the provider selected by the configuration asset.",
        Category = "Compilation",
        ConfigurationType = typeof(HotUpdateBuildConfiguration),
        ConfigurationRequired = true,
        Multiplicity = BuildStepMultiplicity.Multiple)]
    public sealed class HotUpdateBuildStep : IBuildStep, IBuildStepRequirementsProvider
    {
        public string StepTypeId => BuildStepTypeIds.HotUpdate;

        public BuildStepRequirements GetRequirements(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            IHotUpdateBuildAdapter adapter = RequireAdapter(context, invocation);
            return adapter.GetRequirements(CreateRequest(context, invocation));
        }

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
            HotUpdateBuildConfiguration configuration =
                invocation.GetConfiguration<HotUpdateBuildConfiguration>();
            if (configuration == null)
            {
                errors.Add(
                    "Hot Update requires a HotUpdateBuildConfiguration asset.");
                return errors;
            }

            string providerId = configuration.ProviderId?.Trim();
            if (string.IsNullOrWhiteSpace(providerId))
            {
                errors.Add(
                    $"Hot-update invocation '{invocation.InvocationId}' returned an empty provider id.");
                return errors;
            }

            try
            {
                BuildIdentityPolicy.ValidateBuildIdentifier(
                    providerId,
                    "Hot-update provider identifier");
            }
            catch (ArgumentException exception)
            {
                errors.Add(exception.Message);
                return errors;
            }

            IHotUpdateBuildAdapter adapter;
            try
            {
                adapter = context.ResolveHotUpdateAdapter(invocation);
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
                return errors;
            }

            if (adapter == null)
            {
                errors.Add(
                    $"No compatible '{providerId}' hot-update adapter is available. " +
                    "Install a supported integration or select another provider.");
                return errors;
            }

            try
            {
                IReadOnlyList<string> providerErrors =
                    adapter.Validate(CreateRequest(context, invocation))
                    ?? Array.Empty<string>();
                for (int index = 0; index < providerErrors.Count; index++)
                {
                    if (!string.IsNullOrWhiteSpace(providerErrors[index]))
                    {
                        errors.Add(providerErrors[index]);
                    }
                }
            }
            catch (Exception exception)
            {
                errors.Add(
                    $"Hot-update provider '{providerId}' validation failed: {exception.Message}");
            }

            return errors;
        }

        public void Execute(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            IHotUpdateBuildAdapter adapter = RequireAdapter(context, invocation);
            adapter.Execute(CreateRequest(context, invocation));
        }

        private static IHotUpdateBuildAdapter RequireAdapter(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            IHotUpdateBuildAdapter adapter =
                context.ResolveHotUpdateAdapter(invocation);
            if (adapter != null)
            {
                return adapter;
            }

            HotUpdateBuildConfiguration configuration =
                invocation.GetRequiredConfiguration<HotUpdateBuildConfiguration>();
            throw new BuildFailedException(
                $"No compatible '{configuration.ProviderId}' hot-update adapter is available.");
        }

        internal static HotUpdateBuildRequest CreateRequest(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            return new HotUpdateBuildRequest(
                context,
                invocation,
                invocation.GetRequiredConfiguration<HotUpdateBuildConfiguration>());
        }
    }
}

