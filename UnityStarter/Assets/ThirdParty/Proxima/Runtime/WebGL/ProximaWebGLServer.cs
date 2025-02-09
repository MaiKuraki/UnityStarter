#if UNITY_WEBGL

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace Proxima
{
    internal class ProximaWebGLServer : ProximaServer
    {
        private ConcurrentQueue<(ProximaConnection, string)> _receiveQueue;
        private ProximaDispatcher _dispatcher;
        private WaitForSeconds _wait = new WaitForSeconds(1);
        private ProximaStatus _status;
        private ProximaWebGLConnection _pendingConnection;
        private List<ProximaWebGLConnection> _activeConnections = new List<ProximaWebGLConnection>();
        private string _displayName;
        private string _password;

        public ProximaWebGLServer(ProximaDispatcher dispatcher, ProximaStatus status)
        {
            _dispatcher = dispatcher;
            _status = status;
        }

        public void Start(string displayName, string password)
        {
            Log.Info("Proxima Inspector started for WebGL.");
            _displayName = displayName;
            _password = password;
            _receiveQueue = new ConcurrentQueue<(ProximaConnection, string)>();
            _status.SetConnectInfo(Application.streamingAssetsPath + "/proxima/index.html");
            CreateNewConnection();
        }

        private IEnumerator CreateNewConnectionCoroutine()
        {
            yield return _wait;
            if (_pendingConnection != null)
            {
                yield break;
            }

            try
            {

                Log.Verbose("Opening new Proxima WebGL connection: ");
                var conn = new ProximaWebGLConnection(_displayName, _password, _dispatcher, _status, _receiveQueue);

                conn.OnConnect += () => {
                    Log.Verbose("Connected.");
                    _activeConnections.Add(conn);
                    _status.IncrementConnections();
                    CreateNewConnection();
                };

                conn.OnClose += () => {
                    Log.Verbose("Disconnected.");
                    conn.Dispose();

                    if (conn == _pendingConnection)
                    {
                        if (_receiveQueue != null)
                        {
                            Log.Verbose("Failed to create WebGL connection. Check for JavaScript errors.");
                            _status.SetError("Failed to create WebGL connection. Check for JavaScript errors.");
                        }
                    }
                    else
                    {
                        _activeConnections.Remove(conn);
                        _status.DecrementConnections();
                    }
                };

                _pendingConnection = conn;
            }
            catch (System.Exception e)
            {
                Log.Exception(e);
            }
        }

        private void CreateNewConnection()
        {
            _pendingConnection = null;
            if (_receiveQueue != null && _dispatcher != null)
            {
                _dispatcher.StartCoroutine(CreateNewConnectionCoroutine());
            }
        }

        public void Stop()
        {
            foreach (var connection in _activeConnections)
            {
                connection.Dispose();
            }

            _activeConnections.Clear();
            _pendingConnection?.Dispose();
            _receiveQueue = null;
        }

        public bool TryGetMessage(out (ProximaConnection, string) message)
        {
            if (_receiveQueue != null)
            {
                return _receiveQueue.TryDequeue(out message);
            }
            else
            {
                message = (null, "");
                return false;
            }
        }
    }
}

#endif