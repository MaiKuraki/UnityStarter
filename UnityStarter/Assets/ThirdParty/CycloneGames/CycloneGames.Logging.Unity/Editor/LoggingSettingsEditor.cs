using System;
using CycloneGames.Logging.Pipeline;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Logging.Unity.Editor
{
    [CustomEditor(typeof(LoggingSettings))]
    [CanEditMultipleObjects]
    internal sealed class LoggingSettingsEditor : UnityEditor.Editor
    {
        private static readonly GUIContent ProcessingLabel = new GUIContent("Execution");
        private static readonly GUIContent RegistrationLabel = new GUIContent("Sink Registration");
        private static readonly GUIContent FileLabel = new GUIContent("File Sink");
        private static readonly GUIContent DefaultsLabel = new GUIContent("Filtering");

        private SerializedProperty _executionMode;
        private SerializedProperty _maxQueuedMessages;
        private SerializedProperty _maxQueuedCharacters;
        private SerializedProperty _maxMessageCharacters;
        private SerializedProperty _maxCategoryCharacters;
        private SerializedProperty _maxSourcePathCharacters;
        private SerializedProperty _maxMemberNameCharacters;
        private SerializedProperty _maxFilterCategories;
        private SerializedProperty _maxFilterCharacters;
        private SerializedProperty _reservedCriticalMessages;
        private SerializedProperty _reservedCriticalCharacters;
        private SerializedProperty _unityConsoleMaxQueuedMessages;
        private SerializedProperty _unityConsoleMaxQueuedCharacters;
        private SerializedProperty _unityConsoleOverflowPolicy;
        private SerializedProperty _shutdownDrainTimeoutMs;
        private SerializedProperty _enqueueBlockTimeoutMs;
        private SerializedProperty _maintenanceIntervalMs;
        private SerializedProperty _sinkFailureThreshold;
        private SerializedProperty _overflowPolicy;
        private SerializedProperty _criticalSeverity;
        private SerializedProperty _registerUnityConsoleLogSink;
        private SerializedProperty _registerConsoleLogSink;
        private SerializedProperty _registerFileLogSink;
        private SerializedProperty _usePersistentDataPath;
        private SerializedProperty _fileName;
        private SerializedProperty _allowCustomFilePath;
        private SerializedProperty _customFilePath;
        private SerializedProperty _fileMaintenanceMode;
        private SerializedProperty _maxFileBytes;
        private SerializedProperty _maxArchiveFiles;
        private SerializedProperty _fileFlushBatchSize;
        private SerializedProperty _fileFlushIntervalMs;
        private SerializedProperty _durableFlushOnFatal;
        private SerializedProperty _fileSourcePathMode;
        private SerializedProperty _minimumSeverity;
        private SerializedProperty _categoryFilter;

        private void OnEnable()
        {
            _executionMode = Find("executionMode");
            _maxQueuedMessages = Find("maxQueuedMessages");
            _maxQueuedCharacters = Find("maxQueuedCharacters");
            _maxMessageCharacters = Find("maxMessageCharacters");
            _maxCategoryCharacters = Find("maxCategoryCharacters");
            _maxSourcePathCharacters = Find("maxSourcePathCharacters");
            _maxMemberNameCharacters = Find("maxMemberNameCharacters");
            _maxFilterCategories = Find("maxFilterCategories");
            _maxFilterCharacters = Find("maxFilterCharacters");
            _reservedCriticalMessages = Find("reservedCriticalMessages");
            _reservedCriticalCharacters = Find("reservedCriticalCharacters");
            _unityConsoleMaxQueuedMessages = Find("unityConsoleMaxQueuedMessages");
            _unityConsoleMaxQueuedCharacters = Find("unityConsoleMaxQueuedCharacters");
            _unityConsoleOverflowPolicy = Find("unityConsoleOverflowPolicy");
            _shutdownDrainTimeoutMs = Find("shutdownDrainTimeoutMs");
            _enqueueBlockTimeoutMs = Find("enqueueBlockTimeoutMs");
            _maintenanceIntervalMs = Find("maintenanceIntervalMs");
            _sinkFailureThreshold = Find("sinkFailureThreshold");
            _overflowPolicy = Find("overflowPolicy");
            _criticalSeverity = Find("criticalSeverity");
            _registerUnityConsoleLogSink = Find("registerUnityConsoleLogSink");
            _registerConsoleLogSink = Find("registerConsoleLogSink");
            _registerFileLogSink = Find("registerFileLogSink");
            _usePersistentDataPath = Find("usePersistentDataPath");
            _fileName = Find("fileName");
            _allowCustomFilePath = Find("allowCustomFilePath");
            _customFilePath = Find("customFilePath");
            _fileMaintenanceMode = Find("fileMaintenanceMode");
            _maxFileBytes = Find("maxFileBytes");
            _maxArchiveFiles = Find("maxArchiveFiles");
            _fileFlushBatchSize = Find("fileFlushBatchSize");
            _fileFlushIntervalMs = Find("fileFlushIntervalMs");
            _durableFlushOnFatal = Find("durableFlushOnFatal");
            _fileSourcePathMode = Find("fileSourcePathMode");
            _minimumSeverity = Find("minimumSeverity");
            _categoryFilter = Find("categoryFilter");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }

            DrawHeading(ProcessingLabel);
            Draw(_executionMode);
            Draw(_maxQueuedMessages);
            Draw(_maxQueuedCharacters);
            Draw(_maxMessageCharacters);
            Draw(_maxCategoryCharacters);
            Draw(_maxSourcePathCharacters);
            Draw(_maxMemberNameCharacters);
            Draw(_maxFilterCategories);
            Draw(_maxFilterCharacters);
            Draw(_reservedCriticalMessages);
            Draw(_reservedCriticalCharacters);
            Draw(_unityConsoleMaxQueuedMessages);
            Draw(_unityConsoleMaxQueuedCharacters);
            Draw(_unityConsoleOverflowPolicy);
            Draw(_shutdownDrainTimeoutMs);
            Draw(_enqueueBlockTimeoutMs);
            Draw(_maintenanceIntervalMs);
            Draw(_sinkFailureThreshold);
            Draw(_overflowPolicy);
            Draw(_criticalSeverity);

            if (!_overflowPolicy.hasMultipleDifferentValues
                && (LogQueueOverflowPolicy)_overflowPolicy.enumValueIndex == LogQueueOverflowPolicy.Block)
            {
                EditorGUILayout.HelpBox("Block can stall a producer thread. WebGL bootstrap replaces this pipeline policy with DropNewest.", MessageType.Warning);
            }

            if (!_unityConsoleOverflowPolicy.hasMultipleDifferentValues
                && (LogQueueOverflowPolicy)_unityConsoleOverflowPolicy.enumValueIndex == LogQueueOverflowPolicy.Block)
            {
                EditorGUILayout.HelpBox("Unity Console handoff supports only DropNewest or DropOldest; it cannot block producer threads.", MessageType.Error);
            }

            DrawHeading(RegistrationLabel);
            Draw(_registerUnityConsoleLogSink);
            Draw(_registerConsoleLogSink);
            Draw(_registerFileLogSink);

            if (_registerFileLogSink.hasMultipleDifferentValues || _registerFileLogSink.boolValue)
            {
                DrawHeading(FileLabel);
                Draw(_usePersistentDataPath);
                if (_usePersistentDataPath.hasMultipleDifferentValues || _usePersistentDataPath.boolValue)
                {
                    Draw(_fileName);
                }
                else
                {
                    Draw(_allowCustomFilePath);
                    Draw(_customFilePath);
                    EditorGUILayout.HelpBox("Custom paths are a trusted platform integration boundary. Validate quota, permissions, and lifecycle on each target.", MessageType.Info);
                }

                Draw(_fileMaintenanceMode);
                Draw(_maxFileBytes);
                Draw(_maxArchiveFiles);
                Draw(_fileFlushBatchSize);
                Draw(_fileFlushIntervalMs);
                Draw(_durableFlushOnFatal);
                Draw(_fileSourcePathMode);
            }

            DrawHeading(DefaultsLabel);
            Draw(_minimumSeverity);
            Draw(_categoryFilter);

            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space();
            if (GUILayout.Button("Validate Settings"))
            {
                ValidateTargets();
            }
        }

        private SerializedProperty Find(string name)
        {
            return serializedObject.FindProperty(name);
        }

        private static void Draw(SerializedProperty property)
        {
            EditorGUILayout.PropertyField(property, true);
        }

        private static void DrawHeading(GUIContent content)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(content, EditorStyles.boldLabel);
        }

        private void ValidateTargets()
        {
            try
            {
                foreach (UnityEngine.Object selected in targets)
                {
                    LoggingSettingsBuildProcessor.ValidateSettings((LoggingSettings)selected);
                }

                EditorUtility.DisplayDialog("Logging Settings", "All selected settings are valid.", "OK");
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Logging Settings Validation", exception.Message, "OK");
            }
        }

    }
}
