using System.Collections.Generic;

namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Summary of a completed (or aborted/cancelled) <see cref="PreloadRunner"/> batch. <see cref="FailedReferences"/>
    /// is a live view into the runner's reused failure list; copy it if it must outlive the next batch. Admission-level
    /// rejection deliberately does not retain an oversized input list, so inspect <see cref="HasTruncatedFailureDetails"/>.
    /// </summary>
    public readonly struct PreloadResult
    {
        public readonly PreloadStatus Status;
        public readonly int TotalCount;
        public readonly int SucceededCount;
        public readonly int FailedCount;
        public readonly IReadOnlyList<ChoreographyResourceReference> FailedReferences;

        public PreloadResult(
            PreloadStatus status,
            int totalCount,
            int succeededCount,
            int failedCount,
            IReadOnlyList<ChoreographyResourceReference> failedReferences)
        {
            Status = status;
            TotalCount = totalCount;
            SucceededCount = succeededCount;
            FailedCount = failedCount;
            FailedReferences = failedReferences;
        }

        public bool IsSuccess => Status == PreloadStatus.Completed && FailedCount == 0;

        /// <summary>
        /// True when aggregate failures exceed retained per-reference details, as with a batch rejected by an
        /// admission or traversal ceiling before backend work.
        /// </summary>
        public bool HasTruncatedFailureDetails =>
            FailedCount > (FailedReferences != null ? FailedReferences.Count : 0);
    }
}
