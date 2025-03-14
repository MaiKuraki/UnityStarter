using CycloneGames.Logger;
using UnityEngine;

public class LoggerExample : MonoBehaviour
{
    void Awake()
    {
        MLogger.Instance.AddLogger(new UnityLogger());

        //  if there are no FileLogger, remove this line
        MLogger.Instance.AddLogger(new FileLogger("./AppLog.txt"));
    }
    void Start()
    {
        MLogger.LogInfo("This is Info!");
        MLogger.LogWarning("This is Warning!");
        MLogger.LogError("This is Error!");
    }

    void Update()
    {
        // MLogger.Instance.LogInfo("TickLog!");
    }
}
