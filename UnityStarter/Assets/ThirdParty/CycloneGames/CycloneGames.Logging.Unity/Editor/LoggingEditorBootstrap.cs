#if UNITY_EDITOR
using System;
using CycloneGames.Logging;
using UnityEditor;

namespace CycloneGames.Logging.Unity.Editor
{
    /// <summary>
    /// Owns the default logging pipeline while the Editor is outside Play Mode. Runtime bootstrap
    /// takes ownership during Play Mode, so the two composition roots never share a LogPipeline.
    /// </summary>
    [InitializeOnLoad]
    internal static class LoggingEditorBootstrap
    {
        private static bool _shutdownStarted;
        private static bool _editorQuitting;
        private static bool _testSuspended;

        static LoggingEditorBootstrap()
        {
            LoggingRuntimeHost.CaptureMainThreadForLifecycle();
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
                LoggingInitializationResult initialization = LoggingBootstrap.Initialize();
                if (initialization.Status == LoggingInitializationStatus.ShutdownFailed)
                {
                    LoggingReinitializationResult retry = LoggingBootstrap.Reinitialize();
                    if (!retry.Succeeded)
                    {
                        EmergencyLogWriter.TryWrite(
                            "Editor logging recovery did not complete. New initialization remains blocked until shutdown can be retried safely.");
                    }
                }
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                EmergencyLogWriter.TryWrite(
                    "Editor logging initialization failed. " + exception.GetType().Name);
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

            LoggingRuntimeHost.PumpOnce();
        }

        internal static void SuspendForTests()
        {
            _testSuspended = true;
            EditorApplication.delayCall -= InitializeForEditMode;
            ShutdownEditorOwner();
            LoggingRuntimeHost.ResetForTests();
        }

        internal static void ResumeAfterTests()
        {
            LoggingRuntimeHost.ResetForTests();
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
                LogPipelineShutdownResult result = LoggingBootstrap.Shutdown(LogFlushMode.Buffered);
                if (!result.IsComplete && result.Status != LogPipelineShutdownStatus.NotStarted)
                {
                    _shutdownStarted = false;
                    EmergencyLogWriter.TryWrite(
                        "Editor logging shutdown did not complete. New initialization remains blocked until ownership is safe.");
                }
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                _shutdownStarted = false;
                EmergencyLogWriter.TryWrite(
                    "Editor logging shutdown failed. " + exception.GetType().Name);
            }
        }
    }
}
#endif
