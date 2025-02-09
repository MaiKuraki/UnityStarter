#if PLAYMAKER

using System.Collections;
using HutongGames.PlayMaker;

namespace AdvancedSceneManager.PackageSupport.PlayMaker
{

    [Tooltip("Discards current preloaded scene.")]
    public class DiscardPreload : ASMAction
    {

        protected override IEnumerator RunCoroutine()
        {
            operation = SceneManager.runtime.DiscardPreload();
            yield return operation;
            Finish();
        }

    }

}

#endif
