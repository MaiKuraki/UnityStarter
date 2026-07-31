#if UNITY_EDITOR
using System;
using UnityEditor;

namespace CycloneGames.Logger.Editor
{
    /// <summary>
    /// Owns the default Logger backend while the Editor is outside Play Mode. Runtime bootstrap
    /// takes ownership during Play Mode, so the two composition roots never share a CLogger.
    /// </summary>
    [InitializeOnLoad]
    internal static class LoggerEditorBootstrap
    {
        private static bool _shutdownStarted;
        private static bool _editorQuitting;
        private static bool _testSuspended;

        static LoggerEditorBootstrap()
        {
            LoggerUpdater.CaptureMainThreadForLifecycle();
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            ScheduleInitialization();
        }

        private static void ScheduleInitialization()
        {
            if (_editorQuitting || _testSuspended)
            {
                return;
            }

            EditorApplication.delayCall -= InitializeForEditMode;
            EditorApplication.delayCall += InitializeForEditMode;
        }

        private static void InitializeForEditMode()
        {
            if (_editorQuitting
                || _testSuspended
                || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            _shutdownStarted = false;
            try
            {
                LoggerInitializationResult initialization = LoggerBootstrap.Initialize();
                if (initialization.Status == LoggerInitializationStatus.ShutdownFailed)
                {
                    LoggerReinitializationResult retry = LoggerBootstrap.Reinitialize();
                    if (!retry.Succeeded)
                    {
                        EmergencyLogger.TryWrite(
                            "Editor logger recovery did not complete. New initialization remains blocked until shutdown can be retried safely.");
                    }
                }
                else if (initialization.Status == LoggerInitializationStatus.ExistingLoggerNotOwned)
                {
                    EmergencyLogger.TryWrite(
                        "Editor logger bootstrap preserved a CLogger owned by another composition root.");
                }
            }
            catch (Exception exception)
            {
                EmergencyLogger.TryWrite(
                    "Editor logger initialization failed. " + exception.GetType().Name);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                case PlayModeStateChange.ExitingPlayMode:
                    EditorApplication.delayCall -= InitializeForEditMode;
                    ShutdownEditorOwner();
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    _shutdownStarted = false;
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    _shutdownStarted = false;
                    ScheduleInitialization();
                    break;
            }
        }

        private static void OnBeforeAssemblyReload()
        {
            UnsubscribeLifecycleCallbacks();
            ShutdownEditorOwner();
        }

        private static void OnEditorQuitting()
        {
            _editorQuitting = true;
            UnsubscribeLifecycleCallbacks();
            ShutdownEditorOwner();
        }

        private static void OnEditorUpdate()
        {
            if (_editorQuitting
                || _testSuspended
                || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            LoggerUpdater.PumpOnce();
        }

        internal static void SuspendForTests()
        {
            _testSuspended = true;
            EditorApplication.delayCall -= InitializeForEditMode;
            ShutdownEditorOwner();
            LoggerUpdater.ResetForTests();
        }

        internal static void ResumeAfterTests()
        {
            LoggerUpdater.ResetForTests();
            _testSuspended = false;
            _shutdownStarted = false;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            ScheduleInitialization();
        }

#if UNITY_INCLUDE_TESTS
        internal static void ProcessPlayModeStateChangeForTests(PlayModeStateChange state)
        {
            OnPlayModeStateChanged(state);
        }

        internal static void ResetLifecycleStateForTests()
        {
            EditorApplication.delayCall -= InitializeForEditMode;
            _shutdownStarted = false;
            _editorQuitting = false;
            _testSuspended = true;
        }
#endif

        private static void UnsubscribeLifecycleCallbacks()
        {
            EditorApplication.delayCall -= InitializeForEditMode;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.quitting -= OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        }

        private static void ShutdownEditorOwner()
        {
            if (_shutdownStarted)
            {
                return;
            }

            _shutdownStarted = true;
            try
            {
                LoggerShutdownResult result = LoggerBootstrap.Shutdown(LogFlushMode.Buffered);
                if (!result.IsComplete && result.Status != LoggerShutdownStatus.NotStarted)
                {
                    _shutdownStarted = false;
                    EmergencyLogger.TryWrite(
                        "Editor logger shutdown did not complete. New initialization remains blocked until ownership is safe.");
                }
            }
            catch (Exception exception)
            {
                _shutdownStarted = false;
                EmergencyLogger.TryWrite(
                    "Editor logger shutdown failed. " + exception.GetType().Name);
            }
        }
    }
}
#endif
