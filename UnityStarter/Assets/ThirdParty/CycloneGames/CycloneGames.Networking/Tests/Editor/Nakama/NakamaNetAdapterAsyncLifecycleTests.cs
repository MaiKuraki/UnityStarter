using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Nakama;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.Networking.Adapter.Nakama.Tests.Editor
{
    public sealed class NakamaNetAdapterAsyncLifecycleTests
    {
        private const string Host = "127.0.0.1";

        private GameObject _gameObject;
        private NakamaNetAdapter _adapter;
        private IClient _client;
        private ISocket _socket;
        private ControllableSocketAdapter _socketAdapter;
        private CountingHttpAdapter _httpAdapter;
        private readonly List<ControllableSocketAdapter> _socketAdapters =
            new List<ControllableSocketAdapter>();
        private readonly List<GameObject> _autoCreatedSocketObjects = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return null;

            var preexistingSocketIds = new HashSet<int>();
            UnitySocket[] preexistingSockets = UnityEngine.Object.FindObjectsOfType<UnitySocket>(true);
            for (int i = 0; i < preexistingSockets.Length; i++)
                preexistingSocketIds.Add(preexistingSockets[i].GetInstanceID());

            _gameObject = new GameObject(nameof(NakamaNetAdapterAsyncLifecycleTests));
            _adapter = _gameObject.AddComponent<NakamaNetAdapter>();
            _httpAdapter = new CountingHttpAdapter();
            _client = new Client("http", Host, 7350, "defaultkey", _httpAdapter, false);
            _socketAdapter = new ControllableSocketAdapter();
            _socket = global::Nakama.Socket.From(_client, _socketAdapter);
            _socketAdapters.Add(_socketAdapter);
            SetPrivateField(_adapter, "_client", _client);
            SetPrivateField(_adapter, "_socket", _socket);
            InvokePrivateMethod(_adapter, "Awake");
            UnitySocket[] socketsAfterAwake = UnityEngine.Object.FindObjectsOfType<UnitySocket>(true);
            for (int i = 0; i < socketsAfterAwake.Length; i++)
            {
                UnitySocket candidate = socketsAfterAwake[i];
                if (!preexistingSocketIds.Contains(candidate.GetInstanceID()))
                    _autoCreatedSocketObjects.Add(candidate.gameObject);
            }

            SetPrivateField(_adapter, "_autoAuthenticateDevice", false);
            SetPrivateField(_adapter, "_joinMatchOnConnect", false);

            _adapter.Initialize(_client, _socket, new TestSession());
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_adapter != null)
                _adapter.Stop();

            for (int i = 0; i < _socketAdapters.Count; i++)
                _socketAdapters[i].CompleteAllConnects();

            if (_gameObject != null)
                UnityEngine.Object.DestroyImmediate(_gameObject);

            for (int i = 0; i < _autoCreatedSocketObjects.Count; i++)
            {
                GameObject socketObject = _autoCreatedSocketObjects[i];
                if (socketObject != null)
                    UnityEngine.Object.DestroyImmediate(socketObject);
            }
            _autoCreatedSocketObjects.Clear();
            _socketAdapters.Clear();

            yield return null;
        }

        [Test]
        public void PendingConnect_RepeatedStartClient_DoesNotIssueSecondProviderCall()
        {
            _adapter.StartClient(Host);
            _adapter.StartClient(Host);
            _adapter.StartClient(Host);

            Assert.That(_socketAdapter.ConnectCallCount, Is.EqualTo(1));
            Assert.That(_socketAdapter.PendingConnectCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator StopThenRepeatedStartClient_CoalescesLatestUntilOldConnectTerminates()
        {
            ControllableSocketAdapter retiringSocketAdapter = _socketAdapter;
            _adapter.StartClient(Host);
            Assert.That(retiringSocketAdapter.ConnectCallCount, Is.EqualTo(1));

            _adapter.Stop();
            ControllableSocketAdapter replacementSocketAdapter = InitializeNewSocket();
            _adapter.StartClient(Host);
            _adapter.StartClient(Host);
            _adapter.StartClient(Host);

            Assert.That(retiringSocketAdapter.ConnectCallCount, Is.EqualTo(1));
            Assert.That(replacementSocketAdapter.ConnectCallCount, Is.Zero,
                "A replacement connect must wait for the previous provider task to become terminal.");

            retiringSocketAdapter.CompleteNextConnect();
            Assert.That(replacementSocketAdapter.ConnectCallCount, Is.Zero,
                "The replacement is intentionally deferred to the next Update tick.");

            yield return null;

            Assert.That(retiringSocketAdapter.ConnectCallCount, Is.EqualTo(1));
            Assert.That(replacementSocketAdapter.ConnectCallCount, Is.EqualTo(1));
            Assert.That(replacementSocketAdapter.PendingConnectCount, Is.EqualTo(1));
        }

        [Test]
        public void Stop_InitializeWithRetiredSocket_IsRejected()
        {
            ISocket retiredSocket = _socket;

            _adapter.StartClient(Host);
            _adapter.Stop();

            Assert.That(
                () => _adapter.Initialize(_client, retiredSocket, new TestSession()),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void FreshInjectedSocket_CannotBeInitializedByTwoAdapters()
        {
            GameObject secondGameObject = null;
            NakamaNetAdapter secondAdapter = null;
            try
            {
                secondAdapter = CreateAdditionalAdapter(_client, out secondGameObject);

                Assert.Throws<InvalidOperationException>(
                    () => secondAdapter.Initialize(_client, _socket, new TestSession()));
            }
            finally
            {
                DestroyAdditionalAdapter(secondAdapter, secondGameObject);
            }
        }

        [Test]
        public void StartedEpochSocket_CannotBeInitializedByAnotherAdapterAfterStop()
        {
            ISocket retiredSocket = _socket;
            _adapter.StartClient(Host);
            Assert.That(_socketAdapter.ConnectCallCount, Is.EqualTo(1));
            _adapter.Stop();

            GameObject secondGameObject = null;
            NakamaNetAdapter secondAdapter = null;
            try
            {
                secondAdapter = CreateAdditionalAdapter(_client, out secondGameObject);

                Assert.Throws<InvalidOperationException>(
                    () => secondAdapter.Initialize(_client, retiredSocket, new TestSession()));
            }
            finally
            {
                DestroyAdditionalAdapter(secondAdapter, secondGameObject);
            }
        }

        [Test]
        public void InitializeReplacement_InlineOldCloseCannotReenterOrClaimThirdSocket()
        {
            ControllableSocketAdapter retiringSocketAdapter = _socketAdapter;
            retiringSocketAdapter.RaiseClosedInlineOnClose = true;
            _adapter.StartClient(Host);
            Assert.That(retiringSocketAdapter.ConnectCallCount, Is.EqualTo(1));

            ISocket replacementSocket = CreateUninitializedSocket(out _);
            ISocket reentrantSocket = CreateUninitializedSocket(out _);
            int retiredObserverCallCount = 0;

            void AttemptReentrantInitialize()
            {
                retiredObserverCallCount++;
                _adapter.Initialize(_client, reentrantSocket, new TestSession());
            }

            _adapter.OnDisconnectedFromServer += AttemptReentrantInitialize;
            _adapter.OnClientDisconnected += _ => AttemptReentrantInitialize();

            _adapter.Initialize(_client, replacementSocket, new TestSession());

            Assert.That(retiringSocketAdapter.CloseCallCount, Is.EqualTo(1));
            Assert.That(retiredObserverCallCount, Is.Zero);
            Assert.That(_adapter.Socket, Is.SameAs(replacementSocket));

            GameObject probeGameObject = null;
            NakamaNetAdapter probeAdapter = null;
            try
            {
                probeAdapter = CreateAdditionalAdapter(_client, out probeGameObject);
                Assert.DoesNotThrow(
                    () => probeAdapter.Initialize(_client, reentrantSocket, new TestSession()));
            }
            finally
            {
                DestroyAdditionalAdapter(probeAdapter, probeGameObject);
            }
        }

        [UnityTest]
        public IEnumerator QueuedHandoff_ClearSessionBeforeNextUpdate_DoesNotStartReplacementProvider()
        {
            SetPrivateField(_adapter, "_autoAuthenticateDevice", true);
            ControllableSocketAdapter retiringSocketAdapter = _socketAdapter;
            _adapter.StartClient(Host);
            Assert.That(retiringSocketAdapter.ConnectCallCount, Is.EqualTo(1));

            _adapter.Stop();
            ControllableSocketAdapter replacementSocketAdapter = InitializeNewSocket();
            _adapter.StartClient(Host);
            retiringSocketAdapter.CompleteNextConnect();

            Assert.That(GetPrivateField<bool>(_adapter, "_queuedConnectStartScheduled"), Is.True,
                "The test must clear the session inside the deferred handoff window.");
            Assert.That(_httpAdapter.SendCallCount, Is.Zero);
            Assert.That(replacementSocketAdapter.ConnectCallCount, Is.Zero);

            _adapter.ClearSession();
            yield return null;

            Assert.That(_httpAdapter.SendCallCount, Is.Zero);
            Assert.That(replacementSocketAdapter.ConnectCallCount, Is.Zero);
        }

        [Test]
        public void Stop_SendToServerIsRejectedWithoutCallingProvider()
        {
            _adapter.Stop();

            NetworkSendResult result = _adapter.SendToServer(1, new byte[] { 42 });

            Assert.That(result.Status, Is.EqualTo(NetworkSendStatus.NotConnected));
            Assert.That(_socketAdapter.SendCallCount, Is.Zero);
        }

        [Test]
        public void ThrowingOnErrorObserver_IsPublishedOnceToUniTaskGlobalHook()
        {
            var expected = new InvalidOperationException("observer failure");
            Exception published = null;
            int publishCount = 0;

            void OnUnobserved(Exception exception)
            {
                publishCount++;
                published = exception;
            }

            UniTaskScheduler.UnobservedTaskException += OnUnobserved;
            try
            {
                _adapter.ClearSession();
                _adapter.OnError += (_, _, _) => throw expected;

                _adapter.StartClient(Host);

                Assert.That(publishCount, Is.EqualTo(1));
                Assert.That(published, Is.SameAs(expected));
                Assert.That(_socketAdapter.ConnectCallCount, Is.Zero,
                    "Missing-session validation must fail before a socket provider call.");
            }
            finally
            {
                UniTaskScheduler.UnobservedTaskException -= OnUnobserved;
            }
        }

        private ControllableSocketAdapter InitializeNewSocket()
        {
            ISocket socket = CreateUninitializedSocket(out ControllableSocketAdapter socketAdapter);
            _adapter.Initialize(_client, socket, new TestSession());

            _socket = socket;
            _socketAdapter = socketAdapter;
            return socketAdapter;
        }

        private ISocket CreateUninitializedSocket(out ControllableSocketAdapter socketAdapter)
        {
            socketAdapter = new ControllableSocketAdapter();
            ISocket socket = global::Nakama.Socket.From(_client, socketAdapter);
            _socketAdapters.Add(socketAdapter);
            return socket;
        }

        private NakamaNetAdapter CreateAdditionalAdapter(
            IClient client,
            out GameObject gameObject)
        {
            var preexistingSocketIds = new HashSet<int>();
            UnitySocket[] preexistingSockets = UnityEngine.Object.FindObjectsOfType<UnitySocket>(true);
            for (int i = 0; i < preexistingSockets.Length; i++)
                preexistingSocketIds.Add(preexistingSockets[i].GetInstanceID());

            gameObject = new GameObject($"{nameof(NakamaNetAdapterAsyncLifecycleTests)}-Additional");
            NakamaNetAdapter adapter = gameObject.AddComponent<NakamaNetAdapter>();
            var stagingSocketAdapter = new ControllableSocketAdapter();
            ISocket stagingSocket = global::Nakama.Socket.From(client, stagingSocketAdapter);
            _socketAdapters.Add(stagingSocketAdapter);
            SetPrivateField(adapter, "_client", client);
            SetPrivateField(adapter, "_socket", stagingSocket);
            InvokePrivateMethod(adapter, "Awake");
            SetPrivateField(adapter, "_autoAuthenticateDevice", false);
            SetPrivateField(adapter, "_joinMatchOnConnect", false);

            UnitySocket[] socketsAfterAwake = UnityEngine.Object.FindObjectsOfType<UnitySocket>(true);
            for (int i = 0; i < socketsAfterAwake.Length; i++)
            {
                UnitySocket candidate = socketsAfterAwake[i];
                if (!preexistingSocketIds.Contains(candidate.GetInstanceID()))
                    _autoCreatedSocketObjects.Add(candidate.gameObject);
            }

            return adapter;
        }

        private static void DestroyAdditionalAdapter(
            NakamaNetAdapter adapter,
            GameObject gameObject)
        {
            if (adapter != null)
                adapter.Stop();
            if (gameObject != null)
                UnityEngine.Object.DestroyImmediate(gameObject);
        }

        private static void SetPrivateField<T>(NakamaNetAdapter adapter, string fieldName, T value)
        {
            FieldInfo field = typeof(NakamaNetAdapter).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing test configuration field: {fieldName}");
            field.SetValue(adapter, value);
        }

        private static T GetPrivateField<T>(NakamaNetAdapter adapter, string fieldName)
        {
            FieldInfo field = typeof(NakamaNetAdapter).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing test probe field: {fieldName}");
            return (T)field.GetValue(adapter);
        }

        private static void InvokePrivateMethod(NakamaNetAdapter adapter, string methodName)
        {
            MethodInfo method = typeof(NakamaNetAdapter).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing lifecycle method: {methodName}");
            method.Invoke(adapter, null);
        }

        private sealed class CountingHttpAdapter : IHttpAdapter
        {
            public TransientExceptionDelegate TransientExceptionDelegate => IsTransientException;
            public global::Nakama.ILogger Logger { get; set; }
            public int SendCallCount { get; private set; }

            public Task<string> SendAsync(
                string method,
                Uri uri,
                IDictionary<string, string> headers,
                byte[] body,
                int timeout,
                CancellationToken? cancellationToken)
            {
                SendCallCount++;
                return Task.FromException<string>(
                    new InvalidOperationException("Unexpected authentication provider call."));
            }

            private static bool IsTransientException(Exception exception) => false;
        }

        private sealed class ControllableSocketAdapter : ISocketAdapter
        {
            private readonly Queue<TaskCompletionSource<bool>> _pendingConnects =
                new Queue<TaskCompletionSource<bool>>();

            public event Action Connected;
            public event Action<string> Closed;
            public event Action<Exception> ReceivedError;
            public event Action<ArraySegment<byte>> Received;

            public bool IsConnected { get; private set; }
            public bool IsConnecting { get; private set; }
            public bool RaiseClosedInlineOnClose { get; set; }
            public int ConnectCallCount { get; private set; }
            public int CloseCallCount { get; private set; }
            public int SendCallCount { get; private set; }
            public int PendingConnectCount => _pendingConnects.Count;

            public Task ConnectAsync(Uri uri, int timeout)
            {
                ConnectCallCount++;
                IsConnecting = true;
                var completion = new TaskCompletionSource<bool>();
                _pendingConnects.Enqueue(completion);
                return completion.Task;
            }

            public Task CloseAsync()
            {
                CloseCallCount++;
                IsConnecting = false;
                IsConnected = false;
                if (RaiseClosedInlineOnClose)
                    Closed?.Invoke("inline close");
                return Task.CompletedTask;
            }

            public Task SendAsync(
                ArraySegment<byte> buffer,
                bool reliable,
                CancellationToken cancellationToken)
            {
                SendCallCount++;
                return Task.CompletedTask;
            }

            public void CompleteNextConnect()
            {
                Assert.That(_pendingConnects.Count, Is.GreaterThan(0));
                TaskCompletionSource<bool> completion = _pendingConnects.Dequeue();
                IsConnecting = false;
                IsConnected = true;
                Connected?.Invoke();
                completion.SetResult(true);
            }

            public void CompleteAllConnects()
            {
                while (_pendingConnects.Count > 0)
                    CompleteNextConnect();
            }

            // These provider callbacks are retained to satisfy the real Nakama Socket contract.
            // The lifecycle tests drive only connection completion and never synthesize payloads.
            public void RaiseClosed(string reason) => Closed?.Invoke(reason);
            public void RaiseError(Exception exception) => ReceivedError?.Invoke(exception);
            public void RaiseReceived(ArraySegment<byte> payload) => Received?.Invoke(payload);
        }

        private sealed class TestSession : ISession
        {
            private readonly IDictionary<string, string> _vars = new Dictionary<string, string>();

            public string AuthToken => "test-auth-token";
            public bool Created => false;
            public long CreateTime => 0;
            public long ExpireTime => long.MaxValue;
            public bool IsExpired => false;
            public bool IsRefreshExpired => false;
            public long RefreshExpireTime => long.MaxValue;
            public string RefreshToken => string.Empty;
            public IDictionary<string, string> Vars => _vars;
            public string Username => "test-user";
            public string UserId => "test-user-id";

            public bool HasExpired(DateTime dateTime) => false;
            public bool HasRefreshExpired(DateTime dateTime) => false;
        }
    }
}
