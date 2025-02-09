using System.Collections;
using UnityEngine;

#if INPUTSYSTEM
#endif

namespace AdvancedSceneManager.Defaults
{

    /// <summary>A default loading screen script. Requires the user to press any key before loading screen closes.</summary>
    public class PressAnyButtonLoadingScreen : FadeLoadingScreen
    {

        public override IEnumerator OnOpen()
        {
            color = Color.white; //Override color since we're displaying a background
            yield return FadeIn();
        }

        public override IEnumerator OnClose()
        {
            yield return WaitForAnyKey();
            yield return FadeOut();
        }

    }

}
