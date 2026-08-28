using System.Collections.Generic;
using Build.Pipeline.Editor;
using UnityEditor;

using AtlasPipelineApi = CycloneGames.AtlasPipeline.AtlasPipeline;

namespace Build.Pipeline.Editor.Integrations.AtlasPipeline
{
    /// <summary>
    /// Build-pipeline integration for the CycloneGames atlas tool. It is discovered automatically by the
    /// Build module through TypeCache and can be added to a BuildData recipe as an invocation with
    /// no extra configuration asset.
    /// </summary>
    [BuildStepRegistration(
        "cyclonegames-atlas-pipeline",
        DisplayName = "CycloneGames Atlas Pipeline",
        Description = "Validates CycloneGames atlas import rules and regenerates affected SpriteAtlas assets before content is built.",
        Category = "Atlas")]
    public sealed class AtlasPipelineBuildStep : IBuildStep
    {
        public string StepTypeId => "cyclonegames-atlas-pipeline";

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
            return AtlasPipelineApi.ValidateForBuild(includeNameScan: true);
        }

        public void Execute(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            AtlasPipelineApi.RunForBuild(throwOnError: true);
        }
    }
}
