using System;
using System.Collections.Generic;
using System.IO;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Plain-value description of one asset-path audit run. Deliberately free of
    /// UnityEngine types so the scope wiring — not just the scan rules — can be
    /// exercised outside the Editor. The authoring asset is a thin adapter over this.
    /// </summary>
    public sealed class AssetPathAuditPlanRequest
    {
        public AssetPathAuditPlanRequest(
            string projectRoot,
            bool auditProjectAssetTree = true,
            bool nonAsciiIsErrorInProjectAssetTree = false,
            bool enforceWin32PathBudget = true,
            bool findingsAreErrors = true,
            IReadOnlyList<string> additionalRelativeRoots = null,
            IReadOnlyList<string> excludedRelativePrefixes = null,
            int maximumEntryCount = 0)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("Project root is required.", nameof(projectRoot));
            }

            if (maximumEntryCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumEntryCount),
                    maximumEntryCount,
                    "The entry budget must be zero (use the default) or positive.");
            }

            ProjectRoot = projectRoot;
            AuditProjectAssetTree = auditProjectAssetTree;
            NonAsciiIsErrorInProjectAssetTree = nonAsciiIsErrorInProjectAssetTree;
            EnforceWin32PathBudget = enforceWin32PathBudget;
            FindingsAreErrors = findingsAreErrors;
            AdditionalRelativeRoots = additionalRelativeRoots;
            ExcludedRelativePrefixes = excludedRelativePrefixes;
            MaximumEntryCount = maximumEntryCount;
        }

        public string ProjectRoot { get; }

        public bool AuditProjectAssetTree { get; }

        public bool NonAsciiIsErrorInProjectAssetTree { get; }

        public bool EnforceWin32PathBudget { get; }

        public bool FindingsAreErrors { get; }

        /// <summary>
        /// Total entry budget for the whole run, across every scope. Zero means use
        /// <see cref="PortableAssetPathAudit.DefaultMaximumEntryCount"/>. A project whose
        /// asset tree legitimately exceeds the default raises this deliberately; the
        /// audit then fails on exhaustion, because a gate that passes on a partial scan
        /// is not a gate.
        /// </summary>
        public int MaximumEntryCount { get; }

        public IReadOnlyList<string> AdditionalRelativeRoots { get; }

        public IReadOnlyList<string> ExcludedRelativePrefixes { get; }
    }

    /// <summary>
    /// Turns an <see cref="AssetPathAuditPlanRequest"/> into the audit scopes to scan.
    ///
    /// This exists as its own layer because the scope wiring is where a check silently
    /// loses coverage: an exclusion expressed against the project cannot be shared
    /// between a broad scope and a strict scope nested inside it, because excluding the
    /// nested tree from the broad scope also removes the strict scope's own root. The
    /// plan therefore translates every project-relative exclusion into each scope's own
    /// coordinate space and never lets one scope's exclusion reach another scope.
    /// </summary>
    public static class AssetPathAuditPlan
    {
        public const string ProjectAssetTreeScopeName = "Assets";
        public const string StreamingAssetsScopeName = "StreamingAssets";

        /// <summary>
        /// Project-relative location of the tree Unity copies verbatim into the Player.
        /// </summary>
        public const string StreamingAssetsProjectRelativePath = "Assets/StreamingAssets";

        /// <summary>
        /// The broad asset-tree scope always skips this child: StreamingAssets is scanned
        /// by its own strict scope, and one name must never be reported twice with two
        /// different verdicts.
        /// </summary>
        private const string StreamingAssetsScopeRelativePrefix = "StreamingAssets";

        public static IReadOnlyList<PortableAssetPathAuditScope> CreateScopes(
            AssetPathAuditPlanRequest request,
            ICollection<string> configurationErrors)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string project = Path.GetFullPath(request.ProjectRoot);
            IReadOnlyList<string> exclusions = NormalizeProjectRelativePrefixes(
                request.ExcludedRelativePrefixes);
            var scopes = new List<PortableAssetPathAuditScope>();

            if (request.AuditProjectAssetTree)
            {
                var treeExclusions = new List<string> { StreamingAssetsScopeRelativePrefix };
                AppendExclusionsBelow(treeExclusions, exclusions, "Assets");
                scopes.Add(new PortableAssetPathAuditScope(
                    ProjectAssetTreeScopeName,
                    Path.Combine(project, "Assets"),
                    rejectNonAscii: true,
                    enforceWin32PathBudget: request.EnforceWin32PathBudget,
                    isError: request.FindingsAreErrors,
                    nonAsciiIsError: request.NonAsciiIsErrorInProjectAssetTree,
                    excludedRelativePrefixes: treeExclusions));
            }

            AddStreamingAssetsScope(project, request, exclusions, scopes, configurationErrors);
            AddAdditionalRootScopes(project, request, exclusions, scopes, configurationErrors);

            return scopes.AsReadOnly();
        }

        /// <summary>
        /// StreamingAssets is always scanned. A project that wants to silence it must do
        /// so by renaming the files, because the Player preflight enforces the same rule
        /// and cannot be configured away; pretending otherwise here would only move the
        /// failure later.
        /// </summary>
        private static void AddStreamingAssetsScope(
            string project,
            AssetPathAuditPlanRequest request,
            IReadOnlyList<string> exclusions,
            ICollection<PortableAssetPathAuditScope> scopes,
            ICollection<string> configurationErrors)
        {
            var scopeExclusions = new List<string>();
            AppendExclusionsBelow(
                scopeExclusions,
                exclusions,
                StreamingAssetsProjectRelativePath);
            for (int index = 0; index < exclusions.Count; index++)
            {
                if (string.Equals(
                        exclusions[index],
                        StreamingAssetsProjectRelativePath,
                        StringComparison.OrdinalIgnoreCase)
                    && configurationErrors != null)
                {
                    configurationErrors.Add(
                        $"'{StreamingAssetsProjectRelativePath}' cannot be excluded from the asset path audit. " +
                        "Unity copies that tree verbatim into the Player and the Player preflight enforces " +
                        "portable names there regardless of this step; exclude a subdirectory instead.");
                }
            }

            scopes.Add(new PortableAssetPathAuditScope(
                StreamingAssetsScopeName,
                Path.Combine(project, "Assets", "StreamingAssets"),
                rejectNonAscii: true,
                enforceWin32PathBudget: request.EnforceWin32PathBudget,
                isError: request.FindingsAreErrors,
                nonAsciiIsError: true,
                excludedRelativePrefixes: scopeExclusions));
        }

        private static void AddAdditionalRootScopes(
            string project,
            AssetPathAuditPlanRequest request,
            IReadOnlyList<string> exclusions,
            ICollection<PortableAssetPathAuditScope> scopes,
            ICollection<string> configurationErrors)
        {
            IReadOnlyList<string> configuredRoots = request.AdditionalRelativeRoots;
            if (configuredRoots == null || configuredRoots.Count == 0)
            {
                return;
            }

            for (int index = 0; index < configuredRoots.Count; index++)
            {
                string configured = configuredRoots[index];
                if (string.IsNullOrWhiteSpace(configured))
                {
                    continue;
                }

                string prefix;
                try
                {
                    // Project-relative plus containment is all this needs. The build
                    // output policy is not reused here because it also protects the
                    // build-owned directories, and a content root such as
                    // "Assets/StreamingAssets/Extra" is legitimately inside Assets.
                    BuildPathPolicy.ValidatePortableProjectRelativePath(
                        configured,
                        "Asset path audit root");
                    prefix = Normalize(configured);
                }
                catch (Exception exception)
                {
                    if (configurationErrors != null)
                    {
                        configurationErrors.Add(
                            $"Asset path audit root '{configured}' is invalid: {exception.Message}");
                    }

                    continue;
                }

                if (IsExcludedWholeRoot(exclusions, prefix))
                {
                    // Both the root and its exclusion are explicit configuration, so
                    // honouring the exclusion cannot hide a mandatory check.
                    continue;
                }

                var scopeExclusions = new List<string>();
                AppendExclusionsBelow(scopeExclusions, exclusions, prefix);
                scopes.Add(new PortableAssetPathAuditScope(
                    prefix,
                    ResolveProjectRelative(project, prefix),
                    rejectNonAscii: true,
                    enforceWin32PathBudget: request.EnforceWin32PathBudget,
                    isError: request.FindingsAreErrors,
                    nonAsciiIsError: true,
                    excludedRelativePrefixes: scopeExclusions));
            }
        }

        private static bool IsExcludedWholeRoot(
            IReadOnlyList<string> exclusions,
            string scopePrefix)
        {
            for (int index = 0; index < exclusions.Count; index++)
            {
                if (string.Equals(exclusions[index], scopePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Adds every exclusion strictly below <paramref name="scopePrefix"/>, converted
        /// into that scope's own coordinates. An exclusion that covers the scope root
        /// itself contributes nothing here: dropping a scope is an explicit decision the
        /// caller makes by not declaring it.
        /// </summary>
        private static void AppendExclusionsBelow(
            ICollection<string> destination,
            IReadOnlyList<string> projectRelativeExclusions,
            string scopePrefix)
        {
            for (int index = 0; index < projectRelativeExclusions.Count; index++)
            {
                string exclusion = projectRelativeExclusions[index];
                if (exclusion.Length <= scopePrefix.Length + 1
                    || !exclusion.StartsWith(scopePrefix, StringComparison.OrdinalIgnoreCase)
                    || exclusion[scopePrefix.Length] != '/')
                {
                    continue;
                }

                destination.Add(exclusion.Substring(scopePrefix.Length + 1));
            }
        }

        private static IReadOnlyList<string> NormalizeProjectRelativePrefixes(
            IReadOnlyList<string> prefixes)
        {
            if (prefixes == null || prefixes.Count == 0)
            {
                return Array.Empty<string>();
            }

            var normalized = new List<string>(prefixes.Count);
            for (int index = 0; index < prefixes.Count; index++)
            {
                string prefix = prefixes[index];
                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    normalized.Add(Normalize(prefix));
                }
            }

            return normalized.AsReadOnly();
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/').Trim('/');
        }

        private static string ResolveProjectRelative(string projectRoot, string relativePath)
        {
            string[] segments = relativePath.Split('/');
            string resolved = projectRoot;
            for (int index = 0; index < segments.Length; index++)
            {
                resolved = Path.Combine(resolved, segments[index]);
            }

            return Path.GetFullPath(resolved);
        }
    }
}
