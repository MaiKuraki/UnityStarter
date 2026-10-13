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
        public void ClearSession()
        {
            EnsureMainThread();
            ThrowIfDisposed();
            unchecked
            {
                _sessionGeneration++;
            }
            _session = null;

            if (_connectOperationPending
                || _connectRequestQueued
                || _queuedConnectStartScheduled
                || _lifecycleState == NetworkLifecycleState.StartingClient)
            {
                UnbindSocketEvents();
                CancelCurrentOperations(createReplacement: true);
                ISocket socket = _socket;
                if (socket != null
                    && (_socketOwnedByAdapter || socket.IsConnected || socket.IsConnecting))
                {
                    RequestSocketClose(
                        socket,
                        reportErrors: false,
                        _socketOwnedByAdapter ? _ownedSocketDispatcher : null);
                }
                _lifecycleState = NetworkLifecycleState.Stopped;
            }
        }

        public bool TrySendMatchState(NetworkMatchId matchId, long operationCode, in ArraySegment<byte> payload, NetworkChannel channel = NetworkChannel.Reliable)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (channel != NetworkChannel.Reliable || !matchId.IsValid || !ValidatePayload(payload))
                return false;

            return QueueMatchState(matchId.Value, operationCode, payload, null) == NetworkSendStatus.Queued;
        }

        public void LeaveMatch(NetworkMatchId matchId)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (_socket == null || !matchId.IsValid)
                return;
            if (_leaveMatchOperationPending)
            {
                RaiseError(null, TransportError.Congestion, LeaveMatchCapacityError);
                return;
            }

            _leaveMatchOperationPending = true;
            LeaveMatchAsync(
                _socket,
                matchId.Value,
                _shutdown != null ? _shutdown.Token : CancellationToken.None,
                _operationGeneration).Forget();
        }

        public bool TryCancelMatchmaker(NetworkMatchmakerTicket ticket)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (_socket == null ||
                !ticket.IsValid ||
                !IsMatchmaking ||
                !CurrentTicket.Equals(ticket))
            {
                return false;
            }
            if (_cancelMatchmakerOperationPending)
                return false;

            _cancelMatchmakerOperationPending = true;
            CancelMatchmakerAsync(
                _socket,
                ticket.Value,
                _shutdown != null ? _shutdown.Token : CancellationToken.None,
                _operationGeneration).Forget();
            return true;
        }

        public bool TryGetLocalPresence(out NetworkPresence presence)
        {
            EnsureMainThread();
            if (_match != null && _match.Self != null)
            {
                presence = ToNetworkPresence(_match.Self);
                return presence.IsValid;
            }

            if (_session != null)
            {
                presence = new NetworkPresence(_session.UserId, string.Empty, _session.Username);
                return presence.IsValid;
            }

            presence = default;
            return false;
        }

        public void StartMatchmaker(string query, int minCount, int maxCount)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (_addMatchmakerOperationPending ||
                _cancelMatchmakerOperationPending ||
                _matchmakerJoinOperationPending ||
                IsMatchmaking ||
                CurrentTicket.IsValid)
            {
                RaiseError(null, TransportError.Congestion, MatchmakerStartCapacityError);
                return;
            }

            _addMatchmakerOperationPending = true;
            AddMatchmakerAsync(
                _socket,
                query,
                minCount,
                maxCount,
                _shutdown != null ? _shutdown.Token : CancellationToken.None,
                _operationGeneration).Forget();
        }

        private async UniTask LeaveMatchAsync(
            ISocket socket,
            string matchId,
            CancellationToken cancellationToken,
            int operationGeneration)
        {
            try
            {
                EnsureMainThread();
                await socket.LeaveMatchAsync(matchId);
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                if (_match != null && string.Equals(_match.Id, matchId, StringComparison.Ordinal))
                ClearMatchState(notifyDisconnected: true);
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                RaiseError(null, TransportError.Unexpected, e.Message);
            }
            finally
            {
                if (IsMainThread())
                    _leaveMatchOperationPending = false;
            }
        }

        private async UniTask CancelMatchmakerAsync(
            ISocket socket,
            string ticket,
            CancellationToken cancellationToken,
            int operationGeneration)
        {
            try
            {
                EnsureMainThread();
                await socket.RemoveMatchmakerAsync(ticket);
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                IsMatchmaking = false;
                CurrentTicket = default;
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                RaiseError(null, TransportError.Unexpected, e.Message);
            }
            finally
            {
                if (IsMainThread())
                    _cancelMatchmakerOperationPending = false;
            }
        }

        private async UniTask AddMatchmakerAsync(
            ISocket socket,
            string query,
            int minCount,
            int maxCount,
            CancellationToken cancellationToken,
            int operationGeneration)
        {
            try
            {
                EnsureMainThread();
                if (socket == null || !socket.IsConnected)
                {
                    RaiseError(null, TransportError.Unexpected, MissingSocketError);
                    return;
                }

                IMatchmakerTicket ticket = await socket.AddMatchmakerAsync(query, minCount, maxCount);
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                CurrentTicket = new NetworkMatchmakerTicket(ticket.Ticket);
                IsMatchmaking = true;
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                IsMatchmaking = false;
                RaiseError(null, TransportError.Unexpected, e.Message);
            }
            finally
            {
                if (IsMainThread())
                    _addMatchmakerOperationPending = false;
            }
        }

        private void OnReceivedMatchmakerMatched(
            IMatchmakerMatched matched,
            ISocket socket,
            int socketEventGeneration)
        {
            EnsureMainThread();
            if (matched == null ||
                _matchmakerJoinOperationPending ||
                !IsSocketEventCurrent(socket, socketEventGeneration) ||
                !IsMatchmaking ||
                !CurrentTicket.IsValid ||
                !string.Equals(CurrentTicket.Value, matched.Ticket, StringComparison.Ordinal))
            {
                return;
            }

            CancellationToken cancellationToken = _shutdown != null
                ? _shutdown.Token
                : CancellationToken.None;
            int operationGeneration = _operationGeneration;
            if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                return;

            _matchmakerJoinOperationPending = true;
            HandleMatchmakerMatchedAsync(
                matched,
                socket,
                cancellationToken,
                operationGeneration,
                CurrentTicket).Forget();
        }

        private async UniTask HandleMatchmakerMatchedAsync(
            IMatchmakerMatched matched,
            ISocket socket,
            CancellationToken cancellationToken,
            int operationGeneration,
            NetworkMatchmakerTicket expectedTicket)
        {
            try
            {
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket) ||
                    !IsMatchmaking ||
                    !CurrentTicket.Equals(expectedTicket) ||
                    !string.Equals(expectedTicket.Value, matched.Ticket, StringComparison.Ordinal))
                {
                    return;
                }

                if (string.IsNullOrEmpty(matched.MatchId) && string.IsNullOrEmpty(matched.Token))
                {
                    IsMatchmaking = false;
                    CurrentTicket = default;
                    RaiseError(null, TransportError.Unexpected, InvalidMatchmakerResultError);
                    return;
                }

                IsMatchmaking = false;
                CurrentTicket = new NetworkMatchmakerTicket(matched.Ticket);
                NotifyMatchedObservers(CurrentTicket, new NetworkMatchId(matched.MatchId));

                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket) ||
                    !CurrentTicket.Equals(expectedTicket))
                {
                    return;
                }

                IMatch joinedMatch = await socket.JoinMatchAsync(matched);
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                ClearMatchState(notifyDisconnected: true);
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                _match = joinedMatch;
                _matchId = joinedMatch.Id;
                AddInitialPresences(joinedMatch);
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                if (CurrentTicket.Equals(expectedTicket))
                    CurrentTicket = default;
                RaiseError(null, TransportError.Unexpected, e.Message);
            }
            finally
            {
                if (IsMainThread())
                    _matchmakerJoinOperationPending = false;
            }
        }

        private void NotifyMatchedObservers(
            NetworkMatchmakerTicket ticket,
            NetworkMatchId matchId)
        {
            try
            {
                OnMatched?.Invoke(ticket, matchId);
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                // Product observers do not own the transport state machine. Publish their
                // failure through the same observed UniTask boundary without aborting JoinMatch.
                PublishObserverException(exception);
            }
        }

        private void OnReceivedMatchPresence(
            IMatchPresenceEvent presenceEvent,
            ISocket socket,
            int socketEventGeneration)
        {
            EnsureMainThread();
            if (!IsSocketEventCurrent(socket, socketEventGeneration) ||
                presenceEvent == null ||
                !IsCurrentMatch(presenceEvent.MatchId))
            {
                return;
            }

            NetworkMatchId matchId = new NetworkMatchId(presenceEvent.MatchId);
            IEnumerable<IUserPresence> joins = presenceEvent.Joins;
            if (joins != null)
            {
                foreach (IUserPresence joined in joins)
                {
                    if (!IsSocketEventCurrent(socket, socketEventGeneration)
                        || !IsCurrentMatch(presenceEvent.MatchId))
                        return;
                    if (joined == null)
                        continue;

                    if (!IsLocalPresence(joined))
                    {
                        bool wasConnected = TryGetConnection(joined, out NakamaNetConnection existingConnection)
                                            && existingConnection.IsConnected;
                        NakamaNetConnection connection = GetOrCreateConnection(joined);
                        if (connection != null && !wasConnected)
                        {
                            NotifyObserver(OnClientConnected, (INetConnection)connection);
                            if (!IsSocketEventCurrent(socket, socketEventGeneration)
                                || !IsCurrentMatch(presenceEvent.MatchId)
                                || !IsCurrentConnection(connection))
                            {
                                return;
                            }
                        }
                    }
                    if (!IsSocketEventCurrent(socket, socketEventGeneration)
                        || !IsCurrentMatch(presenceEvent.MatchId))
                        return;
                    NotifyObserver(OnPresenceJoined, matchId, ToNetworkPresence(joined));
                }
            }

            IEnumerable<IUserPresence> leaves = presenceEvent.Leaves;
            if (leaves == null)
                return;

            foreach (IUserPresence left in leaves)
            {
                if (!IsSocketEventCurrent(socket, socketEventGeneration)
                    || !IsCurrentMatch(presenceEvent.MatchId))
                    return;
                if (left == null)
                    continue;

                if (TryGetConnection(left, out NakamaNetConnection connection))
                {
                    _connectionsBySessionId.Remove(GetPresenceKey(left));
                    _connectionsById.Remove(connection.ConnectionId);
                    connection.Invalidate();
                    NotifyObserver(OnClientDisconnected, (INetConnection)connection);
                }

                if (!IsSocketEventCurrent(socket, socketEventGeneration)
                    || !IsCurrentMatch(presenceEvent.MatchId))
                    return;
                NotifyObserver(OnPresenceLeft, matchId, ToNetworkPresence(left));
            }
        }

        private void OnReceivedMatchState(
            IMatchState state,
            ISocket socket,
            int socketEventGeneration)
        {
            EnsureMainThread();
            if (!IsSocketEventCurrent(socket, socketEventGeneration) ||
                state == null ||
                state.State == null)
                return;

            if (_match == null
                || string.IsNullOrEmpty(state.MatchId)
                || !string.Equals(_match.Id, state.MatchId, StringComparison.Ordinal))
            {
                return;
            }

            NakamaNetConnection connection;
            NetworkMessageDirection direction;
            if (state.UserPresence == null)
            {
                connection = GetOrCreateAuthorityConnection(state.MatchId);
                direction = NetworkMessageDirection.ServerToClient;
            }
            else
            {
                if (IsLocalPresence(state.UserPresence))
                    return;

                connection = GetOrCreateConnection(state.UserPresence);
                direction = NetworkMessageDirection.PeerToPeer;
            }

            if (connection == null)
                return;

            connection.RecordReceived(state.State.Length);
            Interlocked.Add(ref _bytesReceived, state.State.Length);
            Interlocked.Increment(ref _packetsReceived);

            var raw = new ArraySegment<byte>(state.State);
            NotifyObserver(
                OnMatchState,
                new NetworkMatchId(state.MatchId),
                (INetConnection)connection,
                raw,
                state.OpCode);
            if (!IsSocketEventCurrent(socket, socketEventGeneration)
                || !IsCurrentMatch(state.MatchId)
                || !IsCurrentConnection(connection))
                return;

            if (state.OpCode != _matchStateOpCode)
                return;

            if (!TryValidateIncoming(connection, raw, out NetworkEnvelopeHeader header, out ArraySegment<byte> payload))
                return;

            NotifyObserver(
                OnDataReceived,
                (INetConnection)connection,
                payload,
                GetChannelId(header.Channel));
            if (!IsSocketEventCurrent(socket, socketEventGeneration)
                || !IsCurrentMatch(state.MatchId)
                || !IsCurrentConnection(connection))
            {
                return;
            }
            try
            {
                var bytes = new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count);
                var message = new NetworkMessagePayload(
                    connection,
                    direction,
                    in header,
                    bytes);
                _messageHandlers.TryDispatch(in message);
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                if (IsSocketEventCurrent(socket, socketEventGeneration)
                    && IsCurrentMatch(state.MatchId)
                    && IsCurrentConnection(connection))
                {
                    RaiseError(connection, TransportError.InvalidReceive, e.Message);
                }
                else
                {
                    PublishObserverException(e);
                }
            }
        }

        private void AddInitialPresences(IMatch match)
        {
            if (match == null)
                return;

            if (match.Authoritative)
                GetOrCreateAuthorityConnection(match.Id);

            string selfKey = GetPresenceKey(match.Self);
            IEnumerable<IUserPresence> presences = match.Presences;
            if (presences == null)
                return;

            foreach (IUserPresence presence in presences)
            {
                if (!IsCurrentMatch(match.Id))
                    return;
                if (presence == null
                    || (!string.IsNullOrEmpty(selfKey)
                        && string.Equals(selfKey, GetPresenceKey(presence), StringComparison.Ordinal)))
                {
                    continue;
                }

                bool wasConnected = TryGetConnection(presence, out NakamaNetConnection existingConnection)
                                    && existingConnection.IsConnected;
                NakamaNetConnection connection = GetOrCreateConnection(presence);
                if (connection != null && !wasConnected)
                {
                    NotifyObserver(OnClientConnected, (INetConnection)connection);
                    if (!IsCurrentMatch(match.Id) || !IsCurrentConnection(connection))
                        return;
                }
                if (!IsCurrentMatch(match.Id))
                    return;
                NotifyObserver(
                    OnPresenceJoined,
                    new NetworkMatchId(match.Id),
                    ToNetworkPresence(presence));
            }
        }

        private NakamaNetConnection GetOrCreateAuthorityConnection(string matchId)
        {
            if (_match == null
                || !_match.Authoritative
                || string.IsNullOrEmpty(matchId)
                || !string.Equals(_match.Id, matchId, StringComparison.Ordinal))
            {
                return null;
            }

            if (_authorityConnection != null)
            {
                if (_authorityConnection.IsAuthorityRoute(matchId))
                    return _authorityConnection;

                _connectionsById.Remove(_authorityConnection.ConnectionId);
                _authorityConnection.Invalidate();
                _authorityConnection = null;
            }

            if (_connectionsById.Count >= _maxConnections || !TryAllocateConnectionId(out int connectionId))
            {
                _lastError = TransportError.Congestion;
                _lastErrorMessage = ConnectionCapacityError;
                return null;
            }

            _authorityConnection = new NakamaNetConnection(
                this,
                null,
                connectionId,
                NakamaConnectionRouteRole.Authority,
                matchId);
            _connectionsById.Add(connectionId, _authorityConnection);
            return _authorityConnection;
        }

        private NakamaNetConnection GetOrCreateConnection(IUserPresence presence)
        {
            if (presence == null)
                return null;

            string key = GetPresenceKey(presence);
            if (string.IsNullOrEmpty(key))
                return null;

            if (_connectionsBySessionId.TryGetValue(key, out NakamaNetConnection connection))
            {
                if (connection.IsValid)
                    return connection;

                _connectionsBySessionId.Remove(key);
                _connectionsById.Remove(connection.ConnectionId);
                connection.Invalidate();
            }

            if (_connectionsById.Count >= _maxConnections || !TryAllocateConnectionId(out int connectionId))
            {
                _lastError = TransportError.Congestion;
                _lastErrorMessage = ConnectionCapacityError;
                return null;
            }

            connection = new NakamaNetConnection(
                this,
                presence,
                connectionId,
                NakamaConnectionRouteRole.Peer,
                key);
            _connectionsBySessionId[key] = connection;
            _connectionsById.Add(connectionId, connection);
            return connection;
        }

        private bool TryGetConnection(IUserPresence presence, out NakamaNetConnection connection)
        {
            if (presence == null)
            {
                connection = null;
                return false;
            }

            return _connectionsBySessionId.TryGetValue(GetPresenceKey(presence), out connection)
                   && IsCurrentConnection(connection);
        }

        private bool IsCurrentConnection(NakamaNetConnection connection)
        {
            if (connection == null
                || !connection.IsValid
                || !connection.IsConnected
                || !connection.IsOwnedBy(this)
                || !_connectionsById.TryGetValue(connection.ConnectionId, out NakamaNetConnection currentById)
                || !ReferenceEquals(currentById, connection))
            {
                return false;
            }

            if (connection.RouteRole == NakamaConnectionRouteRole.Authority)
            {
                return ReferenceEquals(_authorityConnection, connection)
                       && _match != null
                       && _match.Authoritative
                       && connection.IsAuthorityRoute(_match.Id);
            }

            return !string.IsNullOrEmpty(connection.RouteKey)
                   && _connectionsBySessionId.TryGetValue(connection.RouteKey, out NakamaNetConnection currentBySession)
                   && ReferenceEquals(currentBySession, connection);
        }

        internal void SetPlayerId(NakamaNetConnection connection, ulong playerId)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (!IsCurrentConnection(connection))
                throw new ObjectDisposedException(nameof(NakamaNetConnection));

            connection.SetPlayerIdValue(playerId);
        }

        private static string GetPresenceKey(IUserPresence presence)
        {
            if (presence == null)
                return string.Empty;

            return presence.SessionId ?? string.Empty;
        }

        private bool IsLocalPresence(IUserPresence presence)
        {
            if (presence == null || _match == null || _match.Self == null)
                return false;

            string key = GetPresenceKey(presence);
            return !string.IsNullOrEmpty(key)
                   && string.Equals(key, GetPresenceKey(_match.Self), StringComparison.Ordinal);
        }

        private bool IsCurrentMatch(string matchId)
        {
            return _match != null
                   && !string.IsNullOrEmpty(matchId)
                   && string.Equals(_match.Id, matchId, StringComparison.Ordinal);
        }

        private bool TryAllocateConnectionId(out int connectionId)
        {
            if (_nextConnectionId == int.MaxValue)
            {
                connectionId = 0;
                return false;
            }

            connectionId = checked(_nextConnectionId + 1);
            _nextConnectionId = connectionId;
            return true;
        }

        private void ClearMatchState(bool notifyDisconnected)
        {
            int notificationGeneration = _operationGeneration;
            List<NakamaNetConnection> disconnected = notifyDisconnected && _connectionsBySessionId.Count > 0
                ? new List<NakamaNetConnection>(_connectionsBySessionId.Values)
                : null;

            foreach (NakamaNetConnection connection in _connectionsById.Values)
                connection.Invalidate();

            _match = null;
            _authorityConnection = null;
            IsMatchmaking = false;
            CurrentTicket = default;
            _connectionsBySessionId.Clear();
            _connectionsById.Clear();

            if (disconnected == null)
                return;

            for (int i = 0; i < disconnected.Count; i++)
            {
                NotifyObserver(OnClientDisconnected, (INetConnection)disconnected[i]);
                if (_isDestroyed || notificationGeneration != _operationGeneration)
                    return;
            }
        }

        private static NetworkPresence ToNetworkPresence(IUserPresence presence)
        {
            return presence == null
                ? default
                : new NetworkPresence(presence.UserId, presence.SessionId, presence.Username);
        }
    }
}
#endif
