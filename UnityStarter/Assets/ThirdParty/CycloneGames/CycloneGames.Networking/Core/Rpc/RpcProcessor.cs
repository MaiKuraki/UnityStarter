using System;
using System.Collections.Concurrent;
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
        private readonly ConcurrentDictionary<ushort, RpcHandler> _handlers = new();
        private readonly ConcurrentQueue<ushort> _recycledIds = new();
        private readonly INetworkManager _networkManager;
        private int _nextAutoId = NetworkConstants.RpcMsgIdMin;

        private static readonly ushort MaxAutoId = NetworkConstants.UserMsgIdMin > NetworkConstants.RpcMsgIdMin
            ? (ushort)(NetworkConstants.UserMsgIdMin - 1)
            : ushort.MaxValue;

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
        /// Automatically recycles IDs from previously unregistered handlers.
        /// </summary>
        /// <exception cref="OverflowException">
        /// Thrown if all 65435+ auto-assignable RPC IDs are in use simultaneously.
        /// This requires &gt;65K concurrently registered RPC types and should never
        /// occur in normal game code.
        /// </exception>
        public ushort Register<T>(Action<INetConnection, T> handler, RpcTarget target = RpcTarget.Server,
            NetworkChannel channel = NetworkChannel.Reliable) where T : unmanaged
        {
            ushort id;
            if (_recycledIds.TryDequeue(out ushort recycled))
            {
                id = recycled;
            }
            else
            {
                int next = Interlocked.Increment(ref _nextAutoId);
                if (next > ushort.MaxValue)
                {
                    // Wrapped past ushort range. Scan for any recyclables that
                    // arrived after our increment; if still none, the ID space
                    // is genuinely exhausted.
                    if (!_recycledIds.TryDequeue(out recycled))
                        throw new OverflowException(
                            "RpcProcessor: auto-assignable RPC ID space exhausted. " +
                            "Too many concurrently registered handlers.");
                    id = recycled;
                }
                else
                {
                    id = (ushort)next;
                    if (id > MaxAutoId)
                    {
                        // We encroached on reserved user/system message ranges despite
                        // not having wrapped. This means the RPC range was configured
                        // too narrow for the actual handler count.
                        if (!_recycledIds.TryDequeue(out recycled))
                            throw new OverflowException(
                                $"RpcProcessor: auto-assignable RPC ID range exhausted " +
                                $"(max={MaxAutoId}). Increase the RPC range or reduce handler count.");
                        id = recycled;
                    }
                }
            }

            RegisterWithId<T>(id, handler, target, channel);
            return id;
        }

        public void RegisterWithId<T>(ushort rpcId, Action<INetConnection, T> handler,
            RpcTarget target = RpcTarget.Server, NetworkChannel channel = NetworkChannel.Reliable) where T : unmanaged
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

            // Register with the network manager to receive messages on this ID
            _networkManager.RegisterHandler<RpcPayload>(rpcId, OnRpcReceived);
        }

        /// <summary>
        /// Send an RPC. Automatically routes based on registered target.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Send<T>(ushort rpcId, in T data, INetConnection target = null) where T : unmanaged
        {
            // ConcurrentDictionary.TryGetValue is lock-free for reads.
            // The handler Invoke closure is an object reference that remains valid
            // even if the handler is concurrently unregistered.
            if (!_handlers.TryGetValue(rpcId, out RpcHandler handler)) return;

            var payload = RpcPayload.FromBlittable(rpcId, data);

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
            if (_handlers.TryRemove(rpcId, out _))
            {
                _recycledIds.Enqueue(rpcId);
                _networkManager.UnregisterHandler(rpcId);
            }
        }

        private void OnRpcReceived(INetConnection conn, RpcPayload payload)
        {
            if (!_handlers.TryGetValue(payload.RpcId, out RpcHandler handler))
                return;

            using var reader = NetworkBufferPool.Get();
            payload.CopyTo(reader);
            handler.Invoke(conn, reader);
        }
    }

    public struct RpcPayload
    {
        private const int InlineCapacity = 32;

        public ushort RpcId;
        public byte[] Data;
        public int Length;
        public ulong Inline0;
        public ulong Inline1;
        public ulong Inline2;
        public ulong Inline3;
        public byte StorageMode;

        public bool IsInline => StorageMode == 1;

        public ArraySegment<byte> AsSegment()
        {
            return Data == null || IsInline
                ? default
                : new ArraySegment<byte>(Data, 0, Length);
        }

        internal void CopyTo(NetworkBuffer buffer)
        {
            if (IsInline)
            {
                Span<byte> data = stackalloc byte[InlineCapacity];
                WriteUInt64LittleEndian(data.Slice(0, 8), Inline0);
                WriteUInt64LittleEndian(data.Slice(8, 8), Inline1);
                WriteUInt64LittleEndian(data.Slice(16, 8), Inline2);
                WriteUInt64LittleEndian(data.Slice(24, 8), Inline3);
                buffer.SetBuffer(data.Slice(0, Length));
                return;
            }

            if (Data == null || Length <= 0)
            {
                buffer.SetBuffer(ReadOnlySpan<byte>.Empty);
                return;
            }

            buffer.SetBuffer(AsSegment());
        }

        public static unsafe RpcPayload FromBlittable<T>(ushort rpcId, in T value) where T : unmanaged
        {
            int size = sizeof(T);
            if (size <= InlineCapacity)
            {
                RpcPayload payload = new RpcPayload
                {
                    RpcId = rpcId,
                    Length = size,
                    StorageMode = 1
                };

                Span<byte> inlineBytes = stackalloc byte[InlineCapacity];
                inlineBytes.Clear();
                T* valuePtr = stackalloc T[1];
                valuePtr[0] = value;
                new ReadOnlySpan<byte>(valuePtr, size).CopyTo(inlineBytes);

                payload.Inline0 = ReadUInt64LittleEndian(inlineBytes.Slice(0, 8));
                payload.Inline1 = ReadUInt64LittleEndian(inlineBytes.Slice(8, 8));
                payload.Inline2 = ReadUInt64LittleEndian(inlineBytes.Slice(16, 8));
                payload.Inline3 = ReadUInt64LittleEndian(inlineBytes.Slice(24, 8));

                return payload;
            }

            using var buffer = NetworkBufferPool.Get();
            buffer.WriteBlittable(value);
            ArraySegment<byte> segment = buffer.ToArraySegment();

            byte[] heapData = new byte[segment.Count];
            Buffer.BlockCopy(segment.Array!, segment.Offset, heapData, 0, segment.Count);

            return new RpcPayload
            {
                RpcId = rpcId,
                Data = heapData,
                Length = heapData.Length,
                StorageMode = 0
            };
        }

        private static ulong ReadUInt64LittleEndian(ReadOnlySpan<byte> data)
        {
            ulong value = 0UL;
            for (int i = 0; i < data.Length; i++)
                value |= (ulong)data[i] << (i * 8);
            return value;
        }

        private static void WriteUInt64LittleEndian(Span<byte> destination, ulong value)
        {
            for (int i = 0; i < destination.Length; i++)
                destination[i] = (byte)(value >> (i * 8));
        }
    }
}
