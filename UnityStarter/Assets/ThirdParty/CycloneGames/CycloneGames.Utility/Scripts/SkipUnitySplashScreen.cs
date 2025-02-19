using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace CycloneGames.Utility.Runtime
{
    public class SkipUnitySplashScreen
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Function()
        {
            Task.Run(() =>
            {
                SplashScreen.Stop(SplashScreen.StopBehavior.StopImmediate);
            });
        }
    }
}
