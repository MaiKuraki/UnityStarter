#if PLAYMAKER

using System.Collections;
using AdvancedSceneManager.Models;
using HutongGames.PlayMaker;

namespace AdvancedSceneManager.PackageSupport.PlayMaker
{

    [ActionCategory("Advanced Scene Manager")]
    [Tooltip("Opens a collection.")]
    public class OpenCollection : ASMAction
    {

        [RequiredField]
        [Tooltip("The collection to open.")]
        public SceneCollection collection;

        [Tooltip("Specifies whatever collection should open as additive.")]
        public bool additive;

        protected override IEnumerator RunCoroutine()
        {
            operation = additive ? collection.OpenAdditive() : collection.Open();
            yield return operation;
            Finish();
        }

        protected override ASMModel model => collection;
        protected override string action => "Open: ";

    }

}

#endif
