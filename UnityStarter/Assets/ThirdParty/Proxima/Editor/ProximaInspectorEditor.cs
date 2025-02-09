// #define DISABLE_REMOTE

using System;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace Proxima.Editor
{
    [CustomEditor(typeof(ProximaInspector))]
    internal class ProximaInspectorEditor : UnityEditor.Editor
    {
        private SerializedProperty _serverType;
        private SerializedProperty _serverUrl;
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
        private bool _remoteAvailable;

        void OnEnable()
        {
            _serverType = serializedObject.FindProperty("_serverType");
            _serverUrl = serializedObject.FindProperty("_serverUrl");
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

            #if !DISABLE_REMOTE
                _remoteAvailable = AppDomain.CurrentDomain.GetAssemblies().First(
                    assembly => assembly.GetName().Name == "Proxima").GetType("Proxima.ProximaRemoteServer") != null;
            #endif
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_displayName);

            if (_remoteAvailable)
            {
                EditorGUILayout.PropertyField(_serverType);
                if (_serverType.enumValueIndex == (int)ProximaInspector.ServerTypes.Remote)
                {
                    EditorGUILayout.PropertyField(_serverUrl);
                }
            }

            if (!_remoteAvailable || _serverType.enumValueIndex == (int)ProximaInspector.ServerTypes.Embedded)
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
        }
    }
}