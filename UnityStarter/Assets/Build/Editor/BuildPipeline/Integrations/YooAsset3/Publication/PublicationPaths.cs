using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Build.Pipeline.Editor;
using System.Security.Cryptography;
using System.Text;

namespace Build.Pipeline.Integrations.YooAsset3.Publication
{
    internal static class PublicationConstants
    {
        internal const string PublicationIdPrefix = "asset-content:yooasset:";
        internal const string StateRootRelativePath = ".buildpipeline/transactions/yooasset3";

        internal const string JournalDocumentType = "yooasset-publication-transaction";
        internal const int MaximumJournalBytes = 1024 * 1024;
        internal const int MaximumOperationCount = 512;
        internal const int MaximumCopiedEntries = 250000;
        internal const int MaximumCopyDepth = 64;
        internal const long MaximumCopiedBytes = 256L * 1024L * 1024L * 1024L;
        internal const long MaximumSiblingMetaBytes = 1024L * 1024L;
        internal const string ActiveJournalFileName = "active.json";
        internal const string StagePrefix = ".yoo-stage-";
        internal const string BackupPrefix = ".yoo-backup-";

        internal const string PreparedPhase = "Prepared";
        internal const string CommittingPhase = "Committing";
        internal const string RollingBackPhase = "RollingBack";
        internal const string RollbackRefreshPendingPhase = "RollbackRefreshPending";
        internal const string ActivationRefreshPendingPhase = "ActivationRefreshPending";
        internal const string DownstreamActivePhase = "DownstreamActive";
        internal const string SourceQualificationSuspendingPhase = "SourceQualificationSuspending";
        internal const string SourceQualificationSuspendedPhase = "SourceQualificationSuspended";
        internal const string SourceQualificationResumingPhase = "SourceQualificationResuming";
        internal const string AwaitingDecisionPhase = "AwaitingDecision";
        internal const string RefreshPendingPhase = "RefreshPending";
        internal const string CommittedPhase = "Committed";

        internal const string PreparedState = "Prepared";
        internal const string BackupPendingState = "BackupPending";
        internal const string BackedUpState = "BackedUp";
        internal const string InstalledState = "Installed";
    }

    internal static class PublicationPaths
    {
        public static string GetProviderStateRoot(string projectRoot)
        {
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                PublicationConstants.StateRootRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        public static string GetStateRoot(
            string projectRoot,
            string invocationId)
        {
            return Path.Combine(
                GetProviderStateRoot(projectRoot),
                NormalizeInvocationId(invocationId));
        }

        internal static string GetStateRelativePath(string invocationId)
        {
            return PublicationConstants.StateRootRelativePath + "/" + NormalizeInvocationId(invocationId);
        }

        internal static string GetPublicationId(string invocationId)
        {
            return PublicationConstants.PublicationIdPrefix + NormalizeInvocationId(invocationId);
        }

        internal static string NormalizeInvocationId(string invocationId)
        {
            BuildIdentityPolicy.ValidateBuildIdentifier(
                invocationId,
                "YooAsset content invocation id");
            BuildPathPolicy.ValidatePortableFileName(
                invocationId,
                "YooAsset content invocation state directory",
                BuildIdentityPolicy.MaximumBuildIdentifierCharacters);
            return invocationId;
        }

        internal static bool IsValidInvocationId(string invocationId)
        {
            try
            {
                NormalizeInvocationId(invocationId);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        internal static string GetSourceQualificationRoot(PublicationJournal value)
        {
        return Path.GetFullPath(Path.Combine(
        value.workRoot,
        "source-qualification"));
        }

        internal static SourceQualificationPaths GetSourceQualificationPaths(
        PublicationJournal value,
        int operationIndex)
        {
        string operationRoot = Path.GetFullPath(Path.Combine(
        GetSourceQualificationRoot(value),
        operationIndex.ToString("D3", CultureInfo.InvariantCulture)));
        return new SourceQualificationPaths(
        operationRoot,
        Path.Combine(operationRoot, "installed"),
        Path.Combine(operationRoot, "installed.meta"),
        Path.Combine(operationRoot, "original.meta"));
        }

        internal static void ValidateSourceQualificationPath(
        PublicationJournal value,
        string path)
        {
        if (!PublicationSafety.IsStrictDescendant(value.workRoot, path))
        {
        throw new InvalidOperationException(
        $"YooAsset source qualification holding path escaped its transaction work root: '{path}'.");
        }

        PublicationSafety.ValidateNoPathRedirection(value.projectRoot, path);
        }
    }
}
