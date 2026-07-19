using CycloneGames.Networking.Security;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Networking.Adapter.Mirror.Editor
{
    [CustomEditor(typeof(MirrorNetAdapter), true)]
    [CanEditMultipleObjects]
    internal sealed class MirrorNetAdapterEditor : UnityEditor.Editor
    {
        private static readonly string[] DrawnProperties =
        {
            "m_Script",
            "_enableMessageValidation",
            "_maxPayloadSize",
            "_enableRateLimiter",
            "_maxMessagesPerSecond",
            "_burstMessages",
            "_requireAuthenticatedMessages",
            "_requireEncryptedTransport"
        };

        private SerializedProperty _enableMessageValidation;
        private SerializedProperty _maxPayloadSize;
        private SerializedProperty _enableRateLimiter;
        private SerializedProperty _maxMessagesPerSecond;
        private SerializedProperty _burstMessages;
        private SerializedProperty _requireAuthenticatedMessages;
        private SerializedProperty _requireEncryptedTransport;

        private void OnEnable()
        {
            _enableMessageValidation = serializedObject.FindProperty("_enableMessageValidation");
            _maxPayloadSize = serializedObject.FindProperty("_maxPayloadSize");
            _enableRateLimiter = serializedObject.FindProperty("_enableRateLimiter");
            _maxMessagesPerSecond = serializedObject.FindProperty("_maxMessagesPerSecond");
            _burstMessages = serializedObject.FindProperty("_burstMessages");
            _requireAuthenticatedMessages = serializedObject.FindProperty("_requireAuthenticatedMessages");
            _requireEncryptedTransport = serializedObject.FindProperty("_requireEncryptedTransport");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Mirror Network Adapter", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Bridges Cyclone wire frames to Mirror. All Mirror runtime access, lifecycle operations, sends, broadcasts, disconnects, and callbacks are owned by the Unity main thread. Missing transport capacity fails closed.",
                MessageType.Info);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Validation and Rate Limits", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_enableMessageValidation);
            EditorGUILayout.PropertyField(_maxPayloadSize);
            EditorGUILayout.PropertyField(_enableRateLimiter);
            bool disableRateLimits = !_enableRateLimiter.hasMultipleDifferentValues && !_enableRateLimiter.boolValue;
            using (new EditorGUI.DisabledScope(disableRateLimits))
            {
                EditorGUILayout.PropertyField(_maxMessagesPerSecond);
                EditorGUILayout.PropertyField(_burstMessages);
            }
            int maximumPayloadSize = NetworkConstants.MaxMTU - NetworkWireProtocol.HeaderLength;
            if (!_maxPayloadSize.hasMultipleDifferentValues
                && (_maxPayloadSize.intValue <= 0 || _maxPayloadSize.intValue > maximumPayloadSize))
            {
                EditorGUILayout.HelpBox(
                    $"Max Payload Size must be between 1 and {maximumPayloadSize} bytes. Runtime fails fast on invalid configuration.",
                    MessageType.Error);
            }
            if (!_maxMessagesPerSecond.hasMultipleDifferentValues && _maxMessagesPerSecond.intValue <= 0)
            {
                EditorGUILayout.HelpBox("Max Messages Per Second must be positive.", MessageType.Error);
            }
            if (!_burstMessages.hasMultipleDifferentValues && _burstMessages.intValue < 0)
            {
                EditorGUILayout.HelpBox("Burst Messages cannot be negative.", MessageType.Error);
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Transport Requirements", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_requireAuthenticatedMessages);
            EditorGUILayout.PropertyField(_requireEncryptedTransport);

            DrawPropertiesExcluding(serializedObject, DrawnProperties);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
