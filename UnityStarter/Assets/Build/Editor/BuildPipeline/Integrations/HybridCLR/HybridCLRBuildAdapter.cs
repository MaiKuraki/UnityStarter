using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [HotUpdateAdapterRegistration(
        HybridCLRHotUpdateProviderIds.Standard,
        typeof(HybridCLRBuildConfig))]
    public class HybridCLRBuildAdapter :
        IHotUpdateBuildAdapter,
        IHotUpdatePlayerBuildValidator
    {
        public virtual string ProviderId => HybridCLRHotUpdateProviderIds.Standard;
        public virtual Type ConfigurationType => typeof(HybridCLRBuildConfig);

        public BuildStepRequirements GetRequirements(HotUpdateBuildRequest request)
        {
            RequireConfiguration(request);
            return BuildStepRequirements.UnityGlobalState;
        }

        public IReadOnlyList<string> Validate(HotUpdateBuildRequest request)
        {
            var errors = new List<string>();
            HybridCLRBuildConfig config;
            try
            {
                config = RequireConfiguration(request);
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
                return errors;
            }

            ValidateSingleHybridCLRInvocation(request, errors);

            if (request.Context.Request.ScriptingBackend !=
                ScriptingImplementation.IL2CPP)
            {
                errors.Add(
                    "HybridCLR requires the IL2CPP scripting backend. " +
                    "The integration never allows the package to rewrite the backend during a nested Player build.");
            }

            string supportFailure = HybridCLRVersionSupport.ValidateSupport(
                request.Invocation.Incrementality);
            if (supportFailure != null)
            {
                errors.Add(
                    "HybridCLR is not installed or its supported editor API is unavailable: " +
                    supportFailure);
            }

            if (config.GetHotUpdateAssemblyNames().Count == 0)
            {
                errors.Add(
                    "HybridCLRBuildConfig must contain at least one hot-update assembly.");
            }

            string hotUpdateOutput = null;
            if (string.IsNullOrWhiteSpace(
                    config.GetHotUpdateDllOutputDirectoryPath()))
            {
                errors.Add(
                    "HybridCLRBuildConfig must define a Hot-Update DLL output directory.");
            }
            else
            {
                hotUpdateOutput = ValidateGeneratedOutput(
                    request.Context.Request.ProjectRoot,
                    config.GetHotUpdateDllOutputDirectoryPath(),
                    "Hot-update DLL",
                    errors);
            }

            if (string.IsNullOrWhiteSpace(config.GetAOTDllOutputDirectoryPath()))
            {
                errors.Add(
                    "HybridCLRBuildConfig must define an AOT DLL output directory.");
            }

            string aotOutput = ValidateGeneratedOutput(
                request.Context.Request.ProjectRoot,
                config.GetAOTDllOutputDirectoryPath(),
                "AOT DLL",
                errors);

            EnsureDistinctGeneratedOutputs(hotUpdateOutput, aotOutput, errors);
            if (hotUpdateOutput != null && aotOutput != null)
            {
                try
                {
                    request.Context.RegisterExclusiveOutputPaths(
                        request.Invocation.InvocationId,
                        new[] { hotUpdateOutput, aotOutput });
                }
                catch (Exception exception)
                {
                    errors.Add(
                        "HybridCLR exclusive output claim validation failed: " +
                        exception.Message);
                }

                try
                {
                    HybridCLRBuilder.ValidateManagedOutputOwnership(
                        config,
                        request.Context.Request.ProjectRoot);
                }
                catch (Exception exception)
                {
                    errors.Add(
                        "HybridCLR generated-output ownership validation failed: " +
                        exception.Message);
                }
            }

            ValidateProvider(request, config, errors);

            if (errors.Count == 0)
            {
                try
                {
                    HybridCLRReleaseBaselineTransaction.EnsureNoPendingRecovery(
                        request.Context.Request.ProjectRoot);
                    if (request.Invocation.Incrementality ==
                        BuildIncrementality.Incremental)
                    {
                        HybridCLRReleaseBaselineExpectation expectation =
                            HybridCLRReleaseBaselineStore.CreateExpectation(
                                request.Context,
                                request.Invocation,
                                config);
                        HybridCLRReleaseBaselineStore.ValidateAndResolve(expectation);
                    }
                    else if (HybridCLRReleaseBaselineEligibility
                             .TryGetExplicitReleasePlayerConsumer(
                                 request.Context,
                                 request.Invocation,
                                 out _,
                                 out _))
                    {
                        HybridCLRReleaseBaselineExpectation expectation =
                            HybridCLRReleaseBaselineStore.CreateExpectation(
                                request.Context,
                                request.Invocation,
                                config);
                        HybridCLRReleaseBaselineStore.ValidateForReplacement(
                            expectation);
                    }
                }
                catch (Exception exception)
                {
                    errors.Add(
                        "HybridCLR release-baseline preflight failed: " +
                        exception.Message);
                }
            }

            return errors;
        }

        public IReadOnlyList<string> ValidatePlayerBuild(
            HotUpdateBuildRequest request)
        {
            RequireConfiguration(request);
            if (!request.Context.Request.CheatEnabled)
            {
                return Array.Empty<string>();
            }

            return new[]
            {
                "HybridCLR and per-build ENABLE_CHEAT cannot currently be combined safely for a Player build: " +
                "the installed HybridCLR compilation API does not accept the Player's extra scripting defines. " +
                "Disable Cheat or select a provider integration that explicitly supports invocation-local defines."
            };
        }

        public void Execute(HotUpdateBuildRequest request)
        {
            HybridCLRBuildConfig configuration = RequireConfiguration(request);
            IBuildDownstreamInputPublication publication = null;
            IBuildDeferredPublication baselinePublication = null;
            try
            {
                if (request.Invocation.Incrementality ==
                    BuildIncrementality.Incremental)
                {
                    HybridCLRReleaseBaselineExpectation expectation =
                        HybridCLRReleaseBaselineStore.CreateExpectation(
                            request.Context,
                            request.Invocation,
                            configuration);
                    HybridCLRReleaseBaseline baseline =
                        HybridCLRReleaseBaselineStore.ValidateAndResolve(
                            expectation);
                    publication = HybridCLRBuilder.CompileDllAndCopy(
                        request.Context.Request.Target,
                        configuration,
                        baseline);
                }
                else
                {
                    HybridCLRReleaseBaselineExpectation expectation = null;
                    string playerInvocationId = null;
                    if (HybridCLRReleaseBaselineEligibility
                        .TryGetExplicitReleasePlayerConsumer(
                            request.Context,
                            request.Invocation,
                            out string consumerInvocationId,
                            out string ineligibleReason))
                    {
                        expectation =
                            HybridCLRReleaseBaselineStore.CreateExpectation(
                                request.Context,
                                request.Invocation,
                                configuration);
                        playerInvocationId = consumerInvocationId;
                    }
                    else
                    {
                        Debug.Log(
                            $"[BuildPipeline] Clean HybridCLR invocation '{request.Invocation.InvocationId}' " +
                            $"will not publish a release baseline. {ineligibleReason}");
                    }

                    publication = HybridCLRBuilder.GenerateAllAndCopy(
                        request.Context.Request.Target,
                        configuration,
                        expectation,
                        playerInvocationId,
                        request.Context.Version,
                        out baselinePublication);
                }

                IBuildDownstreamInputPublication ownedPublication = publication;
                request.Context.RegisterDeferredPublication(ownedPublication);
                publication = null;
                if (baselinePublication != null)
                {
                    IBuildDeferredPublication ownedBaselinePublication =
                        baselinePublication;
                    request.Context.RegisterDeferredPublication(
                        ownedBaselinePublication);
                    baselinePublication = null;
                }

                ownedPublication.ActivateForDownstream();
                VerifyOutputs(request.Context.Request, configuration);
            }
            finally
            {
                baselinePublication?.Dispose();
                publication?.Dispose();
            }
        }

        protected virtual void ValidateProvider(
            HotUpdateBuildRequest request,
            HybridCLRBuildConfig configuration,
            ICollection<string> errors)
        {
        }

        private HybridCLRBuildConfig RequireConfiguration(
            HotUpdateBuildRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!(request.Configuration is HybridCLRBuildConfig configuration)
                || configuration.GetType() != ConfigurationType)
            {
                throw new InvalidOperationException(
                    $"Hot-update provider '{ProviderId}' requires a {ConfigurationType.Name} configuration asset.");
            }

            if (!string.Equals(
                    configuration.ProviderId,
                    ProviderId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Hot-update configuration provider '{configuration.ProviderId}' does not match adapter '{ProviderId}'.");
            }

            return configuration;
        }

        private static void ValidateSingleHybridCLRInvocation(
            HotUpdateBuildRequest request,
            ICollection<string> errors)
        {
            IReadOnlyList<BuildStepInvocation> invocations =
                request.Context.Request.GetInvocationsByStepType(
                    BuildStepTypeIds.HotUpdate);
            var hybridInvocationIds = new List<string>();
            for (int index = 0; index < invocations.Count; index++)
            {
                if (invocations[index].Configuration is HybridCLRBuildConfig)
                {
                    hybridInvocationIds.Add(invocations[index].InvocationId);
                }
            }

            if (hybridInvocationIds.Count > 1)
            {
                errors.Add(
                    "HybridCLR editor generation APIs use process-global output and support one invocation per run. " +
                    $"Conflicting invocations: [{string.Join(", ", hybridInvocationIds)}].");
            }
        }

        private static void VerifyOutputs(
            BuildRequest request,
            HybridCLRBuildConfig config)
        {
            string hotUpdateDirectory = ResolveProjectAssetPath(
                request.ProjectRoot,
                config.GetHotUpdateDllOutputDirectoryPath());
            var missing = new List<string>();
            foreach (string assemblyName in config.GetHotUpdateAssemblyNames())
            {
                string path = Path.Combine(
                    hotUpdateDirectory,
                    assemblyName + ".dll.bytes");
                if (!File.Exists(path))
                {
                    missing.Add(path);
                }
            }

            string listPath = Path.Combine(hotUpdateDirectory, "HotUpdate.bytes");
            if (!File.Exists(listPath))
            {
                missing.Add(listPath);
            }

            string aotDirectory = ResolveProjectAssetPath(
                request.ProjectRoot,
                config.GetAOTDllOutputDirectoryPath());
            string aotListPath = Path.Combine(aotDirectory, "AOT.bytes");
            if (!File.Exists(aotListPath))
            {
                missing.Add(aotListPath);
            }

            if (missing.Count > 0)
            {
                throw new BuildFailedException(
                    "HybridCLR output verification failed:\n" +
                    string.Join("\n", missing));
            }

            HybridCLRBuilder.ValidateManagedOutputOwnership(
                config,
                request.ProjectRoot);
        }

        private static string ResolveProjectAssetPath(
            string projectRoot,
            string assetPath)
        {
            try
            {
                return BuildPathPolicy.ResolveGeneratedAssetsDirectory(
                    projectRoot,
                    assetPath);
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    $"HybridCLR output must be a safe project-relative Assets directory: '{assetPath}'. {exception.Message}");
            }
        }

        private static string ValidateGeneratedOutput(
            string projectRoot,
            string assetPath,
            string label,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            try
            {
                return BuildPathPolicy.ResolveGeneratedAssetsDirectory(
                    projectRoot,
                    assetPath);
            }
            catch (Exception exception)
            {
                errors.Add($"{label} output is unsafe: {exception.Message}");
                return null;
            }
        }

        private static void EnsureDistinctGeneratedOutputs(
            string hotUpdateOutput,
            string aotOutput,
            ICollection<string> errors)
        {
            if (!string.IsNullOrEmpty(hotUpdateOutput)
                && !string.IsNullOrEmpty(aotOutput)
                && string.Equals(
                    hotUpdateOutput,
                    aotOutput,
                    StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Hot-update DLL and AOT DLL outputs must use different directories: '{hotUpdateOutput}'.");
            }
        }
    }
}
