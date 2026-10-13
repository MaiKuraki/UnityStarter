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
        public void StartServer()
        {
            EnsureMainThread();
            ThrowIfDisposed();
            throw new NotSupportedException(UnsupportedServerError);
        }

        public void StartClient(string address)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (!string.IsNullOrWhiteSpace(address)
                && !string.Equals(address, _host, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Nakama endpoint selection is owned by the adapter Host, Port, and Scheme configuration.",
                    nameof(address));
            }

            if (_lifecycleState == NetworkLifecycleState.ClientRunning
                && _socket != null
                && _socket.IsConnected)
            {
                return;
            }

            if (_connectOperationPending || _queuedConnectStartScheduled)
            {
                bool isSameActiveRequest = ReferenceEquals(_connectTaskSocket, _socket)
                                           && _connectTaskGeneration == _operationGeneration
                                           && _shutdown != null
                                           && !_shutdown.IsCancellationRequested
                                           && _lifecycleState == NetworkLifecycleState.StartingClient;
                if (isSameActiveRequest)
                    return;

                ThrowIfInjectedSocketRetired();
                QueueLatestConnectRequest();
                return;
            }

            ThrowIfInjectedSocketRetired();
            BeginConnect();
        }

        private void BeginConnect()
        {
            EnsureMainThread();
            ThrowIfDisposed();
            PrepareSocketForConnectionEpoch();
            CancellationToken cancellationToken = CancelCurrentOperations(createReplacement: true);
            int operationGeneration = _operationGeneration;
            _connectTaskSocket = _socket;
            _connectTaskGeneration = operationGeneration;
            _connectOperationPending = true;
            _connectTask = ConnectAndJoinAsync(cancellationToken, operationGeneration);
            _connectTask.AsUniTask(useCurrentSynchronizationContext: false).Forget();
        }

        public void Stop()
        {
            EnsureMainThread();
            if (_isDestroyed)
                return;
            UnbindSocketEvents();
            CancelCurrentOperations(createReplacement: true);

            ISocket socket = _socket;
            bool shouldClose = socket != null
                               && (_socketOwnedByAdapter || socket.IsConnected || socket.IsConnecting);

            ClearMatchState(notifyDisconnected: false);
            _lifecycleState = NetworkLifecycleState.Stopped;
            if (shouldClose)
            {
                RequestSocketClose(
                    socket,
                    reportErrors: true,
                    _socketOwnedByAdapter ? _ownedSocketDispatcher : null);
            }
        }

        public void Disconnect(INetConnection connection)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            throw new NotSupportedException(
                "Nakama clients cannot disconnect an individual remote presence. Stop the local adapter to leave the match.");
        }

        private async Task ConnectAndJoinAsync(
            CancellationToken cancellationToken,
            int operationGeneration)
        {
            IClient client = null;
            ISocket socket = null;

            try
            {
                EnsureClientAndSocket();
                client = _client;
                socket = _socket;
                if (client == null)
                    throw new InvalidOperationException(MissingClientError);
                if (socket == null)
                    throw new InvalidOperationException(MissingSocketError);

                _lifecycleState = NetworkLifecycleState.StartingClient;

                Task pendingClose = GetPendingSocketCloseTask(socket);
                if (pendingClose != null && !pendingClose.IsCompleted)
                    await pendingClose;
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;

                if (_session == null && _autoAuthenticateDevice)
                {
                    string deviceId = string.IsNullOrEmpty(_deviceIdOverride) ? SystemInfo.deviceUniqueIdentifier : _deviceIdOverride;
                    int authenticationSessionGeneration = _sessionGeneration;
                    Task<ISession> authenticationTask;
                    ISession authenticatedSession = await AuthenticateDeviceSingleFlightAsync(
                        client,
                        deviceId,
                        _connectTimeoutSeconds,
                        cancellationToken,
                        authenticationSessionGeneration);
                    authenticationTask = _authenticationTask;
                    EnsureMainThread();
                    if (!IsOperationCurrent(operationGeneration, cancellationToken, socket)
                        || authenticationSessionGeneration != _sessionGeneration)
                        return;
                    _session = authenticatedSession;
                    if (ReferenceEquals(_authenticationTask, authenticationTask)
                        && _authenticationSessionGeneration == authenticationSessionGeneration)
                    {
                        _authenticationTask = null;
                        _authenticationClient = null;
                        _authenticationSessionGeneration = 0;
                    }
                }

                if (_session == null)
                {
                    RaiseError(null, TransportError.Unexpected, MissingSessionError);
                    return;
                }

                if (!socket.IsConnected)
                    await socket.ConnectAsync(_session, _appearOnline, _connectTimeoutSeconds, _languageTag);
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                {
                    if (socket.IsConnected)
                        RequestSocketClose(socket, reportErrors: false);
                    return;
                }

                if (_joinMatchOnConnect && !string.IsNullOrEmpty(_matchId))
                {
                    IMatch joinedMatch = await socket.JoinMatchAsync(_matchId, null);
                    EnsureMainThread();
                    if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    {
                        if (socket.IsConnected)
                            RequestSocketClose(socket, reportErrors: false);
                        return;
                    }
                    ClearMatchState(notifyDisconnected: true);
                    if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                        return;
                    _match = joinedMatch;
                }

                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;

                _lifecycleState = NetworkLifecycleState.ClientRunning;
                NotifyObserver(OnConnectedToServer);
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                AddInitialPresences(_match);
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                _lifecycleState = NetworkLifecycleState.Faulted;
                RaiseError(
                    null,
                    e is TimeoutException ? TransportError.Timeout : TransportError.Unexpected,
                    e.Message);
            }
            finally
            {
                if (IsMainThread())
                {
                    _connectOperationPending = false;
                    ScheduleQueuedConnect();
                }
            }
        }

        private async Task<ISession> AuthenticateDeviceSingleFlightAsync(
            IClient client,
            string deviceId,
            int timeoutSeconds,
            CancellationToken cancellationToken,
            int authenticationSessionGeneration)
        {
            while (true)
            {
                EnsureMainThread();
                Task<ISession> authenticationTask = _authenticationTask;
                if (authenticationTask == null
                    || authenticationTask.IsCompleted
                    || (ReferenceEquals(_authenticationClient, client)
                        && _authenticationSessionGeneration == authenticationSessionGeneration))
                {
                    authenticationTask = GetOrCreateAuthenticationTask(
                        client,
                        deviceId,
                        authenticationSessionGeneration);
                    return await AwaitWithTimeoutAsync(authenticationTask, timeoutSeconds, cancellationToken);
                }

                try
                {
                    await AwaitWithTimeoutAsync(authenticationTask, timeoutSeconds, cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // A previous client's provider task reached the canceled state. The slot is
                    // terminal and can be reused without treating that old result as this request.
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (TimeoutException)
                {
                    throw;
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    // Faults from a previous client are already observed by the authentication
                    // continuation. Once terminal, they do not poison the next client's request.
                }

                EnsureMainThread();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private Task<ISession> GetOrCreateAuthenticationTask(
            IClient client,
            string deviceId,
            int authenticationSessionGeneration)
        {
            EnsureMainThread();
            if (ReferenceEquals(_authenticationClient, client)
                && _authenticationSessionGeneration == authenticationSessionGeneration
                && _authenticationTask != null
                && !_authenticationTask.IsCanceled
                && !_authenticationTask.IsFaulted)
            {
                return _authenticationTask;
            }

            if (_authenticationTask != null && !_authenticationTask.IsCompleted)
                throw new InvalidOperationException(AuthenticationOperationCapacityError);

            Task<ISession> authenticationTask = client.AuthenticateDeviceAsync(deviceId, _username, _createAccount);
            _authenticationClient = client;
            _authenticationSessionGeneration = authenticationSessionGeneration;
            _authenticationTask = authenticationTask;
            _ = authenticationTask.ContinueWith(
                static faulted => { _ = faulted.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return authenticationTask;
        }

        private static async Task<T> AwaitWithTimeoutAsync<T>(
            Task<T> operation,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            int timeoutMilliseconds = timeoutSeconds >= int.MaxValue / 1000
                ? int.MaxValue
                : timeoutSeconds * 1000;
            Task timeoutTask = Task.Delay(timeoutMilliseconds, timeout.Token);
            Task completed = await Task.WhenAny(operation, timeoutTask);
            if (ReferenceEquals(completed, operation))
            {
                timeout.Cancel();
                return await operation;
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"Nakama operation exceeded the {timeoutSeconds}-second timeout.");
        }

        private void QueueLatestConnectRequest()
        {
            if (_isDestroyed || _shutdown == null || _shutdown.IsCancellationRequested)
                return;

            _connectRequestQueued = true;
            _queuedConnectSocket = _socket;
            _queuedConnectGeneration = _operationGeneration;
        }

        private void ScheduleQueuedConnect()
        {
            if (!_connectRequestQueued || _queuedConnectStartScheduled || _isDestroyed)
                return;

            CancellationToken cancellationToken = _shutdown != null
                ? _shutdown.Token
                : CancellationToken.None;
            _queuedConnectStartScheduled = true;
            StartQueuedConnectAsync(cancellationToken).Forget();
        }

        private async UniTask StartQueuedConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (IsMainThread())
                {
                    _queuedConnectStartScheduled = false;
                    ScheduleQueuedConnect();
                }
                return;
            }

            EnsureMainThread();
            _queuedConnectStartScheduled = false;
            if (!_connectRequestQueued)
                return;

            ISocket expectedSocket = _queuedConnectSocket;
            int expectedGeneration = _queuedConnectGeneration;
            ClearQueuedConnectRequest();
            if (_isDestroyed
                || cancellationToken.IsCancellationRequested
                || expectedGeneration != _operationGeneration
                || !ReferenceEquals(expectedSocket, _socket))
            {
                return;
            }

            BeginConnect();
        }

        private void ClearQueuedConnectRequest()
        {
            _connectRequestQueued = false;
            _queuedConnectSocket = null;
            _queuedConnectGeneration = 0;
        }

        private CancellationToken CancelCurrentOperations(bool createReplacement)
        {
            unchecked
            {
                _operationGeneration++;
            }

            ClearQueuedConnectRequest();

            CancellationTokenSource previous = _shutdown;
            _shutdown = createReplacement ? new CancellationTokenSource() : null;
            if (previous != null)
            {
                previous.Cancel();
                previous.Dispose();
            }

            return _shutdown != null ? _shutdown.Token : CancellationToken.None;
        }
    }
}
#endif
