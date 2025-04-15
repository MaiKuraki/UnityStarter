using UnityEngine;
using UnityEditor;

namespace Proxima.Editor
{
    [CustomEditor(typeof(ProximaInspector))]
    internal class ProximaInspectorEditor : UnityEditor.Editor
    {
        private SerializedProperty _connectionType;
        private SerializedProperty _serverUrl;
        private SerializedProperty _appId;
        private SerializedProperty _uniqueName;
        private SerializedProperty _port;
        private SerializedProperty _password;
        private SerializedProperty _displayName;
        private SerializedProperty _useHttps;
        private SerializedProperty _certificate;
        private SerializedProperty _certificatePassword;
        private SerializedProperty _runOnEnable;
        private SerializedProperty _logBufferSize;
        private SerializedProperty _instantiateStatusUI;
        private SerializedProperty _instantiateConnectUI;
        private SerializedProperty _dontDestroyOnLoad;
        private SerializedProperty _setRunInBackground;
        private SerializedProperty _uploadLogs;

        void OnEnable()
        {
            _connectionType = serializedObject.FindProperty("_connectionType");
            _serverUrl = serializedObject.FindProperty("_serverUrl");
            _appId = serializedObject.FindProperty("_appId");
            _uniqueName = serializedObject.FindProperty("_uniqueName");
            _port = serializedObject.FindProperty("_port");
            _displayName = serializedObject.FindProperty("_displayName");
            _password = serializedObject.FindProperty("_password");
            _useHttps = serializedObject.FindProperty("_useHttps");
            _certificate = serializedObject.FindProperty("_certificate");
            _certificatePassword = serializedObject.FindProperty("_certificatePassword");
            _runOnEnable = serializedObject.FindProperty("_runOnEnable");
            _logBufferSize = serializedObject.FindProperty("_logBufferSize");
            _instantiateStatusUI = serializedObject.FindProperty("_instantiateStatusUI");
            _instantiateConnectUI = serializedObject.FindProperty("_instantiateConnectUI");
            _dontDestroyOnLoad = serializedObject.FindProperty("_dontDestroyOnLoad");
            _setRunInBackground = serializedObject.FindProperty("_setRunInBackground");
            _uploadLogs = serializedObject.FindProperty("_uploadLogs");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_displayName);

            if (ProximaInspector.ProxyAvailable)
            {
                EditorGUILayout.PropertyField(_connectionType);
            }

            if (_connectionType.intValue == (int)ProximaInspector.ConnectionTypes.Internet)
            {
#if PROXIMA_DEBUG
                EditorGUILayout.PropertyField(_serverUrl);
#endif
                EditorGUILayout.PropertyField(_appId);
                EditorGUILayout.PropertyField(_uniqueName);
#if PROXIMA_ALPHA
                EditorGUILayout.PropertyField(_uploadLogs);
#endif
            }

#if PROXIMA_DEBUG
            if (_connectionType.intValue == (int)ProximaInspector.ConnectionTypes.Debug)
            {
                EditorGUILayout.PropertyField(_serverUrl);
            }
#endif

            if (_connectionType.intValue == (int)ProximaInspector.ConnectionTypes.LocalNetwork)
            {
                EditorGUILayout.PropertyField(_port);
                EditorGUILayout.PropertyField(_useHttps);
                if (_useHttps.boolValue)
                {
                    EditorGUILayout.PropertyField(_certificate, new GUIContent("    Certificate"));
                    if (_certificate.objectReferenceValue != null)
                    {
                        EditorGUILayout.PropertyField(_certificatePassword, new GUIContent("    Certificate Password"));
                        if (!string.IsNullOrWhiteSpace(_certificatePassword.stringValue))
                        {
                            EditorGUILayout.HelpBox("Setting a password here is not recommended. " +
                                "Create UI for the player to set the password, and then call ProximaInspector.Run(). See the ProximaConnectUI prefab.",
                                MessageType.Warning);
                        }
                    }
                }
            }

            EditorGUILayout.PropertyField(_password);
            if (!Application.isPlaying && !string.IsNullOrWhiteSpace(_password.stringValue))
            {
                EditorGUILayout.HelpBox("Setting a password here is not recommended. " +
                    "Create UI for the player to set the password, and then call ProximaInspector.Run(). See the ProximaConnectUI prefab.",
                    MessageType.Warning);
            }

            EditorGUILayout.PropertyField(_runOnEnable);
            EditorGUILayout.PropertyField(_logBufferSize);
            EditorGUILayout.PropertyField(_instantiateStatusUI);
            EditorGUILayout.PropertyField(_instantiateConnectUI);
            EditorGUILayout.PropertyField(_dontDestroyOnLoad);
            EditorGUILayout.PropertyField(_setRunInBackground);

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
            {
                var proxima = target as ProximaInspector;

                EditorGUILayout.Space();

                var statusMessage = proxima.Status.GetStatusMessage(false);
                if (!string.IsNullOrWhiteSpace(statusMessage))
                {
                    EditorGUILayout.HelpBox(statusMessage, proxima.Status.Error != null ? MessageType.Error : MessageType.Info);
                    EditorGUILayout.Space();
                }

                if (proxima.Status.IsRunning)
                {
                    if (proxima.Status.Listening)
                    {
                        GUILayout.TextField(proxima.Status.ConnectInfo);
                        EditorGUILayout.Space();
                    }

                    if (GUILayout.Button("Stop"))
                    {
                        proxima.Stop();
                    }
                }
                else
                {
                    if (GUILayout.Button("Start"))
                    {
                        proxima.Run();
                    }
                }
            }
        }
    }
}