using UnityEngine;

namespace CycloneGames.GameplayTags.Runtime
{
    /// <summary>
    /// This class ensures that the GameplayTagManager is initialized when the game starts at runtime.
    /// This is crucial for preventing serialization issues in builds where GameplayTagContainers are deserialized
    /// before the tag manager has registered all available tags.
    /// </summary>
    public static class GameplayTagManagerRuntimeInitialization
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            // By calling this here, we guarantee that the tag manager is fully populated
            // before any assets that use GameplayTags (like ScriptableObjects or scene objects) are deserialized and used.
            GameplayTagManager.InitializeIfNeeded();
        }
    }
}