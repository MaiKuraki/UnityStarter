using CycloneGames.Logger;
using UnityEngine;

public class LoggerSample : MonoBehaviour
{
    void Awake()
    {
        CLogger.Instance.AddLogger(new UnityLogger());

        //  if there are no FileLogger, remove this line
        CLogger.Instance.AddLogger(new FileLogger("./AppLog.txt"));
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
        // MLogger.Instance.LogInfo("TickLog!");
    }
}
