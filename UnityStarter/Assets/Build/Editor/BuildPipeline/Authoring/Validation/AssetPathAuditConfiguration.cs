using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Authoring for the <c>asset-path-audit</c> step. The step is optional because
    /// the scan covers a full asset tree, but the names it rejects are the ones that
    /// fail later and less legibly: inside a Player, an archive index, a CDN upload,
    /// or a case-sensitive CI checkout.
    ///
    /// <c>Assets/StreamingAssets</c> is always audited strictly and does not depend on
    /// this asset: Unity copies that tree verbatim into the Player, so the Player
    /// preflight rejects unportable names there whether or not this step is selected.
    /// </summary>
    [CreateAssetMenu(menuName = "CycloneGames/Build/Asset Path Audit Config")]
    public sealed class AssetPathAuditConfiguration : ScriptableObject
    {
        [Tooltip("Scan the whole Assets tree. Non-ASCII findings are warnings unless 'Fail On Project Asset Non-ASCII' is enabled.")]
        public bool auditProjectAssetTree = true;

        [Tooltip("Treat non-ASCII names inside the Assets tree as build errors. Leave disabled while adopting the check: Unity re-addresses imported assets, so the risk is portability (case-sensitive checkouts, bundle names, MAX_PATH) rather than an immediate runtime failure.")]
        public bool failOnNonAsciiInProjectAssetTree = false;

        [Tooltip("Reject entries whose composed path exceeds the Win32 MAX_PATH budget that the Unity toolchain and Windows build agents still enforce.")]
        public bool enforceWin32PathBudget = true;

        [Tooltip("Fail the build on any finding. Disable to publish the same findings as warnings while a project migrates. Assets/StreamingAssets names still fail the Player preflight.")]
        public bool failBuildOnViolation = true;

        [Tooltip("Total entry budget for one audit run. 0 uses the built-in default (200000). The audit fails when the budget is exhausted instead of passing on a partial scan, so raise this only when the asset tree legitimately exceeds it.")]
        public int maximumEntryCount = 0;

        [Tooltip("Extra project-relative content roots to audit strictly, for example 'ServerData' or 'Assets/StreamingAssets/Extra'. Each entry must resolve inside the Unity project.")]
        public string[] additionalRelativeRoots = new string[0];

        [Tooltip("Project-relative prefixes to skip, for example 'Assets/ThirdParty/VendorSamples'. Use for content that never reaches a Player. The StreamingAssets root itself cannot be excluded: the Player preflight enforces portable names there anyway, so excluding it here would only hide the failure. Exclude a subdirectory instead.")]
        public string[] excludedRelativePrefixes = new string[0];
    }
}
