using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using UnityEngine;
using ProximaWebSocketSharp.Server;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Proxima
{
    internal class ProximaEmbeddedServer : ProximaServer
    {
        private ConcurrentQueue<(ProximaConnection, ProximaRequest)> _receiveQueue;
        private HttpServer _server;
        private ProximaDispatcher _dispatcher;
        private int _port;
        private bool _useHttps;
        private PfxAsset _cert;
        private string _certPass;
        private ProximaFileServer _fileServer;
        private ProximaStatus _status;

        public ProximaEmbeddedServer(ProximaDispatcher dispatcher, ProximaStatus status, int port, bool useHttps, PfxAsset cert, string certPass)
        {
            _dispatcher = dispatcher;
            _port = port;
            _useHttps = useHttps;
            _cert = cert;
            _certPass = certPass;
            _status = status;

            if (_useHttps && _cert == null)
            {
                _cert = Resources.Load<PfxAsset>("Proxima/ProximaEmbeddedCert");
                _certPass = "proximapass";
            }

            _fileServer = new ProximaFileServer();
        }

        public void Start(string displayName, string password)
        {
            _server = new HttpServer(System.Net.IPAddress.Any, _port, _useHttps);
            _server.Log.Level = ProximaWebSocketSharp.LogLevel.Trace;

            if (_useHttps)
            {
                _server.SslConfiguration.ServerCertificate = new X509Certificate2(_cert.Bytes, _certPass);
                _server.SslConfiguration.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            }

            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            _server.OnGet += (sender, e) =>
            {
                var req = e.Request;
                var res = e.Response;
                var fileResp = _fileServer.GetFileResponse(req.RawUrl, req.Headers["If-Modified-Since"]);

                if (fileResp.HasValue)
                {
                    if (fileResp.Value.FileChanged)
                    {
                        res.AppendHeader("Last-Modified", fileResp.Value.LastModifiedTime);
                        res.ContentEncoding = System.Text.Encoding.UTF8;
                        res.ContentType = fileResp.Value.ContentType;
                        res.ContentLength64 = fileResp.Value.Bytes.Length;
                        res.OutputStream.Write(fileResp.Value.Bytes, 0, fileResp.Value.Bytes.Length);
                        res.Close();
                    }
                    else
                    {
                        res.StatusCode = 304;
                        res.Close();
                    }
                }
                else
                {
                    res.StatusCode = (int)System.Net.HttpStatusCode.NotFound;
                    res.Close();
                }
            };

            _receiveQueue = new ConcurrentQueue<(ProximaConnection, ProximaRequest)>();
            _server.AddWebSocketService<ProximaEmbeddedConnection>("/api", (api) => api.Initialize(displayName, password, _dispatcher, _status, _receiveQueue));


            _server.Start();
            UpdateConnectionInfo();
        }

        private async void UpdateConnectionInfo()
        {
            var ip = await GetIpAddress();
            var connectionInfo = (_useHttps ? "https" : "http") + "://" + ip + ":" + _port;
            Log.Info("Proxima Inspector started on " + connectionInfo);
            _status.SetConnectInfo(connectionInfo);
        }

        private async Task<string> GetIpAddress()
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    // Try to connect to Google DNS servers. This is a more reliable way to get the local IP address.
                    var task = socket.ConnectAsync("8.8.8.8", 53);
                    await Task.WhenAny(task, Task.Delay(100));
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        return socket.LocalEndPoint.ToString().Split(':')[0];
                    }
                }
            }
            catch (Exception e)
            {
                Log.Exception(e);
            }

            // If not connected to the internet, or this is taking too long, fallback to the first IPv4 address.
            return Dns.GetHostEntry(Dns.GetHostName())
                .AddressList.First(
                    f => f.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .ToString();
        }

        public void Stop()
        {
            _server?.Stop();
            _server = null;
            _receiveQueue = null;
        }

        public bool TryGetMessage(out (ProximaConnection, ProximaRequest) message)
        {
            if (_receiveQueue != null)
            {
                return _receiveQueue.TryDequeue(out message);
            }
            else
            {
                message = (null, null);
                return false;
            }
        }
    }
}