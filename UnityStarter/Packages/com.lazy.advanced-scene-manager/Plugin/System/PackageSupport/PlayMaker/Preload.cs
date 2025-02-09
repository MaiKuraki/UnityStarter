#if PLAYMAKER

using System.Collections;
using AdvancedSceneManager.Models;
using HutongGames.PlayMaker;

namespace AdvancedSceneManager.PackageSupport.PlayMaker
{

    [Tooltip("Preloads a scene.")]
    public class Preload : ASMAction
    {

        [RequiredField]
        [Tooltip("The scene to preload.")]
        public Scene scene;

        protected override IEnumerator RunCoroutine()
        {
            operation = scene.Preload();
            yield return operation;
            Finish();
        }

        protected override ASMModel model => scene;
        protected override string action => "Preload: ";

    }

}

#endif
