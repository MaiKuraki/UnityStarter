using UnityEngine;
using CycloneGames.Logger;
using System.Diagnostics; // For Stopwatch 
using System.IO;
using System.Text;

public class LoggerBenchmark : MonoBehaviour
{
    private const int TestIterations = 10000;
    private Stopwatch stopwatch = new Stopwatch();
    private string reportPath;
    float unityLoggerTimespan = -1;
    float customLoggerTimespan = -1;

    void Start()
    {
        // Setup custom logger 
        CLogger.Instance.AddLogger(new ConsoleLogger());
        CLogger.Instance.AddLogger(new FileLogger(Application.dataPath + "/Logs/Benchmark.log"));
        CLogger.Instance.AddLogger(new UnityLogger());
        CLogger.Instance.SetLogLevel(LogLevel.Trace);

        // Prepare report file 
        reportPath = Path.Combine(Application.dataPath, "Logs/LoggerBenchmarkReport.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));

        // Run tests 
        TestUnityLogger();
        TestCustomLogger();

        // Output final report 
        string finalReport = $"\n{TestIterations} Interactions\nFinal Comparison ({System.DateTime.Now}):\n" +
                    "======================================\n" +
                    "| Logger Type            | Time (ms) |\n" +
                    "|------------------------|-----------|\n" +
                    $"| Unity Debug.Log        | {FormatFloat(unityLoggerTimespan)} |\n" +
                    $"| CLogger LogInfo        | {FormatFloat(customLoggerTimespan)} |\n" +
                    "======================================";

        File.AppendAllText(reportPath, finalReport, System.Text.Encoding.UTF8);
        UnityEngine.Debug.Log(finalReport);
    }

    void OnDestroy()
    {
        CLogger.Instance.Dispose();
    }

    float TestUnityLogger()
    {
        // Warm up 
        UnityEngine.Debug.Log("Unity Logger Warmup");

        stopwatch.Restart();
        for (int i = 0; i < TestIterations; i++)
        {
            UnityEngine.Debug.Log($"Unity test message {i}");
        }
        stopwatch.Stop();

        float elapsedMs = stopwatch.ElapsedMilliseconds;
        string report = $"Unity   Debug.Log - {TestIterations} iterations: {elapsedMs} ms";
        File.AppendAllText(reportPath, report + "\n", System.Text.Encoding.UTF8);
        UnityEngine.Debug.Log(report);
        unityLoggerTimespan = elapsedMs;
        return elapsedMs;
    }

    float TestCustomLogger()
    {
        // Warm up 
        CLogger.LogInfo("Custom Logger Warmup");

        stopwatch.Restart();
        for (int i = 0; i < TestIterations; i++)
        {
            CLogger.LogInfo($"Custom test message {i}", "Benchmark");
        }
        stopwatch.Stop();

        float elapsedMs = stopwatch.ElapsedMilliseconds;
        string report = $"CLogger LogInfo   - {TestIterations} iterations: {elapsedMs} ms";
        File.AppendAllText(reportPath, report + "\n");
        UnityEngine.Debug.Log(report);
        customLoggerTimespan = elapsedMs;
        return elapsedMs;
    }

    public static string FormatFloat(float number, int length = 6)
    {
        try
        {
            int integerPart = (int)System.Math.Truncate(number);
            float decimalPart = System.Math.Abs(number - integerPart);

            string integerStr = integerPart.ToString();
            if (integerStr.Length > length)
            {
                integerStr = integerStr.Substring(0, length);
            }
            string paddedInteger = integerStr.PadLeft(length, ' ');

            string decimalStr = decimalPart.ToString("0.00").Substring(1);

            return paddedInteger + decimalStr;
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"Format float failed. Error: {ex.Message}");
            StringBuilder prefix = new StringBuilder();
            for (int i = 0; i < length - 1; i++)
            {
                prefix.Append(" ");
            }
            prefix.Append("0.00");
            return prefix.ToString();
        }
    }
}