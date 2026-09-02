using System;
using System.Linq;
using static Build.Pipeline.Integrations.YooAsset3.Publication.PublicationConstants;

namespace Build.Pipeline.Integrations.YooAsset3.Publication
{
    /// <summary>
    /// Pure journal wire-format predicates: token shapes, known phases and operation states.
    /// A dependency leaf by design — Store, Validator, MetaGuard and Ownership all consume these,
    /// so this class must not call back into any of them (that was the CG0048 cycle).
    /// </summary>
    internal static class PublicationJournalFormat
    {
internal static bool IsTransactionId(string value)
        {
            return value != null && value.Length == 32 && value.All(character =>
                character >= '0' && character <= '9' || character >= 'a' && character <= 'f');
        }

internal static bool IsSha256(string value)
        {
            return IsHexToken(value, 64);
        }

internal static bool IsHexToken(string value, int length)
        {
            return value != null && value.Length == length && value.All(character =>
                character >= '0' && character <= '9' ||
                character >= 'A' && character <= 'F' ||
                character >= 'a' && character <= 'f');
        }

internal static bool IsKnownPhase(string value)
        {
            return string.Equals(value, PreparedPhase, StringComparison.Ordinal) ||
                   string.Equals(value, CommittingPhase, StringComparison.Ordinal) ||
                   string.Equals(value, RollingBackPhase, StringComparison.Ordinal) ||
                   string.Equals(value, RollbackRefreshPendingPhase, StringComparison.Ordinal) ||
                   string.Equals(value, ActivationRefreshPendingPhase, StringComparison.Ordinal) ||
                   string.Equals(value, DownstreamActivePhase, StringComparison.Ordinal) ||
                   string.Equals(value, SourceQualificationSuspendingPhase, StringComparison.Ordinal) ||
                   string.Equals(value, SourceQualificationSuspendedPhase, StringComparison.Ordinal) ||
                   string.Equals(value, SourceQualificationResumingPhase, StringComparison.Ordinal) ||
                   string.Equals(value, AwaitingDecisionPhase, StringComparison.Ordinal) ||
                   string.Equals(value, RefreshPendingPhase, StringComparison.Ordinal) ||
                   string.Equals(value, CommittedPhase, StringComparison.Ordinal);
        }

internal static bool IsKnownOperationState(string value)
        {
            return string.Equals(value, PreparedState, StringComparison.Ordinal) ||
                   string.Equals(value, BackupPendingState, StringComparison.Ordinal) ||
                   string.Equals(value, BackedUpState, StringComparison.Ordinal) ||
                   string.Equals(value, InstalledState, StringComparison.Ordinal);
        }

internal static bool IsSourceQualificationPhase(string value)
        {
            return string.Equals(value, SourceQualificationSuspendingPhase, StringComparison.Ordinal) ||
                   string.Equals(value, SourceQualificationSuspendedPhase, StringComparison.Ordinal) ||
                   string.Equals(value, SourceQualificationResumingPhase, StringComparison.Ordinal);
        }
    }
}
