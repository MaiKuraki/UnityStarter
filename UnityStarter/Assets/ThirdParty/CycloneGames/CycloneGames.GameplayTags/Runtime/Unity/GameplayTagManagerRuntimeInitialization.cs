#if UNITY_5_3_OR_NEWER
using UnityEngine;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayTags.Unity.Runtime
{
    /// <summary>
    /// Ensures GameplayTags are initialized before Unity deserializes gameplay-tag-dependent assets.
    /// </summary>
    public static class GameplayTagManagerRuntimeInitialization
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            GameplayTagManager.InitializeIfNeeded();
        }
    }
}
#endif
