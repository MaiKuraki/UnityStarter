using System.Collections.Generic;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Zero-write preflight that rejects file and folder names a supported Player
    /// target, archive index, CDN host, or case-sensitive checkout cannot carry.
    ///
    /// The step is optional because it walks a whole asset tree. The subset that is
    /// never optional — <c>Assets/StreamingAssets</c>, which Unity copies verbatim
    /// into the Player — is enforced by <see cref="PlayerBuildStep"/> instead, so a
    /// project that never selects this step still cannot ship an unportable
    /// StreamingAssets name.
    ///
    /// The step owns only two things: mapping the authoring asset onto an
    /// <see cref="AssetPathAuditPlanRequest"/>, and reporting the result. Scope
    /// construction lives in <see cref="AssetPathAuditPlan"/> so the wiring can be
    /// tested without an Editor, and scanning lives in
    /// <see cref="PortableAssetPathAudit"/>.
    /// </summary>
    [BuildStepRegistration(
        AssetPathAuditBuildStep.StepTypeIdValue,
        DisplayName = "Asset Path Audit",
        Description =
            "Scan asset paths for names that break supported Player targets: non-ASCII, illegal or reserved names, and paths over the Win32 MAX_PATH budget.",
        Category = "Validation",
        ConfigurationType = typeof(AssetPathAuditConfiguration),
        ConfigurationRequired = false)]
    public sealed class AssetPathAuditBuildStep : IBuildStep
    {
        public const string StepTypeIdValue = "asset-path-audit";

        public string StepTypeId => StepTypeIdValue;

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
            PortableAssetPathAuditResult result = RunAudit(
                context.Request.ProjectRoot,
                invocation.GetConfiguration<AssetPathAuditConfiguration>(),
                errors);
            for (int index = 0; index < result.ErrorMessages.Count; index++)
            {
                errors.Add(result.ErrorMessages[index]);
            }

            // Coverage loss fails the build. The audit deliberately reports rather
            // than throws, but a gate that passes on a partial scan is worse than no
            // gate: it certifies content it never looked at.
            string coverageError = result.DescribeIncompleteCoverage(
                nameof(AssetPathAuditConfiguration.maximumEntryCount));
            if (!string.IsNullOrEmpty(coverageError))
            {
                errors.Add(coverageError);
            }

            LogSummaryAndWarnings(result);
            return errors;
        }

        /// <summary>
        /// Reports only. The audit exists to gate the build, and re-running it here
        /// keeps the step write-free and idempotent; findings cannot change between
        /// preflight and execution because the step itself never mutates the tree.
        /// </summary>
        public void Execute(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            PortableAssetPathAuditResult result = RunAudit(
                context.Request.ProjectRoot,
                invocation.GetConfiguration<AssetPathAuditConfiguration>(),
                configurationErrors: null);
            LogSummaryAndWarnings(result);
        }

        internal static PortableAssetPathAuditResult RunAudit(
            string projectRoot,
            AssetPathAuditConfiguration configuration,
            ICollection<string> configurationErrors)
        {
            return RunAudit(
                CreateRequest(projectRoot, configuration),
                configurationErrors);
        }

        internal static PortableAssetPathAuditResult RunAudit(
            AssetPathAuditPlanRequest request,
            ICollection<string> configurationErrors)
        {
            IReadOnlyList<PortableAssetPathAuditScope> scopes =
                AssetPathAuditPlan.CreateScopes(request, configurationErrors);
            return request.MaximumEntryCount > 0
                ? PortableAssetPathAudit.Audit(
                    request.ProjectRoot,
                    scopes,
                    maximumEntryCount: request.MaximumEntryCount)
                : PortableAssetPathAudit.Audit(request.ProjectRoot, scopes);
        }

        private static AssetPathAuditPlanRequest CreateRequest(
            string projectRoot,
            AssetPathAuditConfiguration configuration)
        {
            if (configuration == null)
            {
                return new AssetPathAuditPlanRequest(projectRoot);
            }

            return new AssetPathAuditPlanRequest(
                projectRoot,
                auditProjectAssetTree: configuration.auditProjectAssetTree,
                nonAsciiIsErrorInProjectAssetTree: configuration.failOnNonAsciiInProjectAssetTree,
                enforceWin32PathBudget: configuration.enforceWin32PathBudget,
                findingsAreErrors: configuration.failBuildOnViolation,
                additionalRelativeRoots: configuration.additionalRelativeRoots,
                excludedRelativePrefixes: configuration.excludedRelativePrefixes,
                maximumEntryCount: configuration.maximumEntryCount);
        }

        private static void LogSummaryAndWarnings(PortableAssetPathAuditResult result)
        {
            Debug.Log($"[Asset Path Audit] {result.DescribeSummary()}");
            for (int index = 0; index < result.WarningMessages.Count; index++)
            {
                Debug.LogWarning("[Asset Path Audit] " + result.WarningMessages[index]);
            }
        }
    }
}
