using System;

namespace CycloneGames.Logger
{
    internal interface ILogProcessor : IDisposable
    {
        bool TryReserve(LogLevel level, int estimatedCharacters, bool allowEviction, out int reservedCharacters);
        bool TryCommit(LogMessage message, int reservedCharacters, int actualCharacters);
        void CancelReservation(int reservedCharacters);
        void Pump(int maxItems, int budgetMilliseconds);
        bool TryFlush(int timeoutMs);
        LoggerShutdownResult Shutdown(int timeoutMs);
        LogProcessingStatistics GetStatistics();
        bool IsStopped { get; }
    }
}
