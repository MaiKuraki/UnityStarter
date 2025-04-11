using UnityEngine;
using VitalRouter;
using CycloneGames.Cheat;

namespace CycloneGames.Cheat.Sample
{
    [VitalRouter.Routes]
    public partial class CheatSampleGameLogic : MonoBehaviour
    {

        void OnEnable()
        {
            // You must have at least one route method like OnReceiveMessage in this class, 
            // otherwise this Api 'MapTo' and 'UnmapRoutes' will not callable.
            MapTo(VitalRouter.Router.Default);
        }

        void OnDisable()
        {
            UnmapRoutes();
        }

        [VitalRouter.Route]
        void OnMsg(CheatCommand simpleMsg)
        {
            UnityEngine.Debug.Log($"Receive Cheat: {simpleMsg.CommandID}");

            switch (simpleMsg.CommandID)
            {
                case "Protocol_CheatMessage_A": 
                UnityEngine.Debug.Log("Execute Command A"); 
                    break;
                case "Protocol_CheatMessage_B": 
                UnityEngine.Debug.Log("Execute Command B"); 
                    break;
            }
        }

        [VitalRouter.Route]
        void OnReceiveMessage(CheatCommand<GameData> cmd)
        {
            UnityEngine.Debug.Log($"Receive Cheat: {cmd.CommandID}, Arg: {cmd.Arg}");
        }

        [VitalRouter.Route]
        void OnReceiveMessage(CheatCommandClass<string> cmd)
        {
            UnityEngine.Debug.Log($"Receive Cheat: {cmd.CommandID}, Arg: {cmd.Arg.ToString()}");
        }

    }
}