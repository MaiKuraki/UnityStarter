namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Factory for creating compressed Dynamic Atlas instances.
    /// </summary>
    public class CompressedDynamicAtlasFactory
    {
        private static CompressedDynamicAtlasService _sharedInstance;
        private static readonly object _sharedLock = new object();
        private static UnityEngine.TextureFormat _sharedFormat;

        /// <summary>
        /// Creates a new compressed Dynamic Atlas service for the specified format.
        /// </summary>
        /// <param name="format">Compressed texture format (ASTC/ETC2/BC)</param>
        /// <param name="pageSize">Page size in pixels (will be aligned to block size)</param>
        /// <param name="blockPadding">Padding between sprites in blocks</param>
        public CompressedDynamicAtlasService Create(
            UnityEngine.TextureFormat format,
            int pageSize = 2048,
            int blockPadding = 1)
        {
            return new CompressedDynamicAtlasService(format, pageSize, blockPadding);
        }

        /// <summary>
        /// Gets or creates a shared instance for the specified format.
        /// Note: All sources must use the same format as the shared instance.
        /// </summary>
        public CompressedDynamicAtlasService GetSharedInstance(
            UnityEngine.TextureFormat format,
            int pageSize = 2048,
            int blockPadding = 1)
        {
            if (_sharedInstance == null || _sharedFormat != format)
            {
                lock (_sharedLock)
                {
                    if (_sharedInstance == null || _sharedFormat != format)
                    {
                        _sharedInstance?.Dispose();
                        _sharedInstance = Create(format, pageSize, blockPadding);
                        _sharedFormat = format;
                    }
                }
            }
            return _sharedInstance;
        }

        /// <summary>
        /// Creates a platform-optimized compressed atlas using the recommended format.
        /// </summary>
        public CompressedDynamicAtlasService CreatePlatformOptimized(int pageSize = 2048)
        {
            var format = TextureFormatHelper.GetRecommendedCompressedFormat();

            if (TextureFormatHelper.GetBlockSize(format) <= 1)
            {
                // Platform doesn't support compressed formats well, return null
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                CycloneGames.Logger.CLogger.LogWarning(
                    "[CompressedDynamicAtlasFactory] No suitable compressed format available. " +
                    "Use DynamicAtlasService instead.");
#endif
                return null;
            }

            return Create(format, pageSize);
        }

        /// <summary>
        /// Clears the shared instance.
        /// </summary>
        public static void ClearSharedInstance()
        {
            lock (_sharedLock)
            {
                _sharedInstance?.Dispose();
                _sharedInstance = null;
            }
        }
    }
}
