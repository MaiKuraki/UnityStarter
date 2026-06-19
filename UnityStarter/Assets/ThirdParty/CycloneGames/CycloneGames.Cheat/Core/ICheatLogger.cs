using System;

namespace CycloneGames.Cheat.Core
{
    public interface ICheatLogger
    {
        void LogError(string message);
        void LogException(Exception exception);
    }
}
