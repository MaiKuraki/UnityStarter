using System;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Connection pattern abstraction: persistent (long) vs request-response (short) connections.
    /// 
    /// Long connection (持久连接): TCP/WebSocket/Reliable UDP. Used by MMOs, FPS, MOBA, RTS.
    ///   - Maintains state across the session lifetime
    ///   - Bidirectional message flow
    ///   - Connection/disconnection lifecycle events
    ///   - The existing INetTransport / INetworkManager covers this model
    /// 
    /// Short connection (短连接): HTTP/REST, one-shot UDP, request-response pattern.
    ///   - Stateless: each request is independent
    ///   - Used for: login, lobby queries, leaderboards, store, analytics, master server
    ///   - Not suitable for real-time gameplay
    /// 
    /// This file provides the missing short-connection abstraction.
    /// </summary>
    public enum ConnectionPattern : byte
    {
        /// <summary>
        /// Persistent bidirectional connection (TCP, WebSocket, reliable UDP).
        /// Covered by INetTransport + INetworkManager.
        /// </summary>
        Persistent,

        /// <summary>
        /// Stateless request-response pattern (HTTP, one-shot UDP).
        /// Covered by INetworkRequest below.
        /// </summary>
        RequestResponse,

        /// <summary>
        /// Connectionless unreliable datagrams (raw UDP).
        /// For scenarios like server browsing, ping measurement if no persistent connection needed.
        /// </summary>
        Connectionless
    }

    /// <summary>
    /// Stateless request-response interface for short connections.
    /// Implement for HTTP, REST API, or custom master-server protocol.
    /// </summary>
    public interface INetworkRequest
    {
        /// <summary>
        /// Send a request and get a response asynchronously.
        /// </summary>
        void SendRequest(NetworkRequest request, Action<NetworkResponse> callback);

        /// <summary>
        /// Cancel an in-flight request.
        /// </summary>
        void CancelRequest(int requestId);

        /// <summary>
        /// Whether the backend is reachable.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Base URL or endpoint for request routing.
        /// </summary>
        string BaseUrl { get; set; }
    }

    public struct NetworkRequest
    {
        public int RequestId;
        public string Endpoint;        // e.g. "/api/lobby/list"
        public RequestMethod Method;
        public byte[] Payload;          // Serialized request body (nullable for GET)
        public float TimeoutSeconds;

        public static int _nextId;

        public static NetworkRequest Create(string endpoint, RequestMethod method = RequestMethod.Get,
            byte[] payload = null, float timeout = 10f)
        {
            return new NetworkRequest
            {
                RequestId = System.Threading.Interlocked.Increment(ref _nextId),
                Endpoint = endpoint,
                Method = method,
                Payload = payload,
                TimeoutSeconds = timeout
            };
        }
    }

    public struct NetworkResponse
    {
        public int RequestId;
        public ResponseStatus Status;
        public int StatusCode;          // HTTP-style status code (200, 404, 500, etc.)
        public byte[] Data;             // Response body
        public string ErrorMessage;

        public bool IsSuccess => Status == ResponseStatus.Success && StatusCode >= 200 && StatusCode < 300;
    }

    public enum RequestMethod : byte
    {
        Get,
        Post,
        Put,
        Delete,
        Patch
    }

    public enum ResponseStatus : byte
    {
        Success,
        Timeout,
        NetworkError,
        Cancelled,
        ServerError
    }
}
