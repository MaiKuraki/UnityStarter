using System;

namespace CycloneGames.Logging.Pipeline
{
    public static class LogPipelineFactory
    {
        public static LogPipeline CreateThreaded(LogPipelineOptions options = null, Func<DateTime> timestampProvider = null)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new PlatformNotSupportedException("Threaded logging is unavailable in WebGL players. Use CreateSingleThreaded.");
#else
            LogPipelineOptions capturedOptions = LogPipelineOptions.CreateValidated(options);
            return new LogPipeline(
                (owner, _) => new ThreadedLogProcessor(owner, capturedOptions),
                capturedOptions,
                timestampProvider ?? (() => DateTime.UtcNow));
#endif
        }

        public static LogPipeline CreateSingleThreaded(LogPipelineOptions options = null, Func<DateTime> timestampProvider = null)
        {
            LogPipelineOptions capturedOptions = LogPipelineOptions.CreateValidated(options);
            return new LogPipeline(
                (owner, _) => new SingleThreadLogProcessor(owner, capturedOptions),
                capturedOptions,
                timestampProvider ?? (() => DateTime.UtcNow));
        }
    }
}
