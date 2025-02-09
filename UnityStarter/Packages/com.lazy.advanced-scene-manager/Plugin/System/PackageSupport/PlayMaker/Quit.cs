#if PLAYMAKER

using System.Collections;
using HutongGames.PlayMaker;

namespace AdvancedSceneManager.PackageSupport.PlayMaker
{

    [Tooltip("Runs ASM quit process and quits the game. Note that ASM quit process is optional.")]
    public class Quit : ASMAction
    {

        protected override IEnumerator RunCoroutine()
        {
            SceneManager.app.Quit();
            Finish();
            yield break;
        }

    }

}

#endif
