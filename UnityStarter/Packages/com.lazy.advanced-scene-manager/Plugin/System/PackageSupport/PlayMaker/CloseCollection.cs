#if PLAYMAKER

using System.Collections;
using AdvancedSceneManager.Models;
using HutongGames.PlayMaker;

namespace AdvancedSceneManager.PackageSupport.PlayMaker
{

    [Tooltip("Closes a collection..")]
    public class CloseCollection : ASMAction
    {

        [RequiredField]
        [Tooltip("The collection to close.")]
        public SceneCollection collection;

        protected override IEnumerator RunCoroutine()
        {
            operation = collection.Close();
            yield return operation;
            Finish();
        }

        protected override ASMModel model => collection;
        protected override string action => "Close: ";

    }

}

#endif
