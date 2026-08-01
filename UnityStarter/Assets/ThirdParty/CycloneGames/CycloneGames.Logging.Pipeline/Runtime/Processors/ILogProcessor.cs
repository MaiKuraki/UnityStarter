using System;

namespace CycloneGames.Logging
{
    internal interface ILogProcessor : IDisposable
    {
        bool TryReserve(LogSeverity level, int estimatedCharacters, bool allowEviction, out int reservedCharacters);
        bool TryCommit(LogEvent message, int reservedCharacters, int actualCharacters);
        void CancelReservation(int reservedCharacters);
        void Pump(int maxItems, int budgetMilliseconds);
        bool TryFlush(int timeoutMs);
        LogPipelineShutdownResult Shutdown(int timeoutMs);
        LogPipelineStatistics GetStatistics();
        bool IsStopped { get; }
    }
}
