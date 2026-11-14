using System;
using CycloneGames.Logging;

namespace CycloneGames.Factory.Samples.VContainer
{
    internal static class FactoryVContainerSamplesLog
    {
        internal const string Category = "CycloneGames.Factory.Samples.VContainer";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
