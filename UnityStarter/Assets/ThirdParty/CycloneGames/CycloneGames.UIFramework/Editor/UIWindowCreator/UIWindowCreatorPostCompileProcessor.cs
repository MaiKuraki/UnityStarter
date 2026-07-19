using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CycloneGames.IO;
using CycloneGames.Logger;
using CycloneGames.UIFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.UIFramework.Editor
{
    [InitializeOnLoad]
    internal static class UIWindowCreatorPostCompileProcessor
    {
        public static event Action StatusChanged;

        [Serializable]
        private sealed class PendingQueue
        {
            public int schemaVersion = PendingSchemaVersion;
            public List<PendingOperation> operations = new List<PendingOperation>();
        }

        [Serializable]
        private sealed class PendingOperation
        {
            public string scriptName;
            public string namespaceName;
            public string scriptPath;
            public string scriptGuid;
            public string scriptAssemblyName;
            public string prefabPath;
            public string prefabGuid;
            public string configPath;
            public string configGuid;
            public int sourceMode;
            public int attempts;
            public bool failed;
            public string error;
        }

        private const string LogCategory = "UIWindowCreator";
        private const string PendingFileName = "CycloneGames.UIFramework.WindowCreator.Pending.json";
        private const int MaxPendingBytes = 1024 * 1024;
        private const int PendingSchemaVersion = 2;
        private const int MaxOperations = 128;
        private const int MaxScriptNameLength = 128;
        private const int MaxNamespaceLength = 512;
        private const int MaxAssemblyNameLength = 256;
        private const int MaxAssetPathLength = 1024;
        private const int MaxErrorLength = 4096;
        private const int MaxAttempts = 100;
        private const double CheckIntervalSeconds = 0.2d;

        private static readonly string PendingPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "UserSettings", PendingFileName));

        private static PendingQueue _queue;
        private static double _nextCheckTime;
        private static string _journalError = string.Empty;
        private static bool _journalBlocked;

        static UIWindowCreatorPostCompileProcessor()
        {
            EditorApplication.delayCall += ResumeIfPending;
        }

        public static void Schedule(
            string scriptName,
            string namespaceName,
            string scriptPath,
            string scriptGuid,
            string prefabPath,
            string prefabGuid,
            string configPath,
            string configGuid,
            UIWindowConfiguration.PrefabSource sourceMode)
        {
            if (!UIWindowAssemblyValidator.TryResolveOutputAssemblyName(
                    scriptPath,
                    out string scriptAssemblyName,
                    out string assemblyError))
            {
                throw new InvalidOperationException(
                    "Cannot schedule UIWindow binding because the generated script assembly could not be resolved: " +
                    assemblyError);
            }

            PendingOperation scheduled = new PendingOperation
            {
                scriptName = scriptName ?? string.Empty,
                namespaceName = namespaceName ?? string.Empty,
                scriptPath = scriptPath ?? string.Empty,
                scriptGuid = scriptGuid ?? string.Empty,
                scriptAssemblyName = scriptAssemblyName,
                prefabPath = prefabPath ?? string.Empty,
                prefabGuid = prefabGuid ?? string.Empty,
                configPath = configPath ?? string.Empty,
                configGuid = configGuid ?? string.Empty,
                sourceMode = (int)sourceMode,
            };
            if (!TryValidateOperation(scheduled, out string validationError))
            {
                throw new ArgumentException(
                    "Invalid UIWindow creator pending operation: " + validationError,
                    nameof(scriptName));
            }

            VerifyAssetIdentity(scheduled.scriptPath, scheduled.scriptGuid, "Generated script");
            VerifyAssetIdentity(scheduled.prefabPath, scheduled.prefabGuid, "Pending prefab");
            VerifyAssetIdentity(scheduled.configPath, scheduled.configGuid, "Pending configuration");

            PendingQueue queue = LoadQueue();
            if (_journalBlocked)
            {
                throw new InvalidDataException(
                    "The UIWindow creator pending journal is blocked after a validation failure. " +
                    "Remove the failed record before scheduling another operation.");
            }

            for (int i = queue.operations.Count - 1; i >= 0; i--)
            {
                PendingOperation existing = queue.operations[i];
                if (string.Equals(existing.configPath, configPath, StringComparison.Ordinal))
                {
                    queue.operations.RemoveAt(i);
                }
            }

            if (queue.operations.Count >= MaxOperations)
            {
                throw new InvalidOperationException(
                    $"The UIWindow creator pending queue is limited to {MaxOperations} operations.");
            }

            queue.operations.Add(scheduled);

            SaveQueue(queue);
            StartPolling();
        }

        public static void GetStatus(out int pendingCount, out int failedCount, out string lastError)
        {
            PendingQueue queue = LoadQueue();
            pendingCount = 0;
            failedCount = string.IsNullOrEmpty(_journalError) ? 0 : 1;
            lastError = _journalError;

            for (int i = 0; i < queue.operations.Count; i++)
            {
                PendingOperation operation = queue.operations[i];
                if (operation.failed)
                {
                    failedCount++;
                    if (!string.IsNullOrEmpty(operation.error))
                    {
                        lastError = operation.error;
                    }
                }
                else
                {
                    pendingCount++;
                }
            }
        }

        public static void RetryFailed()
        {
            PendingQueue queue = LoadQueue();
            bool changed = false;
            for (int i = 0; i < queue.operations.Count; i++)
            {
                PendingOperation operation = queue.operations[i];
                if (!operation.failed)
                {
                    continue;
                }

                operation.failed = false;
                operation.error = string.Empty;
                operation.attempts = 0;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            SaveQueue(queue);
            StartPolling();
        }

        public static void RemoveFailed()
        {
            PendingQueue queue = LoadQueue();
            bool changed = ClearJournalFailure();
            for (int i = queue.operations.Count - 1; i >= 0; i--)
            {
                if (!queue.operations[i].failed)
                {
                    continue;
                }

                queue.operations.RemoveAt(i);
                changed = true;
            }

            if (changed)
            {
                SaveQueue(queue);
            }
        }

        public static void Cancel(string configPath)
        {
            if (!UIWindowCreationValidator.TryValidateAssetFilePath(
                    configPath,
                    ".asset",
                    out string canonicalConfigPath,
                    out string validationError))
            {
                if (!string.IsNullOrEmpty(configPath))
                {
                    CLogger.LogWarning(
                        $"Ignored invalid pending cancellation path '{configPath}': {validationError}",
                        LogCategory);
                }
                return;
            }

            PendingQueue queue = LoadQueue();
            bool changed = false;
            for (int i = queue.operations.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(queue.operations[i].configPath, canonicalConfigPath, StringComparison.Ordinal))
                {
                    continue;
                }

                queue.operations.RemoveAt(i);
                changed = true;
            }

            if (changed)
            {
                SaveQueue(queue);
            }
        }

        private static void ResumeIfPending()
        {
            PendingQueue queue = LoadQueue();
            for (int i = 0; i < queue.operations.Count; i++)
            {
                if (!queue.operations[i].failed)
                {
                    StartPolling();
                    return;
                }
            }
        }

        private static void StartPolling()
        {
            EditorApplication.update -= OnEditorUpdate;
            _nextCheckTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.timeSinceStartup < _nextCheckTime)
            {
                return;
            }

            _nextCheckTime = EditorApplication.timeSinceStartup + CheckIntervalSeconds;
            PendingQueue queue = LoadQueue();
            bool changed = false;
            bool hasRunnable = false;

            for (int i = queue.operations.Count - 1; i >= 0; i--)
            {
                PendingOperation operation = queue.operations[i];
                if (operation.failed)
                {
                    continue;
                }

                hasRunnable = true;
                if (!TryValidateOperation(operation, out string operationError))
                {
                    FailOperation(
                        operation,
                        "Pending operation validation failed before completion: " + operationError);
                    changed = true;
                    continue;
                }

                Type scriptType = FindScriptType(operation);
                if (scriptType == null)
                {
                    operation.attempts++;
                    changed = true;
                    if (operation.attempts >= MaxAttempts)
                    {
                        FailOperation(
                            operation,
                            "Generated type was not available. Resolve compiler errors, then create the window again to retry.");
                    }

                    continue;
                }

                try
                {
                    Complete(operation, scriptType);
                    queue.operations.RemoveAt(i);
                    changed = true;
                }
                catch (Exception exception)
                {
                    FailOperation(operation, exception.Message);
                    changed = true;
                    CLogger.LogError(
                        $"Window creator post-compile binding failed for '{operation.scriptName}': {exception}",
                        LogCategory);
                }
            }

            if (changed)
            {
                SaveQueue(queue);
            }

            if (!hasRunnable || queue.operations.Count == 0)
            {
                EditorApplication.update -= OnEditorUpdate;
            }
        }

        private static void Complete(PendingOperation operation, Type scriptType)
        {
            if (!TryValidateOperation(operation, out string operationError))
            {
                throw new InvalidDataException(
                    "Pending operation validation failed immediately before asset writes: " + operationError);
            }

            if (!typeof(UIWindow).IsAssignableFrom(scriptType))
            {
                throw new InvalidOperationException(
                    $"Generated type '{scriptType.FullName}' does not derive from UIWindow.");
            }

            string expectedFullName = string.IsNullOrEmpty(operation.namespaceName)
                ? operation.scriptName
                : operation.namespaceName + "." + operation.scriptName;
            if (!string.Equals(scriptType.FullName, expectedFullName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Resolved type '{scriptType.FullName}' does not match pending type '{expectedFullName}'.");
            }

            string resolvedAssemblyName = scriptType.Assembly.GetName().Name;
            if (!string.Equals(
                    resolvedAssemblyName,
                    operation.scriptAssemblyName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Resolved type assembly '{resolvedAssemblyName}' does not match pending assembly " +
                    $"'{operation.scriptAssemblyName}'.");
            }

            VerifyAssetIdentity(operation.scriptPath, operation.scriptGuid, "Generated script");
            VerifyAssetIdentity(operation.prefabPath, operation.prefabGuid, "Pending prefab");
            VerifyAssetIdentity(operation.configPath, operation.configGuid, "Pending configuration");

            MonoScript generatedScript = AssetDatabase.LoadAssetAtPath<MonoScript>(operation.scriptPath);
            if (generatedScript == null || generatedScript.GetClass() != scriptType)
            {
                throw new InvalidOperationException(
                    $"Generated script '{operation.scriptPath}' no longer owns type '{expectedFullName}'.");
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(operation.prefabPath);
            if (prefab == null || !string.Equals(prefab.name, operation.scriptName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Pending prefab '{operation.prefabPath}' is missing or does not match '{operation.scriptName}'.");
            }

            UIWindow[] prefabWindows = prefab.GetComponentsInChildren<UIWindow>(true);
            if (prefabWindows.Length != 1 ||
                prefabWindows[0].gameObject != prefab ||
                (prefabWindows[0].GetType() != typeof(UIWindow) &&
                 prefabWindows[0].GetType() != scriptType))
            {
                throw new InvalidOperationException(
                    $"Pending prefab '{operation.prefabPath}' must have exactly one root UIWindow authority matching the generated type.");
            }

            UIWindowConfiguration config =
                AssetDatabase.LoadAssetAtPath<UIWindowConfiguration>(operation.configPath);
            var sourceMode = (UIWindowConfiguration.PrefabSource)operation.sourceMode;
            if (config == null ||
                !string.Equals(config.WindowId, operation.scriptName, StringComparison.Ordinal) ||
                config.Source != sourceMode ||
                config.Layer == null)
            {
                throw new InvalidOperationException(
                    $"Pending configuration '{operation.configPath}' no longer matches the generated prefab, type, or source mode.");
            }

            if (sourceMode == UIWindowConfiguration.PrefabSource.PrefabReference &&
                (config.WindowPrefab == null ||
                 !string.Equals(
                     AssetDatabase.GetAssetPath(config.WindowPrefab.gameObject),
                     operation.prefabPath,
                     StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Pending configuration '{operation.configPath}' references a different prefab.");
            }
            if (sourceMode == UIWindowConfiguration.PrefabSource.PathLocation &&
                string.IsNullOrWhiteSpace(config.PrefabLocation))
            {
                throw new InvalidOperationException(
                    $"Pending configuration '{operation.configPath}' has no runtime prefab location.");
            }
            if (sourceMode == UIWindowConfiguration.PrefabSource.AssetReference &&
                !config.PrefabAssetReference.IsValid)
            {
                throw new InvalidOperationException(
                    $"Pending configuration '{operation.configPath}' has an invalid asset reference.");
            }

            if (!UIWindowPrefabScriptBinder.AddScriptComponentToPrefab(
                    operation.prefabPath,
                    scriptType,
                    operation.scriptName))
            {
                throw new InvalidOperationException(
                    $"Failed to bind '{scriptType.FullName}' to '{operation.prefabPath}'.");
            }

            if (sourceMode == UIWindowConfiguration.PrefabSource.PrefabReference &&
                !UpdateOwnedPrefabReference(operation))
            {
                throw new InvalidOperationException(
                    $"Failed to update configuration '{operation.configPath}'.");
            }

            AssetDatabase.SaveAssets();
            CLogger.LogInfo(
                $"Completed post-compile binding for UIWindow '{operation.scriptName}'.",
                LogCategory);
        }

        private static bool UpdateOwnedPrefabReference(PendingOperation operation)
        {
            VerifyAssetIdentity(operation.prefabPath, operation.prefabGuid, "Pending prefab");
            VerifyAssetIdentity(operation.configPath, operation.configGuid, "Pending configuration");
            return UIWindowConfigurationWriter.UpdatePrefabReference(
                operation.configPath,
                operation.prefabPath);
        }

        private static Type FindScriptType(PendingOperation operation)
        {
            if (operation == null || string.IsNullOrEmpty(operation.scriptName))
            {
                return null;
            }

            string fullName = string.IsNullOrEmpty(operation.namespaceName)
                ? operation.scriptName
                : operation.namespaceName + "." + operation.scriptName;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                if (!string.Equals(
                        assemblies[i].GetName().Name,
                        operation.scriptAssemblyName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Type type = assemblies[i].GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static void VerifyAssetIdentity(string assetPath, string expectedGuid, string label)
        {
            string currentGuid = AssetDatabase.AssetPathToGUID(assetPath);
            string resolvedPath = AssetDatabase.GUIDToAssetPath(expectedGuid);
            if (!string.Equals(currentGuid, expectedGuid, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(resolvedPath, assetPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{label} identity changed after scheduling. Expected GUID '{expectedGuid}' at " +
                    $"'{assetPath}', current GUID='{currentGuid}', current path='{resolvedPath}'. " +
                    "The replacement asset was preserved.");
            }
        }

        private static PendingQueue LoadQueue()
        {
            if (_queue != null)
            {
                return _queue;
            }

            if (!File.Exists(PendingPath))
            {
                return _queue = new PendingQueue();
            }

            try
            {
                string json = SystemFileStore.Default.ReadText(PendingPath, MaxPendingBytes);
                _queue = JsonUtility.FromJson<PendingQueue>(json) ?? new PendingQueue();
                if (_queue.schemaVersion != PendingSchemaVersion)
                {
                    throw new InvalidDataException(
                        $"Unsupported window creator pending schema {_queue.schemaVersion}.");
                }
                _queue.operations ??= new List<PendingOperation>();
                if (!TryValidateQueue(_queue, out string validationError))
                {
                    throw new InvalidDataException(validationError);
                }
            }
            catch (Exception exception)
            {
                string quarantinePath;
                string quarantineError;
                bool quarantined = TryQuarantineFile(
                    PendingPath,
                    out quarantinePath,
                    out quarantineError);
                _journalBlocked = !quarantined;
                _journalError = quarantined
                    ? $"Invalid pending journal was quarantined to '{quarantinePath}': {exception.Message}"
                    : $"Invalid pending journal is blocked and could not be quarantined: {exception.Message}. {quarantineError}";
                CLogger.LogError(
                    "Failed to load window creator pending queue: " + _journalError,
                    LogCategory);
                _queue = new PendingQueue();
            }

            return _queue;
        }

        private static void SaveQueue(PendingQueue queue)
        {
            _queue = queue ?? new PendingQueue();
            if (_journalBlocked)
            {
                throw new InvalidDataException(
                    "The pending journal is blocked after a validation failure and cannot be overwritten.");
            }
            if (!TryValidateQueue(_queue, out string validationError))
            {
                throw new InvalidDataException(
                    "Refusing to save an invalid UIWindow creator pending queue: " + validationError);
            }

            string directory = Path.GetDirectoryName(PendingPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (_queue.operations.Count == 0)
            {
                if (File.Exists(PendingPath))
                {
                    File.Delete(PendingPath);
                }

                StatusChanged?.Invoke();
                return;
            }

            string json = JsonUtility.ToJson(_queue, true);
            SystemFileStore.Default.WriteTextAtomically(PendingPath, json);
            StatusChanged?.Invoke();
        }

        internal static bool ValidatePendingOperationForTests(
            string scriptName,
            string namespaceName,
            string prefabPath,
            string configPath,
            int sourceMode,
            int attempts,
            bool failed,
            string error,
            out string validationError)
        {
            string folder = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/') ?? "Assets";
            return TryValidateOperation(
                new PendingOperation
                {
                    scriptName = scriptName,
                    namespaceName = namespaceName,
                    scriptPath = folder + "/" + scriptName + ".cs",
                    scriptGuid = "11111111111111111111111111111111",
                    scriptAssemblyName = "CycloneGames.UIFramework.Tests",
                    prefabPath = prefabPath,
                    prefabGuid = "22222222222222222222222222222222",
                    configPath = configPath,
                    configGuid = "33333333333333333333333333333333",
                    sourceMode = sourceMode,
                    attempts = attempts,
                    failed = failed,
                    error = error
                },
                out validationError);
        }

        internal static bool TryQuarantineFile(
            string sourcePath,
            out string quarantinePath,
            out string error)
        {
            quarantinePath = string.Empty;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                error = "Quarantine source path is empty.";
                return false;
            }

            try
            {
                string absoluteSource = Path.GetFullPath(sourcePath);
                if (!File.Exists(absoluteSource))
                {
                    error = $"Quarantine source '{absoluteSource}' does not exist.";
                    return false;
                }

                string directory = Path.GetDirectoryName(absoluteSource);
                string fileName = Path.GetFileNameWithoutExtension(absoluteSource);
                string extension = Path.GetExtension(absoluteSource);
                string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");
                for (int attempt = 0; attempt < 100; attempt++)
                {
                    string suffix = attempt == 0 ? string.Empty : "." + attempt;
                    string candidate = Path.GetFullPath(Path.Combine(
                        directory,
                        fileName + ".invalid." + stamp + suffix + extension));
                    if (File.Exists(candidate))
                    {
                        continue;
                    }

                    File.Move(absoluteSource, candidate);
                    quarantinePath = candidate;
                    return true;
                }

                error = "Could not allocate a unique quarantine file name.";
                return false;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                quarantinePath = string.Empty;
                return false;
            }
        }

        private static bool TryValidateQueue(PendingQueue queue, out string error)
        {
            error = string.Empty;
            if (queue == null || queue.schemaVersion != PendingSchemaVersion)
            {
                error = "Pending queue schema is missing or unsupported.";
                return false;
            }

            queue.operations ??= new List<PendingOperation>();
            if (queue.operations.Count > MaxOperations)
            {
                error = $"Pending queue contains {queue.operations.Count} operations; maximum is {MaxOperations}.";
                return false;
            }

            var configPaths = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < queue.operations.Count; i++)
            {
                PendingOperation operation = queue.operations[i];
                if (!TryValidateOperation(operation, out string operationError))
                {
                    error = $"Pending operation {i} is invalid: {operationError}";
                    return false;
                }
                if (!configPaths.Add(operation.configPath))
                {
                    error = $"Pending operation {i} duplicates config path '{operation.configPath}'.";
                    return false;
                }
            }

            return true;
        }

        private static bool TryValidateOperation(PendingOperation operation, out string error)
        {
            error = string.Empty;
            if (operation == null)
            {
                error = "Operation is null.";
                return false;
            }

            operation.scriptName ??= string.Empty;
            operation.namespaceName ??= string.Empty;
            operation.scriptPath ??= string.Empty;
            operation.scriptGuid ??= string.Empty;
            operation.scriptAssemblyName ??= string.Empty;
            operation.prefabPath ??= string.Empty;
            operation.prefabGuid ??= string.Empty;
            operation.configPath ??= string.Empty;
            operation.configGuid ??= string.Empty;
            operation.error ??= string.Empty;

            if (operation.scriptName.Length > MaxScriptNameLength ||
                !UIWindowCreationValidator.IsValidCSharpIdentifier(operation.scriptName))
            {
                error = $"Script name is invalid or exceeds {MaxScriptNameLength} characters.";
                return false;
            }
            if (operation.namespaceName.Length > MaxNamespaceLength ||
                !UIWindowCreationValidator.IsValidNamespace(operation.namespaceName))
            {
                error = $"Namespace is invalid or exceeds {MaxNamespaceLength} characters.";
                return false;
            }
            if (operation.scriptPath.Length > MaxAssetPathLength)
            {
                error = $"Script path exceeds {MaxAssetPathLength} characters.";
                return false;
            }
            if (!UIWindowCreationValidator.TryValidateAssetFilePath(
                    operation.scriptPath,
                    ".cs",
                    out string canonicalScriptPath,
                    out string scriptError) ||
                !string.Equals(operation.scriptPath, canonicalScriptPath, StringComparison.Ordinal) ||
                !string.Equals(
                    Path.GetFileNameWithoutExtension(operation.scriptPath),
                    operation.scriptName,
                    StringComparison.Ordinal))
            {
                error = "Script path is invalid or does not match the pending script type: " + scriptError;
                return false;
            }
            if (!IsValidAssetGuid(operation.scriptGuid))
            {
                error = "Script GUID is missing or invalid.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(operation.scriptAssemblyName) ||
                operation.scriptAssemblyName.Length > MaxAssemblyNameLength ||
                ContainsControlCharacter(operation.scriptAssemblyName))
            {
                error = $"Script assembly name is missing, invalid, or exceeds {MaxAssemblyNameLength} characters.";
                return false;
            }
            if (operation.prefabPath.Length > MaxAssetPathLength)
            {
                error = $"Prefab path exceeds {MaxAssetPathLength} characters.";
                return false;
            }
            if (!UIWindowCreationValidator.TryValidateAssetFilePath(
                    operation.prefabPath,
                    ".prefab",
                    out string canonicalPrefabPath,
                    out string prefabError) ||
                !string.Equals(operation.prefabPath, canonicalPrefabPath, StringComparison.Ordinal))
            {
                error = "Prefab path is invalid: " + prefabError;
                return false;
            }
            if (!IsValidAssetGuid(operation.prefabGuid))
            {
                error = "Prefab GUID is missing or invalid.";
                return false;
            }
            if (operation.configPath.Length > MaxAssetPathLength)
            {
                error = $"Config path exceeds {MaxAssetPathLength} characters.";
                return false;
            }
            if (!UIWindowCreationValidator.TryValidateAssetFilePath(
                    operation.configPath,
                    ".asset",
                    out string canonicalConfigPath,
                    out string configError) ||
                !string.Equals(operation.configPath, canonicalConfigPath, StringComparison.Ordinal))
            {
                error = "Config path is invalid: " + configError;
                return false;
            }
            if (!IsValidAssetGuid(operation.configGuid))
            {
                error = "Config GUID is missing or invalid.";
                return false;
            }

            if (!string.Equals(
                    Path.GetFileNameWithoutExtension(operation.prefabPath),
                    operation.scriptName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    Path.GetFileNameWithoutExtension(operation.configPath),
                    operation.scriptName + "_Config",
                    StringComparison.Ordinal))
            {
                error = "Prefab/config file names do not match the pending script type.";
                return false;
            }

            if (!Enum.IsDefined(typeof(UIWindowConfiguration.PrefabSource), operation.sourceMode))
            {
                error = $"Source mode value {operation.sourceMode} is not defined.";
                return false;
            }
            if (operation.attempts < 0 || operation.attempts > MaxAttempts ||
                (!operation.failed && operation.attempts >= MaxAttempts))
            {
                error = $"Attempt count must be between 0 and {MaxAttempts}; runnable records must stay below the limit.";
                return false;
            }
            if (operation.error.Length > MaxErrorLength)
            {
                error = $"Stored error exceeds {MaxErrorLength} characters.";
                return false;
            }
            if (!operation.failed && !string.IsNullOrEmpty(operation.error))
            {
                error = "Runnable operations cannot retain a failure message.";
                return false;
            }
            if (operation.failed && string.IsNullOrWhiteSpace(operation.error))
            {
                error = "Failed operations require an error message.";
                return false;
            }

            return true;
        }

        private static bool IsValidAssetGuid(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 32)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (!Uri.IsHexDigit(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsControlCharacter(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static void FailOperation(PendingOperation operation, string error)
        {
            operation.failed = true;
            operation.error = Truncate(error, MaxErrorLength);
            CLogger.LogWarning(
                $"Window creator pending operation failed for '{operation.scriptName}': {operation.error}",
                LogCategory);
        }

        private static bool ClearJournalFailure()
        {
            if (string.IsNullOrEmpty(_journalError))
            {
                return false;
            }

            if (_journalBlocked && File.Exists(PendingPath))
            {
                try
                {
                    File.Delete(PendingPath);
                }
                catch (Exception exception)
                {
                    _journalError = "Failed to clear invalid pending journal: " + exception.Message;
                    CLogger.LogError(_journalError, LogCategory);
                    return false;
                }
            }

            _journalBlocked = false;
            _journalError = string.Empty;
            return true;
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "Unknown pending operation failure.";
            }

            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}
