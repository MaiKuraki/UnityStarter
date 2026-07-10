using System;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Logger.Editor
{
    [CustomEditor(typeof(LoggerSettings))]
    [CanEditMultipleObjects]
    internal sealed class LoggerSettingsEditor : UnityEditor.Editor
    {
        private static readonly GUIContent ProcessingLabel = new GUIContent("Processing");
        private static readonly GUIContent RegistrationLabel = new GUIContent("Sink Registration");
        private static readonly GUIContent FileLabel = new GUIContent("File Sink");
        private static readonly GUIContent DefaultsLabel = new GUIContent("Runtime Defaults");

        private SerializedProperty _processing;
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
        private SerializedProperty _guaranteedLevel;
        private SerializedProperty _registerUnityLogger;
        private SerializedProperty _registerConsoleLogger;
        private SerializedProperty _registerFileLogger;
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
        private SerializedProperty _defaultLevel;
        private SerializedProperty _defaultFilter;

        private void OnEnable()
        {
            _processing = Find("processing");
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
            _guaranteedLevel = Find("guaranteedLevel");
            _registerUnityLogger = Find("registerUnityLogger");
            _registerConsoleLogger = Find("registerConsoleLogger");
            _registerFileLogger = Find("registerFileLogger");
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
            _defaultLevel = Find("defaultLevel");
            _defaultFilter = Find("defaultFilter");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }

            DrawHeading(ProcessingLabel);
            Draw(_processing);
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
            Draw(_guaranteedLevel);

            if (!_overflowPolicy.hasMultipleDifferentValues
                && (LogQueueOverflowPolicy)_overflowPolicy.enumValueIndex == LogQueueOverflowPolicy.Block)
            {
                EditorGUILayout.HelpBox("Block can stall a core producer thread. WebGL bootstrap replaces this core policy with DropNewest.", MessageType.Warning);
            }

            if (!_unityConsoleOverflowPolicy.hasMultipleDifferentValues
                && (LogQueueOverflowPolicy)_unityConsoleOverflowPolicy.enumValueIndex == LogQueueOverflowPolicy.Block)
            {
                EditorGUILayout.HelpBox("Unity Console handoff supports only DropNewest or DropOldest; it cannot block producer threads.", MessageType.Error);
            }

            DrawHeading(RegistrationLabel);
            Draw(_registerUnityLogger);
            Draw(_registerConsoleLogger);
            Draw(_registerFileLogger);

            if (_registerFileLogger.hasMultipleDifferentValues || _registerFileLogger.boolValue)
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
            Draw(_defaultLevel);
            Draw(_defaultFilter);

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
                    LoggerSettingsBuildProcessor.ValidateSettings((LoggerSettings)selected);
                }

                EditorUtility.DisplayDialog("Logger Settings", "All selected settings are valid.", "OK");
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Logger Settings Validation", exception.Message, "OK");
            }
        }

    }
}
