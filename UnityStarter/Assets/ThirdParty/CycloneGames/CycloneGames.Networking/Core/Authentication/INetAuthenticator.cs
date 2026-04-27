using System;

namespace CycloneGames.Networking.Authentication
{
    /// <summary>
    /// Abstraction for network authentication flow.
    /// Implement to integrate with custom auth systems (tokens, Steam, Epic, etc.).
    /// </summary>
    public interface INetAuthenticator
    {
        /// <summary>
        /// Called on client when connection is established but before authentication.
        /// Client should send auth credentials via the transport.
        /// </summary>
        void OnClientAuthenticate(INetConnection connection);

        /// <summary>
        /// Called on server when receiving auth data from client.
        /// Validate and call AcceptClient or RejectClient accordingly.
        /// </summary>
        void OnServerAuthenticate(INetConnection connection, ReadOnlySpan<byte> authData);

        /// <summary>
        /// Accept a client connection after successful authentication.
        /// </summary>
        void AcceptClient(INetConnection connection);

        /// <summary>
        /// Reject a client connection with a reason.
        /// </summary>
        void RejectClient(INetConnection connection, string reason);

        /// <summary>
        /// Raised when authentication completes (success or failure).
        /// </summary>
        event Action<INetConnection, bool, string> OnAuthenticationResult;
    }
}