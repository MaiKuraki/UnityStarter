using System;
using System.IO;
using System.Text;

namespace CycloneGames.AtlasPipeline.Pure
{
    /// <summary>
    /// Crash-safe file replacement for the atlas manifest. The manifest is the committed record CI
    /// compares against, so a torn write is worse than no write: a half-written file parses as
    /// garbage or, worse, parses with missing entries and reports the wrong drift. The writer
    /// therefore never touches the destination until a fully written, verified temporary file is
    /// sitting next to it, and the swap is a single rename on the same volume.
    /// <para>
    /// Failure contract: when anything fails, the previous file is left byte-for-byte intact, the
    /// temporary file is kept for diagnosis (it is always the same ".tmp" path, so the next
    /// successful write replaces it rather than accumulating), and the error is reported through
    /// the out parameter instead of an exception.
    /// </para>
    /// </summary>
    public static class AtlasManifestFile
    {
        public const string TempSuffix = ".tmp";

        /// <summary>
        /// Writes <paramref name="content"/> to <paramref name="absolutePath"/> atomically: write
        /// to a sibling temporary file (UTF-8 without BOM, LF preserved from the content), verify
        /// it with <paramref name="validator"/>, then swap it in.
        /// </summary>
        /// <param name="validator">
        /// Receives the temporary file's content exactly as read back from disk. Returns false to
        /// reject the write — the destination is then left untouched and the temporary file is kept
        /// for diagnosis.
        /// </param>
        /// <returns>True when the destination now holds the content.</returns>
        public static bool TryWriteAtomically(
            string absolutePath,
            string content,
            Func<string, bool> validator,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                error = "No destination path.";
                return false;
            }

            string directory = Path.GetDirectoryName(absolutePath);
            if (string.IsNullOrEmpty(directory))
            {
                error = "Destination path has no directory: " + absolutePath;
                return false;
            }

            string tempPath = absolutePath + TempSuffix;
            try
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // UTF-8 without BOM, and no newline translation: the manifest is committed, so a
                // CRLF or BOM difference between machines would show up as a whole-file diff.
                File.WriteAllText(tempPath, content, new UTF8Encoding(false));

                // Verified by reading the bytes BACK off the disk, not by trusting the string we
                // just handed to the writer: this is the check that catches a torn or failed write.
                if (!File.Exists(tempPath))
                {
                    error = "Temporary manifest '" + tempPath + "' was not written.";
                    return false;
                }

                string readBack = File.ReadAllText(tempPath);
                if (validator == null || !validator(readBack))
                {
                    error = "Temporary manifest '" + tempPath
                            + "' failed content verification; it is kept for diagnosis and the "
                            + "previous manifest is untouched.";
                    return false;
                }

                if (File.Exists(absolutePath))
                {
                    // Same directory, same volume: File.Replace is atomic where the platform
                    // supports it and leaves the destination intact when it fails.
                    File.Replace(tempPath, absolutePath, null);
                }
                else
                {
                    File.Move(tempPath, absolutePath);
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message
                        + " — the previous manifest was left unchanged"
                        + (File.Exists(tempPath)
                            ? " and the temporary file '" + tempPath + "' is kept for diagnosis."
                            : ".");
                return false;
            }
        }
    }
}
