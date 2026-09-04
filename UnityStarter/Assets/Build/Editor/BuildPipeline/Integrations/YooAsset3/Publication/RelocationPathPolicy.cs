using System;
using System.IO;

namespace Build.Pipeline.Integrations.YooAsset3.Publication
{
    /// <summary>
    /// Trust boundary for relocation journal entries. A relocation journal is a plain JSON file on
    /// disk, so every originalPath/relocatedPath inside it is untrusted input: recovery validates
    /// each entry against roots derived only from the trusted project root (never from the
    /// journal's own location) before any file is moved.
    ///
    /// Validation deliberately happens on the recovery path (immediately before a move, via
    /// <see cref="ValidateEntryRoots"/>) rather than inside
    /// RelocationJournalStore.ValidateDocument: loading a journal stays purely structural, and a
    /// tampered entry fails closed as a single Conflict state instead of making the whole journal
    /// unloadable (which would complicate the "before deleting a journal" semantics).
    /// </summary>
    internal static class RelocationPathPolicy
    {
        internal const string StreamingAssetsRootRelativePath = "Assets/StreamingAssets";

        internal static string GetStreamingAssetsRoot(string projectRoot)
        {
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                StreamingAssetsRootRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        /// <summary>
        /// Throws when the entry's paths escape their approved roots or reach them through a
        /// reparse point. The relocated artifact must live strictly below the shared Temp
        /// relocation root (<see cref="RelocationJournalStore.GetRelocationRoot"/>); the original
        /// must live strictly below the project's StreamingAssets directory, the only area Unity
        /// copies into the Player and therefore the only area the build session ever relocates
        /// from.
        /// </summary>
        internal static void ValidateEntryRoots(string projectRoot, RelocationEntry entry)
        {
            if (entry == null)
            {
                throw new InvalidOperationException("A relocation journal entry is required.");
            }

            string root = Path.GetFullPath(projectRoot);
            string relocatedPath = Path.GetFullPath(entry.relocatedPath ?? string.Empty);
            string originalPath = Path.GetFullPath(entry.originalPath ?? string.Empty);
            string relocationRoot = RelocationJournalStore.GetRelocationRoot(root);
            string streamingAssetsRoot = GetStreamingAssetsRoot(root);

            if (!PublicationSafety.IsStrictDescendant(relocationRoot, relocatedPath))
            {
                throw new InvalidOperationException(
                    "Relocation journal entry rejected: the relocated path is outside the relocation " +
                    $"root. Relocation root: '{relocationRoot}', relocated path: '{relocatedPath}'.");
            }

            if (!PublicationSafety.IsStrictDescendant(streamingAssetsRoot, originalPath))
            {
                throw new InvalidOperationException(
                    "Relocation journal entry rejected: the original path is outside StreamingAssets. " +
                    $"StreamingAssets root: '{streamingAssetsRoot}', original path: '{originalPath}'.");
            }

            // Per-segment reparse point (junction/symlink) inspection between the project root and
            // each path, shared with the publication journal store.
            PublicationSafety.ValidateNoPathRedirection(root, relocatedPath);
            PublicationSafety.ValidateNoPathRedirection(root, originalPath);
        }
    }
}
