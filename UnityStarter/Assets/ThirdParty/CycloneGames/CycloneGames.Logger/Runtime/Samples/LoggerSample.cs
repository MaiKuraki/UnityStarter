using UnityEngine;

namespace CycloneGames.Logger.Sample
{
    public class LoggerSample : MonoBehaviour
    {
        private void Awake()
        {
            CLogger.Instance.AddLogger(new UnityLogger());
            
            CLogger.LogInfo($"Register Concurrent Logger");
        }

        void Start()
        {
            CLogger.LogInfo($"Test Logger");
        }
    }
}
