using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;

namespace Proxima
{
    internal class ProximaLogCommands
    {
        [Serializable]
        public struct LogInfo
        {
            public long LogTime;
            public string LogString;
            public string StackTrace;
            public LogType Type;
        }

        private static HashSet<string> _pendingStreamIds;
        private static CircularList<LogInfo> _logs;
        private static IEnumerable<LogInfo> _streamLogs;
        private static int _streamIndex = 0;
        private static int _lastUpdateFrame;

        public static void SetLogCapacity(int capacity)
        {
            _logs = new CircularList<LogInfo>(capacity);
        }

        [ProximaInitialize]
        public static void Initialize()
        {
            _pendingStreamIds = new HashSet<string>();
            Application.logMessageReceived += OnLogMessageReceived;
        }

        [ProximaTeardown]
        public static void Teardown()
        {
            _pendingStreamIds = null;
            Application.logMessageReceived -= OnLogMessageReceived;
        }

        [ProximaStreamStart("LogStream")]
        public static void StartStream(string id)
        {
            _pendingStreamIds.Add(id);
        }

        [ProximaStreamUpdate("LogStream")]
        public static IEnumerable<LogInfo> UpdateStream(string id)
        {
            if (_lastUpdateFrame != Time.frameCount)
            {
                _lastUpdateFrame = Time.frameCount;
                bool hasUpdate = _streamIndex != _logs.ItemsAdded;
                _streamLogs = hasUpdate ? _logs.GetRange(_streamIndex) : null;
                _streamIndex = _logs.ItemsAdded;
            }

            if (_pendingStreamIds.Contains(id))
            {
                _pendingStreamIds.Remove(id);
                return _logs.ItemsAdded > 0 ? _logs : null;
            }

            return _streamLogs;
        }

        [ProximaStreamStop("LogStream")]
        public static void StopStream(string id)
        {
            _pendingStreamIds.Remove(id);
        }

        private static void OnLogMessageReceived(string message, string stackTrace, LogType type)
        {
            #if PROXIMA_LOG_VERBOSE
                if (message.StartsWith("PXV:"))
                {
                    return;
                }
            #endif

            var logInfo = new LogInfo
            {
                LogTime = (long) System.DateTime.UtcNow.Subtract(new System.DateTime(1970, 1, 1)).TotalMilliseconds,
                LogString = message,
                StackTrace = stackTrace,
                Type = type
            };

            _logs.Add(logInfo);
        }

        [ProximaCommand("Internal")]
        public static string GetLogFile()
        {
            if (string.IsNullOrEmpty(Application.consoleLogPath))
            {
                throw new Exception("Log file not available");
            }

            try
            {
                // If we just try to open the file we'll get a sharing violation, so need to copy it first.
                var temp = Path.Combine(Application.temporaryCachePath, Path.GetTempFileName());
                File.Copy(Application.consoleLogPath, temp, true);
                var result = File.ReadAllText(temp);
                File.Delete(temp);
                return result;
            }
            catch (Exception e)
            {
                Log.Error("Failed to get log file: " + e.Message);
                return "Failed to get log file: " + e.Message;
            }
        }

        [ProximaCommand("Internal")]
        public static string GetPreviousLogFile()
        {
            if (string.IsNullOrEmpty(Application.consoleLogPath))
            {
                throw new Exception("Log file not available");
            }

            try
            {
                var path = Application.consoleLogPath.Replace(".log", "-prev.log");
                return File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Log.Error("Failed to get previous log file: " + e.Message);
                return "Failed to get previous log file: " + e.Message;
            }
        }

        [ProximaCommand("Internal")]
        public static bool GetLogFilesAvailable()
        {
            return !string.IsNullOrEmpty(Application.consoleLogPath) && File.Exists(Application.consoleLogPath);
        }
    }
}