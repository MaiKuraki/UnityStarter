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
        private INetworkRuntimeContext BuildRuntimeContext()
        {
            EnsureMainThread();
            var context = new NetworkRuntimeContext(NetworkRuntimeIds.Nakama, "Nakama", this, Features);
            context.AddService<INetworkSessionService>(this);
            context.AddService<INetworkMatchStateService>(this);
            context.AddService<INetworkMatchmakerService>(this);
            context.AddService<INetworkPresenceService>(this);
            if (_client != null)
                context.AddService<IClient>(_client);
            if (_socket != null)
                context.AddService<ISocket>(_socket);
            return context.Build();
        }

        private void RefreshRuntimeContext()
        {
            if (_runtimeContext == null)
                return;

            _runtimeContext.Dispose();
            _runtimeContext = BuildRuntimeContext();
        }

        private void NotifyObserver(Action observer)
        {
            try
            {
                observer?.Invoke();
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                PublishObserverException(exception);
            }
        }

        private void NotifyObserver<T>(Action<T> observer, T value)
        {
            try
            {
                observer?.Invoke(value);
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                PublishObserverException(exception);
            }
        }

        private void NotifyObserver<T1, T2>(Action<T1, T2> observer, T1 value1, T2 value2)
        {
            try
            {
                observer?.Invoke(value1, value2);
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                PublishObserverException(exception);
            }
        }

        private void NotifyObserver<T1, T2, T3>(
            Action<T1, T2, T3> observer,
            T1 value1,
            T2 value2,
            T3 value3)
        {
            try
            {
                observer?.Invoke(value1, value2, value3);
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                PublishObserverException(exception);
            }
        }

        private void NotifyObserver<T1, T2, T3, T4>(
            Action<T1, T2, T3, T4> observer,
            T1 value1,
            T2 value2,
            T3 value3,
            T4 value4)
        {
            try
            {
                observer?.Invoke(value1, value2, value3, value4);
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                PublishObserverException(exception);
            }
        }

        private void RaiseError(INetConnection connection, TransportError error, string message)
        {
            EnsureMainThread();
            if (_isDestroyed)
                return;

            _lastError = error;
            _lastErrorMessage = message ?? string.Empty;
            try
            {
                OnError?.Invoke(connection, error, _lastErrorMessage);
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                // Error observers cannot re-enter an async transport catch block and cause a
                // duplicate notification. Their failure still reaches the UniTask global hook.
                PublishObserverException(exception);
            }
        }

        private static void PublishObserverException(Exception exception)
        {
            UniTask.FromException(exception).Forget();
        }

        private bool IsOperationCurrent(int operationGeneration, CancellationToken cancellationToken, ISocket socket)
        {
            return !_isDestroyed
                   && !cancellationToken.IsCancellationRequested
                   && operationGeneration == _operationGeneration
                   && ReferenceEquals(socket, _socket);
        }

        private bool IsSocketEventCurrent(ISocket socket, int socketEventGeneration)
        {
            return _socketEventsBound
                   && socketEventGeneration == _socketEventGeneration
                   && ReferenceEquals(socket, _socketEventSource)
                   && ReferenceEquals(socket, _socket);
        }

        private bool IsMainThread()
        {
            return _mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId;
        }

        private static bool IsRecoverableException(Exception exception)
        {
            return exception is not OutOfMemoryException
                   && exception is not AccessViolationException;
        }

        private void ThrowIfDisposed()
        {
            if (_isDestroyed || _lifecycleState == NetworkLifecycleState.Disposed)
                throw new ObjectDisposedException(nameof(NakamaNetAdapter));
        }

        private void EnsureMainThread()
        {
            if (!IsMainThread())
            {
                throw new InvalidOperationException(
                    "NakamaNetAdapter requires the injected ISocket callbacks and task continuations to run on the Unity main owner thread after Awake. Cross-thread callbacks are rejected and are not queued.");
            }
        }
    }
}
#endif
