using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
        private ConcurrentQueue<(ProximaConnection, string)> _receiveQueue;
        private HttpServer _server;
        private ProximaDispatcher _dispatcher;
        private int _port;
        private bool _useHttps;
        private PfxAsset _cert;
        private string _certPass;
        private Dictionary<string, ProximaStatic.StaticFile> _pathToFile;
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

            var staticFiles = Resources.Load<ProximaStatic>("Proxima/web");
            _pathToFile = new Dictionary<string, ProximaStatic.StaticFile>();
            foreach (var file in staticFiles.Files)
            {
                _pathToFile.Add(file.Path, file);
            }
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
                var path = req.RawUrl.Split('?')[0];

                path = path == "/" ? "index.html" : path.Substring(1);

                if (_pathToFile.TryGetValue(path, out var file) || _pathToFile.TryGetValue(path + ".html", out file))
                {
                    var lastModifiedDt = epoch.AddMilliseconds(file.LastModified);
                    if (req.Headers.AllKeys.Contains("If-Modified-Since"))
                    {
                        var ifModifiedSince = req.Headers["If-Modified-Since"];

                        try
                        {
                            if (HttpDateParse.ParseHttpDate(ifModifiedSince, out var ifModifiedSinceDt))
                            {
                                ifModifiedSinceDt = ifModifiedSinceDt.ToUniversalTime();
                                if (lastModifiedDt <= ifModifiedSinceDt)
                                {
                                    res.StatusCode = 304;
                                    res.Close();
                                    return;
                                }
                            }
                        } catch (Exception) {}
                    }

                    var lastModified = string.Format("{0:ddd, dd MMM yyyy HH:mm:ss} GMT", lastModifiedDt);
                    res.AppendHeader("Last-Modified", lastModified);
                    res.ContentEncoding = System.Text.Encoding.UTF8;
                    res.ContentType = ProximaMimeTypes.Get(Path.GetExtension(file.Path));
                    res.ContentLength64 = file.Bytes.Length;
                    res.OutputStream.Write(file.Bytes, 0, file.Bytes.Length);
                    res.Close();
                }
                else
                {
                    res.StatusCode = (int)System.Net.HttpStatusCode.NotFound;
                    res.Close();
                }
            };

            _receiveQueue = new ConcurrentQueue<(ProximaConnection, string)>();
            _server.AddWebSocketService<ProximaEmbeddedConnection>("/api", (api) => api.Initialize(displayName, password, _dispatcher, _status, _receiveQueue));


            _server.Start();
            UpdateConnectionInfo();
        }

        public async void UpdateConnectionInfo()
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