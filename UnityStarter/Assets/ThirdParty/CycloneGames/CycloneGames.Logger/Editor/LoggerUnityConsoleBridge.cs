#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;

namespace CycloneGames.Logger.Editor
{
    /// <summary>
    /// Writes Editor-only Console entries whose row double-click callback retains the original caller location.
    /// </summary>
    [InitializeOnLoad]
    internal static class LoggerUnityConsoleBridge
    {
        private const int EntryIdentifier = unchecked((int)0xC1096E52u);
        private const BindingFlags StaticMemberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags InstanceMemberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly object[] AddEntryArguments = new object[1];
        private static readonly object[] AddEventHandlerArguments = new object[1];
        private static ConstructorInfo _entryConstructor;
        private static FieldInfo _messageField;
        private static FieldInfo _fileField;
        private static FieldInfo _lineField;
        private static FieldInfo _modeField;
        private static FieldInfo _identifierField;
        private static MethodInfo _addEntryMethod;
        private static int _logMode;
        private static int _warningMode;
        private static int _errorMode;
        private static bool _available;

        internal static bool IsAvailable => _available;

        static LoggerUnityConsoleBridge()
        {
            _available = TryInitialize();
            if (_available)
            {
                LoggerUpdater.SetEditorConsoleWriter(TryWriteEntry);
            }
        }

        private static bool TryInitialize()
        {
            try
            {
                Assembly unityEditorAssembly = typeof(EditorWindow).Assembly;
                Type consoleWindowType = unityEditorAssembly.GetType("UnityEditor.ConsoleWindow", throwOnError: false);
                Type logEntryType = unityEditorAssembly.GetType("UnityEditor.LogEntry", throwOnError: false);
                Type logEntriesType = unityEditorAssembly.GetType("UnityEditor.LogEntries", throwOnError: false);
                Type modeType = consoleWindowType?.GetNestedType("Mode", BindingFlags.NonPublic);
                if (consoleWindowType == null || logEntryType == null || logEntriesType == null || modeType == null)
                {
                    return false;
                }

                _entryConstructor = logEntryType.GetConstructor(InstanceMemberFlags, null, Type.EmptyTypes, null);
                _messageField = logEntryType.GetField("message", InstanceMemberFlags);
                _fileField = logEntryType.GetField("file", InstanceMemberFlags);
                _lineField = logEntryType.GetField("line", InstanceMemberFlags);
                _modeField = logEntryType.GetField("mode", InstanceMemberFlags);
                _identifierField = logEntryType.GetField("identifier", InstanceMemberFlags);
                _addEntryMethod = logEntriesType.GetMethod(
                    "AddMessageWithDoubleClickCallback",
                    StaticMemberFlags,
                    null,
                    new[] { logEntryType },
                    null);

                EventInfo doubleClickEvent = consoleWindowType.GetEvent(
                    "entryWithManagedCallbackDoubleClicked",
                    StaticMemberFlags);
                MethodInfo addEventHandler = doubleClickEvent?.GetAddMethod(nonPublic: true);
                MethodInfo callbackMethod = typeof(LoggerUnityConsoleBridge).GetMethod(
                    nameof(OnEntryDoubleClicked),
                    StaticMemberFlags);
                if (_entryConstructor == null
                    || _messageField == null
                    || _fileField == null
                    || _lineField == null
                    || _modeField == null
                    || _identifierField == null
                    || _addEntryMethod == null
                    || doubleClickEvent?.EventHandlerType == null
                    || addEventHandler == null
                    || callbackMethod == null)
                {
                    return false;
                }

                Delegate callback = Delegate.CreateDelegate(
                    doubleClickEvent.EventHandlerType,
                    callbackMethod,
                    throwOnBindFailure: false);
                if (callback == null)
                {
                    return false;
                }

                int noStacktraceMode = ReadMode(modeType, "DontExtractStacktrace");
                _logMode = ReadMode(modeType, "ScriptingLog") | noStacktraceMode;
                _warningMode = ReadMode(modeType, "ScriptingWarning") | noStacktraceMode;
                _errorMode = ReadMode(modeType, "ScriptingError") | noStacktraceMode;

                AddEventHandlerArguments[0] = callback;
                try
                {
                    addEventHandler.Invoke(null, AddEventHandlerArguments);
                }
                finally
                {
                    AddEventHandlerArguments[0] = null;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int ReadMode(Type modeType, string name)
        {
            object value = Enum.Parse(modeType, name, ignoreCase: false);
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static bool TryWriteEntry(LogLevel level, string message, string sourcePath, int lineNumber)
        {
            if (!_available || string.IsNullOrEmpty(sourcePath) || lineNumber <= 0)
            {
                return false;
            }

            if (!TryGetAllowedFullPath(sourcePath, out string fullPath))
            {
                return false;
            }

            try
            {
                object entry = _entryConstructor.Invoke(null);
                _messageField.SetValue(entry, message);
                _fileField.SetValue(entry, fullPath);
                _lineField.SetValue(entry, lineNumber);
                _modeField.SetValue(entry, GetMode(level));
                _identifierField.SetValue(entry, EntryIdentifier);

                AddEntryArguments[0] = entry;
                try
                {
                    _addEntryMethod.Invoke(null, AddEntryArguments);
                }
                finally
                {
                    AddEntryArguments[0] = null;
                }

                return true;
            }
            catch
            {
                _available = false;
                LoggerUpdater.SetEditorConsoleWriter(null);
                return false;
            }
        }

        private static int GetMode(LogLevel level)
        {
            if (level == LogLevel.Warning)
            {
                return _warningMode;
            }

            return level >= LogLevel.Error ? _errorMode : _logMode;
        }

        private static void OnEntryDoubleClicked(object entry)
        {
            if (entry == null)
            {
                return;
            }

            try
            {
                if ((int)_identifierField.GetValue(entry) != EntryIdentifier)
                {
                    return;
                }

                string fullPath = _fileField.GetValue(entry) as string;
                int lineNumber = (int)_lineField.GetValue(entry);
                if (!TryGetAllowedFullPath(fullPath, out fullPath))
                {
                    return;
                }

                UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(fullPath, lineNumber);
            }
            catch
            {
                // A navigation failure must not destabilize the Console or logging pipeline.
            }
        }

        private static bool TryGetAllowedFullPath(string sourcePath, out string fullPath)
        {
            fullPath = null;
            try
            {
                string candidate = Path.IsPathRooted(sourcePath)
                    ? Path.GetFullPath(sourcePath)
                    : Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", sourcePath));
                candidate = LoggerHyperLinkHandler.NormalizePath(candidate);
                if (!LoggerHyperLinkHandler.IsAllowedLoggerSourcePath(candidate) || !File.Exists(candidate))
                {
                    return false;
                }

                fullPath = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif
