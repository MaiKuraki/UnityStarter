using System;

namespace CycloneGames.Networking
{
    public sealed class NullNetworkingDiagnostics : INetworkingDiagnostics
    {
        public static readonly NullNetworkingDiagnostics Instance = new NullNetworkingDiagnostics();

        private NullNetworkingDiagnostics()
        {
        }

        public bool IsEnabled(NetworkingDiagnosticLevel level, string category) => false;

        public void Write(
            NetworkingDiagnosticLevel level,
            string category,
            string message,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
        }

        public void WriteException(
            NetworkingDiagnosticLevel level,
            string category,
            Exception exception,
            string message = null,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
        }
    }
}
