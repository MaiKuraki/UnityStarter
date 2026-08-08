using System;
using System.Collections.Generic;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public static class ObfuzPlayerBuildExtensionIds
    {
        public const string Provider = "obfuz";
        public const string EnvironmentGuard = "obfuz-player-settings";
        public const string Compatibility = "obfuz-player-v1";
    }

    [CreateAssetMenu(menuName = "CycloneGames/Build/Player Extensions/Obfuz")]
    [PlayerBuildExtensionAuthoring(
        ObfuzPlayerBuildExtensionIds.Provider,
        DisplayName = "Obfuz",
        Description = "Uses the durable Obfuz Player pipeline and validates its generated Encryption VM.",
        RequiredEditorTypeName = "Obfuz.Settings.ObfuzSettings")]
    public sealed class ObfuzPlayerBuildExtensionConfiguration :
        PlayerBuildExtensionConfiguration
    {
        public override string ProviderId => ObfuzPlayerBuildExtensionIds.Provider;
    }

    [PlayerBuildExtensionAdapterRegistration(
        ObfuzPlayerBuildExtensionIds.Provider,
        ObfuzPlayerBuildExtensionIds.Compatibility,
        ConfigurationType = typeof(ObfuzPlayerBuildExtensionConfiguration))]
    [PlayerBuildEnvironmentGuardRegistration(
        ObfuzPlayerBuildExtensionIds.EnvironmentGuard)]
    public sealed class ObfuzPlayerBuildExtensionAdapter :
        IPlayerBuildExtensionAdapter,
        IPlayerBuildEnvironmentGuard
    {
        public string ProviderId => ObfuzPlayerBuildExtensionIds.Provider;
        public string CompatibilityId => ObfuzPlayerBuildExtensionIds.Compatibility;
        public string GuardId => ObfuzPlayerBuildExtensionIds.EnvironmentGuard;

        public IReadOnlyList<string> Validate(PlayerBuildExtensionRequest request)
        {
            if (request == null)
            {
                return new[] { "Obfuz Player extension request is required." };
            }

            if (!(request.Configuration is ObfuzPlayerBuildExtensionConfiguration))
            {
                return new[] { "ObfuzPlayerBuildExtensionConfiguration is required." };
            }

            var errors = new List<string>();
            if (!ObfuzIntegrator.IsBaseObfuzAvailable())
            {
                errors.Add(
                    "A compatible base Obfuz package is unavailable. Install Obfuz or remove the Obfuz Player extension.");
                return errors;
            }

            if (!ObfuzIntegrator.TryGetObfuzBuildPipelineEnabled(out bool enabled))
            {
                errors.Add(
                    "Obfuz settings are unavailable or incomplete. Provision and save ProjectSettings/Obfuz.asset before building.");
            }
            else if (!enabled)
            {
                errors.Add(
                    "The Obfuz Player extension is selected, but the durable Obfuz Player pipeline setting is disabled.");
            }

            if (!ObfuzIntegrator.VerifyEncryptionVMCompiled())
            {
                errors.Add(
                    "Obfuz Encryption VM is not compiled. Run Obfuz provisioning before building.");
            }

            return errors;
        }

        public IDisposable BeginPlayerBuild(PlayerBuildExtensionRequest request)
        {
            IReadOnlyList<string> errors = Validate(request);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Obfuz Player extension preflight changed before BuildPlayer: " +
                    string.Join("; ", errors));
            }

            return null;
        }

        public IReadOnlyList<string> ValidateEnvironment(
            PlayerBuildEnvironmentRequest request)
        {
            if (request == null)
            {
                return new[] { "Player build environment request is required." };
            }

            bool selected = request.HasPlayerExtension(ProviderId);
            if (!ObfuzIntegrator.IsBaseObfuzAvailable())
            {
                return Array.Empty<string>();
            }

            if (!ObfuzIntegrator.TryGetObfuzBuildPipelineEnabled(out bool enabled))
            {
                return new[]
                {
                    "Obfuz is installed, but its durable Player pipeline setting cannot be read. Provision and save ProjectSettings/Obfuz.asset before building."
                };
            }

            if (enabled == selected)
            {
                return Array.Empty<string>();
            }

            return new[]
            {
                enabled
                    ? "The durable Obfuz Player pipeline is enabled, but the Player configuration does not select the Obfuz extension. Add the extension or disable and save the Obfuz setting."
                    : "The Player configuration selects Obfuz, but the durable Obfuz Player pipeline is disabled. Enable and save the Obfuz setting."
            };
        }

        public IDisposable BeginEnvironment(PlayerBuildEnvironmentRequest request)
        {
            IReadOnlyList<string> errors = ValidateEnvironment(request);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Obfuz Player environment changed after preflight: " +
                    string.Join("; ", errors));
            }

            return null;
        }
    }
}
