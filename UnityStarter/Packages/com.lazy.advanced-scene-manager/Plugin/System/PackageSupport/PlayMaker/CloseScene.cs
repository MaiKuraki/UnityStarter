#if PLAYMAKER

using System.Collections;
using AdvancedSceneManager.Models;
using HutongGames.PlayMaker;

namespace AdvancedSceneManager.PackageSupport.PlayMaker
{

    [Tooltip("Closes a scene.")]
    public class CloseScene : ASMAction
    {

        [RequiredField]
        [Tooltip("The scene to close. Note that closing current scene will stop PlayMaker from executing any other states on this canvas.")]
        public Scene scene;

        [Tooltip("Optional loading scene.")]
        public Scene loadingScene;

        protected override IEnumerator RunCoroutine()
        {
            operation = scene.Close().With(loadingScene);
            yield return operation;
            Finish();
        }

        protected override ASMModel model => scene;
        protected override string action => "Close: ";

    }

}

#endif
