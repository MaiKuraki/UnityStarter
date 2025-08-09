using CycloneGames.Logger;
using UnityEngine;

public class LoggerSample : MonoBehaviour
{
    void Awake()
    {
        // If you use the centralized bootstrap, this block can be removed.
#if UNITY_WEBGL && !UNITY_EDITOR
        CLogger.ConfigureSingleThreadedProcessing();
#else
        // Threaded processing on platforms that support it.
        CLogger.ConfigureThreadedProcessing();
#endif

        // Per-script registration (remove if centralized bootstrap registers loggers).
        CLogger.Instance.AddLoggerUnique(new UnityLogger());

        // Optional: file logger for the sample (avoid on WebGL/editor duplication scenarios).
#if !UNITY_WEBGL || UNITY_EDITOR
        CLogger.Instance.AddLoggerUnique(new FileLogger("./AppLog.txt"));
#endif
    }

    void Start()
    {
        CLogger.LogInfo("This is Info!");
        CLogger.LogWarning("This is Warning!");
        CLogger.LogError("This is Error!");
    }

    void OnDestroy()
    {
        CLogger.Instance.Dispose();
    }

    void Update()
    {
        // Pump() drains the queue in single-threaded mode; no-op in threaded mode.
        CLogger.Instance.Pump(1024);
    }
}
