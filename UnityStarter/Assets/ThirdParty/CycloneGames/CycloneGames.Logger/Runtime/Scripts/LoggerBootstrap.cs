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

            // 2) Configure processing strategy
            var mode = settings ? settings.processing : LoggerSettings.ProcessingMode.AutoDetect;
            switch (mode)
            {
                case LoggerSettings.ProcessingMode.ForceThreaded:
                    CLogger.ConfigureThreadedProcessing();
                    break;
                case LoggerSettings.ProcessingMode.ForceSingleThread:
                    CLogger.ConfigureSingleThreadedProcessing();
                    break;
                default:
                    // Auto-detect: WebGL -> single-threaded; others -> threaded
                    if (Application.platform == RuntimePlatform.WebGLPlayer)
                        CLogger.ConfigureSingleThreadedProcessing();
                    else
                        CLogger.ConfigureThreadedProcessing();
                    break;
            }

            // 3) Register default loggers
            // Default: register UnityLogger unless explicitly disabled via settings
            bool useUnity = settings ? settings.registerUnityLogger : true;
            bool useFile = settings ? settings.registerFileLogger : false;

            if (useUnity)
            {
                CLogger.Instance.AddLoggerUnique(new UnityLogger());
            }

            if (useFile && Application.platform != RuntimePlatform.WebGLPlayer)
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
            if (settings)
            {
                CLogger.Instance.SetLogLevel(settings.defaultLevel);
                CLogger.Instance.SetLogFilter(settings.defaultFilter);
            }
        }
    }
}


