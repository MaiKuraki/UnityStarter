using System;

namespace CycloneGames.Logging
{
    internal static class EmergencyLogWriter
    {
        internal static void TryWrite(string message, Exception exception = null)
        {
            try
            {
                Console.Error.Write("[CycloneGames.Logging] ");
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
