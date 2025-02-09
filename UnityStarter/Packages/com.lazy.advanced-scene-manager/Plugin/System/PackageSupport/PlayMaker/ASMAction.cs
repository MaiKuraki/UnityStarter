#if PLAYMAKER

using System.Collections;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Models;
using HutongGames.PlayMaker;

namespace AdvancedSceneManager.PackageSupport.PlayMaker
{

    [ActionCategory("Advanced Scene Manager")]
    public abstract class ASMAction : FsmStateAction
    {

        protected abstract IEnumerator RunCoroutine();
        UnityEngine.Coroutine coroutine;
        protected SceneOperation operation;

        public override void OnEnter()
        {
            coroutine = StartCoroutine(RunCoroutine());

        }

        public override void OnExit()
        {
            //operation?.Cancel();
            //if (coroutine != null)
            //    StopCoroutine(coroutine);
        }

        protected virtual ASMModel model { get; }
        protected virtual string action { get; }

        public override string AutoName()
        {

            if (!model || string.IsNullOrEmpty(action))
                return base.AutoName();
            else
                return action + (model is SceneCollection c ? c.title : model.name);

        }

    }

}

#endif
