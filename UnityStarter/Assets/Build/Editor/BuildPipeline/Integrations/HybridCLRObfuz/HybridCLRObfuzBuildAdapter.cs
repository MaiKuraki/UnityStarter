using System;
using System.Collections.Generic;

namespace Build.Pipeline.Editor
{
    [HotUpdateAdapterRegistration(
        HybridCLRHotUpdateProviderIds.Obfuz,
        typeof(HybridCLRObfuzBuildConfig))]
    public sealed class HybridCLRObfuzBuildAdapter : HybridCLRBuildAdapter
    {
        public override string ProviderId =>
            HybridCLRHotUpdateProviderIds.Obfuz;

        public override Type ConfigurationType =>
            typeof(HybridCLRObfuzBuildConfig);

        protected override void ValidateProvider(
            HotUpdateBuildRequest request,
            HybridCLRBuildConfig configuration,
            ICollection<string> errors)
        {
            base.ValidateProvider(request, configuration, errors);

            if (!ObfuzIntegrator.IsBaseObfuzAvailable()
                || !ObfuzIntegrator.IsHybridCLRObfuzAvailable())
            {
                errors.Add(
                    "The HybridCLR + Obfuz provider requires compatible HybridCLR, Obfuz, and Obfuz4HybridCLR packages.");
            }
            else if (!ObfuzIntegrator.VerifyEncryptionVMCompiled())
            {
                errors.Add(
                    "Obfuz Encryption VM is not compiled. Run provisioning before the build.");
            }

            if (request.Invocation.Incrementality ==
                BuildIncrementality.Incremental)
            {
                errors.Add(
                    "Incremental HybridCLR + Obfuz is unavailable because the installed Obfuz4HybridCLR API reads the implicit stripped-AOT directory. " +
                    "Use Clean, or install an integration that accepts the validated release-baseline AOT directory explicitly.");
            }
        }
    }
}
