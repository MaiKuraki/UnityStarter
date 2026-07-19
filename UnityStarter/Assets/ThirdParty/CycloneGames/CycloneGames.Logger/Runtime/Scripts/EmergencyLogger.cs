using System;

namespace CycloneGames.Logger
{
    internal static class EmergencyLogger
    {
        internal static void TryWrite(string message, Exception exception = null)
        {
            try
            {
                Console.Error.Write("[CycloneGames.Logger] ");
                Console.Error.Write(message);
                if (exception != null)
                {
                    Console.Error.Write(" ");
                    Console.Error.Write(exception.GetType().Name);
                }

                Console.Error.WriteLine();
            }
            catch
            {
            }
        }
    }
}
