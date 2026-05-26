using UnityEngine;
using UnityEngine.InputSystem;

namespace CycloneGames.InputSystem.Runtime
{
    internal static class ReactiveInputSystemInitializer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Init()
        {
            // register callbacks
            UnityEngine.InputSystem.InputSystem.onBeforeUpdate += ((InputSystemFrameProvider)InputSystemFrameProvider.BeforeUpdate).OnUpdate;
            UnityEngine.InputSystem.InputSystem.onAfterUpdate += ((InputSystemFrameProvider)InputSystemFrameProvider.AfterUpdate).OnUpdate;
        }
    }
}