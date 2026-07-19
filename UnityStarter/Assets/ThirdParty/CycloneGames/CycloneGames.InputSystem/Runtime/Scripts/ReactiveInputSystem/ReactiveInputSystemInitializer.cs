using UnityEngine;
using UnityEngine.InputSystem;

namespace CycloneGames.InputSystem.Runtime
{
    internal static class ReactiveInputSystemInitializer
    {
        private static bool _isRegistered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Unregister();
            ((InputSystemFrameProvider)InputSystemFrameProvider.BeforeUpdate).Reset();
            ((InputSystemFrameProvider)InputSystemFrameProvider.AfterUpdate).Reset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Init()
        {
            if (_isRegistered) return;
            UnityEngine.InputSystem.InputSystem.onBeforeUpdate += ((InputSystemFrameProvider)InputSystemFrameProvider.BeforeUpdate).OnUpdate;
            UnityEngine.InputSystem.InputSystem.onAfterUpdate += ((InputSystemFrameProvider)InputSystemFrameProvider.AfterUpdate).OnUpdate;
            _isRegistered = true;
        }

        internal static void ResetForTests()
        {
            Reset();
            Init();
        }

        private static void Unregister()
        {
            if (!_isRegistered) return;
            UnityEngine.InputSystem.InputSystem.onBeforeUpdate -= ((InputSystemFrameProvider)InputSystemFrameProvider.BeforeUpdate).OnUpdate;
            UnityEngine.InputSystem.InputSystem.onAfterUpdate -= ((InputSystemFrameProvider)InputSystemFrameProvider.AfterUpdate).OnUpdate;
            _isRegistered = false;
        }
    }
}
