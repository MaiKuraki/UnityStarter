#if CYCLONE_NETWORKING_HAS_NAKAMA
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using CycloneGames.Hash.Core;
using CycloneGames.Networking.Security;
using Nakama;
using UnityEngine;

namespace CycloneGames.Networking.Adapter.Nakama
{
    public sealed partial class NakamaNetAdapter
    {
        private void EnsureClientAndSocket()
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (_client == null)
#if UNITY_WEBGL && !UNITY_EDITOR
                _client = new Client(_scheme, _host, _port, _serverKey, UnityWebRequestAdapter.Instance);
#else
                _client = new Client(_scheme, _host, _port, _serverKey);
#endif
            if (_socket == null)
                CreateOwnedSocket();
            if (!_socketConnectionEpochStarted)
                BindSocketEvents();
        }

        private void PrepareSocketForConnectionEpoch()
        {
            EnsureClientAndSocket();
            if (_socketConnectionEpochStarted)
            {
                if (!_socketOwnedByAdapter)
                    throw new InvalidOperationException(UnavailableSocketEpochError);

                RetireOwnedSocket();
                CreateOwnedSocket();
            }

            ThrowIfSocketUnavailableForNewEpoch();
            BindSocketEvents();
            MarkSocketEpochStarted(_socket, this);
            _socketConnectionEpochStarted = true;
        }

        private void CreateOwnedSocket()
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (_client == null)
                throw new InvalidOperationException(MissingClientError);

            ISocketAdapter transportAdapter;
#if UNITY_WEBGL && !UNITY_EDITOR
            transportAdapter = new JsWebSocketAdapter();
#else
            transportAdapter = new WebSocketStdlibAdapter();
#endif
            UnitySocket dispatcher = UnitySocket.Create(transportAdapter);
            try
            {
                ISocket socket = global::Nakama.Socket.From(_client, dispatcher);
                ClaimSocket(socket, this, isAdapterOwned: true);
                _socket = socket;
                _ownedSocketDispatcher = dispatcher;
                _socketOwnedByAdapter = true;
                _socketConnectionEpochStarted = false;
                RefreshRuntimeContext();
            }
            catch
            {
                if (dispatcher != null)
                    Destroy(dispatcher.gameObject);
                throw;
            }
        }

        private void RetireOwnedSocket()
        {
            EnsureMainThread();
            ISocket retiredSocket = _socket;
            UnitySocket retiredDispatcher = _ownedSocketDispatcher;
            if (retiredSocket != null
                && !RequestSocketClose(retiredSocket, reportErrors: false, retiredDispatcher))
            {
                throw new InvalidOperationException(SocketCloseCapacityError);
            }

            if (retiredSocket != null)
                RelinquishSocketClaim(retiredSocket, this, retire: true);
            UnbindSocketEvents();
            _socket = null;
            _ownedSocketDispatcher = null;
            _socketOwnedByAdapter = false;
            _socketConnectionEpochStarted = false;

        }

        private void ThrowIfInjectedSocketRetired()
        {
            if (_socket == null || _socketOwnedByAdapter)
                return;
            if (_socketConnectionEpochStarted)
                throw new InvalidOperationException(UnavailableSocketEpochError);

            ThrowIfSocketUnavailableForNewEpoch();
        }

        private static bool ClaimSocket(
            ISocket socket,
            NakamaNetAdapter owner,
            bool isAdapterOwned)
        {
            lock (s_socketEpochRegistryGate)
            {
                if (!s_socketEpochRegistry.TryGetValue(socket, out SocketEpochState state))
                {
                    s_socketEpochRegistry.Add(socket, new SocketEpochState(owner, isAdapterOwned));
                    return true;
                }

                if (state.EpochStarted)
                    throw new InvalidOperationException(UnavailableSocketEpochError);

                if (state.Owner != null
                    && state.Owner.TryGetTarget(out NakamaNetAdapter currentOwner))
                {
                    if (!ReferenceEquals(currentOwner, owner)
                        || state.IsAdapterOwned != isAdapterOwned)
                    {
                        throw new InvalidOperationException(UnavailableSocketEpochError);
                    }

                    return false;
                }

                if (state.IsAdapterOwned)
                {
                    state.EpochStarted = true;
                    state.Owner = null;
                    throw new InvalidOperationException(UnavailableSocketEpochError);
                }

                state.Owner = new WeakReference<NakamaNetAdapter>(owner);
                state.IsAdapterOwned = isAdapterOwned;
                return true;
            }
        }

        private static void MarkSocketEpochStarted(ISocket socket, NakamaNetAdapter owner)
        {
            lock (s_socketEpochRegistryGate)
            {
                if (!TryGetSocketStateForOwner(socket, owner, out SocketEpochState state)
                    || state.EpochStarted)
                {
                    throw new InvalidOperationException(UnavailableSocketEpochError);
                }

                state.EpochStarted = true;
            }
        }

        private static void RelinquishSocketClaim(
            ISocket socket,
            NakamaNetAdapter owner,
            bool retire)
        {
            lock (s_socketEpochRegistryGate)
            {
                if (!TryGetSocketStateForOwner(socket, owner, out SocketEpochState state))
                    return;

                if (retire || state.IsAdapterOwned || state.EpochStarted)
                {
                    state.EpochStarted = true;
                    state.Owner = null;
                    return;
                }

                s_socketEpochRegistry.Remove(socket);
            }
        }

        private void ThrowIfSocketUnavailableForNewEpoch()
        {
            lock (s_socketEpochRegistryGate)
            {
                if (!TryGetSocketStateForOwner(_socket, this, out SocketEpochState state)
                    || state.EpochStarted)
                {
                    throw new InvalidOperationException(UnavailableSocketEpochError);
                }
            }
        }

        private static bool TryGetSocketStateForOwner(
            ISocket socket,
            NakamaNetAdapter owner,
            out SocketEpochState state)
        {
            if (socket != null
                && s_socketEpochRegistry.TryGetValue(socket, out state)
                && state.Owner != null
                && state.Owner.TryGetTarget(out NakamaNetAdapter currentOwner)
                && ReferenceEquals(currentOwner, owner))
            {
                return true;
            }

            state = null;
            return false;
        }

        private bool RequestSocketClose(
            ISocket socket,
            bool reportErrors,
            UnitySocket ownedDispatcher = null,
            bool useDestroyReservation = false)
        {
            if (!TryReserveSocketClose(
                    socket,
                    reportErrors,
                    ownedDispatcher,
                    useDestroyReservation,
                    out SocketCloseReservation reservation))
            {
                return false;
            }

            StartReservedSocketClose(socket, reportErrors, reservation);
            return true;
        }

        private bool TryReserveSocketClose(
            ISocket socket,
            bool reportErrors,
            UnitySocket ownedDispatcher,
            bool useDestroyReservation,
            out SocketCloseReservation reservation)
        {
            EnsureMainThread();
            reservation = default;
            if (socket == null)
                return true;

            int availableSlot = -1;
            int slotLimit = useDestroyReservation
                ? _socketCloseTasks.Length
                : MaximumPendingSocketCloseOperations;
            for (int i = 0; i < _socketCloseTasks.Length; i++)
            {
                Task closeTask = _socketCloseTasks[i];
                if (closeTask == null || closeTask.IsCompleted)
                {
                    _socketCloseSockets[i] = null;
                    _socketCloseTasks[i] = null;
                    _socketCloseDispatchers[i] = null;
                    _socketCloseCapacityReported = false;
                    if (i < slotLimit && availableSlot < 0)
                        availableSlot = i;
                    continue;
                }

                if (ReferenceEquals(_socketCloseSockets[i], socket))
                {
                    if (ownedDispatcher != null && _socketCloseDispatchers[i] == null)
                        _socketCloseDispatchers[i] = ownedDispatcher;
                    return true;
                }
            }

            if (availableSlot < 0)
            {
                if (reportErrors && !_socketCloseCapacityReported)
                {
                    _socketCloseCapacityReported = true;
                    RaiseError(null, TransportError.Congestion, SocketCloseCapacityError);
                }
                return false;
            }

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _socketCloseSockets[availableSlot] = socket;
            _socketCloseTasks[availableSlot] = completion.Task;
            _socketCloseDispatchers[availableSlot] = ownedDispatcher;
            reservation = new SocketCloseReservation(availableSlot, completion);
            return true;
        }

        private void StartReservedSocketClose(
            ISocket socket,
            bool reportErrors,
            SocketCloseReservation reservation)
        {
            if (!reservation.RequiresStart)
                return;

            CloseSocketAsync(
                socket,
                reportErrors,
                reservation.Completion,
                reservation.Slot).Forget();
        }

        private void ReleaseSocketCloseReservation(SocketCloseReservation reservation)
        {
            if (!reservation.RequiresStart)
                return;

            int slot = reservation.Slot;
            if ((uint)slot < (uint)_socketCloseTasks.Length
                && ReferenceEquals(_socketCloseTasks[slot], reservation.Completion.Task))
            {
                _socketCloseSockets[slot] = null;
                _socketCloseTasks[slot] = null;
                _socketCloseDispatchers[slot] = null;
            }

            reservation.Completion.TrySetResult(true);
        }

        private Task GetPendingSocketCloseTask(ISocket socket)
        {
            for (int i = 0; i < _socketCloseTasks.Length; i++)
            {
                Task closeTask = _socketCloseTasks[i];
                if (closeTask == null || closeTask.IsCompleted)
                {
                    _socketCloseSockets[i] = null;
                    _socketCloseTasks[i] = null;
                    _socketCloseDispatchers[i] = null;
                    _socketCloseCapacityReported = false;
                    continue;
                }

                if (ReferenceEquals(_socketCloseSockets[i], socket))
                    return closeTask;
            }

            return Task.CompletedTask;
        }

        private async UniTask CloseSocketAsync(
            ISocket socket,
            bool reportErrors,
            TaskCompletionSource<bool> completion,
            int closeSlot)
        {
            try
            {
                EnsureMainThread();
                await socket.CloseAsync();
                EnsureMainThread();
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                EnsureMainThread();
                if (reportErrors && ReferenceEquals(socket, _socket))
                    RaiseError(null, TransportError.ConnectionClosed, e.Message);
            }
            finally
            {
                UnitySocket ownedDispatcher = closeSlot >= 0 && closeSlot < _socketCloseDispatchers.Length
                    ? _socketCloseDispatchers[closeSlot]
                    : null;
                if (ownedDispatcher != null && IsMainThread())
                    Destroy(ownedDispatcher.gameObject);
                completion.TrySetResult(true);
            }
        }

        private void BindSocketEvents()
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (_socket == null || _socketEventsBound)
                return;

            unchecked
            {
                _socketEventGeneration++;
            }
            int socketEventGeneration = _socketEventGeneration;
            ISocket boundSocket = _socket;
            _socketEventSource = boundSocket;
            _socketClosedHandler = reason =>
                OnSocketClosed(reason, boundSocket, socketEventGeneration);
            _socketErrorHandler = exception =>
                OnSocketError(exception, boundSocket, socketEventGeneration);
            _matchStateHandler = state =>
                OnReceivedMatchState(state, boundSocket, socketEventGeneration);
            _matchPresenceHandler = presence =>
                OnReceivedMatchPresence(presence, boundSocket, socketEventGeneration);
            _matchmakerMatchedHandler = matched =>
                OnReceivedMatchmakerMatched(matched, boundSocket, socketEventGeneration);
            boundSocket.Closed += _socketClosedHandler;
            boundSocket.ReceivedError += _socketErrorHandler;
            boundSocket.ReceivedMatchState += _matchStateHandler;
            boundSocket.ReceivedMatchPresence += _matchPresenceHandler;
            boundSocket.ReceivedMatchmakerMatched += _matchmakerMatchedHandler;
            _socketEventsBound = true;
        }

        private void UnbindSocketEvents()
        {
            EnsureMainThread();
            if (!_socketEventsBound)
                return;

            ISocket boundSocket = _socketEventSource;
            if (boundSocket != null)
            {
                if (_socketClosedHandler != null)
                    boundSocket.Closed -= _socketClosedHandler;
                if (_socketErrorHandler != null)
                    boundSocket.ReceivedError -= _socketErrorHandler;
                if (_matchStateHandler != null)
                    boundSocket.ReceivedMatchState -= _matchStateHandler;
                if (_matchPresenceHandler != null)
                    boundSocket.ReceivedMatchPresence -= _matchPresenceHandler;
                if (_matchmakerMatchedHandler != null)
                    boundSocket.ReceivedMatchmakerMatched -= _matchmakerMatchedHandler;
            }

            _socketEventSource = null;
            _socketClosedHandler = null;
            _socketErrorHandler = null;
            _matchStateHandler = null;
            _matchPresenceHandler = null;
            _matchmakerMatchedHandler = null;
            _socketEventsBound = false;
            unchecked
            {
                _socketEventGeneration++;
            }
        }

        private void OnSocketClosed(
            string reason,
            ISocket socket,
            int socketEventGeneration)
        {
            EnsureMainThread();
            if (!IsSocketEventCurrent(socket, socketEventGeneration))
                return;

            // UnitySocket dispatches close notifications from its Update queue. A completed close can
            // therefore be observed after a serialized restart has already reconnected the same socket.
            if (_socket != null
                && _socket.IsConnected
                && (_lifecycleState == NetworkLifecycleState.StartingClient
                    || _lifecycleState == NetworkLifecycleState.ClientRunning))
            {
                return;
            }

            ClearMatchState(notifyDisconnected: true);
            if (!IsSocketEventCurrent(socket, socketEventGeneration))
                return;
            _lifecycleState = NetworkLifecycleState.Stopped;
            _lastError = TransportError.ConnectionClosed;
            _lastErrorMessage = reason ?? string.Empty;
            NotifyObserver(OnDisconnectedFromServer);
        }

        private void OnSocketError(
            Exception exception,
            ISocket socket,
            int socketEventGeneration)
        {
            EnsureMainThread();
            if (!IsSocketEventCurrent(socket, socketEventGeneration))
                return;
            RaiseError(null, TransportError.Unexpected, exception != null ? exception.Message : string.Empty);
        }
    }
}
#endif
