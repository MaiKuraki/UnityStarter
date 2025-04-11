using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.Cheat.Sample
{
    public class CheatSampleCheatPage : MonoBehaviour
    {
        void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F1))
            {
                CheatCommandUtility.PublishCheatCommand("Protocol_CheatMessage_A").Forget();
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.F2))
            {
                CheatCommandUtility.PublishCheatCommand("Protocol_CheatMessage_B").Forget();
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.F3))
            {
                CheatCommandUtility.PublishCheatCommand("Protocol_CustomStringMessage", "Some reference type data.").Forget();
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.F4))
            {
                CheatCommandUtility.PublishCheatCommand("Protocol_GameDataMessage", new GameData(new Vector3(1, 2, 3), new Vector3(4, 5, 6))).Forget();
            }
        }
    }
}

