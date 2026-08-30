using System.Collections.Generic;
using Build.Pipeline.Editor;
using UnityEngine;

using AtlasPipelineApi = CycloneGames.AtlasPipeline.AtlasPipeline;

namespace Build.Pipeline.Editor.Integrations.AtlasPipeline
{
    /// <summary>
    /// Pre-flight gate for the CycloneGames atlas tool: fails when the committed atlas manifest no
    /// longer describes the project, without generating or writing anything.
    /// Use it when the atlas output is not committed and each machine regenerates locally. In that
    /// setup the manifest is the only committed record of what the atlases should contain, so a stale
    /// manifest means art or rules changed and nobody regenerated and committed — which otherwise
    /// surfaces as missing sprites in a shipped player.
    /// Use <see cref="AtlasPipelineBuildStep"/> instead when the build job should generate the
    /// atlases itself.
    /// </summary>
    [BuildStepRegistration(
        "cyclonegames-atlas-pipeline-validate",
        DisplayName = "CycloneGames Atlas Pipeline (validate only)",
        Description =
            "Fails the build when the committed atlas manifest is stale. Generates nothing.",
        Category = "Atlas")]
    public sealed class AtlasPipelineValidateStep : IBuildStep
    {
        public string StepTypeId => "cyclonegames-atlas-pipeline-validate";

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
            var errors = new List<string>(AtlasPipelineApi.ValidateForBuild(includeNameScan: true));

            IReadOnlyList<string> drift = AtlasPipelineApi.ValidateManifestDrift();
            for (int i = 0; i < drift.Count; i++)
            {
                errors.Add("[atlas manifest] " + drift[i]);
            }

            return errors;
        }

        /// <summary>
        /// Intentionally generates nothing: this step exists to gate the build, not to produce
        /// atlases. It logs the remaining drift for diagnostics; the build has already failed in
        /// <see cref="Validate"/> if there was any.
        /// </summary>
        public void Execute(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            IReadOnlyList<string> drift = AtlasPipelineApi.ValidateManifestDrift();
            if (drift.Count == 0)
            {
                Debug.Log(
                    "[CycloneGames Atlas Pipeline] Atlas manifest matches the current project; "
                    + "no regeneration needed.");
                return;
            }

            for (int i = 0; i < drift.Count; i++)
            {
                Debug.Log("[CycloneGames Atlas Pipeline] " + drift[i]);
            }
        }
    }
}
