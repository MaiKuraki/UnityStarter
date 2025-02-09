#if PLAYMAKER

using System.Collections;
using HutongGames.PlayMaker;

namespace AdvancedSceneManager.PackageSupport.PlayMaker
{

    [Tooltip("Finishes load of current preloaded scene.")]
    public class FinishPreload : ASMAction
    {

        protected override IEnumerator RunCoroutine()
        {
            operation = SceneManager.runtime.FinishPreload();
            yield return operation;
            Finish();
        }

    }

}

#endif
