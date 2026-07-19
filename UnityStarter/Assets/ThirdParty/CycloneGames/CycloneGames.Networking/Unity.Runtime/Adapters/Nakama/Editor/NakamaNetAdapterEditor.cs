using System;
using CycloneGames.Networking.Security;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Networking.Adapter.Nakama.Editor
{
    [CustomEditor(typeof(NakamaNetAdapter), true)]
    [CanEditMultipleObjects]
    internal sealed class NakamaNetAdapterEditor : UnityEditor.Editor
    {
        private static readonly string[] DrawnProperties =
        {
            "m_Script",
            "_connectOnStart",
            "_scheme",
            "_host",
            "_port",
            "_serverKey",
            "_appearOnline",
            "_connectTimeoutSeconds",
            "_languageTag",
            "_autoAuthenticateDevice",
            "_createAccount",
            "_deviceIdOverride",
            "_username",
            "_matchId",
            "_joinMatchOnConnect",
            "_matchStateOpCode",
            "_maxPayloadSize",
            "_maxPendingSends",
            "_maxConnections"
        };

        private SerializedProperty _connectOnStart;
        private SerializedProperty _scheme;
        private SerializedProperty _host;
        private SerializedProperty _port;
        private SerializedProperty _serverKey;
        private SerializedProperty _appearOnline;
        private SerializedProperty _connectTimeoutSeconds;
        private SerializedProperty _languageTag;
        private SerializedProperty _autoAuthenticateDevice;
        private SerializedProperty _createAccount;
        private SerializedProperty _deviceIdOverride;
        private SerializedProperty _username;
        private SerializedProperty _matchId;
        private SerializedProperty _joinMatchOnConnect;
        private SerializedProperty _matchStateOpCode;
        private SerializedProperty _maxPayloadSize;
        private SerializedProperty _maxPendingSends;
        private SerializedProperty _maxConnections;

        private void OnEnable()
        {
            _connectOnStart = serializedObject.FindProperty("_connectOnStart");
            _scheme = serializedObject.FindProperty("_scheme");
            _host = serializedObject.FindProperty("_host");
            _port = serializedObject.FindProperty("_port");
            _serverKey = serializedObject.FindProperty("_serverKey");
            _appearOnline = serializedObject.FindProperty("_appearOnline");
            _connectTimeoutSeconds = serializedObject.FindProperty("_connectTimeoutSeconds");
            _languageTag = serializedObject.FindProperty("_languageTag");
            _autoAuthenticateDevice = serializedObject.FindProperty("_autoAuthenticateDevice");
            _createAccount = serializedObject.FindProperty("_createAccount");
            _deviceIdOverride = serializedObject.FindProperty("_deviceIdOverride");
            _username = serializedObject.FindProperty("_username");
            _matchId = serializedObject.FindProperty("_matchId");
            _joinMatchOnConnect = serializedObject.FindProperty("_joinMatchOnConnect");
            _matchStateOpCode = serializedObject.FindProperty("_matchStateOpCode");
            _maxPayloadSize = serializedObject.FindProperty("_maxPayloadSize");
            _maxPendingSends = serializedObject.FindProperty("_maxPendingSends");
            _maxConnections = serializedObject.FindProperty("_maxConnections");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Nakama Network Adapter", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Client-side Nakama integration. The injected ISocket must deliver callbacks and task continuations on this component's Unity main owner thread; cross-thread delivery fails immediately and is not queued.",
                MessageType.Info);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Lifecycle", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_connectOnStart);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Nakama Endpoint", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_scheme);
            EditorGUILayout.PropertyField(_host);
            EditorGUILayout.PropertyField(_port);
            EditorGUILayout.PropertyField(_serverKey);
            EditorGUILayout.PropertyField(_appearOnline);
            EditorGUILayout.PropertyField(_connectTimeoutSeconds);
            EditorGUILayout.PropertyField(_languageTag);
            if (!_host.hasMultipleDifferentValues && string.IsNullOrWhiteSpace(_host.stringValue))
            {
                EditorGUILayout.HelpBox("Host cannot be empty.", MessageType.Error);
            }
            if (!_port.hasMultipleDifferentValues && (_port.intValue < 1 || _port.intValue > 65535))
            {
                EditorGUILayout.HelpBox("Port must be between 1 and 65535.", MessageType.Error);
            }
            if (!_connectTimeoutSeconds.hasMultipleDifferentValues && _connectTimeoutSeconds.intValue <= 0)
            {
                EditorGUILayout.HelpBox("Connect Timeout Seconds must be positive.", MessageType.Error);
            }
            if (!_scheme.hasMultipleDifferentValues
                && !string.Equals(_scheme.stringValue, "http", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(_scheme.stringValue, "https", StringComparison.OrdinalIgnoreCase))
            {
                EditorGUILayout.HelpBox("Scheme must be either HTTP or HTTPS.", MessageType.Error);
            }
            else if (!_scheme.hasMultipleDifferentValues
                     && !string.Equals(_scheme.stringValue, "https", StringComparison.OrdinalIgnoreCase))
            {
                EditorGUILayout.HelpBox(
                    "Use HTTPS outside trusted local development networks. The server key is client-visible configuration and must not grant privileged backend access.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Device Authentication", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_autoAuthenticateDevice);
            bool disableAuthentication = !_autoAuthenticateDevice.hasMultipleDifferentValues && !_autoAuthenticateDevice.boolValue;
            using (new EditorGUI.DisabledScope(disableAuthentication))
            {
                EditorGUILayout.PropertyField(_createAccount);
                EditorGUILayout.PropertyField(_deviceIdOverride);
                EditorGUILayout.PropertyField(_username);
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Realtime Match", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_matchId);
            EditorGUILayout.PropertyField(_joinMatchOnConnect);
            EditorGUILayout.PropertyField(_matchStateOpCode);
            EditorGUILayout.PropertyField(_maxPayloadSize);
            EditorGUILayout.PropertyField(_maxPendingSends);
            EditorGUILayout.PropertyField(_maxConnections);
            EditorGUILayout.HelpBox(
                "SendToServer is available only for authoritative matches. Server-to-client and broadcast routes are unsupported by this client adapter. Peer-origin match state remains peer-to-peer.",
                MessageType.None);
            int maximumPayloadSize = NetworkConstants.MaxMTU - NetworkWireProtocol.HeaderLength;
            if (!_maxPayloadSize.hasMultipleDifferentValues
                && (_maxPayloadSize.intValue <= 0 || _maxPayloadSize.intValue > maximumPayloadSize))
            {
                EditorGUILayout.HelpBox(
                    $"Max Payload Size must be between 1 and {maximumPayloadSize} bytes.",
                    MessageType.Error);
            }
            if (!_maxPendingSends.hasMultipleDifferentValues && _maxPendingSends.intValue <= 0)
            {
                EditorGUILayout.HelpBox("Max Pending Sends must be positive.", MessageType.Error);
            }
            if (!_maxConnections.hasMultipleDifferentValues && _maxConnections.intValue <= 0)
            {
                EditorGUILayout.HelpBox("Max Connections must be positive.", MessageType.Error);
            }

            DrawPropertiesExcluding(serializedObject, DrawnProperties);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
