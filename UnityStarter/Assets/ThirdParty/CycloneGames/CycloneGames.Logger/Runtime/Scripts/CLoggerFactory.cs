using System;

namespace CycloneGames.Logger
{
    public static class CLoggerFactory
    {
        public static CLogger CreateThreaded(LoggerProcessingOptions options = null, Func<DateTime> timestampProvider = null)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new PlatformNotSupportedException("Threaded logger processing is unavailable in WebGL players. Use CreateSingleThreaded.");
#else
            LoggerProcessingOptions capturedOptions = LoggerProcessingOptions.CreateValidated(options);
            return new CLogger(
                (owner, _) => new ThreadedLogProcessor(owner, capturedOptions),
                capturedOptions,
                timestampProvider ?? (() => DateTime.UtcNow));
#endif
        }

        public static CLogger CreateSingleThreaded(LoggerProcessingOptions options = null, Func<DateTime> timestampProvider = null)
        {
            LoggerProcessingOptions capturedOptions = LoggerProcessingOptions.CreateValidated(options);
            return new CLogger(
                (owner, _) => new SingleThreadLogProcessor(owner, capturedOptions),
                capturedOptions,
                timestampProvider ?? (() => DateTime.UtcNow));
        }
    }
}
