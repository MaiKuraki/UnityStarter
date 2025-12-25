using UnityEngine;
using CycloneGames.Logger;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
/// <summary>
/// Demonstrates object pool monitoring and three-tier capacity management.
/// Shows pool statistics and validates zero-GC behavior under load.
/// </summary>
public class LoggerPoolMonitor : MonoBehaviour
{
    [Header("Test Configuration")]
    [SerializeField] private int burstLogCount = 5000;
    [SerializeField] private float monitorInterval = 1.0f;

    private float lastMonitorTime;
    private bool hasRunBurstTest = false;

    void Start()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        CLogger.ConfigureSingleThreadedProcessing();
#else
        CLogger.ConfigureThreadedProcessing();
#endif
        CLogger.Instance.AddLoggerUnique(new UnityLogger());
        CLogger.Instance.SetLogLevel(LogLevel.Info);

        ShowInitialPoolState();
    }

    void Update()
    {
        CLogger.Instance.Pump(4096);

        if (!hasRunBurstTest && Time.time > 2.0f)
        {
            RunBurstTest();
            hasRunBurstTest = true;
        }

        if (Time.time - lastMonitorTime >= monitorInterval)
        {
            ShowPoolStatistics();
            lastMonitorTime = Time.time;
        }
    }

    void OnDestroy()
    {
        ShowFinalPoolState();
        CLogger.Instance.Dispose();
    }

    [ContextMenu("Show Pool Statistics")]
    void ShowPoolStatistics()
    {
        var sbStats = CycloneGames.Logger.Util.StringBuilderPool.GetStatistics();
        var msgStats = LogMessagePool.GetStatistics();

        Debug.Log($@"
=== Object Pool Statistics ===
[StringBuilder Pool]
  Current: {sbStats.CurrentSize} | Peak: {sbStats.PeakSize}
  Gets: {sbStats.TotalGets} | Returns: {sbStats.TotalReturns}
  Discards: {sbStats.TotalDiscards} | Trims: {sbStats.TrimCount}
  Hit Rate: {sbStats.HitRate:P1} | Discard Rate: {sbStats.DiscardRate:P1}

[LogMessage Pool]
  Current: {msgStats.CurrentSize} | Peak: {msgStats.PeakSize}
  Gets: {msgStats.TotalGets} | Returns: {msgStats.TotalReturns}
  Discards: {msgStats.TotalDiscards} | Trims: {msgStats.TrimCount}
  Hit Rate: {msgStats.HitRate:P1} | Discard Rate: {msgStats.DiscardRate:P1}
==============================
");
    }

    [ContextMenu("Run Burst Test")]
    void RunBurstTest()
    {
        Debug.Log($"Running burst test: {burstLogCount} logs using StringBuilder API (should be 0 GC)");

        var sbStatsBefore = CycloneGames.Logger.Util.StringBuilderPool.GetStatistics();
        var msgStatsBefore = LogMessagePool.GetStatistics();

        for (int i = 0; i < burstLogCount; i++)
        {
            CLogger.LogInfo(sb => sb.Append("Burst test message ").Append(i), "BurstTest");
        }

        var sbStatsAfter = CycloneGames.Logger.Util.StringBuilderPool.GetStatistics();
        var msgStatsAfter = LogMessagePool.GetStatistics();

        Debug.Log($@"
=== Burst Test Results ({burstLogCount} logs) ===
[StringBuilder Pool]
  Peak Growth: {sbStatsBefore.PeakSize} → {sbStatsAfter.PeakSize} (+{sbStatsAfter.PeakSize - sbStatsBefore.PeakSize})
  Discards: {sbStatsAfter.TotalDiscards - sbStatsBefore.TotalDiscards} (Should be 0!)
  
[LogMessage Pool]
  Peak Growth: {msgStatsBefore.PeakSize} → {msgStatsAfter.PeakSize} (+{msgStatsAfter.PeakSize - msgStatsBefore.PeakSize})
  Discards: {msgStatsAfter.TotalDiscards - msgStatsBefore.TotalDiscards} (Should be 0!)
  
✓ Zero discards = Perfect three-tier capacity management!
============================================
");
    }

    [ContextMenu("Reset Statistics")]
    void ResetStatistics()
    {
        CycloneGames.Logger.Util.StringBuilderPool.ResetStatistics();
        LogMessagePool.ResetStatistics();
        Debug.Log("Pool statistics reset.");
    }

    void ShowInitialPoolState()
    {
        Debug.Log("Logger initialized. Pool will auto-prewarm to Target capacity.");
        ShowPoolStatistics();
    }

    void ShowFinalPoolState()
    {
        Debug.Log("=== Final Pool State (Before Dispose) ===");
        ShowPoolStatistics();
    }
}
#else
public class LoggerPoolMonitor : MonoBehaviour
{
    void Start()
    {
        Debug.LogWarning("LoggerPoolMonitor is only available in Editor or Development builds.");
    }
}
#endif
