using UnityEngine;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Default bootstrap. Lives in Runtime so projects get a working configuration out-of-the-box.
    /// Override by: (a) settings asset at Resources/CycloneGames.Logger/LoggerSettings, or
    /// (b) a project bootstrap with higher script execution order.
    /// </summary>
    public static class LoggerBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            // 1) Load optional project settings (do not rename the asset or folder)
            var settings = Resources.Load<LoggerSettings>(LoggerSettings.SettingsResourcePath);
            LoggerProcessingOptions processingOptions = null;
            if (settings)
            {
                processingOptions = new LoggerProcessingOptions
                {
                    MaxQueuedMessages = settings.maxQueuedMessages,
                    UnityConsoleMaxQueuedMessages = settings.unityConsoleMaxQueuedMessages,
                    ShutdownDrainTimeoutMs = settings.shutdownDrainTimeoutMs,
                    OverflowPolicy = settings.overflowPolicy,
                    GuaranteedLevel = settings.guaranteedLevel
                };
            }

            // 2) Configure processing strategy
            var mode = settings ? settings.processing : LoggerSettings.ProcessingMode.AutoDetect;
            bool useUnity = !settings || settings.registerUnityLogger;
            bool useFile = settings ? settings.registerFileLogger : false;
            bool canRegisterFileLogger = useFile && Application.platform != RuntimePlatform.WebGLPlayer;
            bool hasDefaultLoggers = useUnity || canRegisterFileLogger;
            bool shouldForceThreadForFileLogger = canRegisterFileLogger
                && mode == LoggerSettings.ProcessingMode.ForceSingleThread;

            switch (mode)
            {
                case LoggerSettings.ProcessingMode.ForceThreaded:
                    CLogger.ConfigureThreadedProcessing(processingOptions);
                    break;
                case LoggerSettings.ProcessingMode.ForceSingleThread:
                    if (shouldForceThreadForFileLogger)
                    {
                        System.Console.Error.WriteLine("[WARNING] LoggerBootstrap: FileLogger requires threaded processing outside WebGL; ForceSingleThread was ignored.");
                        CLogger.ConfigureThreadedProcessing(processingOptions);
                    }
                    else
                    {
                        CLogger.ConfigureSingleThreadedProcessing(processingOptions);
                    }
                    break;
                default:
                    // Auto-detect: WebGL -> single-threaded; others -> threaded
                    if (Application.platform == RuntimePlatform.WebGLPlayer)
                        CLogger.ConfigureSingleThreadedProcessing(processingOptions);
                    else
                        CLogger.ConfigureThreadedProcessing(processingOptions);
                    break;
            }

            // 3) Register default loggers
            if (useUnity)
            {
                CLogger.Instance.AddLoggerUnique(new UnityLogger());
            }

            if (canRegisterFileLogger)
            {
                string path;
                if (settings)
                {
                    if (settings.usePersistentDataPath)
                        path = System.IO.Path.Combine(Application.persistentDataPath, settings.fileName);
                    else if (!string.IsNullOrEmpty(settings.customFilePath))
                        path = settings.customFilePath;
                    else
                        path = System.IO.Path.Combine(Application.persistentDataPath, "App.log");
                }
                else
                {
                    path = System.IO.Path.Combine(Application.persistentDataPath, "App.log");
                }
                CLogger.Instance.AddLoggerUnique(new FileLogger(path));
            }

            // 4) Defaults
            // Do not force defaults unless settings exist; this allows projects to configure via code before first use.
            if (settings && hasDefaultLoggers)
            {
                CLogger.Instance.SetLogLevel(settings.defaultLevel);
                CLogger.Instance.SetLogFilter(settings.defaultFilter);
            }

            // UnityLogger creates LoggerUpdater on demand; file logging is drained by the threaded processor.
            CLogger.ConfigureGlobalStaticLoggingSuppressed(!hasDefaultLoggers);
        }
    }
}
