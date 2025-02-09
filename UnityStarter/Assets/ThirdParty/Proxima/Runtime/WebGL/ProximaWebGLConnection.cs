#if UNITY_WEBGL

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

namespace Proxima
{
    internal class ProximaWebGLConnection : ProximaConnection, IDisposable
    {
        public bool Open => _open;

        public event Action OnConnect;
        public event Action OnClose;

        private ConcurrentQueue<(ProximaConnection, string)> _receiveQueue;

        private string _password;
        private bool _passwordProvided = false;
        private ProximaInstanceHello _hello;
        private DateTime _lastListTime;
        private DateTime _lastSelectTime;
        private ProximaStatus _status;
        private ProximaDispatcher _dispatcher;
        private bool _open;

        private static Dictionary<string, ProximaWebGLConnection> _connections = new Dictionary<string, ProximaWebGLConnection>();

        [DllImport("__Internal")]
        private static extern void ProximaWebGLCreate(
            string displayName,
            string instanceId,
            string productName,
            string companyName,
            string platform,
            string productVersion,
            string proximaVersion,
            string connectionId,
            Action<string, string> messageCb,
            Action<string> closeCB);

        [DllImport("__Internal")]
        private static extern void ProximaWebGLSelected(string connectionId);

        [DllImport("__Internal")]
        private static extern void ProximaWebGLOpenOnMouseUp(string url);

        [DllImport("__Internal")]
        private static extern void ProximaWebGLSend(string connectionId, string message);

        [DllImport("__Internal")]
        private static extern void ProximaWebGLDestroy(string connectionId);

        public ProximaWebGLConnection(string displayName, string password, ProximaDispatcher dispatcher, ProximaStatus status, ConcurrentQueue<(ProximaConnection, string)> queue)
        {
            _receiveQueue = queue;
            _dispatcher = dispatcher;
            _status = status;

            _hello = ProximaSerialization.CreateHello(displayName);
            _password = ProximaSerialization.HashPassword(password, _hello.ConnectionId);

            _connections.Add(_hello.ConnectionId, this);

            ProximaWebGLCreate(
                _hello.DisplayName,
                _hello.InstanceId,
                _hello.ProductName,
                _hello.CompanyName,
                _hello.Platform,
                _hello.ProductVersion,
                _hello.ProximaVersion,
                _hello.ConnectionId,
                OnMessageStatic,
                OnCloseStatic);
        }

        public static void OpenOnMouseUp(string url)
        {
            ProximaWebGLOpenOnMouseUp(url);
        }

        [MonoPInvokeCallback(typeof(Action<string, string>))]
        public static void OnMessageStatic(string connectionId, string message)
        {
            try
            {
                _connections[connectionId].OnMessageInstance(message);
            }
            catch (Exception e)
            {
                Log.Exception(e);
            }
        }

        [MonoPInvokeCallback(typeof(Action<string>))]
        public static void OnCloseStatic(string connectionId)
        {
            try
            {
                _connections[connectionId].OnCloseInstance();
            }
            catch (Exception e)
            {
                Log.Exception(e);
            }
        }

        private void OnCloseInstance()
        {
            _open = false;
            _dispatcher.Dispatch(() => {
                if (_passwordProvided)
                {
                    OnClose?.Invoke();
                }
            });
        }

        public void SendMessage(MemoryStream data)
        {
            var message = System.Text.Encoding.UTF8.GetString(data.GetBuffer(), 0, (int)data.Length);
            Log.Verbose("Sending: " + message);
            ProximaWebGLSend(_hello.ConnectionId, message);
        }

        private void OnMessageInstance(string message)
        {
            Log.Verbose("Received: " + message);

            if (_passwordProvided)
            {
                _receiveQueue.Enqueue((this, message));
            }
            else
            {
                ProximaRequest request;

                try
                {
                    request = JsonUtility.FromJson<ProximaRequest>(message);
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to parse request: " + ex.Message);
                    return;
                }

                if (request.Type == ProximaRequestType.Select)
                {
                    if (_lastSelectTime != null && DateTime.Now - _lastSelectTime < TimeSpan.FromSeconds(1))
                    {
                        SendMessage(ProximaSerialization.ErrorResponse(request, "Too many requests."));
                        return;
                    }

                    MemoryStream response;
                    if (request.Cmd != _hello.InstanceId)
                    {
                        response = ProximaSerialization.ErrorResponse(request, "Invalid selection.");
                    }
                    else if (request.Args.Length != 1 || request.Args[0] != _password)
                    {
                        response = ProximaSerialization.ErrorResponse(request, "Invalid password.");
                    }
                    else
                    {
                        response = ProximaSerialization.DataResponse(request, "OK");
                        _open = true;
                        _passwordProvided = true;
                        _dispatcher.Dispatch(() => OnConnect?.Invoke());
                    }

                    SendMessage(response);
                    _lastSelectTime = DateTime.Now;
                }
                else
                {
                    Log.Info("Unknown request: " + request.Type);
                }
            }
        }

        public void Dispose()
        {
            ProximaWebGLDestroy(_hello.ConnectionId);
        }
    }
}

#endif