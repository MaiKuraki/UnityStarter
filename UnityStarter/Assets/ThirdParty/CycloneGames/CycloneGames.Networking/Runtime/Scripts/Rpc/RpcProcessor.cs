using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using CycloneGames.Networking.Buffers;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.Networking.Rpc
{
    /// <summary>
    /// Handles RPC registration, serialization, and dispatch.
    /// Uses a dictionary of ushort rpcId -> handler for O(1) dispatch.
    /// 
    /// Two usage patterns:
    /// 1. Manual: Register handler lambdas with explicit RPC IDs
    /// 2. Attribute-based: Use [ServerRpc]/[ClientRpc] with code generation or reflection
    /// </summary>
    public sealed class RpcProcessor
    {
        private readonly Dictionary<ushort, RpcHandler> _handlers = new Dictionary<ushort, RpcHandler>(64);
        private readonly object _handlersLock = new object();
        private readonly INetworkManager _networkManager;
        private int _nextAutoId = NetworkConstants.RpcMsgIdMin;

        private struct RpcHandler
        {
            public Action<INetConnection, INetReader> Invoke;
            public RpcTarget Target;
            public NetworkChannel Channel;
        }

        public RpcProcessor(INetworkManager networkManager)
        {
            _networkManager = networkManager;
        }

        /// <summary>
        /// Register a typed RPC handler. Returns the assigned RPC ID.
        /// </summary>
        public ushort Register<T>(Action<INetConnection, T> handler, RpcTarget target = RpcTarget.Server,
            NetworkChannel channel = NetworkChannel.Reliable) where T : unmanaged
        {
            ushort id = (ushort)Interlocked.Increment(ref _nextAutoId);
            RegisterWithId<T>(id, handler, target, channel);
            return id;
        }

        public void RegisterWithId<T>(ushort rpcId, Action<INetConnection, T> handler,
            RpcTarget target = RpcTarget.Server, NetworkChannel channel = NetworkChannel.Reliable) where T : unmanaged
        {
            lock (_handlersLock)
            {
                _handlers[rpcId] = new RpcHandler
                {
                    Invoke = (conn, reader) =>
                    {
                        T data = reader.ReadBlittable<T>();
                        handler(conn, data);
                    },
                    Target = target,
                    Channel = channel
                };
            }

            // Register with the network manager to receive messages on this ID
            _networkManager.RegisterHandler<RpcPayload>(rpcId, OnRpcReceived);
        }

        /// <summary>
        /// Send an RPC. Automatically routes based on registered target.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Send<T>(ushort rpcId, in T data, INetConnection target = null) where T : unmanaged
        {
            RpcHandler handler;
            lock (_handlersLock)
            {
                if (!_handlers.TryGetValue(rpcId, out handler)) return;
            }

            using var buffer = NetworkBufferPool.Get();
            buffer.WriteBlittable(data);

            var payload = new RpcPayload { RpcId = rpcId, Data = buffer.ToArraySegment() };

            switch (handler.Target)
            {
                case RpcTarget.Server:
                    _networkManager.SendToServer(rpcId, payload, handler.Channel);
                    break;
                case RpcTarget.Owner when target != null:
                    _networkManager.SendToClient(target, rpcId, payload, handler.Channel);
                    break;
                case RpcTarget.AllClients:
                    _networkManager.BroadcastToClients(rpcId, payload, handler.Channel);
                    break;
            }
        }

        public void Unregister(ushort rpcId)
        {
            lock (_handlersLock)
            {
                _handlers.Remove(rpcId);
            }
            _networkManager.UnregisterHandler(rpcId);
        }

        private void OnRpcReceived(INetConnection conn, RpcPayload payload)
        {
            RpcHandler handler;
            lock (_handlersLock)
            {
                if (!_handlers.TryGetValue(payload.RpcId, out handler))
                    return;
            }
            using var reader = NetworkBufferPool.GetWithData(payload.Data);
            handler.Invoke(conn, reader);
        }
    }

    public struct RpcPayload
    {
        public ushort RpcId;
        public ArraySegment<byte> Data;
    }
}
