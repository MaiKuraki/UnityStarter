#if PLAYMAKER

using System.Collections;
using System.Linq;
using AdvancedSceneManager.Models;
using HutongGames.PlayMaker;

namespace AdvancedSceneManager.PackageSupport.PlayMaker
{

    [Tooltip("Opens a scene.")]
    public class OpenScene : ASMAction
    {

        [RequiredField]
        [Tooltip("The scene to open.")]
        public Scene scene;

        [Tooltip("An optional loading scene.")]
        public Scene loadingScene;

        [Tooltip("Closes all other scenes.")]
        public bool closeOtherScenes;

        protected override IEnumerator RunCoroutine()
        {
            operation = scene.Open().With(loadingScene);
            if (closeOtherScenes)
                operation.Close(SceneManager.openScenes.Where(s => !s.isPersistent && !s.isLoadingScreen));
            yield return operation;
            Finish();
        }

        protected override ASMModel model => scene;
        protected override string action => "Open: ";

    }

}

#endif
