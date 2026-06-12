using System;

namespace CycloneGames.Logger
{
    public static class CLoggerFactory
    {
        public static CLogger CreateThreaded(LoggerProcessingOptions options = null, Func<DateTime> timestampProvider = null)
        {
            var capturedOptions = LoggerProcessingOptions.CreateValidated(options);
            return new CLogger(owner => new ThreadedLogProcessor(owner, capturedOptions), timestampProvider ?? (() => DateTime.Now));
        }

        public static CLogger CreateSingleThreaded(LoggerProcessingOptions options = null, Func<DateTime> timestampProvider = null)
        {
            var capturedOptions = LoggerProcessingOptions.CreateValidated(options);
            return new CLogger(owner => new SingleThreadLogProcessor(owner, capturedOptions), timestampProvider ?? (() => DateTime.Now));
        }
    }
}
