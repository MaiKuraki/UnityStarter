using System.IO;

namespace CycloneGames.AtlasPipeline.Pure
{
    /// <summary>
    /// Portable fingerprint of one source asset: the on-disk bytes that decide how it imports and
    /// therefore what ends up in the atlas.
    /// <para>
    /// This exists to close a specific hole. The manifest's <c>contentHash</c> covers the ordered
    /// member list plus the governing rule configuration — it is machine-independent, but it says
    /// nothing about the pixels: repainting <c>btn.png</c> without renaming it leaves the hash
    /// untouched, which is why the manifest's drift check deliberately treats a repaint as "not
    /// drift". The only thing that notices a repaint today is
    /// <c>AssetDatabase.GetAssetDependencyHash</c>, and that value reflects the local import cache,
    /// so it can only be compared inside one editor session. A fresh editor — and every CI build —
    /// starts with an empty fingerprint table and regenerates everything, decoding every source
    /// texture to discover that almost nothing changed.
    /// </para>
    /// <para>
    /// Hashing the file bytes gives the same guarantee portably: identical on every machine and on
    /// a clean checkout, and it moves when the pixels move. The asset's <c>.meta</c> is hashed too,
    /// because the import settings that change a sprite's rect live there.
    /// </para>
    /// <para>
    /// Reading bytes is not free, but it is dramatically cheaper than the alternative: at ten
    /// thousand sprites it is a few hundred megabytes of sequential reads against several gigabytes
    /// of decoded textures that would otherwise be held in memory at once.
    /// </para>
    /// </summary>
    public static class AtlasSourceHash
    {
        public const string MetaSuffix = ".meta";

        private const int BufferSize = 8192;

        /// <summary>
        /// Scratch buffer for streaming. Files are read in chunks so a 200 MB PSD never has to be
        /// materialised. Thread-static because a static mutable buffer would be shared state, even
        /// though the pipeline only ever runs on the editor main thread.
        /// </summary>
        [System.ThreadStatic]
        private static byte[] s_buffer;

        /// <summary>
        /// Fingerprint of <paramref name="absoluteAssetPath"/> and its <c>.meta</c>.
        /// Returns <see cref="AtlasHash.NullHash"/> when the asset or its meta cannot be read.
        /// </summary>
        /// <remarks>
        /// Returning "unknown" rather than throwing is deliberate: a missing file means this
        /// fingerprint cannot vouch for the asset, and the caller must then regenerate the atlas
        /// rather than skip it. Callers must treat a <see cref="AtlasHash.NullHash"/> member as
        /// "refuse to skip", never as a value to fold in — folding it with XOR would make the
        /// member vanish from the result.
        /// </remarks>
        public static long Compute(string absoluteAssetPath)
        {
            if (string.IsNullOrEmpty(absoluteAssetPath))
            {
                return AtlasHash.NullHash;
            }

            string metaPath = absoluteAssetPath + MetaSuffix;

            // Cheap existence check up front: opening a stream to discover the file is gone costs
            // an exception, and a missing source is the common case right after a rename.
            if (!File.Exists(absoluteAssetPath) || !File.Exists(metaPath))
            {
                return AtlasHash.NullHash;
            }

            long hash = AtlasHash.BeginFnv1a64();
            if (!AppendFile(ref hash, absoluteAssetPath))
            {
                return AtlasHash.NullHash;
            }

            // Separator: without it, an asset whose tail bytes happen to line up with the head of
            // its .meta could collide with a different pairing.
            AtlasHash.AppendFnv1a64(ref hash, '\u001F');

            return AppendFile(ref hash, metaPath) ? hash : AtlasHash.NullHash;
        }

        /// <summary>
        /// Fingerprint of a single file's bytes, or false when it could not be read.
        /// </summary>
        public static bool TryComputeFile(string absolutePath, out long hash)
        {
            hash = AtlasHash.NullHash;
            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
            {
                return false;
            }

            long working = AtlasHash.BeginFnv1a64();
            if (!AppendFile(ref working, absolutePath))
            {
                return false;
            }

            hash = working;
            return true;
        }

        private static bool AppendFile(ref long hash, string absolutePath)
        {
            byte[] buffer = s_buffer;
            if (buffer == null)
            {
                buffer = new byte[BufferSize];
                s_buffer = buffer;
            }

            try
            {
                // FileShare.ReadWrite: Unity keeps source assets open while importing, and a
                // FileShare.Read request would fail with a sharing violation mid-rescan.
                using (var stream = new FileStream(
                           absolutePath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.ReadWrite,
                           BufferSize,
                           FileOptions.SequentialScan))
                {
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        AtlasHash.AppendFnv1a64(ref hash, buffer, 0, read);
                    }
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (System.UnauthorizedAccessException)
            {
                return false;
            }

            return true;
        }
    }
}
